using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Namines.BuildingBlocks.Security;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Vault.Abstractions;
using Namines.Vault.Infrastructure.Persistence;
using Namines.Vault.Storage;

namespace Namines.Vault.Application;

/// <summary>Bir Vault işleminin kullanıcıya dönen sonucu.</summary>
public sealed record VaultResult(bool Ok, string? Error = null, string? BackupId = null);

/// <summary>
/// Vault modülünün uygulama katmanı sözleşmesi (Layered Modular Monolith Application Service).
/// </summary>
public interface IVaultApplicationService
{
    string StoreDescription { get; }
    IReadOnlyList<string> SupportedEngines { get; }
    IBackupProvider? FindProvider(string? engine);
    Task<string?> ProbeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<VaultBackup>> ListBackupsAsync(string projectId, CancellationToken ct = default);
    Task<IReadOnlyList<VaultRestore>> ListRestoresAsync(string projectId, CancellationToken ct = default);
    Task<VaultBackup?> FindBackupAsync(string projectId, string backupId, CancellationToken ct = default);
    Task<VaultSchedule?> GetScheduleAsync(string projectId, CancellationToken ct = default);
    Task<VaultSchedule> SaveScheduleAsync(
        string projectId,
        string? organizationId,
        bool enabled,
        VaultCadence cadence,
        int hourUtc,
        int? dayOfWeek,
        int retainCount,
        CancellationToken ct = default);
    Task<VaultResult> QueueBackupAsync(
        string projectId,
        string userId,
        VaultBackupKind kind,
        CancellationToken ct = default);
    Task MarkQueueFullFailedAsync(string backupId, CancellationToken ct = default);
    Task RunQueuedBackupAsync(VaultBackup record, CancellationToken ct = default);
    Task<VaultResult> BackupProjectAsync(
        string projectId,
        string userId,
        VaultBackupKind kind,
        CancellationToken ct = default);
    Task<VaultResult> RestoreProjectAsync(
        string projectId,
        VaultBackup backup,
        string userId,
        CancellationToken ct = default,
        IReadOnlyList<string>? tables = null);
    Task DeleteAsync(VaultBackup backup, CancellationToken ct = default);
    Task<VaultResult> PurgeProjectBackupsAsync(string projectId, CancellationToken ct = default);
    Task<VaultResult> VerifyAsync(VaultBackup backup, CancellationToken ct = default);
    Task<int> ApplyRetentionAsync(string projectId, int retainCount, CancellationToken ct = default);
    Task<Stream?> OpenEncryptedAsync(VaultBackup backup, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, (int Count, long TotalBytes)>> GetProjectBackupStatsAsync(
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct = default);
    Task<int> CountProjectBackupsAsync(string projectId, CancellationToken ct = default);
    Task<long> GetTotalBackupBytesAsync(CancellationToken ct = default);
}

/// <summary>
/// Vault modülünün kendi <see cref="VaultDbContext"/>'i, <see cref="ITenantProjectAuthority"/>
/// ve <see cref="IProjectConnectionSecretProvider"/> üzerinden çalışan izole uygulama servisi.
/// </summary>
public sealed class VaultApplicationService : IVaultApplicationService
{
    public static readonly TimeSpan RunningJobStaleAfter = TimeSpan.FromHours(2);

    private readonly VaultDbContext _db;
    private readonly ITenantProjectAuthority _authority;
    private readonly IProjectConnectionSecretProvider _secrets;
    private readonly IEnumerable<IBackupProvider> _providers;
    private readonly IBackupStore _store;
    private readonly BackupCipher _cipher;
    private readonly ILogger<VaultApplicationService> _logger;

