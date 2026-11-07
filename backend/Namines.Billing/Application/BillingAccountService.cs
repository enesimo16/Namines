using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Namines.Billing.Domain;
using Namines.BuildingBlocks.Contracts;
using Namines.BuildingBlocks.Security;
using Namines.Core.Analysis;

namespace Namines.Billing.Application;

/// <summary>
/// Plan, abonelik ve kota okumaları ile hesap silme/saklama temizliği. Kimlik bilgisi <see cref="IIdentityDirectory"/>'den,
/// çünkü Billing kullanıcı tablosunu tanımaz.
/// </summary>
internal sealed class BillingAccountService : IBillingAccounts, IBillingMaintenance
{
    private readonly IBillingData _data;
    private readonly IIdentityDirectory _identity;
    private readonly IAiQuota _quota;
    private readonly PlanResolver _plans;
    private readonly ISubscriptionCanceller? _canceller;
    private readonly IReadOnlyCollection<ISubscriptionCancellationObserver> _observers;
    private readonly ILogger<BillingAccountService> _logger;

    public BillingAccountService(
        IBillingData data, IIdentityDirectory identity, IAiQuota quota,
        ILogger<BillingAccountService> logger, ISubscriptionCanceller? canceller = null,
        IEnumerable<ISubscriptionCancellationObserver>? observers = null)
    {
        _data = data;
        _identity = identity;
        _quota = quota;
        _plans = new PlanResolver(data, identity);
        _canceller = canceller;
        _observers = observers?.ToList() ?? new List<ISubscriptionCancellationObserver>();
        _logger = logger;
    }

    public Task<UserPlanSnapshot?> GetPlanSnapshotAsync(string userId, CancellationToken ct = default) =>
        _plans.SnapshotAsync(userId, ct);

    public async Task<SubscriptionView?> GetSubscriptionAsync(string userId, CancellationToken ct = default)
    {
        var all = await GetSubscriptionsAsync(new[] { userId }, ct);
        return all.TryGetValue(userId, out var view) ? view : null;
    }

    public async Task<IReadOnlyDictionary<string, SubscriptionView>> GetSubscriptionsAsync(IReadOnlyCollection<string> userIds, CancellationToken ct = default)
    {
        if (userIds is not { Count: > 0 }) return new Dictionary<string, SubscriptionView>();

        var subscriptions = await _data.Subscriptions.AsNoTracking()
            .Where(s => userIds.Contains(s.UserId)).ToDictionaryAsync(s => s.UserId, ct);
        var customers = await _data.Customers.AsNoTracking()
            .Where(c => userIds.Contains(c.UserId)).Select(c => c.UserId).ToListAsync(ct);
        var withCustomer = customers.ToHashSet();

        var result = new Dictionary<string, SubscriptionView>();
        foreach (var id in userIds.Distinct())
        {
            subscriptions.TryGetValue(id, out var s);
            result[id] = new SubscriptionView(id, s?.Status, s?.PlanCode, s?.CurrentPeriodEnd, withCustomer.Contains(id));
        }
        return result;
    }

    public async Task<(int Pro, int Team)> CountPaidPlansAsync(IReadOnlyCollection<string> excludeUserIds, CancellationToken ct = default)
    {
        var pro = await _data.Subscriptions.AsNoTracking().CountAsync(s => s.PlanCode == "pro" && !excludeUserIds.Contains(s.UserId), ct);
        var team = await _data.Subscriptions.AsNoTracking().CountAsync(s => s.PlanCode == "team" && !excludeUserIds.Contains(s.UserId), ct);
        return (pro, team);
    }

    public async Task<IReadOnlyList<string>> ListUserIdsByPlanAsync(string planCode, CancellationToken ct = default) =>
        await _data.Subscriptions.AsNoTracking().Where(s => s.PlanCode == planCode).Select(s => s.UserId).ToListAsync(ct);

    public async Task<QuotaRow> AdminSetPlanAsync(string userId, string planCode, bool isDev, CancellationToken ct = default)
    {
        var subscription = await _data.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (subscription is null)
        {
            subscription = new BillingSubscription { UserId = userId };
            _data.Subscriptions.Add(subscription);
        }

        subscription.PlanCode = planCode;
        subscription.UpdatedAt = DateTime.UtcNow;

        var quota = await _data.UserAIQuotas.FirstOrDefaultAsync(q => q.UserId == userId, ct);
        if (quota is null)
        {
            quota = new UserAIQuota { UserId = userId, DailyUsageCount = 0, LastResetDate = DateTime.UtcNow };
            _data.UserAIQuotas.Add(quota);
        }

        quota.DailyLimit = isDev ? int.MaxValue : planCode switch { "team" => 2000, "pro" => 500, _ => 100 };
        await _data.SaveChangesAsync(ct);
        return new QuotaRow(userId, quota.DailyLimit, quota.DailyUsageCount, quota.LastResetDate);
    }

