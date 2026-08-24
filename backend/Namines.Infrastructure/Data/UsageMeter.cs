using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;

namespace Namines.Infrastructure.Data;

/// <summary>
/// Kullanım ölçümü (new-phase/22-BUSINESS-MODEL.md §5).
///
/// <see cref="OrgAccess"/> ve <see cref="GatewayAccess"/> ile aynı desen: tek
/// kopya. Ölçüm birden çok yerde yeniden yazılırsa biri fatura dönemini yanlış
/// hesaplar ve kullanım sessizce yanlış aya yazılır — fatura itirazında bunu
/// çözmek neredeyse imkânsızdır.
/// </summary>
public static class UsageMeter
{
    /// <summary>
    /// Fatura dönemi: ayın ilk günü, UTC.
    ///
    /// Yerel saatle hesaplamak, ayın son gecesindeki kullanımı sunucunun saat
    /// dilimine göre farklı aylara yazardı — aynı olayın iki farklı faturada
    /// görünmesi ya da hiç görünmemesi demek.
    /// </summary>
    public static DateTime PeriodOf(DateTime moment) =>
        new(moment.Year, moment.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    public static DateTime CurrentPeriod() => PeriodOf(DateTime.UtcNow);

    /// <summary>Kullanımı kaydeder. Kayıt olay bazlıdır; toplam sonradan hesaplanır.</summary>
    public static async Task RecordAsync(
        this AuthDbContext context, string userId, UsageResource resource,
        decimal quantity = 1, string? contextLabel = null, CancellationToken ct = default)
    {
        if (quantity <= 0) return;

        context.UsageEvents.Add(new UsageEvent
        {
            UserId = userId,
            Resource = resource,
            Quantity = quantity,
            BillingPeriod = CurrentPeriod(),
            Context = contextLabel,
        });

        await context.SaveChangesAsync(ct);
    }

    /// <summary>Bu dönemde bu kaynaktan ne kadar kullanılmış?</summary>
    public static async Task<decimal> UsedThisPeriodAsync(
        this AuthDbContext context, string userId, UsageResource resource, CancellationToken ct = default)
    {
        var period = CurrentPeriod();

        // Hiç kayıt yoksa SumAsync null döner; decimal'e çevirmek patlardı.
        return await context.UsageEvents
            .Where(e => e.UserId == userId && e.Resource == resource && e.BillingPeriod == period)
            .SumAsync(e => (decimal?)e.Quantity, ct) ?? 0m;
    }

    /// <summary>Dönemin tamamı — kullanım ekranının ve faturanın kaynağı.</summary>
    public static async Task<IReadOnlyDictionary<UsageResource, decimal>> PeriodSummaryAsync(
        this AuthDbContext context, string userId, CancellationToken ct = default)
    {
        var period = CurrentPeriod();

        var rows = await context.UsageEvents
            .Where(e => e.UserId == userId && e.BillingPeriod == period)
            .GroupBy(e => e.Resource)
            .Select(g => new { Resource = g.Key, Total = g.Sum(e => e.Quantity) })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.Resource, r => r.Total);
    }

    /// <summary>Bu dönemde tahakkuk eden toplam aşırı kullanım tutarı.</summary>
    public static async Task<decimal> OverageSoFarAsync(
        this AuthDbContext context, string userId, PlanTier tier, CancellationToken ct = default)
    {
        var summary = await context.PeriodSummaryAsync(userId, ct);
        return summary.Sum(pair => OveragePricing.Cost(tier, pair.Key, pair.Value));
    }

    /// <summary>
    /// Bu kullanım yapılabilir mi, ve yapılırsa ne kadar tutar?
    ///
    /// Kaydetmez — karar ile kayıt bilinçli olarak ayrı. Reddedilen bir isteği
    /// kaydetmek kullanımı şişirir; kabul edilen ama sonradan başarısız olan bir
    /// işlemi kaydetmek de kullanıcıyı olmamış bir şey için faturalandırır.
    /// Çağıran, işlem GERÇEKTEN olduktan sonra <see cref="RecordAsync"/> çağırır.
    /// </summary>
    public static async Task<UsageDecision> EvaluateAsync(
        this AuthDbContext context, string userId, UsageResource resource,
        decimal requested = 1, CancellationToken ct = default)
    {
        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        var tier = PlanQuotas.Resolve(user?.SubscriptionStatus, user?.PlanCode, user?.IsDev ?? false);

        var settings = await context.UserBillingSettings
            .FirstOrDefaultAsync(s => s.UserId == userId, ct);

        var used = await context.UsedThisPeriodAsync(userId, resource, ct);
        var overageSoFar = await context.OverageSoFarAsync(userId, tier, ct);

        return OveragePricing.Evaluate(
            tier, resource, used, requested,
            // Ayar kaydı yoksa varsayılan: aşırı kullanım KAPALI (22 §5).
            overageEnabled: settings?.OverageEnabled ?? false,
            monthlyCapUsd: settings?.MonthlyCapUsd,
            overageSoFarUsd: overageSoFar);
    }
}