    public VaultApplicationService(
        VaultDbContext db,
        ITenantProjectAuthority authority,
        IProjectConnectionSecretProvider secrets,
        IEnumerable<IBackupProvider> providers,
        IBackupStore store,
        BackupCipher cipher,
        ILogger<VaultApplicationService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string StoreDescription => _store.Description;

    public IReadOnlyList<string> SupportedEngines =>
        _providers.Select(p => p.Engine).OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList();

    public IBackupProvider? FindProvider(string? engine) =>
        engine is null
            ? null
            : _providers.FirstOrDefault(p => string.Equals(p.Engine, engine, StringComparison.OrdinalIgnoreCase));

    public async Task<string?> ProbeAsync(CancellationToken ct = default)
    {
        foreach (var provider in _providers)
        {
            if (await provider.ProbeAsync(ct) is { } problem) return problem;
        }
        return null;
    }

    public async Task<IReadOnlyList<VaultBackup>> ListBackupsAsync(string projectId, CancellationToken ct = default) =>
        await _db.VaultBackups
            .AsNoTracking()
            .Where(b => b.ProjectId == projectId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<VaultRestore>> ListRestoresAsync(string projectId, CancellationToken ct = default) =>
        await _db.VaultRestores
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.StartedAt)
            .ToListAsync(ct);

    public Task<VaultBackup?> FindBackupAsync(string projectId, string backupId, CancellationToken ct = default) =>
        _db.VaultBackups.FirstOrDefaultAsync(b => b.Id == backupId && b.ProjectId == projectId, ct);

    public Task<VaultSchedule?> GetScheduleAsync(string projectId, CancellationToken ct = default) =>
        _db.VaultSchedules.AsNoTracking().FirstOrDefaultAsync(s => s.ProjectId == projectId, ct);

    public async Task<IReadOnlyDictionary<string, (int Count, long TotalBytes)>> GetProjectBackupStatsAsync(
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct = default)
    {
        if (projectIds is null || projectIds.Count == 0)
            return new Dictionary<string, (int Count, long TotalBytes)>();

        return await _db.VaultBackups
            .AsNoTracking()
            .Where(b => projectIds.Contains(b.ProjectId))
            .GroupBy(b => b.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count(), TotalBytes = g.Sum(x => x.SizeBytes) })
            .ToDictionaryAsync(x => x.ProjectId, x => (x.Count, x.TotalBytes), ct);
    }

    public Task<int> CountProjectBackupsAsync(string projectId, CancellationToken ct = default) =>
        _db.VaultBackups.AsNoTracking().CountAsync(b => b.ProjectId == projectId, ct);

    public async Task<long> GetTotalBackupBytesAsync(CancellationToken ct = default) =>
        await _db.VaultBackups.AsNoTracking().SumAsync(b => (long?)b.SizeBytes, ct) ?? 0L;

    public async Task<VaultSchedule> SaveScheduleAsync(
        string projectId,
        string? organizationId,
        bool enabled,
        VaultCadence cadence,
        int hourUtc,
        int? dayOfWeek,
        int retainCount,
        CancellationToken ct = default)
    {
        var schedule = await _db.VaultSchedules.FirstOrDefaultAsync(s => s.ProjectId == projectId, ct);
        if (schedule is null)
        {
            schedule = new VaultSchedule { ProjectId = projectId, OrganizationId = organizationId };
            _db.VaultSchedules.Add(schedule);
        }

        schedule.Enabled = enabled;
        schedule.Cadence = cadence;
        schedule.HourUtc = hourUtc;
        schedule.DayOfWeek = cadence == VaultCadence.Weekly ? dayOfWeek : null;
        schedule.RetainCount = retainCount;
        schedule.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return schedule;
    }

