using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.BuildingBlocks.Security;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;
using Namines.Gateway.Domain;

namespace Namines.Gateway.Application;

public sealed record CreateKeyCommand(
    string? Name, bool CanWrite, bool CanExecuteSql, bool CanExport, DateTime? ExpiresAt,
    string? AllowedOrigins, string? AllowedIps, int? RateLimitPerMinute);

public sealed record CreatedKeyView(
    string Id, string Name, string Prefix, bool CanWrite, bool CanExecuteSql, bool CanExport, DateTime? ExpiresAt,
    string? AllowedOrigins, string? AllowedIps, int? RateLimitPerMinute, string Key);

public sealed record KeyView(
    string Id, string Name, string Prefix, bool CanWrite, bool CanExecuteSql, bool CanExport,
    string? AllowedOrigins, string? AllowedIps, int? RateLimitPerMinute,
    DateTime CreatedAt, DateTime? ExpiresAt, DateTime? RevokedAt, DateTime? LastUsedAt);

public sealed record PermissionCommand(string? TableName, bool CanRead, bool CanWrite, bool CanExport, string? MaskedColumns);

public sealed record PermissionView(string TableName, bool CanRead, bool CanWrite, bool CanExport, string? MaskedColumns, DateTime UpdatedAt);

public sealed record SetPermissionResult(string TableName, bool CanRead, bool CanWrite, bool CanExport, string? MaskedColumns);

public sealed record DownloadKey(string RawKey);

/// <summary>
/// Anahtar ve izin yönetiminin aktörlü yüzeyi. Yetki burada da denetlenir: bu servisi çağıran başka
/// bir yol (uç, iş) controller'daki denetimi atlayamaz. Görünmeyen projede 404, görülüp yönetilemeyen 403.
/// </summary>
public interface IGatewayKeyService
{
    Task<bool> CanManageAsync(string actorUserId, string projectId, CancellationToken ct = default);

    Task<ModuleOutcome<CreatedKeyView>> CreateAsync(string actorUserId, string projectId, CreateKeyCommand command, CancellationToken ct = default);

    Task<ModuleOutcome<IReadOnlyList<KeyView>>> ListAsync(string actorUserId, string projectId, CancellationToken ct = default);

    /// <summary>İdempotent: ikinci iptal ilk zamanı değiştirmez. Başka projenin anahtarı bulunamaz.</summary>
    Task<ModuleOutcome<bool>> RevokeAsync(string actorUserId, string projectId, string keyId, CancellationToken ct = default);

    Task<ModuleOutcome<IReadOnlyList<PermissionView>>> ListPermissionsAsync(string actorUserId, string projectId, CancellationToken ct = default);

    /// <summary>İki yetki de kapalıysa satır silinir; Value null döner.</summary>
    Task<ModuleOutcome<SetPermissionResult?>> SetPermissionAsync(string actorUserId, string projectId, PermissionCommand command, CancellationToken ct = default);

    /// <summary>
    /// İndirilen projeye gömülen anahtarı döndürür: öncekini iptal eder, yenisini üretir. Ham anahtar
    /// bir daha gösterilemediği için eskisi zaten kullanılamaz, tek yapılabilecek bu rotasyon. Owner ister.
    /// </summary>
    Task<ModuleOutcome<DownloadKey>> RotateDownloadKeyAsync(string actorUserId, string projectId, CancellationToken ct = default);
}

/// <summary>Başka modüllerin akışlarının ihtiyaç duyduğu kapsamlı işlemler. Yetki çağıranda.</summary>
public interface IGatewayMaintenance
{
    Task<int> RevokeMemberKeysAsync(string projectId, string userId, bool privilegedOnly, CancellationToken ct = default);

    Task<int> RevokeKeysByCreatorAsync(string userId, CancellationToken ct = default);

    /// <summary>Proje silmede anahtar ve izinler. Denetim kayıtları bilerek kalır: projeden uzun yaşar.</summary>
    Task PurgeProjectAsync(string projectId, CancellationToken ct = default);

    Task SeedTablePermissionAsync(string projectId, string tableName, bool canRead, bool canWrite, IReadOnlyList<string> maskedColumns, CancellationToken ct = default);

    Task<int> PurgeAuditAsync(int days, DateTime nowUtc, TimeSpan pause, CancellationToken ct = default);
}

internal sealed class GatewayKeyService : IGatewayKeyService, IGatewayMaintenance
{
    private const string ProjectNotFound = "Project not found.";
    public const string DownloadKeyName = "Downloaded project";

