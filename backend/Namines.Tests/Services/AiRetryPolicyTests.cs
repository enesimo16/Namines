using System;
using Namines.Core.Interfaces;
using Xunit;

namespace Namines.Tests.Services;

public class AiRetryPolicyTests
{
    [Fact]
    public void Grants_the_requested_delay_when_within_budget()
    {
        var policy = new AiRetryPolicy(maxTotalWaitSeconds: 600, maxSingleWaitSeconds: 60);

        Assert.True(policy.TryNextDelay(TimeSpan.FromSeconds(20), out var granted));
        Assert.Equal(TimeSpan.FromSeconds(20), granted);
    }

    [Fact]
    public void Caps_a_single_absurd_delay_instead_of_hanging_the_request()
    {
        // Sağlayıcı "3600 saniye bekle" derse isteği bir saat asılı bırakmak,
        // hata döndürmekten daha kötü: kullanıcı ne olduğunu hiç öğrenemez.
        var policy = new AiRetryPolicy(maxTotalWaitSeconds: 600, maxSingleWaitSeconds: 60);

        Assert.True(policy.TryNextDelay(TimeSpan.FromSeconds(3600), out var granted));
        Assert.Equal(TimeSpan.FromSeconds(60), granted);
    }

    [Fact]
    public void Stops_once_the_total_budget_is_spent()
    {
        var policy = new AiRetryPolicy(maxTotalWaitSeconds: 50, maxSingleWaitSeconds: 60);

        Assert.True(policy.TryNextDelay(TimeSpan.FromSeconds(30), out _));
        Assert.True(policy.TryNextDelay(TimeSpan.FromSeconds(20), out _));
        Assert.False(policy.TryNextDelay(TimeSpan.FromSeconds(1), out _));
    }

    [Fact]
    public void Trims_the_last_delay_to_what_is_left_rather_than_refusing_it()
    {
        // Bütçenin 10 saniyesi kalmışken 30 saniyelik bir bekleme isteğini
        // tamamen reddetmek, elimizdeki bütçeyi kullanmadan pes etmek olurdu.
        var policy = new AiRetryPolicy(maxTotalWaitSeconds: 40, maxSingleWaitSeconds: 60);

        Assert.True(policy.TryNextDelay(TimeSpan.FromSeconds(30), out _));
        Assert.True(policy.TryNextDelay(TimeSpan.FromSeconds(30), out var granted));
        Assert.Equal(TimeSpan.FromSeconds(10), granted);
    }

    [Fact]
    public void Disabled_policy_never_waits()
    {
        Assert.False(AiRetryPolicy.Disabled.TryNextDelay(TimeSpan.FromSeconds(1), out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_budget_disables_retrying(int budget)
    {
        var policy = new AiRetryPolicy(budget, maxSingleWaitSeconds: 60);
        Assert.False(policy.TryNextDelay(TimeSpan.FromSeconds(1), out _));
    }

    [Fact]
    public void A_zero_requested_delay_retries_promptly_and_costs_no_budget()
    {
        // Sağlayıcı "Retry-After: 0" derse hemen yeniden denemek doğru davranış.
        var policy = new AiRetryPolicy(maxTotalWaitSeconds: 600, maxSingleWaitSeconds: 60);

        Assert.True(policy.TryNextDelay(TimeSpan.Zero, out var granted));
        Assert.Equal(TimeSpan.Zero, granted);
        Assert.Equal(TimeSpan.Zero, policy.Spent);
    }

    [Fact]
    public void A_zero_delay_still_terminates_because_attempts_are_capped()
    {
        // BULUNMA YERİ: ilk sürümde tek durdurucu süre bütçesiydi ve sıfır
        // bekleme bütçeden hiçbir şey yemiyordu. Sağlayıcı sürekli
        // "Retry-After: 0" derse döngü hiç bitmiyor, kullanıcıya dönmeyen ve
        // sağlayıcıyı ağ hızında döven bir tur oluşuyordu.
        var policy = new AiRetryPolicy(
            maxTotalWaitSeconds: 600, maxSingleWaitSeconds: 60, maxAttempts: 3);

        for (var i = 0; i < 3; i++)
            Assert.True(policy.TryNextDelay(TimeSpan.Zero, out _));

        Assert.False(policy.TryNextDelay(TimeSpan.Zero, out _));
        Assert.Equal(TimeSpan.Zero, policy.Spent);
    }

    [Fact]
    public void Sub_second_delays_cannot_produce_thousands_of_attempts()
    {
        // Sıfır gerekmiyor bile: sağlayıcı ondalıklı süre veriyor ve
        // tekrarlayan bir "0.017s", 600 saniyelik bütçede otuz beş bin istek
        // demekti. Deneme sayacı buna da sınır koyuyor.
        var policy = new AiRetryPolicy(
            maxTotalWaitSeconds: 600, maxSingleWaitSeconds: 60, maxAttempts: 10);

        var granted = 0;
        while (policy.TryNextDelay(TimeSpan.FromMilliseconds(17), out _)) granted++;

        Assert.Equal(10, granted);
    }

    [Fact]
    public void Attempts_are_counted_across_threads_without_overshooting_the_cap()
    {
        // Aynı istek içindeki paralel parçalar bu nesneyi PAYLAŞIYOR; sayaç
        // korunmazsa iki parça aynı anda son hakkı alabilirdi.
        var policy = new AiRetryPolicy(
            maxTotalWaitSeconds: 600, maxSingleWaitSeconds: 60, maxAttempts: 50);

        var granted = 0;
        // Lambda parametresi bilerek adlandırıldı: "_" olsaydı aşağıdaki
        // "out _" onu hedef alır ve derleme hatası verirdi.
        System.Threading.Tasks.Parallel.For(0, 200, index =>
        {
            if (policy.TryNextDelay(TimeSpan.FromSeconds(1), out _))
                System.Threading.Interlocked.Increment(ref granted);
        });

        Assert.Equal(50, granted);
        Assert.Equal(50, policy.Attempts);
    }
}
