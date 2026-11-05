using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.BuildingBlocks.Security;
using Namines.Gateway.Domain;

namespace Namines.Gateway.Application;

public sealed record AuditBucket(DateTime BucketStart, int Create, int Update, int Delete, int Import, int Rpc, int Sql);

public sealed record TopTable(string TableName, int Count);

/// <summary>
/// Proje bazlı zaman serisinin tek gerçek kaynağı denetim kaydı. Uydurma sayı göstermek yerine yalnızca
/// gerçekten ölçülen şey döner: yazma işlemleri.
/// </summary>
public sealed record AnalyticsResult(
    IReadOnlyList<AuditBucket> Buckets,
    int TotalWrites,
    double SuccessRate,
    long TotalAffectedRows,
    IReadOnlyList<TopTable> TopTables,
    int HumanCount,
    int ApplicationCount,
    int SchemaVersionCount);

public sealed record AuditEntryView(
    string Id, string Kind, string? TableName, string? RowKey, string? Columns, int AffectedRows, bool Succeeded,
    string? ApiKeyPrefix, string? ActorUserId, DateTime CreatedAt);

public sealed record AuditTrailPage(int TotalCount, int Page, int PageSize, IReadOnlyList<AuditEntryView> Entries);

public sealed record AuditQuery(DateTime? From, DateTime? To, IReadOnlyList<GatewayWriteKind>? Kinds, string? TableName, bool? Succeeded, int Page, int PageSize);

/// <summary>Gateway denetim kaydı: yazma hiçbir zaman asıl isteği düşürmez, okuma yönetici yetkisi ister.</summary>
public interface IGatewayAuditLog
{
    /// <summary>
    /// İçerik yok: yalnızca kolon ADLARI. Yazılan veri müşterinindir, bizim veritabanımıza kopyalamak yeni
    /// sızıntı yüzeyi açardı. Hata yutulur: denetim yazılamadı diye müşteri isteği düşmemeli.
    /// </summary>
    Task RecordAsync(
        GatewayApiKey? key, string? actorUserId, GatewayWriteKind kind, string? tableName, string? rowKey,
        IEnumerable<string>? columns, int affectedRows, bool succeeded, CancellationToken ct = default, string? projectId = null);

    Task<ModuleOutcome<AuditTrailPage>> TrailAsync(string actorUserId, string projectId, AuditQuery query, CancellationToken ct = default);

    Task<ModuleOutcome<AnalyticsResult>> AnalyticsAsync(string actorUserId, string projectId, DateTime from, DateTime to, string bucket, CancellationToken ct = default);
}

internal sealed class GatewayAuditLog : IGatewayAuditLog
{
    private const string ProjectNotFound = "Project not found.";
    private const string AdminRequired = "Admin permission is required for this section.";

    private sealed class BucketRow
    {
        public DateTime Bucket { get; set; }
        public int Kind { get; set; }
        public int Cnt { get; set; }
    }

    private readonly IGatewayData _db;
    private readonly ITenantProjectAuthority _authority;
    private readonly IStudioBranches _branches;

    public GatewayAuditLog(IGatewayData db, ITenantProjectAuthority authority, IStudioBranches branches)
    {
        _db = db;
        _authority = authority;
        _branches = branches;
    }

    public async Task RecordAsync(
        GatewayApiKey? key, string? actorUserId, GatewayWriteKind kind, string? tableName, string? rowKey,
        IEnumerable<string>? columns, int affectedRows, bool succeeded, CancellationToken ct = default, string? projectId = null)
    {
        var entry = new GatewayAuditEntry
        {
            // Oturum yolunda proje anahtardan gelmez, çağıran açıkça geçer. Ham bağlantı dizesi gönderen
            // canvas çağrılarında proje bilgisi yok: "session" yer tutucusu.
            ProjectId = projectId ?? key?.ProjectId ?? "session",
            ApiKeyId = key?.Id,
            ApiKeyPrefix = key?.Prefix,
            ActorUserId = actorUserId,
            Kind = kind,
            TableName = tableName,
            RowKey = rowKey,
            Columns = columns is null ? null : string.Join(",", columns),
            AffectedRows = affectedRows,
            Succeeded = succeeded,
        };
        try
        {
            _db.GatewayAuditEntries.Add(entry);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception)
        {
            // Bilerek sessiz: bkz. arayüz notu.
        }
        finally
        {
            // Düşen satır izleyicide kalırsa sonraki kayıtlar da aynı hataya takılırdı.
            _db.Entry(entry).State = EntityState.Detached;
        }
    }

    private async Task<string?> DenyAsync(string actorUserId, string projectId, CancellationToken ct)
    {
        // Görünmeyen projede 404: hangi projelerin var olduğu sızmasın. Görüp yönetemeyene açık 403.
        if (!await _authority.CanViewAsync(projectId, actorUserId, ct)) return ProjectNotFound;
        if (!await _authority.CanManageMembersAsync(projectId, actorUserId, ct)) return AdminRequired;
        return null;
    }

