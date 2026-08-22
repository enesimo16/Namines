using Namines.Core.Analysis;
using Namines.Core.Models.Auth;

namespace Namines.Tests.Analysis;

/// <summary>
/// Aşırı kullanım fiyatlandırması ve karar mantığı
/// (new-phase/22-BUSINESS-MODEL.md §5).
///
/// Bu kod PARA hesaplıyor; iki yönde de yanlış olabilir ve ikisi de kötü. Fazla
/// ücretlendirmek güveni bitirir, az ücretlendirmek geliri sessizce sızdırır.
/// Testler dokümandaki tabloyu ve "aşırı kullanım varsayılan KAPALI" kuralını
/// kilitliyor.
/// </summary>
public class OveragePricingTests
{
    [Fact]
    public void Pro_matches_the_documented_table()
    {
        Assert.Equal(500, OveragePricing.For(PlanTier.Pro, UsageResource.AiCall).Included);
        Assert.Equal(0.03m, OveragePricing.For(PlanTier.Pro, UsageResource.AiCall).UnitPriceUsd);

        var api = OveragePricing.For(PlanTier.Pro, UsageResource.ApiRequest);
        Assert.Equal(500_000, api.Included);
        Assert.Equal(1.50m, api.UnitPriceUsd);
        // Birim boyutu 1 varsayılsaydı API aşımı 100 bin kat pahalı faturalanırdı.
        Assert.Equal(100_000, api.UnitSize);
    }

    [Fact]
    public void Usage_within_the_allowance_costs_nothing()
    {
        Assert.Equal(0m, OveragePricing.Cost(PlanTier.Pro, UsageResource.AiCall, 500));
        Assert.Equal(0m, OveragePricing.Cost(PlanTier.Pro, UsageResource.AiCall, 0));
    }

    [Fact]
    public void Overage_is_charged_per_unit_beyond_the_allowance()
    {
        // 510 çağrı = 10 aşım × $0.03
        Assert.Equal(0.30m, OveragePricing.Cost(PlanTier.Pro, UsageResource.AiCall, 510));
    }

    [Fact]
    public void A_partial_unit_rounds_up()
    {
        // 600.001 istek: dahil 500K, aşan 100.001 → iki dilim değil ama bir dilim
        // + başlanmış bir dilim. Aşağı yuvarlamak, bir birim aşan her kullanımı
        // ücretsiz yapardı.
        Assert.Equal(1.50m, OveragePricing.Cost(PlanTier.Pro, UsageResource.ApiRequest, 600_000));
        Assert.Equal(3.00m, OveragePricing.Cost(PlanTier.Pro, UsageResource.ApiRequest, 600_001));
    }

    [Fact]
    public void Enterprise_is_unlimited_and_never_billed_as_overage()
    {
        // Sınır da fiyat da sözleşmeden gelir, koda gömülmez.
        Assert.Equal(0m, OveragePricing.Cost(PlanTier.Enterprise, UsageResource.AiCall, 1_000_000));
    }

    // ── Karar mantığı ────────────────────────────────────────────────────────

    [Fact]
    public void Within_the_allowance_the_request_is_allowed_and_free()
    {
        var decision = OveragePricing.Evaluate(
            PlanTier.Pro, UsageResource.AiCall, alreadyUsed: 10, requested: 1,
            overageEnabled: false, monthlyCapUsd: null, overageSoFarUsd: 0);

        Assert.True(decision.Allowed);
        Assert.Equal(0m, decision.OverageCostUsd);
    }

    [Fact]
    public void Beyond_the_allowance_with_overage_off_the_request_stops()
    {
        // 22 §5'in varsayılanı. Beklenmeyen bir fatura, durmuş bir hizmetten
        // daha kötüdür — ve kullanıcı isterse tek tıkla açar.
        var decision = OveragePricing.Evaluate(
            PlanTier.Pro, UsageResource.AiCall, alreadyUsed: 500, requested: 1,
            overageEnabled: false, monthlyCapUsd: null, overageSoFarUsd: 0);

        Assert.False(decision.Allowed);
        Assert.Contains("overage", decision.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Beyond_the_allowance_with_overage_on_the_request_continues_and_costs()
    {
        var decision = OveragePricing.Evaluate(
            PlanTier.Pro, UsageResource.AiCall, alreadyUsed: 500, requested: 1,
            overageEnabled: true, monthlyCapUsd: null, overageSoFarUsd: 0);

        Assert.True(decision.Allowed);
        Assert.Equal(0.03m, decision.OverageCostUsd);
    }

    [Fact]
    public void The_cost_reported_is_only_for_this_request()
    {
        // Toplam maliyeti döndürseydi, çağıran her istekte kümülatif tutarı
        // görüp aynı parayı tekrar tekrar tahakkuk ettirirdi.
        var decision = OveragePricing.Evaluate(
            PlanTier.Pro, UsageResource.AiCall, alreadyUsed: 600, requested: 1,
            overageEnabled: true, monthlyCapUsd: null, overageSoFarUsd: 3.00m);

        Assert.Equal(0.03m, decision.OverageCostUsd);
    }

    [Fact]
    public void A_spending_cap_stops_the_request_even_with_overage_on()
    {
        // "Sınırsız fatura" hiçbir kullanıcının istediği şey değil.
        var decision = OveragePricing.Evaluate(
            PlanTier.Pro, UsageResource.AiCall, alreadyUsed: 500, requested: 1,
            overageEnabled: true, monthlyCapUsd: 1.00m, overageSoFarUsd: 1.00m);

        Assert.False(decision.Allowed);
        Assert.Contains("spending cap", decision.Reason!);
    }

    [Fact]
    public void A_resource_that_cannot_be_billed_is_refused_even_with_overage_on()
    {
        // Free'de branch veritabanı 0 ve fiyatı yok; aşırı kullanımı açmak onu
        // satın alınabilir yapmaz, mesaj yükseltmeyi söylemeli.
        var decision = OveragePricing.Evaluate(
            PlanTier.Free, UsageResource.BranchDatabase, alreadyUsed: 0, requested: 1,
            overageEnabled: true, monthlyCapUsd: null, overageSoFarUsd: 0);

        Assert.False(decision.Allowed);
        Assert.Contains("Upgrade", decision.Reason!);
    }

    [Fact]
    public void Enterprise_is_always_allowed()
    {
        var decision = OveragePricing.Evaluate(
            PlanTier.Enterprise, UsageResource.ApiRequest, alreadyUsed: 10_000_000, requested: 1,
            overageEnabled: false, monthlyCapUsd: null, overageSoFarUsd: 0);

        Assert.True(decision.Allowed);
    }
}
