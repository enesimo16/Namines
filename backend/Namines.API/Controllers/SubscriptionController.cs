using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Namines.Core.Analysis;
// Stripe SDK'sında da PlanTier var; kendi katman enum'umuzu takma adla alıyoruz.
using PlanTier = Namines.Core.Analysis.PlanTier;
using Namines.Core.Models.Auth;
using Stripe;
using Stripe.Checkout;
using System.Linq;
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

    // GET /api/subscription/plans
    // Satılan planlar ve fiyatları. [AllowAnonymous]: fiyat listesi giriş
    // yapmadan da görülebilmeli — aksi halde ürünün ne kadar tuttuğunu öğrenmek
    // için önce hesap açmak gerekirdi.
    //
    // Fiyatlar ARTIK ekranda düz metin değil: React bileşenine "$7.5/mo" yazmak,
    // Stripe'taki fiyat değiştiğinde ekranın eski tutarı göstermeye devam etmesi
    // demekti ve kullanıcı farkı ancak kartından çekilen tutarda görürdü.
    [AllowAnonymous]
    [HttpGet("plans")]
    public IActionResult GetPlans()
    {
        var plans = new[] { PlanTier.Pro, PlanTier.Team }.Select(tier => new
        {
            plan = tier.ToString().ToLowerInvariant(),
            yearlyDiscountPercent = PricingCatalog.YearlyDiscountPercent(tier),
            prices = PricingCatalog.For(tier).Select(p => new
            {
                interval = p.Interval.ToString().ToLowerInvariant(),
                amountUsd = p.AmountUsd,
                monthlyEquivalentUsd = p.MonthlyEquivalentUsd,
                // Fiyat kimliği yapılandırılmamışsa o düğme ekranda çalışmaz —
                // kullanıcıyı 500 veren bir düğmeye tıklatmak yerine önceden
                // söylüyoruz. (Yıllık fiyatlar Stripe'ta ayrı ayrı kurulmalı.)
                available = !string.IsNullOrWhiteSpace(_config[p.ConfigKey]),
            }),
        });

        return Ok(plans);
    }

    // POST /api/subscription/checkout?plan=pro|team&interval=monthly|yearly
    // Creates a Stripe Hosted Checkout session for the requested plan.
    // Returns { url } — the frontend redirects the user there.
    // Card data NEVER touches our server. Stripe handles PCI-DSS.
    [HttpPost("checkout")]
    public async Task<IActionResult> CreateCheckoutSession(
        [FromQuery] string plan = "pro",
        [FromQuery] string interval = "monthly")
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId!);
        if (user == null) return Unauthorized();

        // If user already has an active subscription, redirect to portal instead —
        // Checkout üzerinden ikinci bir abonelik açmak, aynı kullanıcıya iki kez
        // fatura kesmek olurdu. Plan değişikliği (Pro↔Team, aylık↔yıllık) portal üzerinden.
        if (user.SubscriptionStatus == "active" && !string.IsNullOrEmpty(user.StripeCustomerId))
            return Ok(new { redirect = "portal" });

        // Bilinmeyen bir plan adı sessizce Pro'ya düşmüyor: kullanıcı Team'e
        // tıklayıp yanlışlıkla Pro'ya abone olurdu ve bunu ancak faturada fark ederdi.
        // Aynı gerekçe dönem için de geçerli — "yearly" yazıp aylık ödemek daha da kötü.
        var tier = PricingCatalog.ParseTier(plan);
        if (tier is null)
            return BadRequest(new { error = $"Unknown plan '{plan}'. Use 'pro' or 'team'." });

        var billing = PricingCatalog.ParseInterval(interval);
        if (billing is null)
            return BadRequest(new { error = $"Unknown billing interval '{interval}'. Use 'monthly' or 'yearly'." });

        var price = PricingCatalog.Find(tier.Value, billing.Value);
        if (price is null)
            return BadRequest(new { error = $"The {plan} plan is not sold on a {interval} basis." });

        var priceId = _config[price.ConfigKey];
        if (string.IsNullOrWhiteSpace(priceId))
            return StatusCode(500, new { error = $"Stripe price ID for '{plan}' ({interval}) is not configured." });

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
                { "namines_username", user.UserName ?? "" },
                { "namines_plan", plan },
                { "namines_interval", interval }
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
