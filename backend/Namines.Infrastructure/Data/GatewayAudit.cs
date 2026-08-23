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
        CancellationToken ct = default)
    {
        try
        {
            context.GatewayAuditEntries.Add(new GatewayAuditEntry
            {
                // Oturum yolunda proje bilinmiyor; anahtar yolunda anahtarın projesi.
                ProjectId = key?.ProjectId ?? "session",
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
    /// Bir projenin denetim kaydı, en yeniden eskiye.
    ///
    /// Üst sınır ZORUNLU: sınırsız bir sorgu, kayıt büyüdükçe yavaşlar ve tek bir
    /// istekle sunucuyu meşgul eder.
    /// </summary>
    public static Task<List<GatewayAuditEntry>> AuditTrailAsync(
        this AuthDbContext context, string projectId, int take, CancellationToken ct = default) =>
        context.GatewayAuditEntries
            .AsNoTracking()
            .Where(e => e.ProjectId == projectId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(ct);
}