    private readonly IGatewayData _db;
    private readonly ITenantProjectAuthority _authority;
    private readonly IBillingAccounts _billing;

    public GatewayKeyService(IGatewayData db, ITenantProjectAuthority authority, IBillingAccounts billing)
    {
        _db = db;
        _authority = authority;
        _billing = billing;
    }

    public async Task<bool> CanManageAsync(string actorUserId, string projectId, CancellationToken ct = default) =>
        !string.IsNullOrWhiteSpace(actorUserId) && await _authority.CanManageMembersAsync(projectId, actorUserId, ct);

    private async Task<bool> IsOwnerAsync(string actorUserId, string projectId, CancellationToken ct) =>
        await _authority.GetRoleAsync(projectId, actorUserId, ct) == OrgRole.Owner;

    private async Task<(PlanTier Tier, int Ceiling)> PlanCeilingAsync(string userId, CancellationToken ct)
    {
        var account = await _billing.GetPlanSnapshotAsync(userId, ct);
        var tier = PlanQuotas.Resolve(account?.SubscriptionStatus, account?.PlanCode, account?.IsDev ?? false);
        return (tier, PlanQuotas.For(tier).GatewayRequestsPerMinute);
    }

    public async Task<ModuleOutcome<CreatedKeyView>> CreateAsync(string actorUserId, string projectId, CreateKeyCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await CanManageAsync(actorUserId, projectId, ct))
            return ModuleOutcome<CreatedKeyView>.NotFound(ProjectNotFound);

        if (string.IsNullOrWhiteSpace(command.Name))
            return ModuleOutcome<CreatedKeyView>.Invalid("A name is required for the key.");

        // Sıfır ya da negatif limit anahtarı kullanılamaz kılardı; "limit yok" saymak da yanlış olurdu.
        if (command.RateLimitPerMinute is <= 0)
            return ModuleOutcome<CreatedKeyView>.Invalid("Rate limit must be greater than zero.");

        // İstek hakkı planın tavanını aşamaz: ücretsiz hesap kendine 100.000 rpm'lik anahtar üretemesin.
        var (tier, ceiling) = await PlanCeilingAsync(actorUserId, ct);
        if (command.RateLimitPerMinute > ceiling)
            return ModuleOutcome<CreatedKeyView>.Invalid($"The {tier} plan allows at most {ceiling} requests per minute per key.");

        // Ham SQL ve dışa aktarım üretenin kendi rolünden daha geniş olamaz: bir Admin kendine ham-SQL
        // anahtarı basıp Owner-yalnız eşiği anahtar yolundan aşabiliyordu.
        if ((command.CanExecuteSql || command.CanExport) && !await IsOwnerAsync(actorUserId, projectId, ct))
            return ModuleOutcome<CreatedKeyView>.Forbidden(
                "Only the project Owner can grant raw SQL or bulk export to a key. " +
                "Raw SQL bypasses table permissions, and export downloads whole tables.");

        // Liste geçersizse anahtar HİÇ üretilmez: geçersiz girdiyi sessizce atmak, kısıtladığını sanan
        // kullanıcıya daha geniş ya da kullanılamaz bir anahtar bırakırdı.
        var origins = GatewayAllowlistRules.ParseOrigins(command.AllowedOrigins);
        if (AllowlistError("origin", origins, command.AllowedOrigins) is { } originError)
            return ModuleOutcome<CreatedKeyView>.Invalid(originError);

        var ips = GatewayAllowlistRules.ParseIps(command.AllowedIps);
        if (AllowlistError("IP or CIDR", ips, command.AllowedIps) is { } ipError)
            return ModuleOutcome<CreatedKeyView>.Invalid(ipError);

        var (entity, rawKey) = GatewayKeys.CreateKey(projectId, command.Name.Trim(), actorUserId, command.CanWrite, command.ExpiresAt);
        entity.CanExecuteSql = command.CanExecuteSql;
        entity.CanExport = command.CanExport;
        GatewayAllowlistRules.ApplyOrigins(entity, origins.Entries);
        GatewayAllowlistRules.ApplyIps(entity, ips.Entries);
        // Belirtilmemişse planın hakkı: kullanıcıyı sayı uydurmaya zorlamak çoğunu yanlış seçtirirdi.
        entity.RateLimitPerMinute = command.RateLimitPerMinute ?? ceiling;

        _db.GatewayApiKeys.Add(entity);
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();

