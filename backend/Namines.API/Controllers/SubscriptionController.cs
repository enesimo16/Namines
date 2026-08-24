using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Namines.Core.Models.Auth;
using Stripe;
using Stripe.Checkout;
using System.Security.Claims;

namespace Namines.API.Controllers;

[ApiController]
[Route("api/subscription")]
[Authorize]
public class SubscriptionController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _config;

    public SubscriptionController(
        UserManager<ApplicationUser> userManager,
        IConfiguration config)
    {
        _userManager = userManager;
        _config = config;
        StripeConfiguration.ApiKey = config["Stripe:SecretKey"];
    }

    // POST /api/subscription/checkout?plan=pro|team
    // Creates a Stripe Hosted Checkout session for the requested plan.
    // Returns { url } — the frontend redirects the user there.
    // Card data NEVER touches our server. Stripe handles PCI-DSS.
    [HttpPost("checkout")]
    public async Task<IActionResult> CreateCheckoutSession([FromQuery] string plan = "pro")
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId!);
        if (user == null) return Unauthorized();

        // If user already has an active subscription, redirect to portal instead —
        // Checkout üzerinden ikinci bir abonelik açmak, aynı kullanıcıya iki kez
        // fatura kesmek olurdu. Plan değişikliği (Pro↔Team) portal üzerinden.
        if (user.SubscriptionStatus == "active" && !string.IsNullOrEmpty(user.StripeCustomerId))
            return Ok(new { redirect = "portal" });

        // Bilinmeyen bir plan adı sessizce Pro'ya düşmüyor: kullanıcı Team'e
        // tıklayıp yanlışlıkla Pro'ya abone olurdu ve bunu ancak faturada fark ederdi.
        var normalizedPlan = plan?.Trim().ToLowerInvariant();
        var priceId = normalizedPlan switch
        {
            "team" => _config["Stripe:TeamPriceId"],
            "pro" => _config["Stripe:ProPriceId"],
            _ => null,
        };
        if (priceId is null)
            return BadRequest(new { error = $"Unknown plan '{plan}'. Use 'pro' or 'team'." });
        if (string.IsNullOrWhiteSpace(priceId))
            return StatusCode(500, new { error = $"Stripe price ID for '{normalizedPlan}' is not configured." });

        var frontendUrl = _config["App:FrontendUrl"] ?? "http://localhost:3000";

        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            PaymentMethodTypes = new List<string> { "card" },
            LineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    Price = priceId,
                    Quantity = 1
                }
            },
            // Pass our internal user ID so the webhook can identify who paid
            ClientReferenceId = userId,
            // Pre-fill customer email if user has one
            CustomerEmail = string.IsNullOrEmpty(user.StripeCustomerId) ? user.Email : null,
            Customer = string.IsNullOrEmpty(user.StripeCustomerId) ? null : user.StripeCustomerId,
            SuccessUrl = $"{frontendUrl}/?upgrade=success",
            CancelUrl  = $"{frontendUrl}/?upgrade=canceled",
            // Metadata for audit trail
            Metadata = new Dictionary<string, string>
            {
                { "namines_user_id", userId! },
                { "namines_username", user.UserName ?? "" }
            }
        };

        var service = new SessionService();
        var session = await service.CreateAsync(options);

        return Ok(new { url = session.Url });
    }

    // POST /api/subscription/portal
    // Generates a Stripe Customer Portal link.
    // The user can cancel, update their card, or view invoices — all on Stripe's PCI-DSS servers.
    [HttpPost("portal")]
    public async Task<IActionResult> CreatePortalSession()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId!);
        if (user == null) return Unauthorized();

        if (string.IsNullOrEmpty(user.StripeCustomerId))
            return BadRequest(new { error = "No active subscription found. Please upgrade to Pro first." });

        var frontendUrl = _config["App:FrontendUrl"] ?? "http://localhost:3000";

        var options = new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = user.StripeCustomerId,
            ReturnUrl = $"{frontendUrl}/",
        };

        var service = new Stripe.BillingPortal.SessionService();
        var session = await service.CreateAsync(options);

        return Ok(new { url = session.Url });
    }

    // GET /api/subscription/status
    // Returns the subscription status for the authenticated user.
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId!);
        if (user == null) return Unauthorized();

        return Ok(new
        {
            status = user.SubscriptionStatus ?? "none",
            isPro = user.Type == UserType.Corporate,
            currentPeriodEnd = user.CurrentPeriodEnd,
            hasStripeCustomer = !string.IsNullOrEmpty(user.StripeCustomerId)
        });
    }
}
