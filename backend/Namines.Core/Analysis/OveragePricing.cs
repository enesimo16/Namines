using System;
using Namines.Core.Models.Auth;

namespace Namines.Core.Analysis;

/// <param name="Included">Plana dahil miktar. -1 = sınırsız.</param>
/// <param name="UnitPriceUsd">Aşırı kullanımın birim fiyatı.</param>
/// <param name="UnitSize">
/// Fiyatın kaç birimi kapsadığı. API isteği "100K başına $1.50" olduğu için
/// 100.000; çoğu kaynakta 1. Bunu 1 varsaymak, API aşımını 100 bin kat pahalı
/// faturalandırırdı.
/// </param>
public sealed record ResourcePricing(decimal Included, decimal UnitPriceUsd, decimal UnitSize);

/// <summary>
/// Plana dahil miktarlar ve aşırı kullanım fiyatları
/// (new-phase/22-BUSINESS-MODEL.md §5).
///
/// Fiyatlar burada SABİT ve bu bilinçli bir başlangıç: Stripe fiyat id'leriyle
/// eşleme henüz yok (bkz. CHECKLIST "Kodun beklediği kararlar"). Sabit tablo,
/// ölçüm motorunun bugün doğru çalışmasını sağlıyor; Stripe geldiğinde bu tablo
/// tek yerden değişir. Fiyatları koda gömmemek adına ölçümü ertelemek, en çok
/// ihtiyaç duyulan şeyi — kimin ne kadar kullandığını — bilmemek demekti.
/// </summary>
public static class OveragePricing
{
    public static ResourcePricing For(PlanTier tier, UsageResource resource) => tier switch
    {
        // Free'de aşırı kullanım YOK: dahil miktar biter, hizmet durur. Ücretsiz
        // bir hesaba fatura çıkarmanın yolu zaten yok (ödeme yöntemi kayıtlı değil).
        PlanTier.Free => resource switch
        {
            UsageResource.AiCall => new(100, 0m, 1),
            UsageResource.ApiRequest => new(10_000, 0m, 1),
            UsageResource.BranchDatabase => new(0, 0m, 1),
            UsageResource.StorageGigabyteMonth => new(0, 0m, 1),
            UsageResource.ConsoleUser => new(1, 0m, 1),
            UsageResource.DataTransferGigabyte => new(1, 0m, 1),
            _ => new(0, 0m, 1),
        },

        PlanTier.Pro => resource switch
        {
            UsageResource.AiCall => new(500, 0.03m, 1),
            UsageResource.ApiRequest => new(500_000, 1.50m, 100_000),
            UsageResource.BranchDatabase => new(2, 4.00m, 1),
            UsageResource.StorageGigabyteMonth => new(0.5m, 0.30m, 1),
            UsageResource.ConsoleUser => new(3, 6.00m, 1),
            UsageResource.DataTransferGigabyte => new(10, 0.10m, 1),
            _ => new(0, 0m, 1),
        },

        PlanTier.Team => resource switch
        {
            UsageResource.AiCall => new(5_000, 0.03m, 1),
            UsageResource.ApiRequest => new(5_000_000, 1.50m, 100_000),
            UsageResource.BranchDatabase => new(20, 4.00m, 1),
            UsageResource.StorageGigabyteMonth => new(10, 0.30m, 1),
            UsageResource.ConsoleUser => new(10, 6.00m, 1),
            UsageResource.DataTransferGigabyte => new(100, 0.10m, 1),
            _ => new(0, 0m, 1),
        },

        // Enterprise sözleşmeli: sınır da fiyat da anlaşmadan gelir, koda gömülmez.
        _ => new(-1, 0m, 1),
    };

    /// <summary>
    /// Verilen kullanım için aşırı kullanım tutarı.
    ///
    /// Kısmi birim YUKARI yuvarlanıyor: 100.001 API isteği, 100K'lık iki dilim
    /// değil ama bir dilim + başlanmış bir dilim. Aşağı yuvarlamak, dahil miktarı
    /// bir birim aşan her kullanımı ücretsiz yapardı.
    /// </summary>
    public static decimal Cost(PlanTier tier, UsageResource resource, decimal used)
    {
        var pricing = For(tier, resource);

        if (pricing.Included < 0) return 0m;          // sınırsız
        if (pricing.UnitPriceUsd <= 0) return 0m;     // aşırı kullanım satılmıyor
        if (used <= pricing.Included) return 0m;

        var excess = used - pricing.Included;
        var units = Math.Ceiling(excess / pricing.UnitSize);
        return units * pricing.UnitPriceUsd;
    }

    /// <summary>
    /// Bu kullanım devam edebilir mi?
    ///
    /// Kural (22 §5): dahil miktar içindeyse EVET. Aşıldıysa yalnızca aşırı
    /// kullanım AÇIKSA ve harcama tavanı dolmadıysa evet. Kapalıyken hizmet durur —
    /// beklenmeyen bir fatura, durmuş bir hizmetten daha kötüdür.
    /// </summary>
    public static UsageDecision Evaluate(
        PlanTier tier, UsageResource resource, decimal alreadyUsed, decimal requested,
        bool overageEnabled, decimal? monthlyCapUsd, decimal overageSoFarUsd)
    {
        var pricing = For(tier, resource);
        var total = alreadyUsed + requested;

        if (pricing.Included < 0 || total <= pricing.Included)
            return new UsageDecision(true, 0m, null);

        if (!overageEnabled)
            return new UsageDecision(false, 0m,
                pricing.UnitPriceUsd > 0
                    ? $"You have used your included {pricing.Included:0.##} for this billing period. " +
                      "Turn on overage billing to continue, or wait for the period to reset."
                    : $"This resource is capped at {pricing.Included:0.##} on the {tier} plan and " +
                      "cannot be extended. Upgrade to raise the limit.");

        if (pricing.UnitPriceUsd <= 0)
            return new UsageDecision(false, 0m,
                $"This resource is capped at {pricing.Included:0.##} on the {tier} plan and cannot " +
                "be billed as overage. Upgrade to raise the limit.");

        // Bu isteğin EK maliyeti: toplam maliyetten hâlihazırda tahakkuk edeni çıkar.
        var cost = Cost(tier, resource, total) - Cost(tier, resource, alreadyUsed);

        if (monthlyCapUsd is not null && overageSoFarUsd + cost > monthlyCapUsd)
            return new UsageDecision(false, cost,
                $"This would exceed your monthly spending cap of ${monthlyCapUsd:0.00}. " +
                "Raise the cap to continue.");

        return new UsageDecision(true, cost, null);
    }
}

/// <param name="Allowed">İşlem devam edebilir mi?</param>
/// <param name="OverageCostUsd">Bu isteğin ek maliyeti; dahil miktardaysa 0.</param>
/// <param name="Reason">Reddedildiyse kullanıcıya gösterilecek sebep.</param>
public sealed record UsageDecision(bool Allowed, decimal OverageCostUsd, string? Reason);
