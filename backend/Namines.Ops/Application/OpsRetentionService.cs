using Namines.BuildingBlocks.Persistence;

namespace Namines.Ops.Application;

public interface IOpsRetentionService
{
    /// <summary>Gün sayısı 0 veya negatifse o tablo atlanır (veri kaybına doğru varsayılan yok).</summary>
    Task<int> PurgeSqlExecutionAuditsAsync(int days, DateTime nowUtc, TimeSpan pause, CancellationToken ct = default);

    Task<int> PurgeFeedbacksAsync(int days, DateTime nowUtc, TimeSpan pause, CancellationToken ct = default);
}

internal sealed class OpsRetentionService : IOpsRetentionService
{
    private readonly IOpsData _db;

    public OpsRetentionService(IOpsData db) => _db = db;

    public Task<int> PurgeSqlExecutionAuditsAsync(int days, DateTime nowUtc, TimeSpan pause, CancellationToken ct = default)
    {
        if (days <= 0) return Task.FromResult(0);
        var cutoff = nowUtc.AddDays(-days);
        return BatchedDelete.RunAsync(_db.SqlExecutionAudits.Where(a => a.CreatedAt < cutoff), a => a.Id, pause, ct);
    }

    public Task<int> PurgeFeedbacksAsync(int days, DateTime nowUtc, TimeSpan pause, CancellationToken ct = default)
    {
        if (days <= 0) return Task.FromResult(0);
        var cutoff = nowUtc.AddDays(-days);
        return BatchedDelete.RunAsync(_db.Feedbacks.Where(f => f.CreatedAt < cutoff), f => f.Id, pause, ct);
    }
}