    public async Task<ModuleOutcome<AuditTrailPage>> TrailAsync(string actorUserId, string projectId, AuditQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var denied = await DenyAsync(actorUserId, projectId, ct);
        if (denied == ProjectNotFound) return ModuleOutcome<AuditTrailPage>.NotFound(denied);
        if (denied is not null) return ModuleOutcome<AuditTrailPage>.Forbidden(denied);

        var (rows, total) = await QueryTrailAsync(projectId, query, ct);
        var entries = rows.Select(e => new AuditEntryView(
            e.Id, e.Kind.ToString().ToLowerInvariant(), e.TableName, e.RowKey, e.Columns, e.AffectedRows, e.Succeeded,
            e.ApiKeyPrefix, e.ActorUserId, e.CreatedAt)).ToList();

        // Yanıtın tarihsel sayfa numarası isteğin kendisi: sınıra çekilmiş değer değil.
        return ModuleOutcome<AuditTrailPage>.Ok(new AuditTrailPage(total, query.Page, query.PageSize, entries));
    }

    internal async Task<(List<GatewayAuditEntry> Entries, int TotalCount)> QueryTrailAsync(string projectId, AuditQuery query, CancellationToken ct)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 500);

        var q = _db.GatewayAuditEntries.AsNoTracking().Where(e => e.ProjectId == projectId);
        if (query.From is { } from) q = q.Where(e => e.CreatedAt >= from);
        if (query.To is { } to) q = q.Where(e => e.CreatedAt <= to);
        if (query.Kinds is { Count: > 0 } kinds) q = q.Where(e => kinds.Contains(e.Kind));
        if (!string.IsNullOrWhiteSpace(query.TableName)) q = q.Where(e => e.TableName == query.TableName);
        if (query.Succeeded is { } succeeded) q = q.Where(e => e.Succeeded == succeeded);

        // Filtrelenmiş toplam: sayfalama çubuğu filtresiz bir toplamla çelişmesin.
        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (rows, total);
    }

    public async Task<ModuleOutcome<AnalyticsResult>> AnalyticsAsync(string actorUserId, string projectId, DateTime from, DateTime to, string bucket, CancellationToken ct = default)
    {
        var denied = await DenyAsync(actorUserId, projectId, ct);
        if (denied == ProjectNotFound) return ModuleOutcome<AnalyticsResult>.NotFound(denied);
        if (denied is not null) return ModuleOutcome<AnalyticsResult>.Forbidden(denied);
        return ModuleOutcome<AnalyticsResult>.Ok(await ComputeAnalyticsAsync(projectId, from, to, bucket, ct));
    }

    internal async Task<AnalyticsResult> ComputeAnalyticsAsync(string projectId, DateTime from, DateTime to, string bucket, CancellationToken ct)
    {
        var truncUnit = bucket == "hour" ? "hour" : "day";

        var baseQuery = _db.GatewayAuditEntries.AsNoTracking()
            .Where(e => e.ProjectId == projectId && e.CreatedAt >= from && e.CreatedAt <= to);

        // date_trunc'ın EF çevirisi yok: ham SQL, parametreler yine parametreli. Şema adı model ile aynı.
        var rawRows = await _db.Database.SqlQuery<BucketRow>(
            $@"SELECT date_trunc({truncUnit}, ""CreatedAt"") AS ""Bucket"", ""Kind"" AS ""Kind"", COUNT(*)::int AS ""Cnt""
               FROM gateway.""GatewayAuditEntries""
               WHERE ""ProjectId"" = {projectId} AND ""CreatedAt"" >= {from} AND ""CreatedAt"" <= {to}
               GROUP BY 1, 2")
            .ToListAsync(ct);

        var buckets = rawRows
            .GroupBy(r => r.Bucket)
            .OrderBy(g => g.Key)
            .Select(g => new AuditBucket(
                g.Key,
                Create: g.Where(r => r.Kind == (int)GatewayWriteKind.Create).Sum(r => r.Cnt),
                Update: g.Where(r => r.Kind == (int)GatewayWriteKind.Update).Sum(r => r.Cnt),
                Delete: g.Where(r => r.Kind == (int)GatewayWriteKind.Delete).Sum(r => r.Cnt),
                Import: g.Where(r => r.Kind == (int)GatewayWriteKind.Import).Sum(r => r.Cnt),
                Rpc: g.Where(r => r.Kind == (int)GatewayWriteKind.Rpc).Sum(r => r.Cnt),
                Sql: g.Where(r => r.Kind == (int)GatewayWriteKind.Sql).Sum(r => r.Cnt)))
            .ToList();

        var totalWrites = await baseQuery.CountAsync(ct);
        var succeededCount = await baseQuery.CountAsync(e => e.Succeeded, ct);
        var totalAffectedRows = await baseQuery.SumAsync(e => (long)e.AffectedRows, ct);

        var topTablesRaw = await baseQuery
            .Where(e => e.TableName != null)
            .GroupBy(e => e.TableName)
            .Select(g => new { TableName = g.Key!, Count = g.Count() })
            .OrderByDescending(t => t.Count)
            .Take(10)
            .ToListAsync(ct);
        var topTables = topTablesRaw.Select(t => new TopTable(t.TableName, t.Count)).ToList();

        // İnsan (ActorUserId) ve uygulama (ApiKeyPrefix) karşılıklı dışlayıcı: toplamları TotalWrites'ı aşmaz.
        var humanCount = await baseQuery.CountAsync(e => e.ActorUserId != null, ct);
        var applicationCount = await baseQuery.CountAsync(e => e.ApiKeyPrefix != null, ct);

        var schemaVersionCount = await _branches.CountVersionsAsync(projectId, from, to, ct);

        return new AnalyticsResult(
            buckets,
            totalWrites,
            totalWrites == 0 ? 0.0 : (double)succeededCount / totalWrites,
            totalAffectedRows,
            topTables,
            humanCount,
            applicationCount,
            schemaVersionCount);
    }
}
