using System;
using System.Collections.Generic;
using System.Linq;

namespace Namines.Core.Analysis;

/// <summary>Faturalandırma dönemi. Yıllık, 12 ayı TEK işlemde tahsil eder.</summary>
public enum BillingInterval
{
    Monthly = 0,
    Yearly = 1,
}

/// <param name="Tier">Bu fiyatın açtığı plan.</param>
/// <param name="Interval">Aylık mı yıllık mı.</param>
/// <param name="AmountUsd">Tahsil edilen tutar — yıllıkta 12 aylık toplam.</param>
/// <param name="ConfigKey">
/// Stripe fiyat kimliğinin yapılandırmadaki anahtarı. Fiyatın kendisi burada,
/// KİMLİĞİ yapılandırmada: fiyat kimliği ortama göre değişiyor (test/canlı) ve
/// bir sırdan çok bir adres; koda gömmek test anahtarıyla canlıya çıkmak demekti.
/// </param>
public sealed record PlanPrice(
    PlanTier Tier,
    BillingInterval Interval,
    decimal AmountUsd,
    string ConfigKey)
{
    /// <summary>Yıllıkta aya düşen tutar; aylıkta tutarın kendisi.</summary>
    public decimal MonthlyEquivalentUsd =>
        Interval == BillingInterval.Yearly ? Math.Round(AmountUsd / 12m, 2) : AmountUsd;
}

/// <summary>
/// Satılan planların TEK fiyat kaynağı.
///
/// <b>Neden sunucuda:</b> fiyat daha önce yalnızca bir React bileşeninin içinde
/// düz metin olarak duruyordu ("$7.5/mo"). Stripe'taki fiyat değiştiğinde ekran
/// eski tutarı göstermeye devam ederdi ve kullanıcı farkı ancak kartından çekilen
/// tutarda görürdü. Artık ekran bu listeyi okuyor.
///
/// <b>Yıllık indirim "2 ay bedava" olarak kuruldu</b> (10 ay öde, 12 ay kullan).
/// Sebebi pazarlama değil aritmetik: Stripe'ın işlem başına sabit $0,30'u aylık
/// planda her ay tekrar kesiliyor; yıllıkta bir kez kesiliyor. İndirimin bir
/// kısmını bu tasarruf zaten karşılıyor (bkz. second-phase/16-KOTA-VE-MALIYET.md).
/// </summary>
public static class PricingCatalog
{
    public static readonly IReadOnlyList<PlanPrice> Prices = new[]
    {
        new PlanPrice(PlanTier.Pro,  BillingInterval.Monthly,  15.00m, "Stripe:ProPriceId"),
        new PlanPrice(PlanTier.Pro,  BillingInterval.Yearly,  150.00m, "Stripe:ProYearlyPriceId"),
        new PlanPrice(PlanTier.Team, BillingInterval.Monthly,  40.00m, "Stripe:TeamPriceId"),
        new PlanPrice(PlanTier.Team, BillingInterval.Yearly,  400.00m, "Stripe:TeamYearlyPriceId"),
    };

    public static PlanPrice? Find(PlanTier tier, BillingInterval interval) =>
        Prices.FirstOrDefault(p => p.Tier == tier && p.Interval == interval);

    /// <summary>
    /// Ücretsiz katman bu listede YOK — satılan bir şey değil, dolayısıyla
    /// Stripe'ta karşılığı da yok. Sıfır fiyatlı bir kayıt eklemek, ödeme
    /// akışının onu da bir fiyat kimliğiyle eşleştirmeye çalışması demekti.
    /// </summary>
    public static IEnumerable<PlanPrice> For(PlanTier tier) =>
        Prices.Where(p => p.Tier == tier);

    /// <summary>
    /// Plan adını ("pro" / "team") katmana çevirir. Bilinmeyen ad <c>null</c> —
    /// sessizce Pro'ya düşmüyor: kullanıcı Team'e tıklayıp Pro'ya abone olur
    /// ve bunu ancak faturada fark ederdi.
    /// </summary>
    public static PlanTier? ParseTier(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "pro" => PlanTier.Pro,
        "team" => PlanTier.Team,
        _ => null,
    };

    public static BillingInterval? ParseInterval(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "month" or "monthly" => BillingInterval.Monthly,
        "year" or "yearly" or "annual" => BillingInterval.Yearly,
        _ => null,
    };

    /// <summary>
    /// Yıllık planın aylığa göre yüzde indirimi — ekranda gösterilen rozet.
    ///
    /// Hesaplanıyor, yazılmıyor: fiyatlardan biri değişip indirim etiketi elle
    /// güncellenmezse, ekranda gerçek olmayan bir indirim duruyor olurdu.
    /// Bu, fiyat etiketiyle kesilen tutarın uyuşmaması kadar ciddi bir hata.
    /// </summary>
    public static int? YearlyDiscountPercent(PlanTier tier)
    {
        var monthly = Find(tier, BillingInterval.Monthly);
        var yearly = Find(tier, BillingInterval.Yearly);
        if (monthly is null || yearly is null || monthly.AmountUsd <= 0) return null;

        var fullYear = monthly.AmountUsd * 12m;
        if (yearly.AmountUsd >= fullYear) return null;

        return (int)Math.Round((1m - yearly.AmountUsd / fullYear) * 100m);
    }
}
