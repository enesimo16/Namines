using Namines.Core.Analysis;

namespace Namines.Tests.Analysis;

/// <summary>
/// Plan kotaları (new-phase/06-DATA-PLANE.md §10).
///
/// Kotanın iki yönü de yanlış olabilir ve ikisi de kötü: fazla gevşek olması
/// sunucuyu düşürür, fazla katı olması ödeme yapan kullanıcıyı engeller. Testler
/// sınırların dokümandaki tabloyla aynı olduğunu ve "sınırsız" ile "sıfır"ın
/// birbirine karışmadığını kilitliyor.
/// </summary>
public class PlanQuotaTests
{
    [Fact]
    public void Free_cannot_open_a_branch_database()
    {
        // Her branch veritabanı host'ta kalıcı bir container; ücretsiz katmanda
        // sınırsız açılması sunucuyu düşürür.
        Assert.Equal(0, PlanQuotas.For(PlanTier.Free).BranchDatabases);
    }

    [Theory]
    [InlineData(PlanTier.Pro, 2)]
    [InlineData(PlanTier.Team, 20)]
    public void Paid_tiers_match_the_documented_table(PlanTier tier, int expected)
    {
        Assert.Equal(expected, PlanQuotas.For(tier).BranchDatabases);
    }

    [Fact]
    public void Enterprise_is_unlimited_not_zero()
    {
        // -1 "sınırsız" demek. 0 ile karıştırılırsa en pahalı plan hiçbir şey
        // açamaz hâle gelir — sessiz ve utanç verici bir hata.
        var limits = PlanQuotas.For(PlanTier.Enterprise);

        Assert.Equal(-1, limits.BranchDatabases);
        Assert.False(PlanQuotas.IsExceeded(limits.BranchDatabases, 1000));
    }

    [Fact]
    public void A_zero_limit_is_exceeded_by_the_very_first_request()
    {
        Assert.True(PlanQuotas.IsExceeded(0, 0));
    }

    [Fact]
    public void A_limit_is_reached_at_the_limit_not_after_it()
    {
        // 2 sınırında 2 açıkken üçüncü istek reddedilmeli; ">" kullanılsaydı
        // kullanıcı her zaman bir fazla açardı.
        Assert.False(PlanQuotas.IsExceeded(2, 1));
        Assert.True(PlanQuotas.IsExceeded(2, 2));
        Assert.True(PlanQuotas.IsExceeded(2, 3));
    }

    [Theory]
    [InlineData("active", PlanTier.Pro)]
    [InlineData("trialing", PlanTier.Pro)]
    [InlineData("ACTIVE", PlanTier.Pro)]
    public void An_active_subscription_is_pro(string status, PlanTier expected)
    {
        Assert.Equal(expected, PlanQuotas.Resolve(status));
    }

    [Theory]
    [InlineData("past_due")]
    [InlineData("canceled")]
    [InlineData("unpaid")]
    [InlineData(null)]
    [InlineData("")]
    public void A_lapsed_or_missing_subscription_is_free(string? status)
    {
        // Ödemesi aksayan bir hesabın ücretli kaynak açmaya devam etmesi, faturayı
        // büyütmekten başka işe yaramaz.
        Assert.Equal(PlanTier.Free, PlanQuotas.Resolve(status));
    }

    [Fact]
    public void The_limit_message_tells_the_user_what_to_do()
    {
        var unavailable = PlanQuotas.LimitMessage(PlanTier.Free, "branch databases", 0);
        Assert.Contains("not available", unavailable);
        Assert.Contains("Upgrade", unavailable);

        var reached = PlanQuotas.LimitMessage(PlanTier.Pro, "branch databases", 2);
        Assert.Contains("2", reached);
        Assert.Contains("Close one", reached);
    }
}
