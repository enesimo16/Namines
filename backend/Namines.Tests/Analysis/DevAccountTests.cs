using Namines.Core.Analysis;

namespace Namines.Tests.Analysis;

/// <summary>
/// Sahip/geliştirici hesabının plan davranışı.
///
/// <b>Neden test ediliyor:</b> "sınırsız" bir hesabın en sinsi kırılma biçimi,
/// sınırsızlığın yanlış işaretle ifade edilmesi — bir yerde -1 "sınırsız"
/// demekken başka bir yerde "her isteği reddet" anlamına gelebiliyor.
/// </summary>
public class DevAccountTests
{
    [Fact]
    public void The_owner_flag_beats_the_subscription_status()
    {
        // Sahip hesabının Stripe'ta kaydı yok, yani SubscriptionStatus'ü boş.
        // Önce aboneliğe bakılsaydı kendi ürününün sahibi Free katmanda kalırdı.
        Assert.Equal(PlanTier.Dev, PlanQuotas.Resolve(null, isDev: true));
        Assert.Equal(PlanTier.Dev, PlanQuotas.Resolve("canceled", isDev: true));
        Assert.Equal(PlanTier.Dev, PlanQuotas.Resolve("past_due", isDev: true));
    }

    [Fact]
    public void Nobody_becomes_a_dev_by_paying()
    {
        // Dev rolü satılabilir bir plan değil; yalnızca bayrakla gelir.
        Assert.Equal(PlanTier.Pro, PlanQuotas.Resolve("active", isDev: false));
        Assert.Equal(PlanTier.Free, PlanQuotas.Resolve(null, isDev: false));
    }

    // ── Pro / Team ayrımı (plan kodu) ────────────────────────────────────────

    [Fact]
    public void An_active_subscription_without_a_plan_code_defaults_to_pro()
    {
        // Checkout.session.completed tetiklenirken plan kodu henüz okunamamış
        // olabilir; bilinmeyen kod Free'ye değil Pro'ya düşüyor — ödeyen birine
        // hizmet vermemek, fazla vermekten daha kötü bir hata.
        Assert.Equal(PlanTier.Pro, PlanQuotas.Resolve("active", planCode: null, isDev: false));
        Assert.Equal(PlanTier.Pro, PlanQuotas.Resolve("active", planCode: "unknown", isDev: false));
    }

    [Fact]
    public void The_team_plan_code_only_applies_while_the_subscription_is_active()
    {
        // Ödemesi aksamış bir Team hesabının eski plan kodu veritabanında
        // kalmaya devam edebilir; onu okumak, ödemeyi durduran birine Team
        // hakkı vermeye devam etmek olurdu.
        Assert.Equal(PlanTier.Team, PlanQuotas.Resolve("active", planCode: "team", isDev: false));
        Assert.Equal(PlanTier.Free, PlanQuotas.Resolve("canceled", planCode: "team", isDev: false));
        Assert.Equal(PlanTier.Free, PlanQuotas.Resolve("past_due", planCode: "team", isDev: false));
    }

    [Fact]
    public void Dev_still_beats_a_team_plan_code()
    {
        Assert.Equal(PlanTier.Dev, PlanQuotas.Resolve("active", planCode: "team", isDev: true));
    }

    [Fact]
    public void The_owner_token_budget_is_an_arithmetic_ceiling_not_minus_one()
    {
        // Günlük token kontrolü "kullanılan + istenen > tavan" aritmetiği.
        // Oraya -1 koymak her isteği "tavanı aştın" saydırırdı: sınırsız hesap
        // hiçbir şey yapamazdı.
        var limits = PlanQuotas.For(PlanTier.Dev);

        Assert.True(limits.DailyAiTokens > 0);
        Assert.False(0 + 100_000 > limits.DailyAiTokens);
    }

    [Fact]
    public void Counted_resources_use_minus_one_for_unlimited()
    {
        // Sayaç alanlarında -1 "sınırsız" demek ve IsExceeded bunu bilmeli.
        var limits = PlanQuotas.For(PlanTier.Dev);

        Assert.Equal(-1, limits.BranchDatabases);
        Assert.Equal(-1, limits.EphemeralRunsPerDay);
        Assert.Equal(-1, limits.ByodbConnections);
        Assert.False(PlanQuotas.IsExceeded(limits.BranchDatabases, current: 9999));
    }

    [Fact]
    public void The_owner_reaches_the_most_capable_model()
    {
        // Sınırsız bir hesabın en pahalı modele erişememesi, sınırsızlığın
        // yarısını yok sayardı.
        Assert.Equal(NaiModel.Pro, NaiCatalog.ClampToPlan(NaiModel.Pro, PlanTier.Dev));
    }

    [Fact]
    public void The_owner_beats_every_sellable_plan()
    {
        // ClampToPlan "daha büyük plan daha çok hak" varsayımıyla karşılaştırma
        // yapıyor; Owner enum'un sonunda olmazsa sessizce Free gibi davranırdı.
        foreach (PlanTier tier in Enum.GetValues<PlanTier>())
        {
            if (tier == PlanTier.Dev) continue;

            Assert.True(PlanTier.Dev > tier, $"{tier} sorts above Owner.");
        }
    }
}
