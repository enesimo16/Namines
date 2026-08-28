using System.Threading.Tasks;
using Namines.Core.Analysis;
using Namines.Infrastructure.AI;

namespace Namines.Tests.Analysis;

/// <summary>
/// Tek bir çağrının harcayabileceği token tavanı ve gerçek kullanımın ölçülmesi.
///
/// <b>Çözdüğü sorun:</b> <c>max_tokens</c> kullanıcıdan geliyordu ve plana
/// bakılmıyordu; ücretsiz bir kullanıcı 32.000 yazabiliyordu. Kota da tur
/// başına sabit 2.500 düştüğü için bu, aynı kotayla gerçekte on kat fazla
/// harcamak demekti — sayaç ölçmüyor, sadece sayıyordu.
/// </summary>
public class TokenCeilingTests
{
    private static AiAdvancedSettings WithMaxTokens(string value) =>
        AiAdvancedSettings.Default with { MaxTokens = value };

    [Fact]
    public void A_free_user_cannot_request_the_full_provider_ceiling()
    {
        var settings = WithMaxTokens("32000");

        Assert.Equal(32_000, settings.MaxTokensValue);          // ham istek
        Assert.Equal(6_000, settings.MaxTokensFor(PlanTier.Free)); // plana çekilmiş
    }

    [Fact]
    public void Paid_plans_get_progressively_higher_ceilings()
    {
        var settings = WithMaxTokens("32000");

        var free = settings.MaxTokensFor(PlanTier.Free);
        var pro = settings.MaxTokensFor(PlanTier.Pro);
        var team = settings.MaxTokensFor(PlanTier.Team);

        Assert.True(free < pro, "Pro, Free'den yüksek olmalı");
        Assert.True(pro <= team, "Team, Pro'dan düşük olmamalı");
    }

    [Fact]
    public void A_modest_request_is_never_raised_to_the_ceiling()
    {
        // Tavan bir SINIR, bir varsayılan değil — kullanıcı 1.000 istediyse
        // 6.000 vermek, istemediği bir maliyeti ona yüklemek olurdu.
        var settings = WithMaxTokens("1000");

        Assert.Equal(1_000, settings.MaxTokensFor(PlanTier.Free));
        Assert.Equal(1_000, settings.MaxTokensFor(PlanTier.Team));
    }

    [Fact]
    public void A_garbage_value_falls_back_to_the_default_and_is_still_capped()
    {
        var settings = WithMaxTokens("sonsuz");

        Assert.Equal(4_096, settings.MaxTokensValue);
        Assert.Equal(4_096, settings.MaxTokensFor(PlanTier.Free));
    }

    // ── Gerçek kullanım ölçümü ───────────────────────────────────────────────

    [Fact]
    public void The_tracker_reports_nothing_until_the_provider_actually_measures()
    {
        var tracker = new AiUsageTracker();

        Assert.False(tracker.HasMeasurement);
        Assert.Equal(0, tracker.TotalTokens);
    }

    [Fact]
    public void Usage_accumulates_across_the_rounds_of_one_request()
    {
        // Şema hattı bir istekte birden çok çağrı yapıyor (draft → inspect →
        // repair); kota bunların TOPLAMINI düşmeli, sonuncusunu değil.
        var tracker = new AiUsageTracker();

        tracker.Record(1_200);
        tracker.Record(900);
        tracker.Record(2_400);

        Assert.True(tracker.HasMeasurement);
        Assert.Equal(4_500, tracker.TotalTokens);
    }

    [Fact]
    public void A_zero_or_negative_report_is_ignored_rather_than_counted_as_a_measurement()
    {
        // Sağlayıcı usage döndürmediğinde çağıran tahmine düşmeli; sıfırı
        // "ölçüldü ve sıfır" saymak, harcamayı bedava göstermek olurdu.
        var tracker = new AiUsageTracker();

        tracker.Record(0);
        tracker.Record(-5);

        Assert.False(tracker.HasMeasurement);
        Assert.Equal(0, tracker.TotalTokens);
    }

    [Fact]
    public async Task Concurrent_rounds_do_not_lose_each_others_usage()
    {
        // Turlar ileride paralelleşebilir; kayıp artış eksik ölçüm demek ve
        // eksik ölçüm tam da düzeltmeye çalıştığımız hata.
        var tracker = new AiUsageTracker();

        await Task.WhenAll(Enumerable.Range(0, 200)
            .Select(_ => Task.Run(() => tracker.Record(10))));

        Assert.Equal(2_000, tracker.TotalTokens);
    }
}
