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
    public void A_zero_or_negative_requested_delay_still_costs_nothing_and_is_allowed()
    {
        // Sağlayıcı "Retry-After: 0" derse hemen yeniden denemek doğru davranış.
        var policy = new AiRetryPolicy(maxTotalWaitSeconds: 600, maxSingleWaitSeconds: 60);

        Assert.True(policy.TryNextDelay(TimeSpan.Zero, out var granted));
        Assert.Equal(TimeSpan.Zero, granted);
    }
}
