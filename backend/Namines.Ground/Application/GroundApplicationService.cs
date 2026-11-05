using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Namines.BuildingBlocks.Security;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;
using Namines.Ground.Abstractions;
using Namines.Ground.Infrastructure.Persistence;

namespace Namines.Ground.Application;

/// <summary>Bir Ground işleminin kullanıcıya dönen sonucu.</summary>
public sealed record GroundResult(bool Ok, string? Error = null, GroundDatabase? Database = null);

/// <summary>
/// Namines Ground modülünün uygulama katmanı sözleşmesi (Layered Modular Monolith Application Service).
/// </summary>
public interface IGroundApplicationService
{
    IReadOnlyList<IDatabaseProvider> Providers { get; }
    IDatabaseProvider? FindProvider(string name);
    Task<GroundDatabase?> GetByProjectIdAsync(string projectId, CancellationToken ct = default);
    Task<bool> HasManagedDatabaseAsync(string projectId, CancellationToken ct = default);
    Task<int> CountTotalDatabasesAsync(CancellationToken ct = default);
    Task<int> CountActiveProjectDatabasesAsync(string projectId, CancellationToken ct = default);
    Task<IReadOnlySet<string>> GetProjectIdsWithDatabaseAsync(IReadOnlyCollection<string> projectIds, CancellationToken ct = default);
    Task<GroundResult> ProvisionAsync(
        string projectId,
        string userId,
        IDatabaseProvider provider,
        CancellationToken ct = default);
    Task<GroundResult> RequestDeleteAsync(GroundDatabase record, CancellationToken ct = default);
    Task<GroundResult> CancelDeleteAsync(
        GroundDatabase record,
        IDatabaseProvider provider,
        CancellationToken ct = default);
    Task<int> ReclaimStaleClaimsAsync(TimeSpan lease, CancellationToken ct = default);
    Task<GroundResult> DeprovisionNowAsync(GroundDatabase record, CancellationToken ct = default);
    Task<GroundResult> DeprovisionAndRemoveProjectRecordsAsync(string projectId, CancellationToken ct = default);
    Task<int> PurgeExpiredAsync(int graceDays, CancellationToken ct = default);
    Task<DatabaseMetrics> GetMetricsAsync(GroundDatabase record, CancellationToken ct = default);
    Task<string?> GetStorageWarningAsync(
        GroundDatabase record,
        DatabaseMetrics metrics,
        CancellationToken ct = default);
}

/// <summary>
/// Namines Ground modülünün kendi <see cref="GroundDbContext"/>'i, <see cref="ITenantProjectAuthority"/>
/// ve <see cref="IProjectConnectionSecretProvider"/> üzerinden çalışan izole uygulama servisi.
/// </summary>
public sealed class GroundApplicationService : IGroundApplicationService
{
    public static readonly TimeSpan StaleClaimLease = TimeSpan.FromMinutes(30);

    private readonly GroundDbContext _db;
    private readonly ITenantProjectAuthority _authority;
    private readonly IProjectConnectionSecretProvider _secrets;
    private readonly IEnumerable<IDatabaseProvider> _providers;
    private readonly ILogger<GroundApplicationService> _logger;

