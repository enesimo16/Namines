using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.BuildingBlocks.Security;
using Namines.Core.Analysis;
using Namines.Core.Models;
using Namines.Studio.Domain;
using Namines.Studio.Infrastructure.Persistence;

namespace Namines.Studio.Application;

public sealed record CreateBranchCommand(string ProjectId, string Name, string? ParentBranchId, int? ForkedFromVersion, bool IsDefault);

public sealed record MergePreview(string SourceBranch, string TargetBranch, int BaseVersion, ThreeWayMergeResult Result);

/// <summary>
/// Son kullanıcının branch ve sürüm işlemleri. Yetki burada: Editor ve üstü. Yetkisiz ile yok aynı
/// yanıt (<see cref="ModuleOutcomeKind.NotFound"/>): başkasının branch kimliğinin varlığı sızmasın.
/// </summary>
public interface IBranchService
{
    Task<ModuleOutcome<StudioBranch>> CreateAsync(string actorUserId, CreateBranchCommand command, CancellationToken ct = default);

    Task<ModuleOutcome<IReadOnlyList<StudioBranch>>> ListAsync(string actorUserId, string projectId, CancellationToken ct = default);

    Task<ModuleOutcome<StudioBranch>> GetDefaultAsync(string actorUserId, string projectId, CancellationToken ct = default);

    /// <summary>Value.Created: bu çağrı mı oluşturdu.</summary>
    Task<ModuleOutcome<(StudioBranch Branch, bool Created)>> EnsureDefaultAsync(string actorUserId, string projectId, CancellationToken ct = default);

    Task<ModuleOutcome<StudioVersionHeader>> CommitAsync(string actorUserId, string branchId, string? schemaJson, string? message, CancellationToken ct = default);

    Task<ModuleOutcome<IReadOnlyList<StudioVersionHeader>>> ListVersionsAsync(string actorUserId, string branchId, CancellationToken ct = default);

    Task<ModuleOutcome<StudioSchemaVersion>> GetVersionAsync(string actorUserId, string versionId, CancellationToken ct = default);

    /// <summary>Yetkiyi doğrular, branch'i döndürür: canlı veritabanı uçları bundan sonra sağlayıcıya gider.</summary>
    Task<ModuleOutcome<StudioBranch>> AuthorizeBranchAsync(string actorUserId, string branchId, CancellationToken ct = default);

    Task<ModuleOutcome<MergePreview>> PreviewMergeAsync(string actorUserId, string branchId, CancellationToken ct = default);
}

internal sealed class StudioBranchService : IStudioBranches, IBranchService
{
    private const string BranchNotFound = "Branch not found.";
    private const string ProjectNotFound = "Project not found, or it does not belong to this user.";
    private const int CommitAttempts = 5;

    // Frontend enum'ları JSON'a string yazıyor; dönüştürücü olmadan bu alanlarda JsonException atılır.
    private static readonly JsonSerializerOptions SchemaJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly StudioDbContext _db;
    private readonly ITenantProjectAuthority _authority;

    public StudioBranchService(StudioDbContext db, ITenantProjectAuthority authority)
    {
        _db = db;
        _authority = authority;
    }

    // ── Eşleme ───────────────────────────────────────────────────────────────

    private static readonly System.Linq.Expressions.Expression<Func<Branch, StudioBranch>> BranchOf = b =>
        new StudioBranch(b.Id, b.ProjectId, b.Name, b.ParentBranchId, b.ForkedFromVersion, b.IsDefault, b.CreatedByUserId, b.CreatedAt, b.ClosedAt);

    private static readonly System.Linq.Expressions.Expression<Func<SchemaVersion, StudioVersionHeader>> HeaderOf = v =>
        new StudioVersionHeader(v.Id, v.ProjectId, v.BranchId, v.Version, v.Checksum, v.Message, v.TableCount, v.AuthorUserId, v.CreatedAt);

    private static readonly System.Linq.Expressions.Expression<Func<SchemaVersion, StudioSchemaVersion>> VersionOf = v =>
        new StudioSchemaVersion(v.Id, v.ProjectId, v.BranchId, v.Version, v.Checksum, v.SchemaJson, v.Message, v.TableCount, v.AuthorUserId, v.CreatedAt);

    private static StudioBranch Map(Branch b) =>
        new(b.Id, b.ProjectId, b.Name, b.ParentBranchId, b.ForkedFromVersion, b.IsDefault, b.CreatedByUserId, b.CreatedAt, b.ClosedAt);

