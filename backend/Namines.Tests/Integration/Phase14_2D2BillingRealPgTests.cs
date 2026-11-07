using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Namines.Billing.Application;
using Namines.Billing.Domain;
using Namines.BuildingBlocks.Contracts;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;
using Namines.Identity.Infrastructure.Persistence;
using Namines.Tests.TestSupport;
using Npgsql;
using Xunit;
using PlanTier = Namines.Core.Analysis.PlanTier;

namespace Namines.Tests.Integration;

/// <summary>
/// Faz 14.2D-2 faturalama, gerçek PostgreSQL'de: eşzamanlı kota tüketiminde aşım olmaması (kullanıcı tavanı, paylaşılan
/// Free havuzu, ekip havuzu) ve Stripe webhook'unun eşzamanlı tekrar teslimatta TEK etki bırakması. Her çağrı KENDİ
/// bağlamını kullanır: tek bağlamı paylaşmak eşzamanlılığı değil EF'in thread-safe olmamasını sınardı.
/// </summary>
[Collection("Docker")]
public sealed class Phase14_2D2BillingRealPgTests : IClassFixture<Phase14_2BPostgresFixture>
{
    private readonly Phase14_2BPostgresFixture _pg;

    public Phase14_2D2BillingRealPgTests(Phase14_2BPostgresFixture pg) => _pg = pg;

    private static AuthDbContext Open(string cs) =>
        new(new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(cs).Options);