    public async Task<QuotaRow?> GetQuotaRowAsync(string userId, CancellationToken ct = default)
    {
        var rows = await GetQuotaRowsAsync(new[] { userId }, ct);
        return rows.TryGetValue(userId, out var row) ? row : null;
    }

    public async Task<IReadOnlyDictionary<string, QuotaRow>> GetQuotaRowsAsync(IReadOnlyCollection<string> userIds, CancellationToken ct = default)
    {
        if (userIds is not { Count: > 0 }) return new Dictionary<string, QuotaRow>();
        return await _data.UserAIQuotas.AsNoTracking()
            .Where(q => userIds.Contains(q.UserId))
            .ToDictionaryAsync(q => q.UserId, q => new QuotaRow(q.UserId, q.DailyLimit, q.DailyUsageCount, q.LastResetDate), ct);
    }

    public async Task<QuotaRow> AdminAdjustQuotaAsync(string userId, bool isDev, bool resetToZero, int? bonusPoints, int maxBonus, CancellationToken ct = default)
    {
        var quota = await _data.UserAIQuotas.FirstOrDefaultAsync(q => q.UserId == userId, ct);
        if (quota is null)
        {
            quota = new UserAIQuota { UserId = userId, DailyLimit = isDev ? int.MaxValue : 100, DailyUsageCount = 0, LastResetDate = DateTime.UtcNow };
            _data.UserAIQuotas.Add(quota);
        }

        if (resetToZero)
        {
            quota.DailyUsageCount = 0;
            quota.LastResetDate = DateTime.UtcNow;
        }

        if (bonusPoints is > 0 && quota.DailyLimit != int.MaxValue)
        {
            var bonus = Math.Min(bonusPoints.Value, maxBonus);
            quota.DailyLimit = (int)Math.Min((long)quota.DailyLimit + bonus, int.MaxValue - 1);
        }

        await _data.SaveChangesAsync(ct);
        return new QuotaRow(userId, quota.DailyLimit, quota.DailyUsageCount, quota.LastResetDate);
    }

    public async Task<long> SumQuotaUsedTodayAsync(CancellationToken ct = default) =>
        await _data.UserAIQuotas.AsNoTracking().SumAsync(q => (long)q.DailyUsageCount, ct);

    public async Task<CancellationBacklog> GetCancellationBacklogAsync(int take, CancellationToken ct = default)
    {
        var open = _data.PendingSubscriptionCancellations.AsNoTracking().Where(c => c.CompletedAt == null);
        var total = await open.CountAsync(ct);
        var stuck = await open.CountAsync(c => c.Attempts >= SubscriptionCancellationProcessor.AlertAfterAttempts, ct);
        var items = await open
            .OrderByDescending(c => c.Attempts).ThenBy(c => c.CreatedAt)
            .Take(take)
            .Select(c => new PendingCancellationRow(
                c.Id, c.SubscriptionId, c.CreatedAt, c.Attempts, c.LastAttemptAt, c.NextAttemptAt,
                c.Attempts >= SubscriptionCancellationProcessor.AlertAfterAttempts))
            .ToListAsync(ct);
        return new CancellationBacklog(total, stuck, items);
    }

    public async Task<IReadOnlyList<string>> ListUserIdsWithPaidPlanAsync(CancellationToken ct = default) =>
        await _data.Subscriptions.AsNoTracking()
            .Where(s => s.PlanCode != null && s.PlanCode != "" && s.PlanCode != "free")
            .Select(s => s.UserId).ToListAsync(ct);

    public async Task<QuotaRow> ProvisionDefaultQuotaAsync(string userId, CancellationToken ct = default)
    {
        var existing = await GetQuotaRowAsync(userId, ct);
        if (existing is not null) return existing;

        var quota = new UserAIQuota { UserId = userId, DailyLimit = 100, DailyUsageCount = 0, LastResetDate = DateTime.UtcNow };
        _data.UserAIQuotas.Add(quota);
        await _data.SaveChangesAsync(ct);
        return new QuotaRow(userId, quota.DailyLimit, quota.DailyUsageCount, quota.LastResetDate);
    }

