using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace Namines.Tests.Integration;

/// <summary>
/// AI bütçesinin tek sahibi (22 §5).
///
/// <b>Bu testler, kuralın İKİNCİ bir kopyası yazıldığında üç şeyin birden yanlış
/// gitmesinden sonra yazıldı</b> ve üçünü ayrı ayrı kilitliyor: kotanın token
/// sayması, paylaşılan havuza dokunulması ve gün sınırının TR saatine göre
/// olması. Hiçbiri o zaman testlerde görünmemişti.
///
/// Gerçek PostgreSQL'e karşı: <c>ExecuteUpdate</c> ile yapılan atomik artış
/// bellek içi sağlayıcıda aynı şeyi kanıtlamaz.
/// </summary>
[Collection("Docker")]
public class AiQuotaServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private DbContextOptions<AuthDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        if (!DockerAvailable.Value) return;
        await _container.StartAsync();

        _options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(_container.GetConnectionString()).Options;

        await using var context = new AuthDbContext(_options);
        await context.Database.MigrateAsync();
        context.Users.Add(new ApplicationUser { Id = "u1", UserName = "u1" });
        context.Users.Add(new ApplicationUser { Id = "owner", UserName = "owner", IsDev = true });
        // Team plani: aktif abonelik + PlanCode "team".
        context.Users.Add(new ApplicationUser
        {
            Id = "t1", UserName = "t1", SubscriptionStatus = "active", PlanCode = "team",
        });
        context.Users.Add(new ApplicationUser
        {
            Id = "t2", UserName = "t2", SubscriptionStatus = "active", PlanCode = "team",
        });
        await context.SaveChangesAsync();
    }

    public Task DisposeAsync() => DockerAvailable.Value ? _container.DisposeAsync().AsTask() : Task.CompletedTask;

    private (AuthDbContext Context, AiQuotaService Service) Service(int perUserCap = 20_000, long pool = 100_000)
    {
        var context = new AuthDbContext(_options);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiPool:PerUserDailyTokens"] = perUserCap.ToString(),
            ["AiPool:DailyTokenPool"] = pool.ToString(),
        }).Build();

        return (context, new AiQuotaService(context, configuration));
    }

    private async Task ResetAsync()
    {
        await using var context = new AuthDbContext(_options);
        await context.UserAIQuotas.ExecuteDeleteAsync();
        await context.GlobalAiUsages.ExecuteDeleteAsync();
    }

    // ── Token birimi ─────────────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task The_counter_measures_tokens_not_calls()
    {
        // Kopyanın en pahalı hatası buydu: çağrı başına 1 artırınca 20.000'lik
        // tavan pratikte hiç dolmuyordu.
        await ResetAsync();
        var (context, service) = Service();
        await using var _ = context;

        await service.EnsureQuotaAsync("u1");
        await service.ConsumeAsync("u1", 2500);

        var quota = await context.UserAIQuotas.SingleAsync(q => q.UserId == "u1");
        Assert.Equal(2500, quota.DailyUsageCount);
    }

    [RequiresDockerFact]
    public async Task Spending_also_draws_from_the_shared_pool()
    {
        // Kopya havuza hiç dokunmuyordu; paylaşılan bütçe ölçülemez hâle geliyordu.
        await ResetAsync();
        var (context, service) = Service();
        await using var _ = context;

        await service.EnsureQuotaAsync("u1");
        await service.ConsumeAsync("u1", 3000);

        var today = DateTime.UtcNow.Date;
        var used = await context.GlobalAiUsages.Where(g => g.Date == today).Select(g => g.TokensUsed).SingleAsync();
        Assert.Equal(3000, used);
    }

    [RequiresDockerFact]
    public async Task The_first_spend_of_the_day_creates_the_pool_row()
    {
        // Satır yoksa harcamayı sessizce kaybetmek, havuzu anlamsız kılardı.
        await ResetAsync();
        var (context, service) = Service();
        await using var _ = context;

        await service.EnsureQuotaAsync("u1");
        await service.ConsumeAsync("u1", 500);
        await service.ConsumeAsync("u1", 500);

        var today = DateTime.UtcNow.Date;
        Assert.Equal(1000, await context.GlobalAiUsages.Where(g => g.Date == today).Select(g => g.TokensUsed).SingleAsync());
    }

    // ── Kapılar ──────────────────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task A_user_over_their_cap_is_refused()
    {
        await ResetAsync();
        var (context, service) = Service(perUserCap: 3000);
        await using var _ = context;

        await service.EnsureQuotaAsync("u1");
        await service.ConsumeAsync("u1", 2500);

        Assert.Equal(AiQuotaDecision.UserExhausted, await service.CheckAsync("u1", 1000));
        Assert.Equal(AiQuotaDecision.Allowed, await service.CheckAsync("u1", 400));
    }

    [RequiresDockerFact]
    public async Task An_exhausted_pool_is_reported_separately_from_a_full_user()
    {
        // İkisi farklı sebep: biri "bugünlük paylaşılan bütçe bitti", diğeri
        // "senin hakkın bitti". Aynı mesajı vermek kullanıcıya yanlış şey yaptırır.
        await ResetAsync();
        var (context, service) = Service(perUserCap: 20_000, pool: 1000);
        await using var _ = context;

        await service.EnsureQuotaAsync("u1");
        await service.ConsumeAsync("u1", 900);

        Assert.Equal(AiQuotaDecision.PoolExhausted, await service.CheckAsync("u1", 500));
    }

    [RequiresDockerFact]
    public async Task Checking_does_not_spend()
    {
        // Kontrol ile harcamayı ayırmak, başarısız bir çağrının bütçeden
        // düşmemesini sağlıyor.
        await ResetAsync();
        var (context, service) = Service();
        await using var _ = context;

        await service.EnsureQuotaAsync("u1");
        await service.CheckAsync("u1", 5000);
        await service.CheckAsync("u1", 5000);

        Assert.Equal(0, (await context.UserAIQuotas.SingleAsync(q => q.UserId == "u1")).DailyUsageCount);
    }

    // ── Gün sınırı ───────────────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task Yesterdays_usage_does_not_close_today()
    {
        await ResetAsync();
        var (context, service) = Service();
        await using var _ = context;

        var quota = await service.EnsureQuotaAsync("u1");
        quota.DailyUsageCount = 19_000;
        quota.LastResetDate = DateTime.UtcNow.AddDays(-2);
        await context.SaveChangesAsync();

        var refreshed = await service.EnsureQuotaAsync("u1");
        Assert.Equal(0, refreshed.DailyUsageCount);
    }

    [RequiresDockerFact]
    public async Task The_day_boundary_follows_Turkish_time_not_utc()
    {
        // Middleware TR saatine (UTC+3) göre sıfırlıyordu, kopya UTC'ye göre.
        // Aynı sayaç bazı saatlerde sıfırlanmış bazı saatlerde sıfırlanmamış
        // görünüyordu. UTC 22:00, TR'de ertesi gün 01:00'dir.
        await ResetAsync();
        var (context, service) = Service();
        await using var _ = context;

        var quota = await service.EnsureQuotaAsync("u1");
        quota.DailyUsageCount = 5000;
        // Aynı UTC gününde ama TR gününde DÜN: UTC 20:00 → TR 23:00 (dün),
        // şimdi UTC 22:00 → TR 01:00 (bugün).
        quota.LastResetDate = DateTime.UtcNow.Date.AddHours(20).AddDays(-1);
        await context.SaveChangesAsync();

        Assert.Equal(0, (await service.EnsureQuotaAsync("u1")).DailyUsageCount);
    }

    [RequiresDockerFact]
    public async Task The_cap_is_normalised_to_the_current_configuration()
    {
        // Eski yüzde-tabanlı satırlar (DailyLimit = 100) token tavanıyla
        // karıştırılırsa kullanıcı 100 token sonra kesilirdi.
        await ResetAsync();
        await using (var seed = new AuthDbContext(_options))
        {
            seed.UserAIQuotas.Add(new UserAIQuota { UserId = "u1", DailyLimit = 100, DailyUsageCount = 0 });
            await seed.SaveChangesAsync();
        }

        var (context, service) = Service(perUserCap: 20_000);
        await using var _ = context;

        Assert.Equal(20_000, (await service.EnsureQuotaAsync("u1")).DailyLimit);
    }

    [RequiresDockerFact]
    public async Task Spending_never_pushes_the_counter_past_the_cap()
    {
        // Tavanı aşan bir değer, ertesi günün bütçesini de yemiş gibi görünürdü.
        await ResetAsync();
        var (context, service) = Service(perUserCap: 1000);
        await using var _ = context;

        await service.EnsureQuotaAsync("u1");
        await service.ConsumeAsync("u1", 900);
        await service.ConsumeAsync("u1", 900);

        Assert.Equal(1000, (await context.UserAIQuotas.SingleAsync(q => q.UserId == "u1")).DailyUsageCount);
    }

    // ── Sahip hesabı ─────────────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task The_owner_passes_even_when_the_shared_pool_is_empty()
    {
        // Havuz "ücretsiz kullanıcılar toplamda şu kadar harcasın" demek;
        // geliştiricinin kendi ürününü deneyemez hâle gelmesi değil. Sıradan
        // kullanıcı aynı anda reddediliyor — bu, kontrolün gerçekten çalıştığını
        // ve testin boş yere geçmediğini gösteriyor.
        await ResetAsync();
        var (context, service) = Service(pool: 1000);
        await using var _ = context;

        await service.ConsumeAsync("u1", 5000);

        Assert.Equal(AiQuotaDecision.PoolExhausted, await service.CheckAsync("u1", 10));
        Assert.Equal(AiQuotaDecision.Allowed, await service.CheckAsync("owner", 10));
    }

    [RequiresDockerFact]
    public async Task The_owner_passes_even_after_spending_more_than_any_plan_allows()
    {
        await ResetAsync();
        var (context, service) = Service();
        await using var _ = context;

        await service.EnsureQuotaAsync("owner");
        await service.ConsumeAsync("owner", 5_000_000);

        Assert.Equal(AiQuotaDecision.Allowed, await service.CheckAsync("owner", 1_000_000));
    }

    // ── Team: ortak havuz ────────────────────────────────────────────────────

    /// <summary>Iki Team uyesini ayni organizasyona koyar.</summary>
    private async Task<string> SeedTeamAsync()
    {
        await using var seed = new AuthDbContext(_options);
        var org = await seed.GetOrCreatePersonalOrgAsync("t1", "t1");
        var already = await seed.OrganizationMembers
            .AnyAsync(m => m.OrganizationId == org.Id && m.UserId == "t2");
        if (!already)
        {
            seed.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = org.Id, UserId = "t2", Role = OrgRole.Editor,
            });
            await seed.SaveChangesAsync();
        }
        return org.Id;
    }

    [RequiresDockerFact]
    public async Task One_teammate_spending_reduces_what_the_other_can_use()
    {
        // Ortak havuzun tanimi bu. Ayri ayri saysaydi uc kisi ayri ayri tavanina
        // kadar harcayip ekibin toplam butcesini katlardi.
        await ResetAsync();
        await SeedTeamAsync();
        var (context, service) = Service();
        await using var _ = context;

        await service.EnsureQuotaAsync("t1");
        await service.ConsumeAsync("t1", 600_000);   // havuzun tamami

        Assert.Equal(AiQuotaDecision.TeamExhausted, await service.CheckAsync("t2", 1_000));
    }

    [RequiresDockerFact]
    public async Task A_teammate_can_use_more_than_one_share_when_others_are_idle()
    {
        // Havuzu uyeye bolmemenin tek sebebi bu: bosta duran uyenin payi
        // cope gitmemeli. Uye basi tavan payin iki kati (400K).
        await ResetAsync();
        await SeedTeamAsync();
        var (context, service) = Service();
        await using var _ = context;

        Assert.Equal(400_000, await service.PerUserCapAsync("t1"));

        await service.EnsureQuotaAsync("t1");
        // Tek payindan (200K) fazlasi -- havuzda yer oldugu icin gecmeli.
        Assert.Equal(AiQuotaDecision.Allowed, await service.CheckAsync("t1", 300_000));
    }

    [RequiresDockerFact]
    public async Task No_single_teammate_can_drain_the_whole_pool()
    {
        // Ortak havuzun bilinen zayifligi aclik. Uye basi tavan bunu kesiyor:
        // havuzda yer olsa bile bir kisi hepsini alamiyor.
        await ResetAsync();
        await SeedTeamAsync();
        var (context, service) = Service();
        await using var _ = context;

        await service.EnsureQuotaAsync("t1");

        Assert.Equal(AiQuotaDecision.UserExhausted, await service.CheckAsync("t1", 600_000));
    }

    [RequiresDockerFact]
    public async Task A_paying_customer_is_not_blocked_by_the_free_tier_pool()
    {
        // Bu testin varlik sebebi: paylasilan havuz 100.000/gun idi ve HERKESI
        // bagliyordu. Pro'ya 200.000, bir Team'e 600.000 satiliyordu -- yani
        // parasini odemis musteri, ucretsiz kullanicilarin tukettigi bir tavana
        // takilip aldigi hakki hic kullanamiyordu.
        await ResetAsync();
        await SeedTeamAsync();
        var (context, service) = Service(pool: 1_000);   // havuz bilerek minicik
        await using var _ = context;

        await service.EnsureQuotaAsync("u1");
        await service.ConsumeAsync("u1", 5_000);          // havuz tasti

        // Ucretsiz kullanici durdu...
        Assert.Equal(AiQuotaDecision.PoolExhausted, await service.CheckAsync("u1", 10));
        // ...ama odeyen musteri etkilenmedi.
        Assert.Equal(AiQuotaDecision.Allowed, await service.CheckAsync("t1", 10));
    }

    [RequiresDockerFact]
    public async Task The_owner_spending_is_still_recorded()
    {
        // Sınırsız olmak, maliyetin görünmez olması anlamına gelmemeli —
        // ölçülemeyen bir harcama, faturayı görene kadar fark edilmez.
        await ResetAsync();
        var (context, service) = Service();
        await using var _ = context;

        await service.EnsureQuotaAsync("owner");
        await service.ConsumeAsync("owner", 4321);

        var today = DateTime.UtcNow.Date;
        Assert.Equal(4321, await context.GlobalAiUsages
            .Where(g => g.Date == today).Select(g => g.TokensUsed).SingleAsync());
    }
}
