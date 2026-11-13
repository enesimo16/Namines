using Namines.Billing.Application;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// 14.2D-2 denetimi R3-n1: checkout ve <c>customer.subscription.updated</c> yolları erişimi kesen durumlar için
/// AYNI listeyi kullanmalı. Önceden güncelleme yolu <c>incomplete_expired</c> ve <c>paused</c>'da kullanıcıyı
/// ücretli bırakıyordu.
/// </summary>
public sealed class SubscriptionAccessTests
{
    [Theory]
    [InlineData("canceled")]
    [InlineData("unpaid")]
    [InlineData("incomplete_expired")]
    [InlineData("paused")]
    public void These_statuses_revoke_paid_access(string status) =>
        Assert.True(SubscriptionAccess.Revokes(status));

    [Theory]
    [InlineData("active")]
    [InlineData("trialing")]
    [InlineData("past_due")] // yumuşak dönem: kesin düşüş deleted/unpaid'de
    [InlineData("incomplete")]
    [InlineData(null)]
    public void These_statuses_do_not_revoke_on_their_own(string? status) =>
        Assert.False(SubscriptionAccess.Revokes(status));
}
