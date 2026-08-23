using Namines.Core.Analysis;

namespace Namines.Tests.Analysis;

/// <summary>
/// Plan → hak eşlemesi (22 §2, 08 §5).
///
/// <b>Bu testler, planın hiçbir sınırı etkilemediğinin fark edilmesinden sonra
/// yazıldı.</b> Abonelik bilgisi veritabanında duruyordu ama günlük AI bütçesi
/// yapılandırmadan tek bir sayı olarak okunuyordu — yani <b>ücretli kullanıcı da
/// ücretsiz kullanıcı da aynı 20.000 token'ı alıyordu</b>: para ödeyen
/// karşılığını almıyor, ödemeyen de kısıtlanmıyordu.
/// </summary>
public class PlanQuotaTests
{
    // ── Planlar birbirinden ayrışıyor mu ─────────────────────────────────────

    [Fact]
    public void A_paid_plan_gets_more_ai_budget_than_the_free_one()
    {
        // Ödeyen kullanıcının karşılığını alması, planın var olma sebebi.
        Assert.True(PlanQuotas.For(PlanTier.Pro).DailyAiTokens >
                    PlanQuotas.For(PlanTier.Free).DailyAiTokens);
    }

    [Theory]
    [InlineData(PlanTier.Free, PlanTier.Pro)]
    [InlineData(PlanTier.Pro, PlanTier.Team)]
    [InlineData(PlanTier.Team, PlanTier.Enterprise)]
    public void Every_step_up_is_a_real_step_up(PlanTier lower, PlanTier higher)
    {
        // Bir üst plan, alttakiyle aynı hakları veriyorsa kullanıcı neden
        // yükseltsin? Eşitlik de bir hatadır.
        var below = PlanQuotas.For(lower);
        var above = PlanQuotas.For(higher);

        Assert.True(above.DailyAiTokens > below.DailyAiTokens);
        Assert.True(above.GatewayRequestsPerMinute > below.GatewayRequestsPerMinute);
    }

    [Fact]
    public void The_free_plan_is_usable_but_narrow()
    {
        // Sıfır vermek ürünü denenemez kılar; cömert vermek ücretliye geçme
        // sebebini yok eder.
        var free = PlanQuotas.For(PlanTier.Free);

        Assert.True(free.DailyAiTokens > 0);
        Assert.True(free.GatewayRequestsPerMinute > 0);
    }

    // ── Abonelik durumundan plan çıkarımı ────────────────────────────────────

    [Theory]
    [InlineData("active")]
    [InlineData("trialing")]
    [InlineData("ACTIVE")]
    public void A_live_subscription_is_a_paid_plan(string status)
    {
        Assert.Equal(PlanTier.Pro, PlanQuotas.Resolve(status));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("canceled")]
    [InlineData("unpaid")]
    [InlineData("past_due")]
    [InlineData("something_new_stripe_invented")]
    public void Anything_else_falls_to_free(string? status)
    {
        // Ters yönde düşmek — bilinmeyen bir durumu ücretli saymak — ödeme
        // yapmamış birine ücretli kaynak açtırırdı ve bu, faturayı büyütmekten
        // başka işe yaramaz.
        Assert.Equal(PlanTier.Free, PlanQuotas.Resolve(status));
    }

    // ── Sınır kontrolü ───────────────────────────────────────────────────────

    [Fact]
    public void Unlimited_is_never_reported_as_exceeded()
    {
        // -1 "sınırsız" demek. Onu bir sayı gibi karşılaştırmak, sınırsız planı
        // en kısıtlı plan hâline getirirdi.
        Assert.False(PlanQuotas.IsExceeded(-1, 1_000_000));
    }

    [Fact]
    public void A_zero_limit_means_the_feature_is_off()
    {
        Assert.True(PlanQuotas.IsExceeded(0, 0));
        Assert.Contains("not available", PlanQuotas.LimitMessage(PlanTier.Free, "branch databases", 0));
    }

    [Fact]
    public void The_limit_message_says_what_to_do_next()
    {
        // "Limit aşıldı" demek yetmez; kullanıcı ne yapacağını bilmeli.
        var message = PlanQuotas.LimitMessage(PlanTier.Pro, "branch databases", 2);

        Assert.Contains("upgrade", message, StringComparison.OrdinalIgnoreCase);
    }
}
