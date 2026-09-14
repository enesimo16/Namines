using Namines.Core.Analysis;
using Xunit;

namespace Namines.Tests.Analysis;

/// <summary>
/// Onarım derinliği plana bağlı olmalı.
///
/// <b>Neden kota tablosunda, çağıranda değil:</b> tavan çağıranda sabitlenirse
/// (eskiden öyleydi) ödeyen kullanıcı yalnızca daha büyük bir token havuzu alır
/// ama agent aynı noktada pes eder — yani derinlik için ödenen para bir karşılık
/// üretmez.
/// </summary>
public class PlanQuotaAgentRoundsTests
{
    [Fact]
    public void A_free_plan_gets_the_shallow_repair_budget()
    {
        Assert.Equal(2, PlanQuotas.For(PlanTier.Free).MaxAgentRepairTurns);
    }

    [Fact]
    public void Paid_plans_repair_deeper_than_free()
    {
        var free = PlanQuotas.For(PlanTier.Free).MaxAgentRepairTurns;

        Assert.True(PlanQuotas.For(PlanTier.Pro).MaxAgentRepairTurns > free);
        Assert.True(PlanQuotas.For(PlanTier.Team).MaxAgentRepairTurns > free);
        Assert.True(PlanQuotas.For(PlanTier.Enterprise).MaxAgentRepairTurns > free);
    }

    [Fact]
    public void Pro_and_team_share_the_same_repair_depth()
    {
        // Bir Team koltuğu bir Pro hesabıyla aynı hakkı taşır; ekip olmak
        // kimseyi kısıtlamaz (bkz. DailyAiTokens'daki aynı gerekçe).
        Assert.Equal(
            PlanQuotas.For(PlanTier.Pro).MaxAgentRepairTurns,
            PlanQuotas.For(PlanTier.Team).MaxAgentRepairTurns);
    }

    [Fact]
    public void The_owner_account_repairs_at_least_as_deep_as_any_paid_plan()
    {
        Assert.True(
            PlanQuotas.For(PlanTier.Dev).MaxAgentRepairTurns >=
            PlanQuotas.For(PlanTier.Enterprise).MaxAgentRepairTurns);
    }

    [Fact]
    public void Every_plan_can_repair_at_least_once()
    {
        // Sıfır onarım turu agent hattını tek çağrıya indirger ve denetimi
        // anlamsızlaştırır — bulgu bulunur ama hiç düzeltilmez.
        foreach (var tier in new[]
                 { PlanTier.Free, PlanTier.Pro, PlanTier.Team, PlanTier.Enterprise, PlanTier.Dev })
        {
            Assert.True(
                PlanQuotas.For(tier).MaxAgentRepairTurns >= 1,
                $"{tier} plan repairs zero times.");
        }
    }
}