    public async Task<QuotaRow> SyncQuotaOnLoginAsync(string userId, bool isDev, CancellationToken ct = default)
    {
        var defaultLimit = isDev ? int.MaxValue : 100;
        var quota = await _data.UserAIQuotas.FirstOrDefaultAsync(q => q.UserId == userId, ct);
        if (quota is null)
        {
            quota = new UserAIQuota { UserId = userId, DailyLimit = defaultLimit, DailyUsageCount = 0, LastResetDate = DateTime.UtcNow };
            _data.UserAIQuotas.Add(quota);
        }
        else
        {
            if (isDev) quota.DailyLimit = int.MaxValue;
            else if (quota.DailyLimit < 100) quota.DailyLimit = 100;

            // Gün sınırı Türkiye saatine göre (UTC+3).
            if (quota.LastResetDate.AddHours(3).Date < DateTime.UtcNow.AddHours(3).Date)
            {
                quota.DailyUsageCount = 0;
                quota.LastResetDate = DateTime.UtcNow;
            }
        }

        await _data.SaveChangesAsync(ct);
        return new QuotaRow(userId, quota.DailyLimit, quota.DailyUsageCount, quota.LastResetDate);
    }

    public async Task<IReadOnlyList<TokenDayRow>> ListGlobalUsageAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
        await _data.GlobalAiUsages.AsNoTracking()
            .Where(g => g.Date >= fromUtc && g.Date <= toUtc)
            .OrderBy(g => g.Date)
            .Select(g => new TokenDayRow(g.Date, g.TokensUsed))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TokenDayRow>> ListUsageCallsPerDayAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var rows = await _data.UsageEvents.AsNoTracking()
            .Where(e => e.Resource == Namines.Core.Models.Auth.UsageResource.AiCall && e.OccurredAt >= fromUtc && e.OccurredAt <= toUtc)
            .Select(e => e.OccurredAt)
            .ToListAsync(ct);
        return rows.GroupBy(r => r.Date).OrderBy(g => g.Key).Select(g => new TokenDayRow(g.Key, g.Count())).ToList();
    }

    public async Task<IReadOnlyList<QuotaRow>> ListTopQuotaUsersAsync(int take, CancellationToken ct = default) =>
        await _data.UserAIQuotas.AsNoTracking()
            .OrderByDescending(q => q.DailyUsageCount)
            .Take(take)
            .Select(q => new QuotaRow(q.UserId, q.DailyLimit, q.DailyUsageCount, q.LastResetDate))
            .ToListAsync(ct);

    public async Task<string?> QueueSubscriptionCancellationAsync(string userId, CancellationToken ct = default)
    {
        var subscription = await _data.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (string.IsNullOrWhiteSpace(subscription?.StripeSubscriptionId)) return null;

        // Kullanıcıya FK yok ve kullanıcı kimliği tutulmuyor: satır hesaptan uzun yaşamak zorunda.
        var outbox = new PendingSubscriptionCancellation { SubscriptionId = subscription.StripeSubscriptionId };
        _data.PendingSubscriptionCancellations.Add(outbox);
        await _data.SaveChangesAsync(ct);
        return outbox.Id;
    }

    public async Task<bool> TryCancelNowAsync(string outboxId, CancellationToken ct = default) =>
        await new SubscriptionCancellationProcessor(_data, _canceller, _logger, _observers).AttemptAsync(outboxId, DateTime.UtcNow);

    public async Task<int> ErasePersonalDataAsync(string userId, CancellationToken ct = default)
    {
        var erased = 0;

        // Kullanım olayı faturalama kaydı: vergi/mutabakat saklama süresi boyunca satır kalır, kişi bağı kopar.
        erased += await _data.UsageEvents.Where(e => e.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.UserId, (string?)null), ct);

        erased += await _data.UserAIQuotas.Where(q => q.UserId == userId).ExecuteDeleteAsync(ct);
        erased += await _data.UserBillingSettings.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
        erased += await _data.Subscriptions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
        erased += await _data.Customers.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        return erased;
    }

    public Task<int> PurgeUsageEventsAsync(DateTime olderThanUtc, TimeSpan pause, CancellationToken ct = default) =>
        Namines.BuildingBlocks.Persistence.BatchedDelete.RunAsync(
            _data.UsageEvents.Where(e => e.OccurredAt < olderThanUtc), e => e.Id, pause, ct);

    public Task<int> PurgeCompletedCancellationsAsync(DateTime olderThanUtc, TimeSpan pause, CancellationToken ct = default) =>
        Namines.BuildingBlocks.Persistence.BatchedDelete.RunAsync(
            _data.PendingSubscriptionCancellations.Where(c => c.CompletedAt != null && c.CompletedAt < olderThanUtc), c => c.Id, pause, ct);

    public Task<int> PurgeStripeWebhookEventsAsync(DateTime olderThanUtc, TimeSpan pause, CancellationToken ct = default) =>
        Namines.BuildingBlocks.Persistence.BatchedDelete.RunAsync(
            _data.StripeWebhookEvents.Where(e => e.ProcessedAt < olderThanUtc), e => e.Id, pause, ct);
}