    public GroundApplicationService(
        GroundDbContext db,
        ITenantProjectAuthority authority,
        IProjectConnectionSecretProvider secrets,
        IEnumerable<IDatabaseProvider> providers,
        ILogger<GroundApplicationService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<IDatabaseProvider> Providers => _providers.ToList();

    public IDatabaseProvider? FindProvider(string name) =>
        _providers.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public Task<GroundDatabase?> GetByProjectIdAsync(string projectId, CancellationToken ct = default) =>
        _db.GroundDatabases.FirstOrDefaultAsync(g => g.ProjectId == projectId, ct);

    public Task<bool> HasManagedDatabaseAsync(string projectId, CancellationToken ct = default) =>
        _db.GroundDatabases.AnyAsync(g => g.ProjectId == projectId, ct);

    public Task<int> CountTotalDatabasesAsync(CancellationToken ct = default) =>
        _db.GroundDatabases.AsNoTracking().CountAsync(ct);

    public Task<int> CountActiveProjectDatabasesAsync(string projectId, CancellationToken ct = default) =>
        _db.GroundDatabases.AsNoTracking().CountAsync(g => g.ProjectId == projectId && g.Status != GroundStatus.Deleted, ct);

    public async Task<IReadOnlySet<string>> GetProjectIdsWithDatabaseAsync(
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct = default)
    {
        if (projectIds is null || projectIds.Count == 0)
            return new HashSet<string>();

        var ids = await _db.GroundDatabases
            .AsNoTracking()
            .Where(g => projectIds.Contains(g.ProjectId))
            .Select(g => g.ProjectId)
            .ToListAsync(ct);

        return ids.ToHashSet();
    }

    public async Task<GroundResult> ProvisionAsync(
        string projectId,
        string userId,
        IDatabaseProvider provider,
        CancellationToken ct = default)
    {
        var snapshot = await _authority.GetProjectSnapshotAsync(projectId, ct);
        if (snapshot is null)
            return new GroundResult(false, "Project not found.");

        var existing = await _db.GroundDatabases
            .FirstOrDefaultAsync(g => g.ProjectId == projectId, ct);

        if (existing is not null)
        {
            if (existing.Status == GroundStatus.PendingDelete)
                return new GroundResult(false,
                    "This project's managed database is pending deletion. Cancel the deletion first.");

            return new GroundResult(true, Database: existing);
        }

        if (await IsQuotaExceededAsync(userId, ct) is { } quotaError)
            return new GroundResult(false, quotaError);

        var record = new GroundDatabase
        {
            ProjectId = snapshot.ProjectId,
            Provider = provider.Name,
            CreatedByUserId = userId,
            Status = GroundStatus.Provisioning,
            ClaimedAt = DateTime.UtcNow,
        };

        _db.GroundDatabases.Add(record);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.Entry(record).State = EntityState.Detached;
            var winner = await _db.GroundDatabases
                .FirstOrDefaultAsync(g => g.ProjectId == projectId, ct);

            return winner is not null
                ? new GroundResult(true, Database: winner)
                : new GroundResult(false, "The managed database record could not be created.");
        }

        try
        {
            var provisioned = await provider.CreateAsync(
                new ProvisionSpec(snapshot.ProjectId, snapshot.ProjectName), ct);

            record.ProviderProjectId = provisioned.ProviderProjectId;
            record.ProviderBranchId = provisioned.ProviderBranchId;
            record.Region = provisioned.Region;
            record.Status = GroundStatus.Active;
            record.ClaimedAt = null;

            await _secrets.SetConnectionAsync(
                snapshot.ProjectId,
                provisioned.ConnectionString,
                "PostgreSQL",
                ct);

            _logger.LogInformation(
                "Ground: {ProjectId} icin {Provider} uzerinde veritabani acildi.",
                snapshot.ProjectId, provider.Name);
        }
        catch (Exception ex)
        {
            record.Status = GroundStatus.Failed;
            record.ClaimedAt = null;
            record.Error = "Provisioning failed. Contact support if this persists.";
            _logger.LogError(ex, "Ground: {ProjectId} icin provizyon basarisiz.", snapshot.ProjectId);
        }
        finally
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }

