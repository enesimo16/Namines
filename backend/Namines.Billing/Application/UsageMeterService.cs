using Microsoft.EntityFrameworkCore;
using Namines.Billing.Domain;
using Namines.BuildingBlocks.Contracts;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;

namespace Namines.Billing.Application;

/// <summary>
/// Kullanım ölçümü (new-phase/22-BUSINESS-MODEL.md §5). Ölçüm birden çok yerde yeniden yazılırsa biri fatura
/// dönemini yanlış hesaplar ve kullanım sessizce yanlış aya yazılır; fatura itirazında bunu çözmek neredeyse
/// imkânsızdır. Bu yüzden tek kopya.
/// </summary>
public static class UsageMeter
{
    /// <summary>
    /// Fatura dönemi: ayın ilk günü, UTC. Yerel saatle hesaplamak, ayın son gecesindeki kullanımı sunucunun saat
    /// dilimine göre farklı aylara yazardı: aynı olayın iki faturada görünmesi ya da hiç görünmemesi demek.
    /// </summary>
    public static DateTime PeriodOf(DateTime moment) =>
        new(moment.Year, moment.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    public static DateTime CurrentPeriod() => PeriodOf(DateTime.UtcNow);
}

internal sealed class UsageMeterService : IUsageMeter
{
    private readonly IBillingData _data;
    private readonly PlanResolver _plans;

    public UsageMeterService(IBillingData data, IIdentityDirectory identity)
    {
        _data = data;
        _plans = new PlanResolver(data, identity);
    }

    /// <summary>Kayıt olay bazlıdır; toplam sonradan hesaplanır.</summary>
    public async Task RecordAsync(string userId, UsageResource resource, decimal quantity = 1, string? contextLabel = null, CancellationToken ct = default)
    {
        if (quantity <= 0) return;

        _data.UsageEvents.Add(new UsageEvent
        {
            UserId = userId,
            Resource = resource,
            Quantity = quantity,
            BillingPeriod = UsageMeter.CurrentPeriod(),
            Context = contextLabel,
        });
        await _data.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Kaydetmez: karar ile kayıt bilinçli ayrı. Reddedilen isteği kaydetmek kullanımı şişirir; kabul edilip
    /// sonradan başarısız olan işlemi kaydetmek de kullanıcıyı olmamış bir şey için faturalar.
    /// </summary>
    public async Task<UsageDecision> EvaluateAsync(string userId, UsageResource resource, decimal requested = 1, CancellationToken ct = default)
    {
        var tier = await _plans.TierAsync(userId, ct);
        var settings = await _data.UserBillingSettings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);

        var summary = await SummaryAsync(userId, ct);
        var used = summary.TryGetValue(resource, out var value) ? value : 0m;
        var overageSoFar = summary.Sum(pair => OveragePricing.Cost(tier, pair.Key, pair.Value));

        return OveragePricing.Evaluate(
            tier, resource, used, requested,
            // Ayar kaydı yoksa varsayılan: aşırı kullanım KAPALI (22 §5).
            overageEnabled: settings?.OverageEnabled ?? false,
            monthlyCapUsd: settings?.MonthlyCapUsd,
            overageSoFarUsd: overageSoFar);
    }

    private async Task<Dictionary<UsageResource, decimal>> SummaryAsync(string userId, CancellationToken ct)
    {
        var period = UsageMeter.CurrentPeriod();
        var rows = await _data.UsageEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.BillingPeriod == period)
            .GroupBy(e => e.Resource)
            .Select(g => new { Resource = g.Key, Total = g.Sum(e => e.Quantity) })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Resource, r => r.Total);
    }

    public async Task<UsageOverview> GetOverviewAsync(string userId, CancellationToken ct = default)
    {
        var tier = await _plans.TierAsync(userId, ct);
        var summary = await SummaryAsync(userId, ct);
        var settings = await _data.UserBillingSettings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);

        // Kullanılmamış kaynaklar da listelenir: yalnızca kullanılanları göstermek, neyin ne kadar kullanılabildiğini gizlerdi.
        var lines = Enum.GetValues<UsageResource>().Select(resource =>
        {
            var used = summary.TryGetValue(resource, out var value) ? value : 0m;
            var pricing = OveragePricing.For(tier, resource);
            return new UsageResourceLine(
                resource,
                used,
                pricing.Included < 0 ? null : pricing.Included,
                pricing.UnitPriceUsd,
                pricing.UnitSize,
                OveragePricing.Cost(tier, resource, used));
        }).ToList();

        return new UsageOverview(
            tier, UsageMeter.CurrentPeriod(),
            settings?.OverageEnabled ?? false, settings?.MonthlyCapUsd,
            lines.Sum(l => l.OverageCostUsd), lines);
    }

    public async Task<(bool OverageEnabled, decimal? MonthlyCapUsd)> UpdateSettingsAsync(
        string userId, bool overageEnabled, decimal? monthlyCapUsd, CancellationToken ct = default)
    {
        // Negatif tavan "sınırsız" ile karışır; sıfır tavan aşırı kullanımı zaten imkânsız kılar ve
        // "açtım ama çalışmıyor" şikâyetine yol açar.
        if (monthlyCapUsd is <= 0)
            throw new ArgumentOutOfRangeException(nameof(monthlyCapUsd), "Monthly cap must be greater than zero, or omitted for no cap.");

        var settings = await _data.UserBillingSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (settings is null)
        {
            settings = new UserBillingSettings { UserId = userId };
            _data.UserBillingSettings.Add(settings);
        }

        settings.OverageEnabled = overageEnabled;
        settings.MonthlyCapUsd = monthlyCapUsd;
        settings.UpdatedAt = DateTime.UtcNow;
        await _data.SaveChangesAsync(ct);
        return (settings.OverageEnabled, settings.MonthlyCapUsd);
    }
}
