using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Contracts;
using Namines.BuildingBlocks.Security;
using Namines.Core.Analysis;

namespace Namines.Billing.Application;

/// <summary>
/// Kullanıcının planını çözen TEK yer. Abonelik Billing'in, "geliştirici hesabı" bayrağı Identity'nin
/// (Stripe'ın yazdığı bir alanla yetki ayrı kalsın diye ayrı kolon): ikisi burada birleşir.
/// </summary>
internal sealed class PlanResolver
{
    private readonly IBillingData _data;
    private readonly IIdentityDirectory _identity;

    public PlanResolver(IBillingData data, IIdentityDirectory identity)
    {
        _data = data;
        _identity = identity;
    }

    /// <summary>Kullanıcı yoksa null (bilinmeyen kullanıcı için plan uydurulmaz).</summary>
    public async Task<UserPlanSnapshot?> SnapshotAsync(string userId, CancellationToken ct)
    {
        var facts = await _identity.GetUserFactsAsync(userId, ct);
        if (facts is null) return null;

        var subscription = await _data.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);
        return new UserPlanSnapshot(userId, subscription?.Status, subscription?.PlanCode, facts.IsDev);
    }

    public async Task<PlanTier> TierAsync(string userId, CancellationToken ct)
    {
        var snapshot = await SnapshotAsync(userId, ct);
        return PlanQuotas.Resolve(snapshot?.SubscriptionStatus, snapshot?.PlanCode, snapshot?.IsDev ?? false);
    }
}
