using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Namines.Core.Security;
using Namines.Infrastructure.Data;

namespace Namines.Infrastructure.Security;

/// <summary>
/// Saklanan bağlantı dizelerini GÜNCEL şifreleme anahtarıyla yeniden yazar (DB-36).
/// Yeni anahtarı <c>Security:ConnectionEncryptionKeys</c> listesinin sonuna ekleyip
/// uygulamayı dağıttıktan sonra bir kez çalıştırılır; bittiğinde eski anahtar
/// listeden çıkarılabilir. Şema değişikliği gerektirmez, tekrar çalıştırmak güvenlidir
/// (zaten güncel olan kayıtlar atlanır). <c>ExecuteUpdate</c> kullanır: eşzamanlılık
/// jetonu ve izleyici durumu ile uğraşmaz.
/// </summary>
public static class ConnectionSecretReencryptor
{
    public sealed record Result(int Scanned, int Reencrypted, int Failed);

    public static async Task<Result> ReencryptAsync(
        AuthDbContext db, IRotatableSecretProtector protector, ILogger? logger = null, CancellationToken ct = default)
    {
        var rows = await db.CloudProjects.AsNoTracking()
            .Where(p => p.EncryptedConnectionString != null && p.EncryptedConnectionString != "")
            .Select(p => new { p.Id, p.EncryptedConnectionString })
            .ToListAsync(ct);

        int reencrypted = 0, failed = 0;
        foreach (var row in rows)
        {
            var current = row.EncryptedConnectionString!;
            if (protector.IsEncryptedWithCurrentKey(current)) continue;
            try
            {
                var fresh = protector.Protect(protector.Unprotect(current));
                // Koşullu güncelleme: arada başka biri değeri değiştirdiyse ezmez.
                var n = await db.CloudProjects
                    .Where(p => p.Id == row.Id && p.EncryptedConnectionString == current)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.EncryptedConnectionString, fresh), ct);
                reencrypted += n;
            }
            catch (Exception ex)
            {
                failed++;
                logger?.LogError(ex, "Baglanti dizesi yeniden sifrelenemedi (proje {ProjectId}); anahtar listede mi?", row.Id);
            }
        }
        logger?.LogInformation("Yeniden sifreleme: {Scanned} tarandi, {Done} guncellendi, {Failed} basarisiz.",
            rows.Count, reencrypted, failed);
        return new Result(rows.Count, reencrypted, failed);
    }
}