        return record.Status == GroundStatus.Active
            ? new GroundResult(true, Database: record)
            : new GroundResult(false, record.Error);
    }

    public async Task<GroundResult> RequestDeleteAsync(GroundDatabase record, CancellationToken ct = default)
    {
        if (record.Status == GroundStatus.Deleted)
            return new GroundResult(false, "This database has already been deleted.");

        record.Status = GroundStatus.PendingDelete;
        record.DeleteRequestedAt = DateTime.UtcNow;

        await _secrets.ClearConnectionAsync(record.ProjectId, ct);
        await _db.SaveChangesAsync(ct);

        return new GroundResult(true, Database: record);
    }

    public async Task<GroundResult> CancelDeleteAsync(
        GroundDatabase record,
        IDatabaseProvider provider,
        CancellationToken ct = default)
    {
        if (record.Status != GroundStatus.PendingDelete)
            return new GroundResult(false, "There is no pending deletion for this database.");

        if (!provider.Capabilities.CreateIsIdempotentByProjectId)
        {
            return new GroundResult(false,
                $"Canceling deletion is not supported for {provider.Name} yet: recovering the " +
                "connection would require opening a second database, abandoning the original " +
                "and billing both. Let the deletion proceed, or contact support before the " +
                "grace window ends if you need the original data back.");
        }

        var snapshot = await _authority.GetProjectSnapshotAsync(record.ProjectId, ct);
        if (snapshot is null) return new GroundResult(false, "Project not found.");

        var claimed = await _db.GroundDatabases
            .Where(g => g.Id == record.Id && g.Status == GroundStatus.PendingDelete)
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.Status, GroundStatus.Provisioning)
                .SetProperty(g => g.ClaimedAt, (DateTime?)DateTime.UtcNow)
                .SetProperty(g => g.DeleteRequestedAt, (DateTime?)null), ct);

        if (claimed == 0)
            return new GroundResult(false,
                "This deletion is already being processed and can no longer be canceled.");

        await _db.Entry(record).ReloadAsync(ct);

        try
        {
            var provisioned = await provider.CreateAsync(
                new ProvisionSpec(
                    snapshot.ProjectId, snapshot.ProjectName, record.Region,
                    ExistingProviderProjectId: record.ProviderProjectId,
                    ExistingProviderBranchId: record.ProviderBranchId),
                ct);

            await _secrets.SetConnectionAsync(
                snapshot.ProjectId,
                provisioned.ConnectionString,
                "PostgreSQL",
                ct);

            record.Status = GroundStatus.Active;
            record.ClaimedAt = null;
            record.DeleteRequestedAt = null;
            record.Error = null;
        }
        catch (Exception ex)
        {
            record.Status = GroundStatus.PendingDelete;
            record.ClaimedAt = null;
            record.DeleteRequestedAt = DateTime.UtcNow;
            record.Error = "Canceling deletion failed. Contact support if this persists.";
            _logger.LogError(ex, "Ground: {ProjectId} icin silme geri alinamadi.", record.ProjectId);
            await _db.SaveChangesAsync(CancellationToken.None);
            return new GroundResult(false, record.Error);
        }

        await _db.SaveChangesAsync(ct);
        return new GroundResult(true, Database: record);
    }

    public async Task<int> ReclaimStaleClaimsAsync(TimeSpan lease, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var cutoff = now - lease;

        var deleting = await _db.GroundDatabases
            .Where(g => g.Status == GroundStatus.Deleting && g.ClaimedAt != null && g.ClaimedAt < cutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.Status, GroundStatus.PendingDelete)
                .SetProperty(g => g.ClaimedAt, (DateTime?)null), ct);

        var cancelClaims = await _db.GroundDatabases
            .Where(g => g.Status == GroundStatus.Provisioning && g.ClaimedAt != null && g.ClaimedAt < cutoff
                        && g.ProviderProjectId != null && g.ProviderProjectId != "")
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.Status, GroundStatus.PendingDelete)
                .SetProperty(g => g.DeleteRequestedAt, (DateTime?)now)
                .SetProperty(g => g.ClaimedAt, (DateTime?)null), ct);

        var failedProvisions = await _db.GroundDatabases
            .Where(g => g.Status == GroundStatus.Provisioning && g.ClaimedAt != null && g.ClaimedAt < cutoff
                        && (g.ProviderProjectId == null || g.ProviderProjectId == ""))
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.Status, GroundStatus.Failed)
                .SetProperty(g => g.Error, "Provisioning was interrupted. Contact support if this persists.")
                .SetProperty(g => g.ClaimedAt, (DateTime?)null), ct);

        var total = deleting + cancelClaims + failedProvisions;
        if (total > 0)
            _logger.LogWarning(
                "Ground: {Total} asili kayit kurtarildi (Deleting->PendingDelete: {Deleting}, " +
                "iptal->PendingDelete: {Cancel}, Provisioning->Failed: {Failed}).",
                total, deleting, cancelClaims, failedProvisions);

        var legacy = await _db.GroundDatabases.CountAsync(
            g => (g.Status == GroundStatus.Deleting || g.Status == GroundStatus.Provisioning)
                 && g.ClaimedAt == null && g.CreatedAt < cutoff, ct);
        if (legacy > 0)
            _logger.LogWarning(
                "Ground: {Count} kayit ClaimedAt olmadan Deleting/Provisioning'de; migration oncesi " +
                "asili kalmis olabilir, otomatik dokunulmadi — elle incelenmeli.", legacy);

        return total;
    }

    public async Task<GroundResult> DeprovisionNowAsync(GroundDatabase record, CancellationToken ct = default)
    {
        if (record.Status == GroundStatus.Deleted)
            return new GroundResult(true, Database: record);

        var leaseCutoff = DateTime.UtcNow - StaleClaimLease;
        var busy = (record.Status == GroundStatus.Deleting || record.Status == GroundStatus.Provisioning)
                   && record.ClaimedAt != null && record.ClaimedAt >= leaseCutoff;
        if (busy)
            return new GroundResult(false, "The managed database is being created or deleted right now. Try again in a few minutes.");

        if (string.IsNullOrWhiteSpace(record.ProviderProjectId))
            return new GroundResult(true, Database: record);

        var provider = FindProvider(record.Provider);
        if (provider is null)
        {
            _logger.LogError("Ground: {Provider} saglayicisi kayitli degil, {ProjectId} silinemedi.",
                record.Provider, record.ProjectId);
            return new GroundResult(false, "The managed database provider is not available, so the database could not be removed.");
        }

        var previousStatus = record.Status;
        var claimed = await _db.GroundDatabases
            .Where(g => g.Id == record.Id && g.Status == previousStatus)
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.Status, GroundStatus.Deleting)
                .SetProperty(g => g.ClaimedAt, (DateTime?)DateTime.UtcNow), ct);
        if (claimed == 0)
            return new GroundResult(false, "The managed database changed while it was being removed. Try again.");

        await _db.Entry(record).ReloadAsync(ct);

        try
        {
            await provider.DeleteAsync(
                new ProvisionedDatabase(
                    record.ProviderProjectId,
                    record.ProviderBranchId,
                    record.Region ?? string.Empty,
                    ConnectionString: string.Empty),
                ct);

            record.Status = GroundStatus.Deleted;
            record.ClaimedAt = null;
            record.DeletedAt = DateTime.UtcNow;
            record.Error = null;
            await _db.SaveChangesAsync(CancellationToken.None);
            return new GroundResult(true, Database: record);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ground: {ProjectId} proje silinirken saglayicidan kaldirilamadi.", record.ProjectId);

            record.Status = GroundStatus.PendingDelete;
            record.ClaimedAt = null;
            record.DeleteRequestedAt = DateTime.UtcNow.AddDays(-(GroundDefaults.DeleteGraceDays + 1));
            record.Error = "Deletion failed and will be retried automatically.";
            await _db.SaveChangesAsync(CancellationToken.None);
            return new GroundResult(false, "The managed database could not be removed from its provider. The project was not deleted; it will be retried automatically, try again shortly.");
        }
    }

    public async Task<GroundResult> DeprovisionAndRemoveProjectRecordsAsync(string projectId, CancellationToken ct = default)
    {
        var records = await _db.GroundDatabases.Where(g => g.ProjectId == projectId).ToListAsync(ct);
        foreach (var record in records)
        {
            var result = await DeprovisionNowAsync(record, ct);
            if (!result.Ok)
                return result;
        }

        if (records.Count > 0)
        {
            _db.GroundDatabases.RemoveRange(records);
            await _db.SaveChangesAsync(ct);
        }

        return new GroundResult(true);
    }

    public async Task<int> PurgeExpiredAsync(int graceDays, CancellationToken ct = default)
    {
        await ReclaimStaleClaimsAsync(StaleClaimLease, ct);

        var cutoff = DateTime.UtcNow.AddDays(-graceDays);

        var expired = await _db.GroundDatabases
            .Where(g => g.Status == GroundStatus.PendingDelete
                        && g.DeleteRequestedAt != null
                        && g.DeleteRequestedAt < cutoff)
            .ToListAsync(ct);

        var purged = 0;
        foreach (var record in expired)
        {
            var claimed = await _db.GroundDatabases
                .Where(g => g.Id == record.Id && g.Status == GroundStatus.PendingDelete)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(g => g.Status, GroundStatus.Deleting)
                    .SetProperty(g => g.ClaimedAt, (DateTime?)DateTime.UtcNow), ct);

            if (claimed == 0)
            {
                _logger.LogInformation(
                    "Ground: {ProjectId} silinmedi — kayit bu arada baska bir islem tarafindan alindi.",
                    record.ProjectId);
                continue;
            }

            await _db.Entry(record).ReloadAsync(ct);

            var provider = FindProvider(record.Provider);
            if (provider is null)
            {
                record.Status = GroundStatus.PendingDelete;
                record.ClaimedAt = null;
                await _db.SaveChangesAsync(CancellationToken.None);
                _logger.LogError(
                    "Ground: {Provider} saglayicisi kayitli degil, {ProjectId} silinemedi.",
                    record.Provider, record.ProjectId);
                continue;
            }

            if (string.IsNullOrWhiteSpace(record.ProviderProjectId))
            {
                record.Status = GroundStatus.Deleted;
                record.ClaimedAt = null;
                record.DeletedAt = DateTime.UtcNow;
                record.Error = null;
                purged++;
                continue;
            }

            try
            {
                await provider.DeleteAsync(
                    new ProvisionedDatabase(
                        record.ProviderProjectId,
                        record.ProviderBranchId,
                        record.Region ?? string.Empty,
                        ConnectionString: string.Empty),
                    ct);

                record.Status = GroundStatus.Deleted;
                record.ClaimedAt = null;
                record.DeletedAt = DateTime.UtcNow;
                record.Error = null;
                purged++;
            }
            catch (Exception ex)
            {
                record.Status = GroundStatus.PendingDelete;
                record.ClaimedAt = null;
                record.Error = "Deletion failed and will be retried automatically.";
                _logger.LogError(ex, "Ground: {ProjectId} kalici olarak silinemedi.", record.ProjectId);
            }
        }

        if (expired.Count > 0) await _db.SaveChangesAsync(CancellationToken.None);
        return purged;
    }

    public async Task<DatabaseMetrics> GetMetricsAsync(GroundDatabase record, CancellationToken ct = default)
    {
        if (record.Status is GroundStatus.Deleted or GroundStatus.Failed)
            return new DatabaseMetrics(null, null);

        var provider = FindProvider(record.Provider);
        if (provider is null) return new DatabaseMetrics(null, null);

        try
        {
            return await provider.GetMetricsAsync(
                new ProvisionedDatabase(
                    record.ProviderProjectId ?? string.Empty,
                    record.ProviderBranchId,
                    record.Region ?? string.Empty,
                    ConnectionString: string.Empty),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ground: {ProjectId} olcumleri okunamadi.", record.ProjectId);
            return new DatabaseMetrics(null, null);
        }
    }

    public async Task<string?> GetStorageWarningAsync(
        GroundDatabase record,
        DatabaseMetrics metrics,
        CancellationToken ct = default)
    {
        if (metrics.StorageBytes is not { } bytes || string.IsNullOrWhiteSpace(record.CreatedByUserId))
            return null;

        var userPlan = await _authority.GetUserPlanSnapshotAsync(record.CreatedByUserId, ct);
        if (userPlan is null) return null;

        var tier = PlanQuotas.Resolve(userPlan.SubscriptionStatus, userPlan.PlanCode, userPlan.IsDev);
        var threshold = PlanQuotas.For(tier).ManagedDatabaseStorageWarningBytes;

        if (threshold <= 0 || bytes < threshold) return null;

        _logger.LogWarning(
            "Ground: {ProjectId} {Bytes} bayta ulasti, plan esigi {Threshold} bayt.",
            record.ProjectId, bytes, threshold);

        return $"This database has grown past the {threshold / 1024 / 1024} MB guideline for your plan. " +
               "It keeps working — nothing is restricted — but consider upgrading or trimming data.";
    }

    private async Task<string?> IsQuotaExceededAsync(string userId, CancellationToken ct)
    {
        var userPlan = await _authority.GetUserPlanSnapshotAsync(userId, ct);
        if (userPlan is null) return "User not found.";

        var tier = PlanQuotas.Resolve(userPlan.SubscriptionStatus, userPlan.PlanCode, userPlan.IsDev);
        var limit = PlanQuotas.For(tier).ManagedDatabases;

        var current = await _db.GroundDatabases
            .CountAsync(g => g.CreatedByUserId == userId && g.Status != GroundStatus.Deleted, ct);

        if (!PlanQuotas.IsExceeded(limit, current)) return null;

        return limit == 0
            ? "Managed databases are not included in the free plan. You can connect your own " +
              "PostgreSQL server, or upgrade your plan."
            : PlanQuotas.LimitMessage(tier, "managed database", limit);
    }
}