    // ── Modüller arası sözleşme ─────────────────────────────────────────────

    public Task<StudioBranch?> GetAsync(string branchId, CancellationToken ct = default) =>
        _db.Branches.AsNoTracking().Where(b => b.Id == branchId).Select(BranchOf).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<string, StudioBranch>> GetManyAsync(IReadOnlyCollection<string> branchIds, CancellationToken ct = default)
    {
        if (branchIds is not { Count: > 0 }) return new Dictionary<string, StudioBranch>();
        return await _db.Branches.AsNoTracking().Where(b => branchIds.Contains(b.Id)).Select(BranchOf).ToDictionaryAsync(b => b.Id, ct);
    }

    public async Task<IReadOnlyList<StudioBranch>> ListAsync(string projectId, CancellationToken ct = default) =>
        await _db.Branches.AsNoTracking()
            .Where(b => b.ProjectId == projectId)
            .OrderByDescending(b => b.IsDefault)
            .ThenBy(b => b.CreatedAt)
            .Select(BranchOf)
            .ToListAsync(ct);

    /// <summary>
    /// Kısmi unique index eşzamanlı iki çağrıdan yalnızca birinin eklemesine izin verir. Kaybeden 500
    /// vermek yerine kazananın satırını okur: aynı anda katılan iki kullanıcı aynı realtime odasında buluşur.
    /// </summary>
    public async Task<StudioBranch> GetOrCreateDefaultAsync(string projectId, string userId, CancellationToken ct = default)
    {
        var existing = await _db.Branches.AsNoTracking()
            .Where(b => b.ProjectId == projectId && b.IsDefault).Select(BranchOf).FirstOrDefaultAsync(ct);
        if (existing is not null) return existing;

        var created = new Branch
        {
            ProjectId = projectId,
            Name = "main",
            IsDefault = true,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Branches.Add(created);

        try
        {
            await _db.SaveChangesAsync(ct);
            _db.Entry(created).State = EntityState.Detached;
            return Map(created);
        }
        catch (DbUpdateException)
        {
            _db.Entry(created).State = EntityState.Detached;
            var winner = await _db.Branches.AsNoTracking()
                .Where(b => b.ProjectId == projectId && b.IsDefault).Select(BranchOf).FirstOrDefaultAsync(ct);
            // Yarış değilse (başka bir kısıt ihlali) hatayı yutmak yanlış olur.
            if (winner is null) throw;
            return winner;
        }
    }

    public Task<string?> ResolveProjectIdAsync(string branchIdOrRoom, CancellationToken ct = default)
    {
        const string prefix = "room-";
        var bare = branchIdOrRoom.StartsWith(prefix, StringComparison.Ordinal) ? branchIdOrRoom[prefix.Length..] : branchIdOrRoom;
        return _db.Branches.AsNoTracking()
            .Where(b => b.Id == branchIdOrRoom || b.Id == bare)
            .Select(b => b.ProjectId)
            .FirstOrDefaultAsync(ct);
    }

    public Task<StudioSchemaVersion?> GetVersionAsync(string versionId, CancellationToken ct = default) =>
        _db.SchemaVersions.AsNoTracking().Where(v => v.Id == versionId).Select(VersionOf).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<string, StudioVersionHeader>> GetVersionHeadersAsync(IReadOnlyCollection<string> versionIds, CancellationToken ct = default)
    {
        if (versionIds is not { Count: > 0 }) return new Dictionary<string, StudioVersionHeader>();
        return await _db.SchemaVersions.AsNoTracking().Where(v => versionIds.Contains(v.Id)).Select(HeaderOf).ToDictionaryAsync(v => v.Id, ct);
    }

    public Task<StudioSchemaVersion?> GetLatestVersionAsync(string branchId, CancellationToken ct = default) =>
        _db.SchemaVersions.AsNoTracking()
            .Where(v => v.BranchId == branchId)
            .OrderByDescending(v => v.Version)
            .Select(VersionOf)
            .FirstOrDefaultAsync(ct);

    public Task<StudioSchemaVersion?> GetVersionAtAsync(string branchId, int version, CancellationToken ct = default) =>
        _db.SchemaVersions.AsNoTracking()
            .Where(v => v.BranchId == branchId && v.Version == version)
            .Select(VersionOf)
            .FirstOrDefaultAsync(ct);

    public async Task<StudioSchemaVersion> AddVersionAsync(
        string branchId, string schemaJson, string? message, short tableCount, string? authorUserId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaJson);

        var branch = await _db.Branches.AsNoTracking()
            .Where(b => b.Id == branchId)
            .Select(b => new { b.ProjectId })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException(BranchNotFound);

        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schemaJson))).ToLowerInvariant();

        if (!_db.Database.IsNpgsql())
            return await AppendVersionAsync(branchId, branch.ProjectId, checksum, schemaJson, message, tableCount, authorUserId, ct);

        // Yalnızca yeniden denemek yetmez: her turda bir kişi kazanır, sekiz eşzamanlı commit beş denemeyi tüketip
        // kullanıcıya hata döndürüyordu. Dal satırı kilidi numara seçimini sıraya koyar. NO KEY: aynı dala FK ile
        // bağlanan başka yazmaları bekletmez.
        return await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = _db.Database.CurrentTransaction is null ? await _db.Database.BeginTransactionAsync(ct) : null;
            await _db.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM studio."Branches" WHERE "Id" = {branchId} FOR NO KEY UPDATE""", ct);
            var added = await AppendVersionAsync(branchId, branch.ProjectId, checksum, schemaJson, message, tableCount, authorUserId, ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return added;
        });
    }

    private async Task<StudioSchemaVersion> AppendVersionAsync(
        string branchId, string projectId, string checksum, string schemaJson, string? message, short tableCount, string? authorUserId, CancellationToken ct)
    {
        // (BranchId, Version) tekil: kilit almayan yol (SQLite) ya da sıraya girmeyen bir yazıcı aynı numarayı seçerse
        // ikincisi düşer. Commit'in içeriği taban sürüme bağlı değil, sonraki numarayla denemek doğru sıralamayı verir.
        for (var attempt = 1; ; attempt++)
        {
            var last = await _db.SchemaVersions.AsNoTracking()
                .Where(v => v.BranchId == branchId)
                .MaxAsync(v => (int?)v.Version, ct) ?? 0;

            var version = new SchemaVersion
            {
                ProjectId = projectId,
                BranchId = branchId,
                Version = last + 1,
                Checksum = checksum,
                SchemaJson = schemaJson,
                Message = message,
                TableCount = tableCount,
                AuthorUserId = authorUserId,
                CreatedAt = DateTime.UtcNow,
            };
            _db.SchemaVersions.Add(version);

            try
            {
                await _db.SaveChangesAsync(ct);
                _db.Entry(version).State = EntityState.Detached;
                return new StudioSchemaVersion(version.Id, version.ProjectId, version.BranchId, version.Version, version.Checksum, version.SchemaJson, version.Message, version.TableCount, version.AuthorUserId, version.CreatedAt);
            }
            catch (DbUpdateException ex) when (attempt < CommitAttempts && IsUniqueViolation(ex) && _db.Database.CurrentTransaction is null)
            {
                _db.Entry(version).State = EntityState.Detached;
            }
            catch
            {
                // Bağlam kapsamlı: izlenen satır kalırsa aynı istekteki sonraki kayıt onu tekrar yazmayı dener.
                _db.Entry(version).State = EntityState.Detached;
                throw;
            }
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException switch
    {
        System.Data.Common.DbException { SqlState: "23505" } => true,
        System.Data.Common.DbException { ErrorCode: 19 } => true,
        _ => false,
    };

    public Task<int> CountVersionsAsync(string projectId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
        _db.SchemaVersions.AsNoTracking().CountAsync(v => v.ProjectId == projectId && v.CreatedAt >= fromUtc && v.CreatedAt <= toUtc, ct);

    public Task<int> CountBranchesAsync(string projectId, CancellationToken ct = default) =>
        _db.Branches.AsNoTracking().CountAsync(b => b.ProjectId == projectId, ct);

    public Task<int> CountVersionsAsync(string projectId, CancellationToken ct = default) =>
        _db.SchemaVersions.AsNoTracking().CountAsync(v => v.ProjectId == projectId, ct);

    // ── Son kullanıcı işlemleri ─────────────────────────────────────────────

    private Task<bool> CanEditAsync(string actorUserId, string projectId, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(actorUserId) || string.IsNullOrWhiteSpace(projectId)
            ? Task.FromResult(false)
            : _authority.CanEditAsync(projectId, actorUserId, ct);

    public async Task<ModuleOutcome<StudioBranch>> CreateAsync(string actorUserId, CreateBranchCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.ProjectId) || string.IsNullOrWhiteSpace(command.Name))
            return ModuleOutcome<StudioBranch>.Invalid("ProjectId and Name are required.");
        if (!await CanEditAsync(actorUserId, command.ProjectId, ct))
            return ModuleOutcome<StudioBranch>.NotFound(ProjectNotFound);

        if (await _db.Branches.AsNoTracking().AnyAsync(b => b.ProjectId == command.ProjectId && b.Name == command.Name, ct))
            return ModuleOutcome<StudioBranch>.Conflict($"A branch named '{command.Name}' already exists in this project.");

        if (command.ParentBranchId is not null &&
            !await _db.Branches.AsNoTracking().AnyAsync(b => b.Id == command.ParentBranchId && b.ProjectId == command.ProjectId, ct))
            return ModuleOutcome<StudioBranch>.Invalid("ParentBranchId was not found in this project.");

        var branch = new Branch
        {
            ProjectId = command.ProjectId,
            Name = command.Name,
            ParentBranchId = command.ParentBranchId,
            ForkedFromVersion = command.ForkedFromVersion,
            IsDefault = command.IsDefault,
            CreatedByUserId = actorUserId,
            CreatedAt = DateTime.UtcNow,
        };

        _db.ChangeTracker.Clear();
        // Kısmi unique index tek varsayılanı garanti eder; kullanıcıya çıplak kısıt hatası göstermemek
        // için eskisi aynı SaveChanges'te kapatılır.
        if (command.IsDefault)
        {
            var previous = await _db.Branches.Where(b => b.ProjectId == command.ProjectId && b.IsDefault).ToListAsync(ct);
            foreach (var b in previous) b.IsDefault = false;
        }

        _db.Branches.Add(branch);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return ModuleOutcome<StudioBranch>.Conflict($"A branch named '{command.Name}' already exists in this project.");
        }
        _db.ChangeTracker.Clear();
        return ModuleOutcome<StudioBranch>.Ok(Map(branch));
    }

    public async Task<ModuleOutcome<IReadOnlyList<StudioBranch>>> ListAsync(string actorUserId, string projectId, CancellationToken ct = default)
    {
        if (!await CanEditAsync(actorUserId, projectId, ct))
            return ModuleOutcome<IReadOnlyList<StudioBranch>>.NotFound(ProjectNotFound);
        return ModuleOutcome<IReadOnlyList<StudioBranch>>.Ok(await ListAsync(projectId, ct));
    }

    public async Task<ModuleOutcome<StudioBranch>> GetDefaultAsync(string actorUserId, string projectId, CancellationToken ct = default)
    {
        if (!await CanEditAsync(actorUserId, projectId, ct))
            return ModuleOutcome<StudioBranch>.NotFound(ProjectNotFound);
        var branch = await _db.Branches.AsNoTracking().Where(b => b.ProjectId == projectId && b.IsDefault).Select(BranchOf).FirstOrDefaultAsync(ct);
        return branch is null
            ? ModuleOutcome<StudioBranch>.NotFound("This project has no default branch yet. POST to create it.")
            : ModuleOutcome<StudioBranch>.Ok(branch);
    }

    public async Task<ModuleOutcome<(StudioBranch Branch, bool Created)>> EnsureDefaultAsync(string actorUserId, string projectId, CancellationToken ct = default)
    {
        if (!await CanEditAsync(actorUserId, projectId, ct))
            return ModuleOutcome<(StudioBranch, bool)>.NotFound(ProjectNotFound);
        var existed = await _db.Branches.AsNoTracking().AnyAsync(b => b.ProjectId == projectId && b.IsDefault, ct);
        var branch = await GetOrCreateDefaultAsync(projectId, actorUserId, ct);
        return ModuleOutcome<(StudioBranch, bool)>.Ok((branch, !existed));
    }

    public async Task<ModuleOutcome<StudioBranch>> AuthorizeBranchAsync(string actorUserId, string branchId, CancellationToken ct = default)
    {
        var branch = await GetAsync(branchId, ct);
        if (branch is null || !await CanEditAsync(actorUserId, branch.ProjectId, ct))
            return ModuleOutcome<StudioBranch>.NotFound(BranchNotFound);
        return ModuleOutcome<StudioBranch>.Ok(branch);
    }

    public async Task<ModuleOutcome<StudioVersionHeader>> CommitAsync(string actorUserId, string branchId, string? schemaJson, string? message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
            return ModuleOutcome<StudioVersionHeader>.Invalid("SchemaJson cannot be empty.");

        var branch = await AuthorizeBranchAsync(actorUserId, branchId, ct);
        if (branch.Value is null) return ModuleOutcome<StudioVersionHeader>.NotFound(BranchNotFound);
        if (branch.Value.ClosedAt is not null)
            return ModuleOutcome<StudioVersionHeader>.Conflict("A new version cannot be added to a closed branch.");

        short tableCount;
        try
        {
            var parsed = JsonSerializer.Deserialize<DatabaseSchema>(schemaJson, SchemaJsonOptions);
            tableCount = (short)(parsed?.Tables?.Count ?? 0);
        }
        catch (JsonException)
        {
            return ModuleOutcome<StudioVersionHeader>.Invalid("SchemaJson is not a valid DatabaseSchema.");
        }

        var v = await AddVersionAsync(branchId, schemaJson, message, tableCount, actorUserId, ct);
        return ModuleOutcome<StudioVersionHeader>.Ok(new StudioVersionHeader(v.Id, v.ProjectId, v.BranchId, v.Version, v.Checksum, v.Message, v.TableCount, v.AuthorUserId, v.CreatedAt));
    }

    public async Task<ModuleOutcome<IReadOnlyList<StudioVersionHeader>>> ListVersionsAsync(string actorUserId, string branchId, CancellationToken ct = default)
    {
        var branch = await AuthorizeBranchAsync(actorUserId, branchId, ct);
        if (branch.Value is null) return ModuleOutcome<IReadOnlyList<StudioVersionHeader>>.NotFound(BranchNotFound);

        // SchemaJson kasıtlı olarak dışarıda: liste görünümü büyük blob'ları taşımamalı.
        IReadOnlyList<StudioVersionHeader> versions = await _db.SchemaVersions.AsNoTracking()
            .Where(v => v.BranchId == branchId)
            .OrderByDescending(v => v.Version)
            .Select(HeaderOf)
            .ToListAsync(ct);
        return ModuleOutcome<IReadOnlyList<StudioVersionHeader>>.Ok(versions);
    }

    public async Task<ModuleOutcome<StudioSchemaVersion>> GetVersionAsync(string actorUserId, string versionId, CancellationToken ct = default)
    {
        var version = await GetVersionAsync(versionId, ct);
        if (version is null || !await CanEditAsync(actorUserId, version.ProjectId, ct))
            return ModuleOutcome<StudioSchemaVersion>.NotFound("Version not found.");
        return ModuleOutcome<StudioSchemaVersion>.Ok(version);
    }

    public async Task<ModuleOutcome<MergePreview>> PreviewMergeAsync(string actorUserId, string branchId, CancellationToken ct = default)
    {
        var authorized = await AuthorizeBranchAsync(actorUserId, branchId, ct);
        if (authorized.Value is not { } source) return ModuleOutcome<MergePreview>.NotFound(BranchNotFound);

        // Ata kaydı yoksa üç yollu birleştirme YAPILAMAZ. Sessizce iki yollu diff'e düşmek olmayan bir
        // güvence vermek olurdu: "otomatik birleşti" ifadesinin anlamı ortak atadan geliyor.
        if (source.ParentBranchId is null || source.ForkedFromVersion is null)
            return ModuleOutcome<MergePreview>.Invalid(
                "This branch has no recorded ancestor, so it cannot be merged three-way. " +
                "Only branches forked from another branch carry the fork point a merge needs.");

        var target = await GetAsync(source.ParentBranchId, ct);
        if (target is null)
            return ModuleOutcome<MergePreview>.Invalid("The branch this one was forked from no longer exists.");

        var baseSchema = Deserialize((await GetVersionAtAsync(target.Id, source.ForkedFromVersion.Value, ct))?.SchemaJson);
        if (baseSchema is null)
            return ModuleOutcome<MergePreview>.Invalid("The ancestor version this branch was forked from is missing.");

        var ours = Deserialize((await GetLatestVersionAsync(target.Id, ct))?.SchemaJson);
        var theirs = Deserialize((await GetLatestVersionAsync(source.Id, ct))?.SchemaJson);
        if (ours is null || theirs is null)
            return ModuleOutcome<MergePreview>.Invalid("Both branches need at least one committed schema version before they can be merged.");

        var result = SchemaThreeWayMerger.Merge(baseSchema, ours, theirs);
        return ModuleOutcome<MergePreview>.Ok(new MergePreview(source.Name, target.Name, source.ForkedFromVersion.Value, result));
    }

    private static DatabaseSchema? Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<DatabaseSchema>(json, SchemaJsonOptions);
}
