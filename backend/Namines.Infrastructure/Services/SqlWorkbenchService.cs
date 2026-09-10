using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;

namespace Namines.Infrastructure.Services;

/// <summary>Bir sorgu geçmişi kaydının okunabilir hâli.</summary>
public sealed record SqlHistoryItem(
    string Id, string Sql, bool Truncated, int RowCount, bool Succeeded,
    string? ErrorMessage, int DurationMs, DateTime CreatedAt);

/// <summary>Kaydedilmiş bir sorgunun okunabilir hâli.</summary>
public sealed record SavedQueryItem(
    string Id, string Name, string Sql, DateTime CreatedAt, DateTime UpdatedAt);

/// <summary>
/// Desk SQL konsolunun kullanıcıya özel yan defterleri: geçmiş (F-01) ve
/// kaydedilmiş sorgular (F-02).
///
/// <b>Neden ayrı bir servis:</b> Bu iki tablo <see cref="GatewayService"/>'in
/// sorumluluğu değil — o müşterinin VERİTABANINA gidiyor, buradaki her şey
/// bizim kontrol veritabanımızda. İkisini karıştırmak, "hangi bağlantı hangi
/// veritabanına gidiyor" sorusunu bir daha okunmaz hâle getirirdi.
///
/// <b>Yetki bu sınıfta DEĞİL.</b> Proje sahipliği kontrolü çağıranın işi
/// (denetleyicideki Owner kapısı); buradaki her sorgu yalnızca
/// <c>userId</c> ile filtreleniyor. İkinci bir yerde yetki kontrolü yapmak,
/// iki kontrolün bir gün ayrışması demek.
/// </summary>
public interface ISqlWorkbenchService
{
    Task RecordAsync(
        string projectId, string userId, string sql, bool succeeded,
        int rowCount, string? errorMessage, int durationMs, CancellationToken ct);

    Task<IReadOnlyList<SqlHistoryItem>> GetHistoryAsync(
        string projectId, string userId, int limit, CancellationToken ct);

    Task<int> ClearHistoryAsync(string projectId, string userId, CancellationToken ct);

    Task<IReadOnlyList<SavedQueryItem>> GetSavedAsync(
        string projectId, string userId, CancellationToken ct);

    /// <summary>Kaydeder ya da aynı addaki kaydı GÜNCELLER.</summary>
    Task<SavedQueryItem> SaveAsync(
        string projectId, string userId, string name, string sql, CancellationToken ct);

    /// <summary>Siler. Başkasının kaydına dokunamaz — bulunamadı döner.</summary>
    Task<bool> DeleteSavedAsync(string projectId, string userId, string id, CancellationToken ct);
}

public sealed class SqlWorkbenchService : ISqlWorkbenchService
{
    private readonly AuthDbContext _db;

    public SqlWorkbenchService(AuthDbContext db) => _db = db;

    public async Task RecordAsync(
        string projectId, string userId, string sql, bool succeeded,
        int rowCount, string? errorMessage, int durationMs, CancellationToken ct)
    {
        var truncated = sql.Length > SqlQueryHistoryEntry.MaxSqlLength;

        _db.SqlQueryHistory.Add(new SqlQueryHistoryEntry
        {
            ProjectId = projectId,
            UserId = userId,
            Sql = truncated ? sql[..SqlQueryHistoryEntry.MaxSqlLength] : sql,
            Truncated = truncated,
            RowCount = rowCount,
            Succeeded = succeeded,
            // Hata mesajı sunucunun kullanıcıya ZATEN gösterdiği metin; burada
            // kısaltmak, geçmişten bakıldığında nedeni kaybettirirdi.
            ErrorMessage = errorMessage,
            DurationMs = durationMs,
        });

        await _db.SaveChangesAsync(ct);
        await PruneAsync(projectId, userId, ct);
    }

