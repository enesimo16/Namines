using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Namines.BuildingBlocks.Contracts;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;
using Stripe;
using Stripe.Checkout;
// Stripe SDK'sında da PlanTier var; kendi katman enum'umuzu takma adla alıyoruz.
using PlanTier = Namines.Core.Analysis.PlanTier;

namespace Namines.Billing.Application;

/// <summary>
/// Ödeme sayfası ve portal oturumları. Kart verisi hiçbir zaman sunucuya gelmez: Stripe'ın barındırdığı sayfa
/// (PCI-DSS) açılır, kullanıcı oraya yönlendirilir.
/// </summary>
internal sealed class BillingSubscriptionService : ISubscriptionService
{
    private readonly IBillingData _db;
    private readonly IIdentityDirectory _identity;
    private readonly IConfiguration _config;
    private readonly PlanResolver _plans;

    public BillingSubscriptionService(IBillingData db, IIdentityDirectory identity, IConfiguration config)
    {
        _db = db;
        _identity = identity;
        _config = config;
        _plans = new PlanResolver(db, identity);
    }

    /// <summary>
    /// Satılan planlar ve fiyatları (anonim görülebilir: ürünün ne kadar tuttuğunu öğrenmek için hesap açmak gerekmesin).
    /// Fiyat kimliği yapılandırılmamışsa o düğme çalışmaz: kullanıcıyı 500 veren düğmeye tıklatmak yerine önceden söylenir.
    /// </summary>
    public IReadOnlyList<PlanOffer> GetPlans() =>
        new[] { PlanTier.Pro, PlanTier.Team }.Select(tier => new PlanOffer(
            tier.ToString().ToLowerInvariant(),
            PricingCatalog.YearlyDiscountPercent(tier) ?? 0,
            PricingCatalog.For(tier).Select(p => new PlanOfferPrice(
                p.Interval.ToString().ToLowerInvariant(), p.AmountUsd, p.MonthlyEquivalentUsd,
                !string.IsNullOrWhiteSpace(_config[p.ConfigKey]))).ToList())).ToList();

    public async Task<CheckoutResult> CreateCheckoutAsync(string userId, string plan, string interval, CancellationToken ct = default)
    {
        var facts = await _identity.GetUserFactsAsync(userId, ct);
        if (facts is null) return new CheckoutResult(401);

        var subscription = await GetAsync(userId, ct);

        // Etkin aboneliği olan kullanıcı portala yönlendirilir: Checkout ile ikinci abonelik açmak aynı kişiye iki kez
        // fatura kesmek olurdu. Plan değişikliği (Pro↔Team, aylık↔yıllık) portal üzerinden.
        if (subscription.Status == "active" && subscription.StripeCustomerId is not null)
            return new CheckoutResult(200, Redirect: "portal");

        // Bilinmeyen plan adı sessizce Pro'ya düşmez: kullanıcı Team'e tıklayıp yanlışlıkla Pro'ya abone olurdu.
        var tier = PricingCatalog.ParseTier(plan);
        if (tier is null)
            return new CheckoutResult(400, Error: $"Unknown plan '{plan}'. Use 'pro' or 'team'.");

        var billing = PricingCatalog.ParseInterval(interval);
        if (billing is null)
            return new CheckoutResult(400, Error: $"Unknown billing interval '{interval}'. Use 'monthly' or 'yearly'.");

        var price = PricingCatalog.Find(tier.Value, billing.Value);
        if (price is null)
            return new CheckoutResult(400, Error: $"The {plan} plan is not sold on a {interval} basis.");

        var priceId = _config[price.ConfigKey];
        if (string.IsNullOrWhiteSpace(priceId))
            return new CheckoutResult(500, Error: $"Stripe price ID for '{plan}' ({interval}) is not configured.");

        var frontendUrl = _config["App:FrontendUrl"] ?? "http://localhost:3000";
        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            PaymentMethodTypes = new List<string> { "card" },
            LineItems = new List<SessionLineItemOptions> { new() { Price = priceId, Quantity = 1 } },
            // Webhook kimin ödediğini bu kimlikten bilir.
            ClientReferenceId = userId,
            CustomerEmail = subscription.StripeCustomerId is null ? facts.Email : null,
            Customer = subscription.StripeCustomerId,
            SuccessUrl = $"{frontendUrl}/?upgrade=success",
            CancelUrl = $"{frontendUrl}/?upgrade=canceled",
            Metadata = new Dictionary<string, string>
            {
                { "namines_user_id", userId },
                { "namines_username", facts.UserName ?? "" },
                { "namines_plan", plan },
                { "namines_interval", interval },
            },
        };

        var session = await new SessionService(Client()).CreateAsync(options, cancellationToken: ct);
        return new CheckoutResult(200, Url: session.Url);
    }

    public async Task<CheckoutResult> CreatePortalAsync(string userId, CancellationToken ct = default)
    {
        if (await _identity.GetUserFactsAsync(userId, ct) is null) return new CheckoutResult(401);

        var subscription = await GetAsync(userId, ct);
        if (subscription.StripeCustomerId is null)
            return new CheckoutResult(400, Error: "No active subscription found. Please upgrade to Pro first.");

        var frontendUrl = _config["App:FrontendUrl"] ?? "http://localhost:3000";
        var options = new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = subscription.StripeCustomerId,
            ReturnUrl = $"{frontendUrl}/",
        };
        var session = await new Stripe.BillingPortal.SessionService(Client()).CreateAsync(options, cancellationToken: ct);
        return new CheckoutResult(200, Url: session.Url);
    }

    public async Task<SubscriptionStatusView?> GetStatusAsync(string userId, CancellationToken ct = default)
    {
        var facts = await _identity.GetUserFactsAsync(userId, ct);
        if (facts is null) return null;

        var subscription = await GetAsync(userId, ct);
        var tier = PlanQuotas.Resolve(subscription.Status, subscription.PlanCode, facts.IsDev);
        var unlimited = facts.IsDev || tier == PlanTier.Dev;

        return new SubscriptionStatusView(
            unlimited ? "active" : (subscription.Status ?? "none"),
            tier.ToString(),
            unlimited || facts.Type == UserType.Corporate || tier != PlanTier.Free,
            unlimited,
            subscription.CurrentPeriodEnd,
            subscription.StripeCustomerId is not null);
    }

    private IStripeClient Client()
    {
        var key = _config["Stripe:SecretKey"];
        return new StripeClient(key);
    }

    private sealed record Snapshot(string? Status, string? PlanCode, DateTime? CurrentPeriodEnd, string? StripeCustomerId);

    private async Task<Snapshot> GetAsync(string userId, CancellationToken ct)
    {
        var sub = await _db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId, ct);
        return new Snapshot(sub?.Status, sub?.PlanCode, sub?.CurrentPeriodEnd, customer?.StripeCustomerId);
    }
}
