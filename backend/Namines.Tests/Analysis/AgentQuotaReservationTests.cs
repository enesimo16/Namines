using Namines.Core.Analysis;
using Xunit;

namespace Namines.Tests.Analysis;

/// <summary>
/// Bir onarım turu, araç döngüsü (birkaç çağrı) VE okunamayan bir cevapta
/// tetiklenen ReviseSchemaAsync'in kendi iç yeniden deneme döngüsü yüzünden
/// TEK bir "tur" için birden çok upstream çağrıya açılabiliyor (bkz.
/// GroqSchemaDraftSource.RepairWithToolsAsync). Rezervasyon bunu görmezden
/// gelirse kullanıcı günlük bütçesinin TAMAMINI tek bir istekte, önceden hiç
/// uyarılmadan tüketebilir — bu sınıf o hesabı yapıyor.
/// </summary>
public class AgentQuotaReservationTests
{
    [Fact]
    public void A_single_fixed_round_costs_exactly_one_round_equivalent()
    {
        // budgetRounds=1, fixedRounds=1 → onarım turu yok, çarpan devreye
        // girmiyor. Bu, tool-calling'den ÖNCEki tek-çağrılık davranışın aynı
        // kalması gerektiğini kilitliyor — reservasyonu değiştirmek
        // sıradan (gelişmiş olmayan) üretimi cezalandırmamalı.
        Assert.Equal(1, AgentQuotaReservation.RoundEquivalents(budgetRounds: 1, fixedRounds: 1));
    }

    [Fact]
    public void Fixed_rounds_alone_never_get_multiplied()
    {
        // Plan ve taslak turları TEK çağrı yapıyor (GroqSchemaDraftSource.PlanAsync/
        // DraftAsync); yalnızca onarım turları araç döngüsü + fallback yüzünden
        // fan-out riski taşıyor.
        Assert.Equal(2, AgentQuotaReservation.RoundEquivalents(budgetRounds: 2, fixedRounds: 2));
    }

    [Fact]
    public void Each_repair_round_beyond_the_fixed_rounds_is_multiplied()
    {
        // budgetRounds=4, fixedRounds=2 → 2 olası onarım turu, her biri
        // çarpanla sayılıyor.
        var result = AgentQuotaReservation.RoundEquivalents(budgetRounds: 4, fixedRounds: 2);

        Assert.True(result > 4, "Result should exceed the naive round count once repair rounds are multiplied.");
    }

    [Fact]
    public void More_repair_rounds_reserve_proportionally_more()
    {
        var shallow = AgentQuotaReservation.RoundEquivalents(budgetRounds: 4, fixedRounds: 2);
        var deep = AgentQuotaReservation.RoundEquivalents(budgetRounds: 8, fixedRounds: 2);

        Assert.True(deep > shallow);
    }

    [Fact]
    public void Fixed_rounds_never_exceed_the_budget_itself()
    {
        // Savunma: fixedRounds budgetRounds'tan büyük verilirse (çağıran hatası)
        // negatif bir onarım turu sayısına düşmemeli.
        Assert.Equal(1, AgentQuotaReservation.RoundEquivalents(budgetRounds: 1, fixedRounds: 5));
    }
}