    public async Task<VaultResult> QueueBackupAsync(
        string projectId,
        string userId,
        VaultBackupKind kind,
        CancellationToken ct = default)
    {
        var snapshot = await _authority.GetProjectSnapshotAsync(projectId, ct);
        if (snapshot is null)
            return new VaultResult(false, "Project not found.");

        var (ok, resolved, error) = await TryResolveConnectionAsync(projectId, ct);
        if (!ok || resolved is null)
            return new VaultResult(false, error);

        var provider = FindProvider(resolved.ConnectionDbType)!;
        var record = NewBackupRecord(snapshot.ProjectId, snapshot.OrganizationId, snapshot.ProjectName, userId, kind, provider.Engine);

        _db.VaultBackups.Add(record);
        await _db.SaveChangesAsync(ct);

        return new VaultResult(true, BackupId: record.Id);
    }

    public async Task MarkQueueFullFailedAsync(string backupId, CancellationToken ct = default)
    {
        var orphan = await _db.VaultBackups.FirstOrDefaultAsync(b => b.Id == backupId, ct);
        if (orphan is null) return;

        orphan.Status = VaultBackupStatus.Failed;
        orphan.ErrorMessage = "The backup queue is full. Try again in a few minutes.";
        orphan.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task RunQueuedBackupAsync(VaultBackup record, CancellationToken ct = default)
    {
        var snapshot = await _authority.GetProjectSnapshotAsync(record.ProjectId, ct);
        if (snapshot is null)
        {
            record.Status = VaultBackupStatus.Failed;
            record.ErrorMessage = "The project no longer exists.";
            record.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            return;
        }

        var (ok, resolved, error) = await TryResolveConnectionAsync(record.ProjectId, ct);
        if (!ok || resolved is null)
        {
            record.Status = VaultBackupStatus.Failed;
            record.ErrorMessage = Shorten(error ?? "The project connection could not be resolved.");
            record.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            return;
        }

        var provider = FindProvider(resolved.ConnectionDbType)!;
        await ExecuteBackupAsync(record, provider, resolved.ConnectionString, snapshot.ProjectId, snapshot.ProjectName, ct);
    }

    public async Task<VaultResult> BackupProjectAsync(
        string projectId,
        string userId,
        VaultBackupKind kind,
        CancellationToken ct = default)
    {
        var snapshot = await _authority.GetProjectSnapshotAsync(projectId, ct);
        if (snapshot is null)
            return new VaultResult(false, "Project not found.");

        var (ok, resolved, error) = await TryResolveConnectionAsync(projectId, ct);
        if (!ok || resolved is null)
            return new VaultResult(false, error);

        var provider = FindProvider(resolved.ConnectionDbType)!;
        var record = NewBackupRecord(snapshot.ProjectId, snapshot.OrganizationId, snapshot.ProjectName, userId, kind, provider.Engine);

        _db.VaultBackups.Add(record);
        await _db.SaveChangesAsync(ct);

        return await ExecuteBackupAsync(record, provider, resolved.ConnectionString, snapshot.ProjectId, snapshot.ProjectName, ct);
    }

    public async Task<VaultResult> RestoreProjectAsync(
        string projectId,
        VaultBackup backup,
        string userId,
        CancellationToken ct = default,
        IReadOnlyList<string>? tables = null)
    {
        if (backup.Status != VaultBackupStatus.Succeeded)
            return new VaultResult(false, "Only a successful backup can be restored.");

        var snapshot = await _authority.GetProjectSnapshotAsync(projectId, ct);
        if (snapshot is null)
            return new VaultResult(false, "Project not found.");

        var (ok, resolved, error) = await TryResolveConnectionAsync(projectId, ct);
        if (!ok || resolved is null)
            return new VaultResult(false, error);

        var provider = FindProvider(backup.Engine);
        if (provider is null)
            return new VaultResult(false,
                $"No backup provider is registered for {backup.Engine}, so this backup cannot be restored.");

        if (!string.Equals(resolved.ConnectionDbType, backup.Engine, StringComparison.OrdinalIgnoreCase))
            return new VaultResult(false,
                $"This backup was taken from {backup.Engine} but the project now points at " +
                $"{resolved.ConnectionDbType}. Restoring across engines is not possible.");

        var preRestore = await BackupProjectAsync(projectId, userId, VaultBackupKind.PreRestore, ct);
        if (!preRestore.Ok)
            return new VaultResult(false,
                $"Restore aborted: the mandatory pre-restore backup failed ({preRestore.Error}).");

        var log = new VaultRestore
        {
            ProjectId = snapshot.ProjectId,
            OrganizationId = snapshot.OrganizationId,
            BackupId = backup.Id,
            PreRestoreBackupId = preRestore.BackupId,
            PerformedByUserId = userId,
        };
        _db.VaultRestores.Add(log);
        await _db.SaveChangesAsync(ct);

        try
        {
            await EnsureCipherTextMatchesRecordAsync(backup, ct);

            await using var encrypted = await _store.OpenAsync(backup.StorageKey, ct)
                ?? throw new InvalidOperationException(
                    "The backup file is missing from storage; it cannot be restored.");

            var spec = new RestoreSpec(resolved.ConnectionString, tables);
            await RunPipelineAsync(
                produce: stream => encrypted.CopyToAsync(stream, ct),
                transform: (input, output) => _cipher.DecryptAsync(
                    input, output, ct, IdentityOf(backup)),
                consume: async stream =>
                {
                    await provider.RestoreAsync(spec, stream, ct);
                    return 0L;
                });

            log.Status = VaultBackupStatus.Succeeded;
        }
        catch (NotSupportedException ex)
        {
            log.Status = VaultBackupStatus.Failed;
            log.ErrorMessage = Shorten(ex.Message);
            await _db.SaveChangesAsync(ct);
            return new VaultResult(false, ex.Message);
        }
        catch (Exception ex)
        {
            log.Status = VaultBackupStatus.Failed;
            log.ErrorMessage = DbConnectionFailure.Classify(ex);
            _logger.LogError(ex, "Vault: {ProjectId} projesine geri yukleme basarisiz.", projectId);
        }
        finally
        {
            log.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
        }

        return log.Status == VaultBackupStatus.Succeeded
            ? new VaultResult(true, BackupId: preRestore.BackupId)
            : new VaultResult(false, log.ErrorMessage);
    }

    public async Task DeleteAsync(VaultBackup backup, CancellationToken ct = default)
    {
        await TryDeleteAsync(backup.StorageKey);
        _db.VaultBackups.Remove(backup);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<VaultResult> PurgeProjectBackupsAsync(string projectId, CancellationToken ct = default)
    {
        var freshCutoff = DateTime.UtcNow - RunningJobStaleAfter;

        var runningBackup = await _db.VaultBackups.AnyAsync(b =>
            b.ProjectId == projectId && b.Status == VaultBackupStatus.Running && b.CreatedAt > freshCutoff, ct);
        var runningRestore = await _db.VaultRestores.AnyAsync(r =>
            r.ProjectId == projectId && r.Status == VaultBackupStatus.Running && r.StartedAt > freshCutoff, ct);
        if (runningBackup || runningRestore)
            return new VaultResult(false, "A backup or restore is still running for this project. Wait for it to finish, then try again.");

        var backups = await _db.VaultBackups.Where(b => b.ProjectId == projectId).ToListAsync(ct);

        string? firstFailure = null;
        foreach (var backup in backups)
        {
            try
            {
                await _store.DeleteAsync(backup.StorageKey, ct);
                _db.VaultBackups.Remove(backup);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Vault: {Key} proje silinirken depodan silinemedi.", backup.StorageKey);
                firstFailure ??= "A backup file could not be deleted from storage.";
            }
        }

        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync(ct);

        return firstFailure is null
            ? new VaultResult(true)
            : new VaultResult(false, firstFailure + " Nothing else was deleted; try again later.");
    }

    public async Task<VaultResult> VerifyAsync(VaultBackup backup, CancellationToken ct = default)
    {
        if (backup.Status != VaultBackupStatus.Succeeded)
            return new VaultResult(false, "Only a successful backup can be verified.");

        var provider = FindProvider(backup.Engine);
        if (provider is null)
            return new VaultResult(false,
                $"No backup provider is registered for {backup.Engine}, so this backup cannot be verified.");

        try
        {
            await using var encrypted = await _store.OpenAsync(backup.StorageKey, ct)
                ?? throw new InvalidOperationException("The backup file is missing from storage.");

            var error = await RunVerifyPipelineAsync(provider, encrypted, backup, ct);

            backup.VerifiedAt = error is null ? DateTime.UtcNow : null;
            backup.VerifyError = error;
        }
        catch (Exception ex)
        {
            backup.VerifiedAt = null;
            backup.VerifyError = DbConnectionFailure.Classify(ex);
            _logger.LogWarning(ex, "Vault: {BackupId} dogrulanamadi.", backup.Id);
        }

        await _db.SaveChangesAsync(CancellationToken.None);

        return backup.VerifiedAt is not null
            ? new VaultResult(true, BackupId: backup.Id)
            : new VaultResult(false, backup.VerifyError);
    }

    public async Task<int> ApplyRetentionAsync(string projectId, int retainCount, CancellationToken ct = default)
    {
        if (retainCount < 1) return 0;

        var expired = await _db.VaultBackups
            .Where(b => b.ProjectId == projectId
                && b.Kind == VaultBackupKind.Scheduled
                && b.Status == VaultBackupStatus.Succeeded)
            .OrderByDescending(b => b.CreatedAt)
            .Skip(retainCount)
            .ToListAsync(ct);

        foreach (var backup in expired)
        {
            await TryDeleteAsync(backup.StorageKey);
            _db.VaultBackups.Remove(backup);
        }

        if (expired.Count > 0) await _db.SaveChangesAsync(ct);
        return expired.Count;
    }

    public async Task<Stream?> OpenEncryptedAsync(VaultBackup backup, CancellationToken ct = default)
    {
        await EnsureCipherTextMatchesRecordAsync(backup, ct);
        return await _store.OpenAsync(backup.StorageKey, ct);
    }

    private VaultBackup NewBackupRecord(
        string projectId, string? organizationId, string projectName, string userId, VaultBackupKind kind, string engine) => new()
    {
        ProjectId = projectId,
        OrganizationId = organizationId,
        CreatedByUserId = userId,
        DatabaseName = projectName,
        Engine = engine,
        Kind = kind,
        StoreDescription = _store.Description,
        StorageKey = $"{projectId}/{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.nvlt",
    };

    private async Task<VaultResult> ExecuteBackupAsync(
        VaultBackup record, IBackupProvider provider, string connectionString,
        string projectId, string projectName, CancellationToken ct)
    {
        try
        {
            var spec = new BackupSpec(connectionString, projectName);
            record.SizeBytes = await RunPipelineAsync(
                produce: stream => provider.BackupAsync(spec, stream, ct),
                transform: (input, output) => _cipher.EncryptAsync(
                    input, output, ct, IdentityOf(record)),
                consume: async stream =>
                {
                    var (written, digest) = await PutWithDigestAsync(record.StorageKey, stream, ct);
                    record.CipherTextSha256 = digest;
                    return written;
                });

            record.Status = VaultBackupStatus.Succeeded;
        }
        catch (Exception ex)
        {
            record.Status = VaultBackupStatus.Failed;
            record.ErrorMessage = DbConnectionFailure.Classify(ex);
            _logger.LogError(ex, "Vault: {ProjectId} projesinin yedegi alinamadi.", projectId);
            await TryDeleteAsync(record.StorageKey);
        }
        finally
        {
            record.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
        }

        return record.Status == VaultBackupStatus.Succeeded
            ? new VaultResult(true, BackupId: record.Id)
            : new VaultResult(false, record.ErrorMessage);
    }

    private static BackupIdentity IdentityOf(VaultBackup backup) =>
        new(backup.ProjectId, backup.Id);

    private async Task<string?> RunVerifyPipelineAsync(
        IBackupProvider provider, Stream encrypted, VaultBackup backup, CancellationToken ct)
    {
        string? error = null;

        await RunPipelineAsync(
            produce: stream => encrypted.CopyToAsync(stream, ct),
            transform: (input, output) => _cipher.DecryptAsync(
                input, output, ct, IdentityOf(backup)),
            consume: async stream =>
            {
                error = await provider.VerifyAsync(stream, ct);
                return 0L;
            });

        return error;
    }

    private async Task EnsureCipherTextMatchesRecordAsync(VaultBackup backup, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(backup.CipherTextSha256)) return;

        await using var stream = await _store.OpenAsync(backup.StorageKey, ct)
            ?? throw new InvalidOperationException(
                "The backup file is missing from storage.");

        using var sha = SHA256.Create();
        var actual = Convert.ToHexString(await sha.ComputeHashAsync(stream, ct));

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(actual),
                Encoding.UTF8.GetBytes(backup.CipherTextSha256)))
        {
            _logger.LogError(
                "Vault: {BackupId} kaydinin ozeti depodaki dosyayla ESLESMIYOR. " +
                "Beklenen {Expected}, bulunan {Actual}.",
                backup.Id, backup.CipherTextSha256, actual);

            throw new InvalidOperationException(
                "This backup file does not match its record and was refused. Contact support.");
        }
    }

