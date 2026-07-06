using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Stripe;

namespace Namines.API.Controllers;

/// <summary>
/// Stripe Webhook Listener — the cryptographic security perimeter.
/// 
/// Security Architecture:
/// - Raw request body is read BEFORE the framework deserializes it,
///   because Stripe signs the raw bytes. Any transformation breaks the signature.
/// - ConstructEvent() validates the HMAC-SHA256 signature using the webhook signing secret.
/// - If the signature is invalid, we return 400 immediately — no event processing.
/// - The endpoint is [AllowAnonymous] because Stripe cannot authenticate via JWT.
///   Security is enforced exclusively through signature validation.
/// </summary>
[ApiController]
[Route("api/webhooks")]
public class StripeWebhookController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AuthDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(
        UserManager<ApplicationUser> userManager,
        AuthDbContext db,
        IConfiguration config,
        ILogger<StripeWebhookController> logger)
    {
        _userManager = userManager;
        _db = db;
        _config = config;
        _logger = logger;
    }

    // POST /api/webhooks/stripe
    // Stripe calls this endpoint for every event.
    // We validate the signature, then process only the two events we care about.
    [HttpPost("stripe")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    [Consumes("application/json")]
    public async Task<IActionResult> HandleStripeWebhook()
    {
        // Read the raw request body — must be done BEFORE any framework binding
        // because Stripe signs the raw bytes
        string json;
        using (var reader = new System.IO.StreamReader(Request.Body, leaveOpen: false))
        {
            json = await reader.ReadToEndAsync();
        }

        var webhookSecret = _config["Stripe:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            _logger.LogError("[STRIPE WEBHOOK] Stripe:WebhookSecret is not configured. Rejecting all webhook calls.");
            return StatusCode(500, "Webhook secret not configured.");
        }

        // ── CRYPTOGRAPHIC SIGNATURE VALIDATION ────────────────────────────────
        // If the signature header is missing, invalid, or timestamp is expired,
        // ConstructEvent() throws and we return 400. No event is processed.
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                json,
                Request.Headers["Stripe-Signature"],
                webhookSecret,
                throwOnApiVersionMismatch: false
            );
        }
        catch (StripeException ex)
        {
            _logger.LogWarning("[STRIPE WEBHOOK] ⚠ Signature validation FAILED: {Message}. Rejecting with 400.", ex.Message);
            return BadRequest(new { error = "Invalid Stripe signature." });
        }

        _logger.LogInformation("[STRIPE WEBHOOK] ✓ Verified event: {Type} | ID: {Id}", stripeEvent.Type, stripeEvent.Id);

        // ── EVENT DISPATCH ─────────────────────────────────────────────────────
        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                await HandleCheckoutCompleted(stripeEvent);
                break;

            case "customer.subscription.deleted":
                await HandleSubscriptionDeleted(stripeEvent);
                break;

            case "customer.subscription.updated":
                await HandleSubscriptionUpdated(stripeEvent);
                break;

            default:
                // Acknowledge the event but take no action — avoids Stripe retries
                _logger.LogDebug("[STRIPE WEBHOOK] Unhandled event type: {Type}", stripeEvent.Type);
                break;
        }

        // Always return 200 to acknowledge receipt to Stripe
        return Ok(new { received = true });
    }

    // ── checkout.session.completed ─────────────────────────────────────────────
    // User completed payment. Activate their subscription and store Stripe IDs.
    private async Task HandleCheckoutCompleted(Event stripeEvent)
    {
        var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
        if (session == null) return;

        // We stored our internal user ID in ClientReferenceId at checkout creation
        var userId = session.ClientReferenceId;
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogError("[STRIPE WEBHOOK] checkout.session.completed: Missing ClientReferenceId. Cannot identify user.");
            return;
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            _logger.LogError("[STRIPE WEBHOOK] checkout.session.completed: User {UserId} not found.", userId);
            return;
        }

        // Retrieve the subscription to get period end date
        DateTime? periodEnd = null;
        if (!string.IsNullOrEmpty(session.SubscriptionId))
        {
            try
            {
                var subService = new SubscriptionService();
                var subscription = await subService.GetAsync(session.SubscriptionId);
                periodEnd = subscription.CurrentPeriodEnd;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[STRIPE WEBHOOK] Could not fetch subscription details: {Message}", ex.Message);
            }
        }

        // Update user entity — elevate to Pro Member
        user.StripeCustomerId     = session.CustomerId;
        user.StripeSubscriptionId = session.SubscriptionId;
        user.SubscriptionStatus   = "active";
        user.CurrentPeriodEnd     = periodEnd;
        user.Type                 = UserType.Corporate; // Elevate to Pro

        var result = await _userManager.UpdateAsync(user);
        if (result.Succeeded)
            _logger.LogInformation("[STRIPE WEBHOOK] ✅ User {UserId} elevated to Pro Member. Subscription: {SubId}", userId, session.SubscriptionId);
        else
            _logger.LogError("[STRIPE WEBHOOK] Failed to update user {UserId}: {Errors}", userId, string.Join(", ", result.Errors.Select(e => e.Description)));
    }

    // ── customer.subscription.deleted ─────────────────────────────────────────
    // Subscription was canceled (by user or due to payment failure).
    // Downgrade user back to Free tier immediately.
    private async Task HandleSubscriptionDeleted(Event stripeEvent)
    {
        var subscription = stripeEvent.Data.Object as Stripe.Subscription;
        if (subscription == null) return;

        // Find user by their Stripe Customer ID
        var user = _userManager.Users.FirstOrDefault(u => u.StripeCustomerId == subscription.CustomerId);
        if (user == null)
        {
            _logger.LogWarning("[STRIPE WEBHOOK] customer.subscription.deleted: No user found for CustomerId {CustomerId}.", subscription.CustomerId);
            return;
        }

        user.SubscriptionStatus   = "canceled";
        user.StripeSubscriptionId = null;
        user.CurrentPeriodEnd     = null;
        user.Type                 = UserType.Individual; // Downgrade to Free

        var result = await _userManager.UpdateAsync(user);
        if (result.Succeeded)
            _logger.LogInformation("[STRIPE WEBHOOK] 🔴 User {UserId} downgraded to Free. Subscription canceled.", user.Id);
        else
            _logger.LogError("[STRIPE WEBHOOK] Failed to downgrade user {UserId}: {Errors}", user.Id, string.Join(", ", result.Errors.Select(e => e.Description)));
    }

    // ── customer.subscription.updated ─────────────────────────────────────────
    // Handles renewals, payment failures (past_due), and reactivations.
    private async Task HandleSubscriptionUpdated(Event stripeEvent)
    {
        var subscription = stripeEvent.Data.Object as Stripe.Subscription;
        if (subscription == null) return;

        var user = _userManager.Users.FirstOrDefault(u => u.StripeCustomerId == subscription.CustomerId);
        if (user == null) return;

        user.SubscriptionStatus = subscription.Status; // active | past_due | canceled | etc.
        user.CurrentPeriodEnd   = subscription.CurrentPeriodEnd;

        // Restore Pro if payment recovered
        if (subscription.Status == "active")
            user.Type = UserType.Corporate;
        // Soft downgrade on past_due — keep access for now, will hard downgrade on deletion
        else if (subscription.Status is "canceled" or "unpaid")
            user.Type = UserType.Individual;

        await _userManager.UpdateAsync(user);
        _logger.LogInformation("[STRIPE WEBHOOK] 🔄 Subscription updated for user {UserId}: {Status}", user.Id, subscription.Status);
    }
}