        // Ham anahtar SADECE burada döner; kayıtta yalnızca özeti var.
        return ModuleOutcome<CreatedKeyView>.Ok(new CreatedKeyView(
            entity.Id, entity.Name, entity.Prefix, entity.CanWrite, entity.CanExecuteSql, entity.CanExport, entity.ExpiresAt,
            GatewayAllowlistRules.DisplayOrigins(entity.RestrictOrigins, origins.Entries),
            GatewayAllowlistRules.DisplayIps(entity.RestrictIps, ips.Entries),
            entity.RateLimitPerMinute, rawKey));
    }

    private static string? AllowlistError(string kind, AllowlistParseResult parsed, string? raw)
    {
        if (parsed.Invalid.Count > 0)
            return $"Invalid {kind} entry: '{parsed.Invalid[0]}'. " +
                   (kind == "origin"
                       ? "Use scheme://host[:port] with no path or trailing slash. Wildcards are not supported."
                       : "Use a plain address or address/prefix.");
        if (parsed.Entries.Count > GatewayAllowlistRules.MaxEntriesPerList)
            return $"At most {GatewayAllowlistRules.MaxEntriesPerList} {kind} entries are allowed per key.";
        if (!parsed.HasAnyEntry && !string.IsNullOrWhiteSpace(raw))
            return $"The {kind} list has no entries. Leave it empty for no restriction.";
        return null;
    }

    public async Task<ModuleOutcome<IReadOnlyList<KeyView>>> ListAsync(string actorUserId, string projectId, CancellationToken ct = default)
    {
        if (!await CanManageAsync(actorUserId, projectId, ct))
            return ModuleOutcome<IReadOnlyList<KeyView>>.NotFound(ProjectNotFound);

        var keys = await _db.GatewayApiKeys.AsNoTracking()
            .Where(k => k.ProjectId == projectId)
            .OrderByDescending(k => k.CreatedAt)
            // KeyHash bilinçli olarak dışarıda: gösterilecek bir değer değil.
            .Select(k => new
            {
                k.Id, k.Name, k.Prefix, k.CanWrite, k.CanExecuteSql, k.CanExport,
                k.RestrictOrigins, k.RestrictIps,
                Origins = k.OriginRules.OrderBy(o => o.Position).Select(o => o.Origin).ToList(),
                Ips = k.IpRules.OrderBy(i => i.Position).Select(i => i.Cidr).ToList(),
                k.RateLimitPerMinute,
                k.CreatedAt, k.ExpiresAt, k.RevokedAt, k.LastUsedAt,
            })
            .AsSplitQuery()
            .ToListAsync(ct);

        IReadOnlyList<KeyView> views = keys.Select(k => new KeyView(
            k.Id, k.Name, k.Prefix, k.CanWrite, k.CanExecuteSql, k.CanExport,
            GatewayAllowlistRules.DisplayOrigins(k.RestrictOrigins, k.Origins),
            GatewayAllowlistRules.DisplayIps(k.RestrictIps, k.Ips),
            k.RateLimitPerMinute, k.CreatedAt, k.ExpiresAt, k.RevokedAt, k.LastUsedAt)).ToList();
        return ModuleOutcome<IReadOnlyList<KeyView>>.Ok(views);
    }

    public async Task<ModuleOutcome<bool>> RevokeAsync(string actorUserId, string projectId, string keyId, CancellationToken ct = default)
    {
        if (!await CanManageAsync(actorUserId, projectId, ct))
            return ModuleOutcome<bool>.NotFound(ProjectNotFound);

        _db.ChangeTracker.Clear();
        var key = await _db.GatewayApiKeys.FirstOrDefaultAsync(k => k.Id == keyId && k.ProjectId == projectId, ct);
        if (key is null) return ModuleOutcome<bool>.NotFound("Key not found.");

        // Silinmez, işaretlenir: "ne zaman iptal edildi" sorusu cevapsız kalmasın.
        key.RevokedAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return ModuleOutcome<bool>.Ok(true);
    }

    public async Task<ModuleOutcome<IReadOnlyList<PermissionView>>> ListPermissionsAsync(string actorUserId, string projectId, CancellationToken ct = default)
    {
        if (!await CanManageAsync(actorUserId, projectId, ct))
            return ModuleOutcome<IReadOnlyList<PermissionView>>.NotFound(ProjectNotFound);

        var permissions = await _db.GatewayTablePermissions.AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .OrderBy(p => p.TableName)
            .Select(p => new
            {
                p.TableName, p.CanRead, p.CanWrite, p.CanExport, p.UpdatedAt,
                Masked = p.MaskedColumnRules.OrderBy(m => m.Position).Select(m => m.ColumnName).ToList(),
            })
            .ToListAsync(ct);

        IReadOnlyList<PermissionView> views = permissions
            .Select(p => new PermissionView(p.TableName, p.CanRead, p.CanWrite, p.CanExport, GatewayAllowlistRules.DisplayMaskedColumns(p.Masked), p.UpdatedAt))
            .ToList();
        return ModuleOutcome<IReadOnlyList<PermissionView>>.Ok(views);
    }

    public async Task<ModuleOutcome<SetPermissionResult?>> SetPermissionAsync(string actorUserId, string projectId, PermissionCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await CanManageAsync(actorUserId, projectId, ct))
            return ModuleOutcome<SetPermissionResult?>.NotFound(ProjectNotFound);
        if (string.IsNullOrWhiteSpace(command.TableName))
            return ModuleOutcome<SetPermissionResult?>.Invalid("A table name is required.");

        var masked = GatewayAllowlistRules.ParseMaskedColumns(command.MaskedColumns);
        if (masked.Any(m => m.Length > GatewayAllowlistRules.MaxMaskedColumnLength))
            return ModuleOutcome<SetPermissionResult?>.Invalid($"A masked column name can be at most {GatewayAllowlistRules.MaxMaskedColumnLength} characters.");

        try
        {
            return await WritePermissionAsync(projectId, command, masked, ct);
        }
        catch (DbUpdateException ex) when (IsConcurrentPermissionWrite(ex))
        {
            // Aynı tabloya eşzamanlı iki PUT: kaybeden kazananın satırı üzerinden bir kez daha dener (BL-C5).
            _db.ChangeTracker.Clear();
            return await WritePermissionAsync(projectId, command, masked, ct);
        }
    }

    private static bool IsConcurrentPermissionWrite(DbUpdateException ex) =>
        ex is DbUpdateConcurrencyException || ex.InnerException is System.Data.Common.DbException { SqlState: "23505" or "23503" };

    private async Task<ModuleOutcome<SetPermissionResult?>> WritePermissionAsync(
        string projectId, PermissionCommand command, IReadOnlyList<string> masked, CancellationToken ct)
    {
        _db.ChangeTracker.Clear();
        var existing = await _db.GatewayTablePermissions
            .Include(p => p.MaskedColumnRules)
            .FirstOrDefaultAsync(p => p.ProjectId == projectId && p.TableName == command.TableName, ct);

        if (!command.CanRead && !command.CanWrite)
        {
            if (existing is not null) _db.GatewayTablePermissions.Remove(existing);
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
            return ModuleOutcome<SetPermissionResult?>.Ok(null);
        }

        if (existing is null)
        {
            existing = new GatewayTablePermission { ProjectId = projectId, TableName = command.TableName! };
            _db.GatewayTablePermissions.Add(existing);
        }

        // Yazma okumayı ima eder: yazabilen ama okuyamayan istemci yazdığını doğrulayamaz.
        existing.CanRead = command.CanRead || command.CanWrite;
        existing.CanWrite = command.CanWrite;
        // Dışa aktarım okumayı gerektirir: okunamayan tabloyu indirmek okuma iznini anlamsız kılardı.
        existing.CanExport = command.CanExport && existing.CanRead;
        SyncMaskedColumns(existing, masked);
        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        var result = new SetPermissionResult(existing.TableName, existing.CanRead, existing.CanWrite, existing.CanExport, GatewayAllowlistRules.DisplayMaskedColumns(masked));
        _db.ChangeTracker.Clear();
        return ModuleOutcome<SetPermissionResult?>.Ok(result);
    }

    private static void SyncMaskedColumns(GatewayTablePermission permission, IReadOnlyList<string> columns)
    {
        foreach (var stale in permission.MaskedColumnRules.Where(m => !columns.Contains(m.ColumnName, StringComparer.Ordinal)).ToList())
            permission.MaskedColumnRules.Remove(stale);

        for (var i = 0; i < columns.Count; i++)
        {
            var row = permission.MaskedColumnRules.FirstOrDefault(m => string.Equals(m.ColumnName, columns[i], StringComparison.Ordinal));
            if (row is null)
                permission.MaskedColumnRules.Add(new GatewayTableMaskedColumn { TablePermissionId = permission.Id, ColumnName = columns[i], Position = i });
            else
                row.Position = i;
        }
    }

    public async Task<ModuleOutcome<DownloadKey>> RotateDownloadKeyAsync(string actorUserId, string projectId, CancellationToken ct = default)
    {
        if (await _authority.GetRoleAsync(projectId, actorUserId, ct) != OrgRole.Owner)
            return ModuleOutcome<DownloadKey>.Forbidden();

        var (_, ceiling) = await PlanCeilingAsync(actorUserId, ct);
        var now = DateTime.UtcNow;
        var (entity, rawKey) = GatewayKeys.CreateKey(projectId, DownloadKeyName, actorUserId, canWrite: true, expiresAt: null);
        entity.RateLimitPerMinute = ceiling;

        // İptal ve yeni anahtar tek işlemde (denetim m-3): ikincisi düşerse eski anahtar iptal, yenisi yok kalırdı.
        // Çağıran zaten bir işlemdeyse ona katılır.
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            await using var tx = _db.Database.CurrentTransaction is null ? await _db.Database.BeginTransactionAsync(ct) : null;
            await _db.GatewayApiKeys
                .Where(k => k.ProjectId == projectId && k.Name == DownloadKeyName && k.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.RevokedAt, (DateTime?)now), ct);
            _db.GatewayApiKeys.Add(entity);
            await _db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            _db.ChangeTracker.Clear();
        });
        return ModuleOutcome<DownloadKey>.Ok(new DownloadKey(rawKey));
    }

    // ── Bakım ────────────────────────────────────────────────────────────────

    public Task<int> RevokeMemberKeysAsync(string projectId, string userId, bool privilegedOnly, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var query = _db.GatewayApiKeys.Where(k => k.ProjectId == projectId && k.CreatedByUserId == userId && k.RevokedAt == null);
        if (privilegedOnly) query = query.Where(k => k.CanExecuteSql || k.CanExport);
        return query.ExecuteUpdateAsync(s => s.SetProperty(k => k.RevokedAt, (DateTime?)now), ct);
    }

    public async Task<int> RevokeKeysByCreatorAsync(string userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var revoked = await _db.GatewayApiKeys
            .Where(k => k.CreatedByUserId == userId && k.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.RevokedAt, (DateTime?)now), ct);
        // Kullanıcı kimliği PII. Kullanıcı satırına giden FK artık modelde değil; temizlik DB'nin SET NULL'una bırakılsa
        // kullanıcı silinmeden önce biten akışlarda iz kalırdı, bu yüzden daha önce iptal edilmişlerde de burada silinir.
        await _db.GatewayApiKeys
            .Where(k => k.CreatedByUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.CreatedByUserId, (string?)null), ct);
        return revoked;
    }

    public async Task PurgeProjectAsync(string projectId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        await _db.AllowedOrigins.Where(o => _db.GatewayApiKeys.Any(k => k.Id == o.ApiKeyId && k.ProjectId == projectId)).ExecuteDeleteAsync(ct);
        await _db.AllowedIps.Where(i => _db.GatewayApiKeys.Any(k => k.Id == i.ApiKeyId && k.ProjectId == projectId)).ExecuteDeleteAsync(ct);
        await _db.GatewayApiKeys.Where(k => k.ProjectId == projectId).ExecuteDeleteAsync(ct);
        await _db.MaskedColumns.Where(m => _db.GatewayTablePermissions.Any(p => p.Id == m.TablePermissionId && p.ProjectId == projectId)).ExecuteDeleteAsync(ct);
        await _db.GatewayTablePermissions.Where(p => p.ProjectId == projectId).ExecuteDeleteAsync(ct);
        _db.ChangeTracker.Clear();
    }

    public async Task SeedTablePermissionAsync(string projectId, string tableName, bool canRead, bool canWrite, IReadOnlyList<string> maskedColumns, CancellationToken ct = default)
    {
        if (await _db.GatewayTablePermissions.AsNoTracking().AnyAsync(p => p.ProjectId == projectId && p.TableName == tableName, ct))
            return;

        _db.GatewayTablePermissions.Add(new GatewayTablePermission
        {
            ProjectId = projectId,
            TableName = tableName,
            CanRead = canRead,
            CanWrite = canWrite,
            MaskedColumnRules = maskedColumns.Select((c, i) => new GatewayTableMaskedColumn { ColumnName = c, Position = i }).ToList(),
        });
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    public Task<int> PurgeAuditAsync(int days, DateTime nowUtc, TimeSpan pause, CancellationToken ct = default)
    {
        if (days <= 0) return Task.FromResult(0);
        var cutoff = nowUtc.AddDays(-days);
        return Namines.BuildingBlocks.Persistence.BatchedDelete.RunAsync(
            _db.GatewayAuditEntries.Where(e => e.CreatedAt < cutoff), e => e.Id, pause, ct);
    }
}
