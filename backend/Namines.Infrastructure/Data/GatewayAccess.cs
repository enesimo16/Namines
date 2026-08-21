using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;

namespace Namines.Infrastructure.Data;

/// <summary>
/// Gateway API anahtarı üretimi, doğrulaması ve tablo izinleri
/// (new-phase/08-GATEWAY-API.md §4.3).
///
/// <see cref="OrgAccess"/> ile aynı gerekçe: bu mantığın TEK kopyası olmalı.
/// Anahtar doğrulaması birden fazla yerde yeniden yazılırsa, biri sona erme
/// kontrolünü unutur ve iptal edilmiş bir anahtar sessizce çalışmaya devam eder.
/// </summary>
public static class GatewayAccess
{
    /// <summary>Anahtarların insan tarafından tanınabilmesi için sabit önek.</summary>
    public const string KeyPrefix = "nmn_";

    private const int PrefixLength = 12;

    /// <summary>
    /// Yeni anahtar üretir. Ham anahtar YALNIZCA buradan döner ve bir daha elde
    /// edilemez — kayıtta sadece özeti tutulur.
    /// </summary>
    public static (GatewayApiKey Entity, string RawKey) CreateKey(
        string projectId, string name, string createdByUserId, bool canWrite, DateTime? expiresAt)
    {
        // 32 bayt = 256 bit entropi. Tahmin edilemezlik anahtarın tek savunması.
        var raw = KeyPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "").Replace("/", "").Replace("=", "");

        var entity = new GatewayApiKey
        {
            ProjectId = projectId,
            Name = name,
            Prefix = raw[..PrefixLength],
            KeyHash = Hash(raw),
            CanWrite = canWrite,
            CreatedByUserId = createdByUserId,
            ExpiresAt = expiresAt,
        };

        return (entity, raw);
    }

    public static string Hash(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Ham anahtarı doğrular. Geçerliyse kaydı, değilse null döner.
    ///
    /// Önek üzerinden aday bulunur ama karar HER ZAMAN tam özet karşılaştırmasıyla
    /// verilir; önek gizli değildir ve tek başına hiçbir şey kanıtlamaz.
    /// Karşılaştırma sabit zamanlıdır: normal string eşitliği ilk farklı baytta
    /// dönerek özet hakkında zamanlama bilgisi sızdırır.
    /// </summary>
    public static async Task<GatewayApiKey?> AuthenticateAsync(
        this AuthDbContext context, string? rawKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawKey) || rawKey.Length <= PrefixLength) return null;
        if (!rawKey.StartsWith(KeyPrefix, StringComparison.Ordinal)) return null;

        var prefix = rawKey[..PrefixLength];
        var expectedHash = Hash(rawKey);

        var candidates = await context.GatewayApiKeys
            .Where(k => k.Prefix == prefix)
            .ToListAsync(ct);

        var match = candidates.FirstOrDefault(k => FixedTimeEquals(k.KeyHash, expectedHash));
        if (match is null) return null;

        if (match.RevokedAt is not null) return null;
        if (match.ExpiresAt is not null && match.ExpiresAt <= DateTime.UtcNow) return null;

        return match;
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var left = Encoding.UTF8.GetBytes(a);
        var right = Encoding.UTF8.GetBytes(b);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    /// <summary>
    /// Bu anahtar bu tabloya bu işlemi yapabilir mi?
    ///
    /// İzin kaydı YOKSA cevap HAYIR (08 §1). Varsayılanı "açık" yapmak, projeye
    /// sonradan eklenen bir tabloyu kimse istemeden internete açardı.
    /// </summary>
    public static async Task<bool> IsTableAllowedAsync(
        this AuthDbContext context, GatewayApiKey key, string tableName, bool forWrite,
        CancellationToken ct = default)
    {
        if (forWrite && !key.CanWrite) return false;

        var permission = await context.GatewayTablePermissions
            .FirstOrDefaultAsync(p => p.ProjectId == key.ProjectId && p.TableName == tableName, ct);

        if (permission is null) return false;
        return forWrite ? permission.CanWrite : permission.CanRead;
    }

    /// <summary>Anahtarın erişebildiği tablolar — <c>/tables</c> ucunun kaynağı.</summary>
    public static async Task<List<GatewayTablePermission>> ReadableTablesAsync(
        this AuthDbContext context, string projectId, CancellationToken ct = default) =>
        await context.GatewayTablePermissions
            .Where(p => p.ProjectId == projectId && p.CanRead)
            .OrderBy(p => p.TableName)
            .ToListAsync(ct);
}
