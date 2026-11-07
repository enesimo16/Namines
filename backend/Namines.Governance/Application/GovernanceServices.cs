using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Contracts;
using Namines.Core.Enums;
using Namines.Core.Models.Auth;
using Namines.Governance.Domain;

namespace Namines.Governance.Application;

/// <summary>
/// Kullanıcının AI politikası. Varsayılanlar <see cref="UserAIPolicy"/>'de tanımlı: başka yerde tekrar yazmak iki
/// yerin ayrışması demekti ("ayarlara hiç dokunmadım ama farklı davranıyor").
/// </summary>
internal sealed class AiPolicyService : IAiPolicies
{
    private readonly IGovernanceData _db;

    public AiPolicyService(IGovernanceData db) => _db = db;

    private static AiPolicySnapshot Snapshot(UserAIPolicy p) => new(
        p.SmartSeed, p.Documentation, p.Scaffolding, p.SchemaGeneration, p.SchemaRevision, p.DbaAnalysis, p.Migration, p.Voice, p.AdvancedJson);

    public async Task<AiPolicySnapshot?> FindAsync(string userId, CancellationToken ct = default)
    {
        var policy = await _db.UserAIPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        return policy is null ? null : Snapshot(policy);
    }

    public async Task<AiPolicySnapshot> EnsureAsync(string userId, CancellationToken ct = default)
    {
        var existing = await FindAsync(userId, ct);
        if (existing is not null) return existing;

        var policy = new UserAIPolicy { UserId = userId, UpdatedAt = DateTime.UtcNow };
        _db.UserAIPolicies.Add(policy);
        try
        {
            await _db.SaveChangesAsync(ct);
            return Snapshot(policy);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex) && _db.Database.CurrentTransaction is null)
        {
            // Eşzamanlı iki ilk çağrı: kazananın satırı okunur, hata yutulmaz ama ikinci satır da açılmaz.
            _db.Entry(policy).State = EntityState.Detached;
            return (await FindAsync(userId, ct))!;
        }
    }

    public async Task SaveAsync(string userId, AiPolicySnapshot modes, string? advancedJson, CancellationToken ct = default)
    {
        var policy = await _db.UserAIPolicies.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (policy is null)
        {
            policy = new UserAIPolicy { UserId = userId };
            _db.UserAIPolicies.Add(policy);
        }

        policy.SmartSeed = modes.SmartSeed;
        policy.Documentation = modes.Documentation;
        policy.Scaffolding = modes.Scaffolding;
        policy.SchemaGeneration = modes.SchemaGeneration;
        policy.SchemaRevision = modes.SchemaRevision;
        policy.DbaAnalysis = modes.DbaAnalysis;
        policy.Migration = modes.Migration;
        policy.Voice = modes.Voice;
        // Gelişmiş ayar gelmediyse DOKUNULMAZ: model seçimlerini kaydeden eski bir istemci, farkında olmadan
        // gelişmiş ayarları varsayılana döndürürdü.
        if (advancedJson is not null) policy.AdvancedJson = advancedJson;
        policy.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> RewriteAdvancedJsonAsync(Func<string, string?> transform, CancellationToken ct = default)
    {
        // Yalnızca kayıtlı bir tercih JSON'u OLAN satırlar aday: null/boş zaten kod içi varsayılanı kullanıyor.
        var candidates = await _db.UserAIPolicies.Where(p => p.AdvancedJson != null && p.AdvancedJson != "").ToListAsync(ct);
        var changed = 0;
        foreach (var policy in candidates)
        {
            var rewritten = transform(policy.AdvancedJson!);
            if (rewritten is null || rewritten == policy.AdvancedJson) continue;
            policy.AdvancedJson = rewritten;
            changed++;
        }
        if (changed > 0) await _db.SaveChangesAsync(ct);
        return changed;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException switch
    {
        DbException { SqlState: "23505" } => true,
        DbException { ErrorCode: 19 } => true,
        _ => false,
    };
}

/// <summary>
/// Değiştirilemez denetim izi, silme/saklama ve Hub/Admin sayaçları. Denetim satırı yalnızca eklenir; veritabanı
/// tetikleyicisi UPDATE ve DELETE'i reddeder (tek istisna: kullanıcı silinince FK'nın yaptığı anonimleştirme ve
/// saklama süresi temizliği).
/// </summary>
internal sealed class GovernanceAuditService :
    IGovernanceAudit, IGovernanceMaintenance, IChangeRequestStats, IGovernanceAdminQueries, ISubscriptionCancellationObserver
{
    private readonly IGovernanceData _db;

    public GovernanceAuditService(IGovernanceData db) => _db = db;

    public async Task<string> AppendAsync(GovernanceAuditEntry entry, CancellationToken ct = default)
    {
        var row = new ChangeRequestAuditLog
        {
            ChangeRequestId = entry.SubjectId,
            ProjectId = entry.ProjectId,
            ChangeRequestTitle = entry.Title,
            Action = entry.Action,
            ActorUserId = entry.ActorUserId,
            Details = entry.Details,
        };
        _db.ChangeRequestAuditLogs.Add(row);
        await _db.SaveChangesAsync(ct);
        return row.Id;
    }

    public Task OnCompletedAsync(string outboxId, string subscriptionId, CancellationToken ct) =>
        AppendAsync(new GovernanceAuditEntry(
            outboxId, string.Empty, null, ChangeRequestAuditAction.SubscriptionCancellationCompleted, null,
            JsonSerializer.Serialize(new { stripe = "canceled" })), ct);

    public async Task<int> ErasePersonalDataAsync(string userId, CancellationToken ct = default)
    {
        var erased = await _db.UserAIPolicies.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);
        erased += await _db.ChangeRequestApprovals.Where(a => a.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.UserId, (string?)null), ct);
        erased += await _db.ChangeRequests.Where(c => c.CreatedByUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedByUserId, (string?)null), ct);
        // Denetim satırının TEK izinli güncellemesi: yalnızca ActorUserId doluyken boşaltmak (tetikleyici istisnası).
        erased += await _db.ChangeRequestAuditLogs.Where(a => a.ActorUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ActorUserId, (string?)null), ct);
        return erased;
    }

    public Task<int> CountChangeRequestsAsync(string projectId, CancellationToken ct = default) =>
        _db.ChangeRequests.AsNoTracking().CountAsync(c => c.ProjectId == projectId, ct);

    public Task<int> DeleteProjectChangeRequestsAsync(string projectId, CancellationToken ct = default) =>
        _db.ChangeRequests.Where(c => c.ProjectId == projectId).ExecuteDeleteAsync(ct);

    public async Task<int> PurgeAuditAsync(DateTime cutoffUtc, TimeSpan pause, CancellationToken ct = default)
    {
        const int batch = 5000;
        var total = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var deleted = 0;
            // Silme izni YALNIZCA bu işlemde açılır (set_config(..., is_local = true)); sızıntı olmaz.
            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                if (_db.Database.IsNpgsql())
                    await _db.Database.ExecuteSqlRawAsync("SELECT set_config('namines.audit_retention', 'on', true)", ct);
                deleted = await _db.ChangeRequestAuditLogs
                    .Where(a => a.CreatedAt < cutoffUtc)
                    .OrderBy(a => a.Id).Take(batch)
                    .ExecuteDeleteAsync(ct);
                await tx.CommitAsync(ct);
            });
            total += deleted;
            if (deleted < batch) return total;
            if (pause > TimeSpan.Zero) await Task.Delay(pause, ct);
        }
    }

    public async Task<(int Open, int Closed)> CountAsync(string projectId, CancellationToken ct = default)
    {
        var open = await _db.ChangeRequests.AsNoTracking()
            .CountAsync(c => c.ProjectId == projectId && c.Status == ChangeRequestStatus.PendingReview, ct);
        var closed = await _db.ChangeRequests.AsNoTracking()
            .CountAsync(c => c.ProjectId == projectId && c.Status != ChangeRequestStatus.PendingReview, ct);
        return (open, closed);
    }

    public async Task<ProjectChangeRequestFlags> GetProjectFlagsAsync(string projectId, CancellationToken ct = default)
    {
        var total = await _db.ChangeRequests.AsNoTracking().CountAsync(c => c.ProjectId == projectId, ct);
        var active = await _db.ChangeRequests.AsNoTracking()
            .AnyAsync(c => c.ProjectId == projectId && (c.Status == ChangeRequestStatus.Approved || c.Status == ChangeRequestStatus.PendingReview), ct);
        return new ProjectChangeRequestFlags(total, active);
    }
}
