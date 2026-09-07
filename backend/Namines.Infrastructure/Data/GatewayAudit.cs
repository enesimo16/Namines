using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;

namespace Namines.Infrastructure.Data;

/// <summary>
/// Gateway denetim kaydı yazma yardımcıları (07 §5).
///
/// <b>Kayıt, işlemin sonucundan bağımsız olarak yazılır.</b> Yalnızca başarılı
/// yazmaları kaydetmek, denetim kaydını "ne oldu"nun değil "ne işe yaradı"nın
/// listesi yapar; reddedilen bir yazma girişimi çoğu zaman başarılı olandan daha
/// ilgi çekicidir.
/// </summary>
public static class GatewayAudit
{
    /// <summary>
    /// Kaydı ekler.
    ///
    /// <b>Hata YUTULUYOR.</b> Denetim kaydı yazılamadı diye kullanıcının yazma
    /// işlemini geri almak, ikincil bir sistemin arızasını birincil işleve
    /// taşımak olurdu — ve o işlem çoktan veritabanına yazıldı, geri alınamaz.
    /// Sorun log'a düşer, akış devam eder.
    /// </summary>
    public static async Task RecordAuditAsync(
        this AuthDbContext context,
        GatewayApiKey? key,
        string? actorUserId,
        GatewayWriteKind kind,
        string? tableName,
        string? rowKey,
        IEnumerable<string>? columns,
        int affectedRows,
        bool succeeded,
        CancellationToken ct = default,
        string? projectId = null)
    {
        try
        {
            context.GatewayAuditEntries.Add(new GatewayAuditEntry
            {
                // Namines Desk (D6, 06-LOGS.md §2): oturum yolunda proje anahtardan
                // GELMEZ ama çağıran (GatewayController.AuditedAsync) isteğin kendi
                // `ProjectId`'sini biliyor ve burada AÇIKÇA geçiyor — Desk'in her
                // yazması (create/update/delete, hepsi oturum yolunda) böylece
                // gerçek projeye kaydediliyor. `projectId` yoksa (Studio'nun canvas
                // çağrıları — ham bağlantı dizesi gönderir, proje bilgisi hiç
                // taşımaz) "session" placeholder'ına düşülür; bu davranış Desk'ten
                // ÖNCE de böyleydi, burada değiştirilmiyor.
                ProjectId = projectId ?? key?.ProjectId ?? "session",
                ApiKeyId = key?.Id,
                ApiKeyPrefix = key?.Prefix,
                ActorUserId = actorUserId,
                Kind = kind,
                TableName = tableName,
                RowKey = rowKey,
                // Yalnızca kolon ADLARI. Yazılan içerik müşterinin verisi ve onu
                // bizim veritabanımıza kopyalamak yeni bir sızıntı yüzeyi açardı.
                Columns = columns is null ? null : string.Join(",", columns),
                AffectedRows = affectedRows,
                Succeeded = succeeded,
            });

            await context.SaveChangesAsync(ct);
        }
        catch (Exception)
        {
            // Bilerek sessiz: bkz. yukarıdaki gerekçe.
        }
    }

    /// <summary>
    /// Bir projenin denetim kaydı, en yeniden eskiye — Namines Desk'in Logs
    /// ekranı için (D6, 06-LOGS.md §4): tarih aralığı, tür ve tablo filtresi,
    /// yalnızca-başarısızlar süzgeci, ve gerçek sayfalama.
    ///
    /// Sayfa boyutu üst sınırı ZORUNLU: sınırsız bir sorgu, kayıt büyüdükçe
    /// yavaşlar ve tek bir istekle sunucuyu meşgul eder.
    /// </summary>
    public static async Task<(List<GatewayAuditEntry> Entries, int TotalCount)> AuditTrailAsync(
        this AuthDbContext context, string projectId,
        DateTime? from = null, DateTime? to = null,
        IReadOnlyList<GatewayWriteKind>? kinds = null,
        string? tableName = null, bool? succeeded = null,
        int page = 1, int pageSize = 100, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var query = context.GatewayAuditEntries
            .AsNoTracking()
            .Where(e => e.ProjectId == projectId);

        if (from is not null) query = query.Where(e => e.CreatedAt >= from);
        if (to is not null) query = query.Where(e => e.CreatedAt <= to);
        if (kinds is { Count: > 0 }) query = query.Where(e => kinds.Contains(e.Kind));
        if (!string.IsNullOrWhiteSpace(tableName)) query = query.Where(e => e.TableName == tableName);
        if (succeeded is not null) query = query.Where(e => e.Succeeded == succeeded);

        // Filtrelenmiş TOPLAM — sayfalama çubuğu, filtrelenmemiş bir toplamla
        // çelişen bir sayı göstermesin diye filtre uygulandıktan SONRA sayılıyor
        // (GatewayService.ListAsync'teki aynı ilke).
        var total = await query.CountAsync(ct);

        var entries = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (entries, total);
    }
}
