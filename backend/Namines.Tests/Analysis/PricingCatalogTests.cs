using Namines.Core.Analysis;

namespace Namines.Tests.Analysis;

/// <summary>
/// Fiyat kataloğu (<see cref="PricingCatalog"/>) — satılan planların tek kaynağı.
///
/// <b>Bulunan boşluk:</b> fiyat yalnızca bir React bileşeninin içinde düz metin
/// olarak duruyordu ("$7.5/mo"). Stripe'taki fiyat değişse ekran eski tutarı
/// göstermeye devam ederdi ve kullanıcı farkı ancak kartından çekilen tutarda
/// görürdü. Bu testler, ekranın okuduğu sayının kurallı kaldığını doğruluyor.
/// </summary>
public class PricingCatalogTests
{
    [Theory]
    [InlineData(PlanTier.Pro)]
    [InlineData(PlanTier.Team)]
    public void Every_paid_plan_is_sold_both_monthly_and_yearly(PlanTier tier)
    {
        Assert.NotNull(PricingCatalog.Find(tier, BillingInterval.Monthly));
        Assert.NotNull(PricingCatalog.Find(tier, BillingInterval.Yearly));
    }

    [Fact]
    public void Free_tier_is_not_in_the_catalog()
    {
        // Ücretsiz katmanın Stripe'ta karşılığı yok. Sıfır fiyatlı bir kayıt
        // eklemek, ödeme akışının onu da bir fiyat kimliğiyle eşleştirmeye
        // çalışması demekti.
        Assert.Empty(PricingCatalog.For(PlanTier.Free));
    }

    [Theory]
    [InlineData(PlanTier.Pro)]
    [InlineData(PlanTier.Team)]
    public void Yearly_costs_less_than_twelve_monthly_payments(PlanTier tier)
    {
        var monthly = PricingCatalog.Find(tier, BillingInterval.Monthly)!;
        var yearly = PricingCatalog.Find(tier, BillingInterval.Yearly)!;

        Assert.True(yearly.AmountUsd < monthly.AmountUsd * 12m,
            "yıllık plan indirimli olmalı — yoksa 'yıllığa geç' önerisi kullanıcıyı zarara sokar");
    }

    [Theory]
    [InlineData(PlanTier.Pro)]
    [InlineData(PlanTier.Team)]
    public void Discount_badge_matches_the_actual_prices(PlanTier tier)
    {
        // İndirim etiketi HESAPLANIYOR, yazılmıyor: fiyatlardan biri değişip
        // etiket elle güncellenmezse ekranda gerçek olmayan bir indirim durur.
        var monthly = PricingCatalog.Find(tier, BillingInterval.Monthly)!;
        var yearly = PricingCatalog.Find(tier, BillingInterval.Yearly)!;
        var expected = (int)Math.Round((1m - yearly.AmountUsd / (monthly.AmountUsd * 12m)) * 100m);

        Assert.Equal(expected, PricingCatalog.YearlyDiscountPercent(tier));
    }

    [Fact]
    public void Monthly_equivalent_of_a_yearly_plan_is_a_twelfth()
    {
        var yearly = PricingCatalog.Find(PlanTier.Pro, BillingInterval.Yearly)!;
        Assert.Equal(Math.Round(yearly.AmountUsd / 12m, 2), yearly.MonthlyEquivalentUsd);
    }

    [Fact]
    public void Monthly_plan_reports_its_own_amount_as_the_monthly_equivalent()
    {
        var monthly = PricingCatalog.Find(PlanTier.Pro, BillingInterval.Monthly)!;
        Assert.Equal(monthly.AmountUsd, monthly.MonthlyEquivalentUsd);
    }

    [Theory]
    [InlineData("pro", PlanTier.Pro)]
    [InlineData("Team", PlanTier.Team)]
    [InlineData("  PRO  ", PlanTier.Pro)]
    public void Plan_names_are_parsed_case_and_whitespace_insensitively(string input, PlanTier expected)
        => Assert.Equal(expected, PricingCatalog.ParseTier(input));

    [Theory]
    [InlineData("enterprise")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("pro-plus")]
    public void Unknown_plan_names_do_not_silently_become_pro(string? input)
    {
        // Sessizce Pro'ya düşseydi, Team'e tıklayan biri Pro'ya abone olur ve
        // bunu ancak faturada fark ederdi.
        Assert.Null(PricingCatalog.ParseTier(input));
    }

    [Theory]
    [InlineData("month", BillingInterval.Monthly)]
    [InlineData("monthly", BillingInterval.Monthly)]
    [InlineData("year", BillingInterval.Yearly)]
    [InlineData("annual", BillingInterval.Yearly)]
    public void Billing_interval_accepts_the_obvious_spellings(string input, BillingInterval expected)
        => Assert.Equal(expected, PricingCatalog.ParseInterval(input));

    [Theory]
    [InlineData("weekly")]
    [InlineData(null)]
    public void Unknown_intervals_do_not_silently_become_monthly(string? input)
    {
        // "yearly" yazıp aylık ödemek, yanlış plana abone olmaktan daha kötü:
        // kullanıcı indirim beklerken tam fiyat ödemiş olur.
        Assert.Null(PricingCatalog.ParseInterval(input));
    }

    [Fact]
    public void Every_price_points_at_a_distinct_configuration_key()
    {
        // İki fiyat aynı yapılandırma anahtarını gösterseydi, aylık ve yıllık
        // aynı Stripe fiyatına giderdi — yani "yıllık" düğmesi aylık abonelik
        // açardı ve kimse bunu ekrandan anlayamazdı.
        var keys = PricingCatalog.Prices.Select(p => p.ConfigKey).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void Pro_is_cheaper_than_team_on_both_intervals()
    {
        foreach (var billing in new[] { BillingInterval.Monthly, BillingInterval.Yearly })
        {
            Assert.True(
                PricingCatalog.Find(PlanTier.Pro, billing)!.AmountUsd <
                PricingCatalog.Find(PlanTier.Team, billing)!.AmountUsd,
                $"{billing}: Pro, Team'den ucuz olmalı");
        }
    }

    [Fact]
    public void Pro_price_leaves_stripes_fixed_fee_a_small_share()
    {
        // second-phase/16-KOTA-VE-MALIYET.md: Stripe'ın işlem başına SABİT
        // $0,30'u, fiyat düştükçe oransal olarak büyüyor. $7,50'de %4'tü.
        // Bu test, fiyatın o eşiğin altına geri düşmesini fark ettirir.
        var pro = PricingCatalog.Find(PlanTier.Pro, BillingInterval.Monthly)!;
        var fixedFeeShare = 0.30m / pro.AmountUsd;

        Assert.True(fixedFeeShare <= 0.025m,
            $"sabit ücret aylık Pro fiyatının %{fixedFeeShare * 100:F1}'i — fiyat çok düşük");
    }
}
