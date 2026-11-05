using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Security;
using Namines.Core.Models.Auth;
using Namines.Gateway.Domain;

namespace Namines.Gateway.Application;

/// <summary>
/// İstek yolu: ham anahtarı doğrular, tablo iznine ve maskeye karar verir. Karar burada; controller
/// yalnızca sonucu HTTP'ye çevirir. Proje rolü Studio sözleşmesinden, anahtar kaydı kendi tablosundan.
/// </summary>
public interface IGatewayAccess
{
    /// <summary>İptal, süre, üreticinin üyeliği ve Owner-yalnız yetkiler dahil; geçersizse null.</summary>
    Task<GatewayApiKey?> AuthenticateAsync(string? rawKey, CancellationToken ct = default);

    Task<bool> IsTableAllowedAsync(GatewayApiKey key, string tableName, bool forWrite, CancellationToken ct = default);

    Task<bool> IsTableExportAllowedAsync(GatewayApiKey key, string tableName, CancellationToken ct = default);

    Task<IReadOnlyList<string>> MaskedColumnsAsync(string projectId, string tableName, CancellationToken ct = default);

    Task<IReadOnlyList<GatewayTablePermission>> ReadableTablesAsync(string projectId, CancellationToken ct = default);

    Task<IReadOnlyList<GatewayTablePermission>> ListPermissionsAsync(string projectId, CancellationToken ct = default);

    /// <summary>Dakikada birden sık yazmaz: kullanılmayan anahtarı fark etmek için, her istekte yük olmamalı.</summary>
    Task TouchLastUsedAsync(GatewayApiKey key, CancellationToken ct = default);
}

internal sealed class GatewayAccessService : IGatewayAccess
{
    private readonly IGatewayData _db;
    private readonly ITenantProjectAuthority _authority;

    public GatewayAccessService(IGatewayData db, ITenantProjectAuthority authority)
    {
        _db = db;
        _authority = authority;
    }

    /// <summary>
    /// Önek üzerinden aday bulunur ama karar HER ZAMAN tam özet karşılaştırmasıyla verilir; önek gizli
    /// değildir. Karşılaştırma sabit zamanlı: dize eşitliği ilk farklı baytta dönerek bilgi sızdırır.
    /// </summary>
    public async Task<GatewayApiKey?> AuthenticateAsync(string? rawKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawKey) || rawKey.Length <= GatewayKeys.PrefixLength) return null;
        if (!rawKey.StartsWith(GatewayKeys.KeyPrefix, StringComparison.Ordinal)) return null;

        var prefix = rawKey[..GatewayKeys.PrefixLength];
        var expectedHash = GatewayKeys.Hash(rawKey);

        // Bölünmüş sorgu: iki koleksiyonun çarpımı, üst sınırdan önceki büyük eski listelerde satır
        // patlaması yapıyordu (BL-C3). Tek aday için üç indeksli okuma.
        var candidates = await _db.GatewayApiKeys
            .AsNoTracking()
            .Include(k => k.OriginRules)
            .Include(k => k.IpRules)
            .AsSplitQuery()
            .Where(k => k.Prefix == prefix)
            .ToListAsync(ct);

        var match = candidates.FirstOrDefault(k => FixedTimeEquals(k.KeyHash, expectedHash));
        if (match is null) return null;

        if (match.RevokedAt is not null) return null;
        if (match.ExpiresAt is not null && match.ExpiresAt <= DateTime.UtcNow) return null;

        // Üreticinin hesabı silinmişse (FK SET NULL) anahtar sahipsiz: kimseye faturalanamaz, kullanılamaz.
        if (match.CreatedByUserId is null) return null;

        // Üretenin projeye erişimi hâlâ var mı: üyeliği kaldırılan birinin anahtarı arka kapı kalmasın.
        if (!await _authority.CanViewAsync(match.ProjectId, match.CreatedByUserId, ct)) return null;

        // Ham SQL / dışa aktarım anahtarları üretenin hâlâ Owner olmasını ister: rolü düşen birinin
        // anahtarı yetki yükseltmenin arka kapısı olarak çalışmaya devam ederdi.
        if ((match.CanExecuteSql || match.CanExport)
            && await _authority.GetRoleAsync(match.ProjectId, match.CreatedByUserId, ct) != OrgRole.Owner)
            return null;

        return match;
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var left = Encoding.UTF8.GetBytes(a);
        var right = Encoding.UTF8.GetBytes(b);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    public async Task<bool> IsTableAllowedAsync(GatewayApiKey key, string tableName, bool forWrite, CancellationToken ct = default)
    {
        if (forWrite && !key.CanWrite) return false;

        var permission = await _db.GatewayTablePermissions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProjectId == key.ProjectId && p.TableName == tableName, ct);

        if (permission is null) return false;
        return forWrite ? permission.CanWrite : permission.CanRead;
    }

    public async Task<bool> IsTableExportAllowedAsync(GatewayApiKey key, string tableName, CancellationToken ct = default)
    {
        if (!key.CanExport) return false;

        var permission = await _db.GatewayTablePermissions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProjectId == key.ProjectId && p.TableName == tableName, ct);

        return permission is not null && permission.CanRead && permission.CanExport;
    }

    public async Task<IReadOnlyList<string>> MaskedColumnsAsync(string projectId, string tableName, CancellationToken ct = default) =>
        await _db.GatewayTablePermissions.AsNoTracking()
            .Where(p => p.ProjectId == projectId && p.TableName == tableName)
            .SelectMany(p => p.MaskedColumnRules)
            .OrderBy(m => m.Position)
            .Select(m => m.ColumnName)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<GatewayTablePermission>> ReadableTablesAsync(string projectId, CancellationToken ct = default) =>
        await _db.GatewayTablePermissions.AsNoTracking()
            .Where(p => p.ProjectId == projectId && p.CanRead)
            .OrderBy(p => p.TableName)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<GatewayTablePermission>> ListPermissionsAsync(string projectId, CancellationToken ct = default) =>
        await _db.GatewayTablePermissions.AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .ToListAsync(ct);

    public async Task TouchLastUsedAsync(GatewayApiKey key, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (key.LastUsedAt is not null && now - key.LastUsedAt <= TimeSpan.FromMinutes(1)) return;
        key.LastUsedAt = now;
        await _db.GatewayApiKeys.Where(k => k.Id == key.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now), ct);
    }
}