    private async Task<(long Written, string Digest)> PutWithDigestAsync(
        string key, Stream source, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        await using var hashing = new CryptoStream(source, sha, CryptoStreamMode.Read, leaveOpen: true);
        var written = await _store.PutAsync(key, hashing, ct);
        return (written, Convert.ToHexString(sha.Hash!));
    }

    private static async Task<long> RunPipelineAsync(
        Func<Stream, Task> produce,
        Func<Stream, Stream, Task> transform,
        Func<Stream, Task<long>> consume)
    {
        var first = new Pipe();
        var second = new Pipe();

        var produceTask = RunStageAsync(() => produce(first.Writer.AsStream()), first.Writer);
        var transformTask = RunStageAsync(
            () => transform(first.Reader.AsStream(), second.Writer.AsStream()), second.Writer);
        var consumeTask = consume(second.Reader.AsStream());

        await Task.WhenAll(produceTask, transformTask, consumeTask);
        return await consumeTask;
    }

    private static async Task RunStageAsync(Func<Task> stage, PipeWriter writer)
    {
        try
        {
            await stage();
            await writer.CompleteAsync();
        }
        catch (Exception ex)
        {
            await writer.CompleteAsync(ex);
            throw;
        }
    }

    private async Task<(bool Ok, ResolvedProjectConnection? Connection, string? Error)> TryResolveConnectionAsync(
        string projectId, CancellationToken ct)
    {
        var resolved = await _secrets.ResolveAsync(projectId, ct);
        if (resolved is null || string.IsNullOrWhiteSpace(resolved.ConnectionString))
        {
            return (false, null, "This project has no live database connection.");
        }

        if (FindProvider(resolved.ConnectionDbType) is null)
        {
            return (false, null,
                $"Vault cannot back up {resolved.ConnectionDbType} yet. " +
                $"Supported engines: {string.Join(", ", SupportedEngines)}.");
        }

        if (!string.IsNullOrEmpty(resolved.HostPolicyDenyReason))
        {
            return (false, null, resolved.HostPolicyDenyReason);
        }

        return (true, resolved, null);
    }

    private async Task TryDeleteAsync(string key)
    {
        try
        {
            await _store.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vault: {Key} depodan silinemedi.", key);
        }
    }

    private static string Shorten(string message)
    {
        var clean = message.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return clean.Length <= 400 ? clean : clean[..400] + "…";
    }
}
