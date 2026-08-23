using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
}