    /// <summary>
    /// Saklama sınırını uygular: en yeni <see cref="SqlQueryHistoryEntry.RetentionPerUserPerProject"/>
    /// kayıt kalır.
    ///
    /// <b>Neden her yazmada:</b> Arka plan işine bırakmak, iş çalışmadığında
    /// sınırın sessizce YOK olması demek — ve o iş, tam da hassas metnin
    /// biriktiği yerde çalışmıyor olur. Budama tek bir <c>Skip</c> sorgusu,
    /// maliyeti ihmal edilebilir.
    /// </summary>
    private async Task PruneAsync(string projectId, string userId, CancellationToken ct)
    {
        var stale = await _db.SqlQueryHistory
            .Where(h => h.UserId == userId && h.ProjectId == projectId)
            .OrderByDescending(h => h.CreatedAt)
            .Skip(SqlQueryHistoryEntry.RetentionPerUserPerProject)
            .Select(h => h.Id)
            .ToListAsync(ct);

        if (stale.Count == 0) return;

        await _db.SqlQueryHistory
            .Where(h => stale.Contains(h.Id))
            .ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<SqlHistoryItem>> GetHistoryAsync(
        string projectId, string userId, int limit, CancellationToken ct) =>
        await _db.SqlQueryHistory
            .AsNoTracking()
            .Where(h => h.UserId == userId && h.ProjectId == projectId)
            .OrderByDescending(h => h.CreatedAt)
            .Take(Math.Clamp(limit, 1, SqlQueryHistoryEntry.RetentionPerUserPerProject))
            .Select(h => new SqlHistoryItem(
                h.Id, h.Sql, h.Truncated, h.RowCount, h.Succeeded,
                h.ErrorMessage, h.DurationMs, h.CreatedAt))
            .ToListAsync(ct);

    public Task<int> ClearHistoryAsync(string projectId, string userId, CancellationToken ct) =>
        _db.SqlQueryHistory
            .Where(h => h.UserId == userId && h.ProjectId == projectId)
            .ExecuteDeleteAsync(ct);

    public async Task<IReadOnlyList<SavedQueryItem>> GetSavedAsync(
        string projectId, string userId, CancellationToken ct) =>
        await _db.SavedQueries
            .AsNoTracking()
            .Where(q => q.UserId == userId && q.ProjectId == projectId)
            .OrderBy(q => q.Name)
            .Select(q => new SavedQueryItem(q.Id, q.Name, q.Sql, q.CreatedAt, q.UpdatedAt))
            .ToListAsync(ct);

    public async Task<SavedQueryItem> SaveAsync(
        string projectId, string userId, string name, string sql, CancellationToken ct)
    {
        // `paramName` BILEREK verilmiyor: bu mesajlar kullaniciya AYNEN
        // gosteriliyor ve .NET, paramName varsa mesajin sonuna
        // " (Parameter 'name')" ekliyor. Kullaniciya kodun degisken adini
        // gostermek, hata mesajini hem cirkin hem kafa karistirici yapiyordu.
        name = name.Trim();
        if (name.Length == 0)
            throw new ArgumentException("A name is required.");
        if (name.Length > SavedQuery.MaxNameLength)
            throw new ArgumentException(
                $"The name cannot be longer than {SavedQuery.MaxNameLength} characters.");
        if (sql.Length > SavedQuery.MaxSqlLength)
            throw new ArgumentException(
                $"The query cannot be longer than {SavedQuery.MaxSqlLength} characters.");

        // Aynı ad = GÜNCELLEME. Hata dönmek, kullanıcıyı önce silmeye zorlardı
        // ve "kaydet"e ikinci kez basmak en doğal düzeltme hareketidir.
        var existing = await _db.SavedQueries
            .FirstOrDefaultAsync(q => q.UserId == userId && q.ProjectId == projectId && q.Name == name, ct);

        if (existing is not null)
        {
            existing.Sql = sql;
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new SavedQueryItem(existing.Id, existing.Name, existing.Sql, existing.CreatedAt, existing.UpdatedAt);
        }

        var count = await _db.SavedQueries
            .CountAsync(q => q.UserId == userId && q.ProjectId == projectId, ct);
        if (count >= SavedQuery.MaxPerUserPerProject)
            throw new InvalidOperationException(
                $"You already have {SavedQuery.MaxPerUserPerProject} saved queries in this project. " +
                "Delete one before saving another.");

        var entity = new SavedQuery { ProjectId = projectId, UserId = userId, Name = name, Sql = sql };
        _db.SavedQueries.Add(entity);
        await _db.SaveChangesAsync(ct);

        return new SavedQueryItem(entity.Id, entity.Name, entity.Sql, entity.CreatedAt, entity.UpdatedAt);
    }

    public async Task<bool> DeleteSavedAsync(
        string projectId, string userId, string id, CancellationToken ct)
    {
        // `UserId` de koşulda: id'yi bilen biri başkasının kaydını silemesin.
        // "Bulunamadı" ile "senin değil" arasında ayrım YAPILMIYOR — ikincisi,
        // o id'nin var olduğunu doğrulardı.
        var deleted = await _db.SavedQueries
            .Where(q => q.Id == id && q.UserId == userId && q.ProjectId == projectId)
            .ExecuteDeleteAsync(ct);

        return deleted > 0;
    }
}
