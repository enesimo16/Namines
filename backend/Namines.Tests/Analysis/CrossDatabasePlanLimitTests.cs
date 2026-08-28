using Namines.Core.Analysis;

namespace Namines.Tests.Analysis;

/// <summary>
/// second-phase/10-COKLU-DB.md'nin açık bıraktığı soru: "kaç DB, hangi planda?"
///
/// <b>Bulunan boşluk:</b> çoklu DB ilişkilerinde hiçbir plan sınırı yoktu —
/// ücretsiz bir kullanıcı sınırsız ilişki kurabiliyordu. Her ilişki, silme
/// öncesi çalışan bir etki analizi ve karşı projenin şemasının çözülmesi
/// demek; yani açık uçlu bir maliyet.
/// </summary>
public class CrossDatabasePlanLimitTests
{
    [Fact]
    public void Free_gets_enough_to_understand_the_feature_but_not_to_map_a_fleet()
    {
        var free = PlanQuotas.For(PlanTier.Free).CrossDatabaseRelations;

        Assert.True(free > 0, "ücretsiz katmanda özellik denenebilmeli");
        Assert.True(free <= 5, "ücretsiz katman bir mikroservis filosunu haritalamaya yetmemeli");
    }

    [Fact]
    public void Paid_plans_allow_progressively_more()
    {
        var free = PlanQuotas.For(PlanTier.Free).CrossDatabaseRelations;
        var pro = PlanQuotas.For(PlanTier.Pro).CrossDatabaseRelations;
        var team = PlanQuotas.For(PlanTier.Team).CrossDatabaseRelations;

        Assert.True(free < pro);
        Assert.True(pro < team);
    }

    [Fact]
    public void Dev_and_enterprise_are_unlimited()
    {
        // -1 = sınırsız; kontrolcü bu değerde sayım bile yapmıyor.
        Assert.Equal(-1, PlanQuotas.For(PlanTier.Dev).CrossDatabaseRelations);
        Assert.Equal(-1, PlanQuotas.For(PlanTier.Enterprise).CrossDatabaseRelations);
    }

    [Fact]
    public void Pro_allows_enough_for_a_realistic_microservice_setup()
    {
        // Tipik hedef kullanım: auth-db + orders-db + billing-db gibi birkaç
        // servis, aralarında birkaç bağ. 25, bunu rahat karşılar.
        Assert.True(PlanQuotas.For(PlanTier.Pro).CrossDatabaseRelations >= 10);
    }
}
