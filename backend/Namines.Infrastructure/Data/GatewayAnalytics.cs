using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;

namespace Namines.Infrastructure.Data;

public sealed record AuditBucket(
    DateTime BucketStart, int Create, int Update, int Delete, int Import, int Rpc, int Sql);

public sealed record TopTable(string TableName, int Count);

/// <summary>
/// Namines Desk — Analytics (D7, 07-ANALYTICS.md §2). Tek gerçek proje-bazlı
/// zaman serisi kaynağı <c>GatewayAuditEntry</c> — Vercel'in Edge Requests/CPU
/// gibi metriklerinin BURADA karşılığı yok (Namines kimsenin uygulamasını
/// çalıştırmıyor); uydurma sayı göstermek yerine yalnızca gerçekten ölçülen
/// şey gösteriliyor: yazma işlemleri.
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

public static class GatewayAnalytics
{
    /// <summary>
    /// Ham SQL satırı — kova zamanı ve tür başına sayım (dahili, yalnızca
    /// <see cref="AnalyticsAsync"/> içinde kullanılır).
    /// </summary>
    private sealed class BucketRow
    {
        public DateTime Bucket { get; set; }
        public int Kind { get; set; }
        public int Cnt { get; set; }
    }

    /// <summary>
    /// Toplama SQL'DE yapılır (§3 gerekçesi): 30 günlük denetim kaydı on
    /// binlerce satır olabilir, hepsini tarayıcıya indirip orada gruplamak
    /// hem yavaş hem gereksiz veri taşıması. Zaman kovası Postgres'in kendi
    /// <c>date_trunc</c>'ıyla hesaplanır — bu Npgsql EF Core 8 sürümünde bir
    /// LINQ çevirisi olarak YOK, o yüzden ham SQL kullanılıyor
    /// (<c>Database.SqlQuery</c>, EF Core 8) — parametreler yine de
    /// parametreli kalıyor, string birleştirme yok. `Kind` tablo şemasında
    /// `integer` (bkz. migration AddGatewayAudit) — enum'a burada, C#
    /// tarafında dönülüyor.
    /// </summary>
    public static async Task<AnalyticsResult> AnalyticsAsync(
        this AuthDbContext context, string projectId, DateTime from, DateTime to, string bucket,
        CancellationToken ct = default)
    {
        var truncUnit = bucket == "hour" ? "hour" : "day";

        var baseQuery = context.GatewayAuditEntries
            .AsNoTracking()
            .Where(e => e.ProjectId == projectId && e.CreatedAt >= from && e.CreatedAt <= to);

        var rawRows = await context.Database.SqlQuery<BucketRow>(
            $@"SELECT date_trunc({truncUnit}, ""CreatedAt"") AS ""Bucket"", ""Kind"" AS ""Kind"", COUNT(*)::int AS ""Cnt""
               FROM ""GatewayAuditEntries""
               WHERE ""ProjectId"" = {projectId} AND ""CreatedAt"" >= {from} AND ""CreatedAt"" <= {to}
               GROUP BY 1, 2")
            .ToListAsync(ct);

        var buckets = rawRows
            .GroupBy(r => r.Bucket)
            .OrderBy(g => g.Key)
            .Select(g => new AuditBucket(
                g.Key,
                g.Where(x => (GatewayWriteKind)x.Kind == GatewayWriteKind.Create).Sum(x => x.Cnt),
                g.Where(x => (GatewayWriteKind)x.Kind == GatewayWriteKind.Update).Sum(x => x.Cnt),
                g.Where(x => (GatewayWriteKind)x.Kind == GatewayWriteKind.Delete).Sum(x => x.Cnt),
                g.Where(x => (GatewayWriteKind)x.Kind == GatewayWriteKind.Import).Sum(x => x.Cnt),
                g.Where(x => (GatewayWriteKind)x.Kind == GatewayWriteKind.Rpc).Sum(x => x.Cnt),
                g.Where(x => (GatewayWriteKind)x.Kind == GatewayWriteKind.Sql).Sum(x => x.Cnt)))
            .ToList();

        // Toplam/başarı/etkilenen-satır ayrı sorgular: her biri tek başına
        // `psql`'de doğrulanabilir tek bir SELECT COUNT/SUM'a karşılık gelir
        // (kabul kriteri 2) — tek bir birleşik sorguya sıkıştırmak, hatası
        // ayıklaması güç bir ifade uğruna bu doğrulanabilirliği kaybederdi.
        var totalWrites = await baseQuery.CountAsync(ct);
        var succeededCount = await baseQuery.CountAsync(e => e.Succeeded, ct);
        var totalAffectedRows = await baseQuery.SumAsync(e => (long)e.AffectedRows, ct);

        // EF Core, kayıt (record) tipini doğrudan projeksiyonda ÇEVİREMİYOR
        // (canlı Postgres'e karşı denenince ortaya çıktı — anonim tipe
        // projelenip TopTable'a sonradan dönülüyor).
        var topTablesRaw = await baseQuery
            .Where(e => e.TableName != null)
            .GroupBy(e => e.TableName)
            .Select(g => new { TableName = g.Key!, Count = g.Count() })
            .OrderByDescending(t => t.Count)
            .Take(10)
            .ToListAsync(ct);
        var topTables = topTablesRaw.Select(t => new TopTable(t.TableName, t.Count)).ToList();

        // İnsan (ActorUserId) vs uygulama (ApiKeyPrefix) — ikisi karşılıklı
        // dışlayıcı (bir yazma ya oturumdan ya anahtardan gelir, bkz.
        // AuthorizeAsync), toplamları TotalWrites'ı aşmaz.
        var humanCount = await baseQuery.CountAsync(e => e.ActorUserId != null, ct);
        var applicationCount = await baseQuery.CountAsync(e => e.ApiKeyPrefix != null, ct);

        var schemaVersionCount = await context.SchemaVersions
            .AsNoTracking()
            .CountAsync(v => v.ProjectId == projectId && v.CreatedAt >= from && v.CreatedAt <= to, ct);

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
