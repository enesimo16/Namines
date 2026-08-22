using Microsoft.EntityFrameworkCore;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace Namines.Tests.Integration;

/// <summary>
/// Kullanım ölçümü (new-phase/22-BUSINESS-MODEL.md §5) — GERÇEK PostgreSQL'e karşı.
///
/// Gerçek veritabanı şart: ölçümün en kolay yanlış gideceği yer toplama ve dönem
/// sınırı, ikisi de sorgu davranışına bağlı. Bellek içi sağlayıcı `decimal`
/// hassasiyetini ve gruplamayı aynı şekilde uygulamaz — testler geçer, fatura
/// yanlış çıkar.
/// </summary>
[Collection("Docker")]
public class UsageMeterTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private AuthDbContext _context = null!;

    public async Task InitializeAsync()
    {
        if (!DockerAvailable.Value) return;
        await _container.StartAsync();

        _context = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(_container.GetConnectionString()).Options);
        await _context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_context is not null) await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    private async Task<string> UserAsync(string? subscriptionStatus = null)
    {
        var id = Guid.NewGuid().ToString();
        _context.Users.Add(new ApplicationUser
        {
            Id = id, UserName = id, SubscriptionStatus = subscriptionStatus,
        });
        await _context.SaveChangesAsync();
        return id;
    }

    [RequiresDockerFact]
    public async Task Recorded_usage_adds_up_within_the_period()
    {
        var userId = await UserAsync();

        await _context.RecordAsync(userId, UsageResource.AiCall, 3);
        await _context.RecordAsync(userId, UsageResource.AiCall, 2);

        Assert.Equal(5m, await _context.UsedThisPeriodAsync(userId, UsageResource.AiCall));
    }

    [RequiresDockerFact]
    public async Task Usage_with_no_events_is_zero_not_an_error()
    {
        // SUM boş kümede NULL döner; decimal'e çevirmek patlardı.
        var userId = await UserAsync();
        Assert.Equal(0m, await _context.UsedThisPeriodAsync(userId, UsageResource.AiCall));
    }

    [RequiresDockerFact]
    public async Task Resources_are_counted_separately()
    {
        var userId = await UserAsync();

        await _context.RecordAsync(userId, UsageResource.AiCall, 5);
        await _context.RecordAsync(userId, UsageResource.ApiRequest, 100);

        Assert.Equal(5m, await _context.UsedThisPeriodAsync(userId, UsageResource.AiCall));
        Assert.Equal(100m, await _context.UsedThisPeriodAsync(userId, UsageResource.ApiRequest));
    }

    [RequiresDockerFact]
    public async Task Usage_does_not_leak_between_users()
    {
        var a = await UserAsync();
        var b = await UserAsync();

        await _context.RecordAsync(a, UsageResource.AiCall, 7);

        Assert.Equal(0m, await _context.UsedThisPeriodAsync(b, UsageResource.AiCall));
    }

    [RequiresDockerFact]
    public async Task A_previous_period_is_not_counted()
    {
        // Dönem yanlış hesaplanırsa geçen ayın kullanımı bu ayın faturasına
        // eklenir — fatura itirazında en zor çözülecek hata sınıfı.
        var userId = await UserAsync();

        _context.UsageEvents.Add(new UsageEvent
        {
            UserId = userId,
            Resource = UsageResource.AiCall,
            Quantity = 999,
            BillingPeriod = UsageMeter.CurrentPeriod().AddMonths(-1),
        });
        await _context.SaveChangesAsync();

        Assert.Equal(0m, await _context.UsedThisPeriodAsync(userId, UsageResource.AiCall));
    }

    [RequiresDockerFact]
    public async Task Fractional_quantities_survive_the_round_trip()
    {
        // Transfer/depolama kesirli; tamsayıya yuvarlamak 0.4 GB'ı ya 0 ya 1 yapardı.
        var userId = await UserAsync();

        await _context.RecordAsync(userId, UsageResource.DataTransferGigabyte, 0.25m);
        await _context.RecordAsync(userId, UsageResource.DataTransferGigabyte, 0.5m);

        Assert.Equal(0.75m, await _context.UsedThisPeriodAsync(userId, UsageResource.DataTransferGigabyte));
    }

    [RequiresDockerFact]
    public async Task Zero_or_negative_quantities_are_not_recorded()
    {
        // Negatif bir miktar kullanımı azaltır; bir hata onu "kredi"ye çevirirdi.
        var userId = await UserAsync();

        await _context.RecordAsync(userId, UsageResource.AiCall, 0);
        await _context.RecordAsync(userId, UsageResource.AiCall, -5);

        Assert.Empty(_context.UsageEvents.Where(e => e.UserId == userId));
    }

    // ── Karar akışı ──────────────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task A_free_user_is_stopped_at_the_allowance()
    {
        var userId = await UserAsync();
        await _context.RecordAsync(userId, UsageResource.AiCall, 100);

        var decision = await _context.EvaluateAsync(userId, UsageResource.AiCall);

        Assert.False(decision.Allowed);
    }

    [RequiresDockerFact]
    public async Task Overage_is_off_until_the_user_turns_it_on()
    {
        // Ayar kaydı olmayan kullanıcı için varsayılan KAPALI olmalı — kayıt
        // yokluğunu "açık" saymak, herkese sürpriz fatura riski açardı.
        var userId = await UserAsync("active");
        await _context.RecordAsync(userId, UsageResource.AiCall, 500);

        var before = await _context.EvaluateAsync(userId, UsageResource.AiCall);
        Assert.False(before.Allowed);

        _context.UserBillingSettings.Add(new UserBillingSettings
        {
            UserId = userId, OverageEnabled = true,
        });
        await _context.SaveChangesAsync();

        var after = await _context.EvaluateAsync(userId, UsageResource.AiCall);
        Assert.True(after.Allowed);
        Assert.Equal(0.03m, after.OverageCostUsd);
    }

    [RequiresDockerFact]
    public async Task A_spending_cap_is_enforced_across_resources()
    {
        // Tavan tek bir kaynağın değil, dönemin tamamının tutarına bakmalı.
        var userId = await UserAsync("active");

        _context.UserBillingSettings.Add(new UserBillingSettings
        {
            UserId = userId, OverageEnabled = true, MonthlyCapUsd = 0.05m,
        });
        await _context.SaveChangesAsync();

        // 502 çağrı → 2 aşım × $0.03 = $0.06, tavan $0.05.
        await _context.RecordAsync(userId, UsageResource.AiCall, 502);

        var decision = await _context.EvaluateAsync(userId, UsageResource.AiCall);

        Assert.False(decision.Allowed);
        Assert.Contains("spending cap", decision.Reason!);
    }

    [RequiresDockerFact]
    public async Task The_period_summary_covers_every_recorded_resource()
    {
        var userId = await UserAsync();

        await _context.RecordAsync(userId, UsageResource.AiCall, 2);
        await _context.RecordAsync(userId, UsageResource.BranchDatabase, 1);

        var summary = await _context.PeriodSummaryAsync(userId);

        Assert.Equal(2m, summary[UsageResource.AiCall]);
        Assert.Equal(1m, summary[UsageResource.BranchDatabase]);
    }
}
