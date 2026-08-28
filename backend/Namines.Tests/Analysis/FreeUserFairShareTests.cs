using Namines.Infrastructure.Data;

namespace Namines.Tests.Analysis;

/// <summary>
/// Ücretsiz kullanıcı tavanının havuzdan ADİL PAY ile sınırlanması.
///
/// <b>Çözdüğü gerçek sorun:</b> plan tavanı 20.000, havuz 100.000'di — yani
/// günün ilk BEŞ kullanıcısı havuzun tamamını tüketebiliyor, altıncıdan
/// itibaren gelen herkes kendi hakkı hiç dolmamışken duvara çarpıyordu.
/// Ücretsiz katmanın vaadi "ilk gelene" değil, "gelen herkese".
/// </summary>
public class FreeUserFairShareTests
{
    private const int PlanCap = 20_000;

    [Fact]
    public void Five_users_can_no_longer_drain_a_pool_meant_for_a_hundred()
    {
        // Eski davranış: 100.000 havuz, 20.000 tavan → 5 kullanıcı havuzu bitirir.
        var cap = AiQuotaService.CalculateFreeUserCap(
            planCap: PlanCap, dailyPool: 100_000, minDailyFreeUsers: 100, minUsefulDailyTokens: 1);

        // pay = 1.000, iki katı = 2.000 → tavan artık 20.000 DEĞİL.
        Assert.Equal(2_000, cap);
        Assert.True(100_000 / cap >= 50, "havuz en az 50 kullanıcıya yetmeli");
    }

    [Fact]
    public void A_pool_sized_for_the_target_lets_every_user_use_their_full_plan_cap()
    {
        // 2.000.000 / 100 = 20.000 pay; iki katı 40.000 ama plan tavanı 20.000
        // olduğu için tavan bağlıyor — yani havuz darboğaz DEĞİL.
        var cap = AiQuotaService.CalculateFreeUserCap(
            planCap: PlanCap, dailyPool: 2_000_000, minDailyFreeUsers: 100, minUsefulDailyTokens: 8_000);

        Assert.Equal(PlanCap, cap);
    }

    [Fact]
    public void The_useful_floor_wins_over_a_crumb_sized_fair_share()
    {
        // 100.000 / 1000 = 100 token. O bütçeyle kullanıcı bir şema üretemez;
        // "bütçen var" deyip ortasında kesmek hiç başlatmamaktan kötü.
        var cap = AiQuotaService.CalculateFreeUserCap(
            planCap: PlanCap, dailyPool: 100_000, minDailyFreeUsers: 1_000, minUsefulDailyTokens: 8_000);

        Assert.Equal(8_000, cap);
    }

    [Fact]
    public void The_plan_cap_is_never_exceeded_however_large_the_pool()
    {
        var cap = AiQuotaService.CalculateFreeUserCap(
            planCap: PlanCap, dailyPool: 100_000_000, minDailyFreeUsers: 10, minUsefulDailyTokens: 8_000);

        Assert.Equal(PlanCap, cap);
    }

    [Fact]
    public void A_user_may_burst_to_twice_their_fair_share_but_not_more()
    {
        // Tam paya kilitlemek boşta duran payı çöpe atardı; tamamına açmak
        // birinin hepsini yemesine izin verirdi. İkiye katlamak ortası —
        // Team havuzundaki kuralla aynı.
        var cap = AiQuotaService.CalculateFreeUserCap(
            planCap: 1_000_000, dailyPool: 500_000, minDailyFreeUsers: 50, minUsefulDailyTokens: 1);

        Assert.Equal(20_000, cap); // pay 10.000 → iki katı
    }

    [Fact]
    public void A_zero_target_falls_back_to_the_plan_cap_instead_of_dividing_by_zero()
    {
        var cap = AiQuotaService.CalculateFreeUserCap(
            planCap: PlanCap, dailyPool: 100_000, minDailyFreeUsers: 0, minUsefulDailyTokens: 8_000);

        Assert.Equal(PlanCap, cap);
    }
}