    private async Task<string> NewDatabaseAsync(string name, params string[] users)
    {
        // Önceki testlerin boştaki bağlantıları sunucunun bağlantı sınırını yemesin (eşzamanlı testler çok bağlantı açar).
        PostgresTestPools.Reset();
        var cs = await _pg.CreateDatabaseAsync(name);
        await using var db = Open(cs);
        await db.MigrateAllModulesAsync();
        foreach (var id in users) db.Users.Add(new ApplicationUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant() });
        await db.SaveChangesAsync();
        return cs;
    }

    private static IConfiguration QuotaConfig(int perUserCap, long pool) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiPool:PerUserDailyTokens"] = perUserCap.ToString(),
            ["AiPool:DailyTokenPool"] = pool.ToString(),
        }).Build();

    /// <summary>Hepsi aynı anda başlasın diye kapıda bekler; her çağrı kendi bağlamı ve kendi servisiyle çalışır.</summary>
    private static async Task<AiQuotaDecision[]> ReserveTogetherAsync(string cs, IConfiguration config, IReadOnlyList<string> userIds, int tokens)
    {
        var start = new TaskCompletionSource();
        var tasks = userIds.Select(async userId =>
        {
            await start.Task;
            await using var db = Open(cs);
            return await TestModules.For(db).AiQuota(config).TryReserveAsync(userId, tokens);
        }).ToArray();
        start.SetResult();
        return await Task.WhenAll(tasks);
    }

    private static async Task<long> PoolUsedAsync(string cs)
    {
        await using var db = Open(cs);
        return await db.Billing().GlobalAiUsages.SumAsync(g => g.TokensUsed);
    }

    // ── Kota: eşzamanlı rezervasyon ──────────────────────────────────────────

    [RequiresDockerFact]
    public async Task One_user_cap_is_never_exceeded_and_losers_are_told_user_exhausted()
    {
        var cs = await NewDatabaseAsync("q_user", "u1");
        const int cap = 10_000, perCall = 1_000, attempts = 40;
        // Havuz büyük: yalnızca kullanıcı tavanı sınır. İlk çağrıda kota satırı yok, oluşturma yarışı da sınanır.
        var config = QuotaConfig(cap, 100_000_000);

        var decisions = await ReserveTogetherAsync(cs, config, Enumerable.Repeat("u1", attempts).ToList(), perCall);

        var allowed = decisions.Count(d => d == AiQuotaDecision.Allowed);
        Assert.Equal(cap / perCall, allowed);
        Assert.All(decisions.Where(d => d != AiQuotaDecision.Allowed), d => Assert.Equal(AiQuotaDecision.UserExhausted, d));

        await using var verify = Open(cs);
        Assert.Equal(cap, (await verify.Billing().UserAIQuotas.SingleAsync(q => q.UserId == "u1")).DailyUsageCount);
        // Kullanıcı adımı havuz adımından SONRA: reddedilenlerin havuz artışı geri alınmış olmalı (tümü ya da hiçbiri).
        Assert.Equal(cap, await PoolUsedAsync(cs));
    }

    [RequiresDockerFact]
    public async Task The_free_pool_is_never_overspent_across_many_users_and_losers_are_told_pool_exhausted()
    {
        const int users = 24, perCall = 1_000, pool = 10_000;
        var ids = Enumerable.Range(0, users).Select(i => "f" + i).ToList();
        var cs = await NewDatabaseAsync("q_pool", ids.ToArray());
        // Kullanıcı tavanı çağrıdan büyük, ama havuz yalnızca 10 çağrıya yeter.
        var config = QuotaConfig(20_000, pool);

        var decisions = await ReserveTogetherAsync(cs, config, ids, perCall);

        var allowed = decisions.Count(d => d == AiQuotaDecision.Allowed);
        Assert.Equal(pool / perCall, allowed);
        Assert.All(decisions.Where(d => d != AiQuotaDecision.Allowed), d => Assert.Equal(AiQuotaDecision.PoolExhausted, d));
        Assert.Equal(pool, await PoolUsedAsync(cs));

        await using var verify = Open(cs);
        var counters = await verify.Billing().UserAIQuotas.AsNoTracking().ToListAsync();
        // Tutarlılık: izin verilen kadar kullanıcı sayacı düşülmüş, reddedilenin sayacı sıfır (bedava ya da kayıp harcama yok).
        Assert.Equal(pool, counters.Sum(q => (long)q.DailyUsageCount));
        Assert.Equal(allowed, counters.Count(q => q.DailyUsageCount == perCall));
    }

    [RequiresDockerFact]
    public async Task The_team_pool_is_never_overspent_and_a_rejected_member_leaves_no_partial_write()
    {
        var team = PlanQuotas.For(PlanTier.Team);
        var limit = (long)team.DailyAiTokens * team.TeamSeats;
        const int members = 12;
        var perCall = (int)(limit / 6); // havuz tam altı çağrıya yeter; üye tavanı (koltuk payının iki katı) çağrıdan büyük
        var ids = Enumerable.Range(0, members).Select(i => "t" + i).ToList();
        var cs = await NewDatabaseAsync("q_team", ids.ToArray());

        await using (var seed = Open(cs))
        {
            seed.Organizations.Add(new Organization { Id = "team-org", Name = "Team", IsPersonal = false, CreatedByUserId = ids[0] });
            await seed.SaveChangesAsync();
            seed.OrganizationMembers.AddRange(ids.Select(id => new OrganizationMember { OrganizationId = "team-org", UserId = id, Role = OrgRole.Editor }));
            await seed.SaveChangesAsync();
            foreach (var id in ids) await seed.SeedSubscriptionAsync(id, "active", "team");
        }

        var decisions = await ReserveTogetherAsync(cs, QuotaConfig(20_000, 100_000_000), ids, perCall);

        Assert.Equal(6, decisions.Count(d => d == AiQuotaDecision.Allowed));
        Assert.All(decisions.Where(d => d != AiQuotaDecision.Allowed), d => Assert.Equal(AiQuotaDecision.TeamExhausted, d));

        await using var verify = Open(cs);
        var teamUsed = await verify.Billing().OrgAiUsages.Where(o => o.OrganizationId == "team-org").SumAsync(o => o.TokensUsed);
        Assert.Equal(6L * perCall, teamUsed);
        Assert.True(teamUsed <= limit);
        // Ekip adımı sonda: reddedilen üyelerin havuz ve kullanıcı artışları geri alınmış olmalı.
        Assert.Equal(6L * perCall, await PoolUsedAsync(cs));
        var counters = await verify.Billing().UserAIQuotas.AsNoTracking().ToListAsync();
        Assert.Equal(6L * perCall, counters.Sum(q => (long)q.DailyUsageCount));
        Assert.Equal(6, counters.Count(q => q.DailyUsageCount == perCall));
    }

    [RequiresDockerFact]
    public async Task A_rejected_reservation_leaves_every_counter_untouched()
    {
        var cs = await NewDatabaseAsync("q_atomic", "u1");
        var config = QuotaConfig(3_000, 100_000_000);

        Assert.Equal(AiQuotaDecision.Allowed, await Reserve(cs, config, "u1", 2_000));
        Assert.Equal(AiQuotaDecision.UserExhausted, await Reserve(cs, config, "u1", 2_000));

        await using var verify = Open(cs);
        Assert.Equal(2_000, (await verify.Billing().UserAIQuotas.SingleAsync()).DailyUsageCount);
        Assert.Equal(2_000, await PoolUsedAsync(cs));

        // Refund eksiye düşmez: aynı rezervasyon iki kez iade edilse bile bedava hak doğmaz.
        await using var refundDb = Open(cs);
        var quota = TestModules.For(refundDb).AiQuota(config);
        await quota.RefundAsync("u1", 2_000);
        await quota.RefundAsync("u1", 2_000);
        Assert.Equal(0, (await refundDb.Billing().UserAIQuotas.AsNoTracking().SingleAsync()).DailyUsageCount);
        Assert.Equal(0, await PoolUsedAsync(cs));
    }

    private static async Task<AiQuotaDecision> Reserve(string cs, IConfiguration config, string userId, int tokens)
    {
        await using var db = Open(cs);
        return await TestModules.For(db).AiQuota(config).TryReserveAsync(userId, tokens);
    }

    // ── Webhook: imza, gizli anahtar, tekrar teslimat, sıra ──────────────────

    private static string Customer(string n) => "cus_" + n;

    private async Task<string> WebhookDatabaseAsync(string name)
    {
        var cs = await NewDatabaseAsync(name, "u1");
        await using var db = Open(cs);
        await db.Users.Where(u => u.Id == "u1").ExecuteUpdateAsync(s => s.SetProperty(u => u.Type, UserType.Individual));
        return cs;
    }

    private static async Task<(int Status, string Body, string Json)> DeliverAsync(
        string cs, string payload, IStripeGateway? gateway = null, string? signature = null, IConfiguration? config = null, bool unsigned = false)
    {
        await using var db = Open(cs);
        var service = KitBStripe.Service(db, config ?? KitBStripe.Config(), gateway);
        var header = unsigned ? null : signature ?? KitBStripe.SignatureHeader(payload, DateTime.UtcNow);
        var result = await KitBStripe.Controller(service, payload, header).HandleStripeWebhook(default);
        return (KitBStripe.StatusOf(result), KitBStripe.BodyOf(result), JsonSerializer.Serialize(((Microsoft.AspNetCore.Mvc.ObjectResult)result).Value));
    }

    [RequiresDockerFact]
    public async Task Concurrent_duplicate_deliveries_of_one_event_have_exactly_one_effect()
    {
        var cs = await WebhookDatabaseAsync("w_dup");
        var payload = KitBStripe.CheckoutCompletedJson("evt_dup", DateTime.UtcNow.AddMinutes(-1), Customer("dup"), "u1", "sub_dup");
        var gateway = new FakeStripeGateway(periodEnd: DateTime.UtcNow.AddDays(30));
        const int deliveries = 12;

        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, deliveries).Select(async _ =>
        {
            await start.Task;
            return await DeliverAsync(cs, payload, gateway);
        }).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(200, r.Status));
        // Tam bir teslimat işledi, kalanı "duplicate" dedi; ve Stripe'a yalnızca bir kez gidildi.
        Assert.Equal(1, results.Count(r => !r.Json.Contains("duplicate")));
        Assert.Equal(deliveries - 1, results.Count(r => r.Json.Contains("duplicate")));
        Assert.Equal(1, gateway.Calls);

        await using var verify = Open(cs);
        var events = await verify.Billing().StripeWebhookEvents.AsNoTracking().ToListAsync();
        Assert.Equal("evt_dup", Assert.Single(events).Id);
        Assert.Equal(Customer("dup"), (await verify.Billing().Customers.AsNoTracking().SingleAsync(c => c.UserId == "u1")).StripeCustomerId);
        var subscription = await verify.Billing().Subscriptions.AsNoTracking().SingleAsync(s => s.UserId == "u1");
        Assert.Equal(("active", "sub_dup"), (subscription.Status, subscription.StripeSubscriptionId));
        Assert.Equal(UserType.Corporate, (await verify.Users.AsNoTracking().SingleAsync(u => u.Id == "u1")).Type);
    }

    [RequiresDockerFact]
    public async Task A_late_older_event_does_not_undo_a_newer_cancellation_and_is_recorded_as_stale()
    {
        var cs = await WebhookDatabaseAsync("w_order");
        await using (var seed = Open(cs))
            await seed.SeedSubscriptionAsync("u1", "active", "pro", "sub_1", Customer("order"));

        var newerCancel = KitBStripe.SubscriptionEventJson("evt_cancel", "customer.subscription.deleted", "canceled", DateTime.UtcNow.AddMinutes(-5), Customer("order"));
        var olderActive = KitBStripe.SubscriptionEventJson("evt_old_active", "customer.subscription.updated", "active", DateTime.UtcNow.AddMinutes(-30), Customer("order"));

        Assert.Equal(200, (await DeliverAsync(cs, newerCancel)).Status);
        var late = await DeliverAsync(cs, olderActive);

        Assert.Equal(200, late.Status);
        Assert.Contains("stale", late.Json);
        await using var verify = Open(cs);
        Assert.Equal("canceled", (await verify.Billing().Subscriptions.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(UserType.Individual, (await verify.Users.AsNoTracking().SingleAsync(u => u.Id == "u1")).Type);
        var rows = await verify.Billing().StripeWebhookEvents.AsNoTracking().ToDictionaryAsync(e => e.Id);
        Assert.True(rows["evt_cancel"].LatestProcessedForCustomer);
        Assert.False(rows["evt_old_active"].LatestProcessedForCustomer);

        // Aynı eski olayın yeniden teslimi: artık duplicate.
        Assert.Contains("duplicate", (await DeliverAsync(cs, olderActive)).Json);
    }

    [RequiresDockerFact]
    public async Task Invalid_missing_stale_and_tampered_signatures_are_rejected_with_400_and_store_nothing()
    {
        var cs = await WebhookDatabaseAsync("w_sig");
        var payload = KitBStripe.CheckoutCompletedJson("evt_sig", DateTime.UtcNow, Customer("sig"), "u1", "sub_sig");
        var other = KitBStripe.CheckoutCompletedJson("evt_other", DateTime.UtcNow, Customer("sig"), "u1", "sub_sig");

        Assert.Equal(400, (await DeliverAsync(cs, payload, signature: "t=1,v1=deadbeef")).Status);
        // Başlık hiç yok: 400 (Stripe.net null başlıkta patlardı, servis artık önceden reddeder).
        Assert.Equal(400, (await DeliverAsync(cs, payload, unsigned: true)).Status);
        // Başka gizli anahtarla imzalı.
        Assert.Equal(400, (await DeliverAsync(cs, payload, signature: KitBStripe.SignatureHeader(payload, DateTime.UtcNow, "whsec_other"))).Status);
        // Doğru anahtar, ama başka bir gövdenin imzası: gövde kurcalanmış.
        Assert.Equal(400, (await DeliverAsync(cs, payload, signature: KitBStripe.SignatureHeader(other, DateTime.UtcNow))).Status);
        // Tolerans penceresi dışında (tekrar oynatma): geçerli imza ama on dakika eski.
        Assert.Equal(400, (await DeliverAsync(cs, payload, signature: KitBStripe.SignatureHeader(payload, DateTime.UtcNow.AddMinutes(-10)))).Status);

        await using var verify = Open(cs);
        Assert.Empty(await verify.Billing().StripeWebhookEvents.ToListAsync());
        Assert.Empty(await verify.Billing().Subscriptions.ToListAsync());
    }

    [RequiresDockerFact]
    public async Task A_missing_webhook_secret_rejects_every_call_with_500()
    {
        var cs = await WebhookDatabaseAsync("w_nosecret");
        var payload = KitBStripe.CheckoutCompletedJson("evt_ns", DateTime.UtcNow, Customer("ns"), "u1", "sub_ns");
        var gateway = new FakeStripeGateway();

        // İmza geçerli olsa bile: gizli anahtar yoksa doğrulama yapılamaz, hiçbir şey kabul edilmez.
        Assert.Equal(500, (await DeliverAsync(cs, payload, gateway, config: KitBStripe.Config(secret: null))).Status);
        Assert.Equal(500, (await DeliverAsync(cs, payload, gateway, config: KitBStripe.Config(secret: "   "))).Status);

        Assert.Equal(0, gateway.Calls);
        await using var verify = Open(cs);
        Assert.Empty(await verify.Billing().StripeWebhookEvents.ToListAsync());
    }

    [RequiresDockerFact]
    public async Task A_failing_stripe_lookup_rolls_the_record_back_and_the_retry_is_processed()
    {
        var cs = await WebhookDatabaseAsync("w_retry");
        var payload = KitBStripe.CheckoutCompletedJson("evt_retry", DateTime.UtcNow.AddMinutes(-1), Customer("retry"), "u1", "sub_retry");
        var gateway = new FakeStripeGateway { FailFirst = 1 };

        Assert.Equal(500, (await DeliverAsync(cs, payload, gateway)).Status);
        await using (var mid = Open(cs))
        {
            // Kayıt ile etki birlikte geri alındı: "zaten işlendi" diye yutulmaz.
            Assert.Empty(await mid.Billing().StripeWebhookEvents.ToListAsync());
            Assert.Empty(await mid.Billing().Customers.ToListAsync());
        }

        var retry = await DeliverAsync(cs, payload, gateway);
        Assert.Equal(200, retry.Status);
        Assert.DoesNotContain("duplicate", retry.Json);
        Assert.Contains("duplicate", (await DeliverAsync(cs, payload, gateway)).Json);

        await using var verify = Open(cs);
        Assert.Equal("active", (await verify.Billing().Subscriptions.AsNoTracking().SingleAsync()).Status);
    }

    [RequiresDockerFact]
    public async Task Unhandled_event_types_are_accepted_and_recorded_without_any_effect()
    {
        var cs = await WebhookDatabaseAsync("w_unhandled");
        var payload = KitBStripe.SubscriptionEventJson("evt_inv", "customer.subscription.paused", "paused", DateTime.UtcNow, Customer("unh"));

        var result = await DeliverAsync(cs, payload);

        Assert.Equal(200, result.Status);
        await using var verify = Open(cs);
        Assert.Equal("evt_inv", (await verify.Billing().StripeWebhookEvents.AsNoTracking().SingleAsync()).Id);
        Assert.Empty(await verify.Billing().Subscriptions.ToListAsync());
    }

    [RequiresDockerFact]
    public async Task Checkout_for_an_unknown_user_is_accepted_but_changes_nothing()
    {
        var cs = await WebhookDatabaseAsync("w_ghost");
        var payload = KitBStripe.CheckoutCompletedJson("evt_ghost", DateTime.UtcNow, Customer("ghost"), "nobody", "sub_ghost");
        var gateway = new FakeStripeGateway();

        Assert.Equal(200, (await DeliverAsync(cs, payload, gateway)).Status);

        Assert.Equal(0, gateway.Calls);
        await using var verify = Open(cs);
        Assert.Empty(await verify.Billing().Customers.ToListAsync());
        Assert.Empty(await verify.Billing().Subscriptions.ToListAsync());
    }

    [RequiresDockerFact]
    public async Task Renewals_map_the_price_to_the_plan_and_past_due_keeps_access_until_the_deletion_event()
    {
        var cs = await WebhookDatabaseAsync("w_plans");
        await using (var seed = Open(cs))
        {
            await seed.SeedSubscriptionAsync("u1", "active", "pro", "sub_1", Customer("plans"));
            await seed.Users.Where(u => u.Id == "u1").ExecuteUpdateAsync(s => s.SetProperty(u => u.Type, UserType.Corporate));
        }
        var config = KitBStripe.Config(extra: new Dictionary<string, string?> { ["Stripe:TeamPriceId"] = "price_team_m" });

        // Pro -> Team (portal üzerinden): plan kodu fiyattan yeniden okunur.
        var upgrade = KitBStripe.SubscriptionEventJson("evt_up", "customer.subscription.updated", "active", DateTime.UtcNow.AddMinutes(-3), Customer("plans"), priceId: "price_team_m");
        Assert.Equal(200, (await DeliverAsync(cs, upgrade, config: config)).Status);
        await using (var a = Open(cs))
            Assert.Equal("team", (await a.Billing().Subscriptions.AsNoTracking().SingleAsync()).PlanCode);

        // Tanınmayan fiyat Pro'ya düşer (daha az hak).
        var unknown = KitBStripe.SubscriptionEventJson("evt_unk", "customer.subscription.updated", "active", DateTime.UtcNow.AddMinutes(-2), Customer("plans"), priceId: "price_unknown");
        Assert.Equal(200, (await DeliverAsync(cs, unknown, config: config)).Status);
        await using (var b = Open(cs))
            Assert.Equal("pro", (await b.Billing().Subscriptions.AsNoTracking().SingleAsync()).PlanCode);

        // Ödeme aksadı: durum past_due, kullanıcı türü düşmez (kesin düşüş deleted olayında).
        var pastDue = KitBStripe.SubscriptionEventJson("evt_pd", "customer.subscription.updated", "past_due", DateTime.UtcNow.AddMinutes(-1), Customer("plans"));
        Assert.Equal(200, (await DeliverAsync(cs, pastDue, config: config)).Status);
        await using (var c = Open(cs))
        {
            Assert.Equal("past_due", (await c.Billing().Subscriptions.AsNoTracking().SingleAsync()).Status);
            Assert.Equal(UserType.Corporate, (await c.Users.AsNoTracking().SingleAsync(u => u.Id == "u1")).Type);
        }
    }

    [RequiresDockerFact]
    public async Task The_usage_overview_lists_every_resource_even_unused_ones_and_defaults_overage_to_off()
    {
        // SUM(decimal) SQLite'ta çevrilemez: toplama davranışı yalnızca gerçek PostgreSQL'de sınanır.
        var cs = await NewDatabaseAsync("u_overview", "u1");
        await using var db = Open(cs);
        var meter = TestModules.For(db).Usage;
        await meter.RecordAsync("u1", UsageResource.AiCall, 7);
        await meter.RecordAsync("u1", UsageResource.DataTransferGigabyte, 0.25m);

        var overview = await meter.GetOverviewAsync("u1");

        Assert.Equal(Enum.GetValues<UsageResource>().Length, overview.Resources.Count);
        Assert.Equal(7m, overview.Resources.Single(r => r.Resource == UsageResource.AiCall).Used);
        Assert.Equal(0.25m, overview.Resources.Single(r => r.Resource == UsageResource.DataTransferGigabyte).Used);
        Assert.Equal(0m, overview.Resources.Single(r => r.Resource == UsageResource.ApiRequest).Used);
        Assert.False(overview.OverageEnabled);
        Assert.Null(overview.MonthlyCapUsd);
        Assert.Equal(UsageMeter.CurrentPeriod(), overview.BillingPeriod);
    }

    [RequiresDockerFact]
    public async Task Two_users_cannot_share_one_stripe_customer()
    {
        var cs = await NewDatabaseAsync("w_unique", "u1", "u2");
        await using var db = Open(cs);
        db.Billing().Customers.Add(new BillingCustomer { UserId = "u1", StripeCustomerId = "cus_same" });
        await db.Billing().SaveChangesAsync();

        db.Billing().Customers.Add(new BillingCustomer { UserId = "u2", StripeCustomerId = "cus_same" });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.Billing().SaveChangesAsync());

        Assert.Equal("23505", ((PostgresException)ex.InnerException!).SqlState);
    }
}
