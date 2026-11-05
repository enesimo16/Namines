using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Namines.Core.Security;
using Namines.Studio.Infrastructure.Persistence;

namespace Namines.Studio.Application;

/// <summary>
/// Saklanan bağlantı dizelerini GÜNCEL şifreleme anahtarıyla yeniden yazar (DB-36).
/// Yeni anahtarı <c>Security:ConnectionEncryptionKeys</c> listesinin sonuna ekleyip
/// uygulamayı dağıttıktan sonra bir kez çalıştırılır; bittiğinde eski anahtar
/// listeden çıkarılabilir. Şema değişikliği gerektirmez, tekrar çalıştırmak güvenlidir
/// (zaten güncel olan kayıtlar atlanır). <c>ExecuteUpdate</c> kullanır: izleyici durumu ile uğraşmaz.
/// </summary>
public interface IConnectionSecretRotation
{
    Task<ConnectionSecretReencryptor.Result> ReencryptAllAsync(IRotatableSecretProtector protector, ILogger? logger = null, CancellationToken ct = default);
}

internal sealed class ConnectionSecretRotation : IConnectionSecretRotation
{
    private readonly StudioDbContext _db;

    public ConnectionSecretRotation(StudioDbContext db) => _db = db;

    public Task<ConnectionSecretReencryptor.Result> ReencryptAllAsync(IRotatableSecretProtector protector, ILogger? logger = null, CancellationToken ct = default) =>
        ConnectionSecretReencryptor.ReencryptAsync(_db, protector, logger, ct);
}

public static class ConnectionSecretReencryptor
{
    public sealed record Result(int Scanned, int Reencrypted, int Failed);

    internal static async Task<Result> ReencryptAsync(
        StudioDbContext db, IRotatableSecretProtector protector, ILogger? logger = null, CancellationToken ct = default)
    {
        var rows = await db.ProjectConnections.AsNoTracking()
            .Where(c => c.EncryptedConnectionString != null && c.EncryptedConnectionString != "")
            .Select(c => new { c.ProjectId, c.EncryptedConnectionString })
            .ToListAsync(ct);

        int reencrypted = 0, failed = 0;
        foreach (var row in rows)
        {
            var current = row.EncryptedConnectionString!;
            if (protector.IsEncryptedWithCurrentKey(current)) continue;
            try
            {
                var fresh = protector.Protect(protector.Unprotect(current, associatedData: row.ProjectId), associatedData: row.ProjectId);
                // Koşullu güncelleme: arada başka biri değeri değiştirdiyse ezmez.
                var n = await db.ProjectConnections
                    .Where(c => c.ProjectId == row.ProjectId && c.EncryptedConnectionString == current)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.EncryptedConnectionString, fresh)
                        .SetProperty(c => c.UpdatedAt, DateTime.UtcNow), ct);
                reencrypted += n;
            }
            catch (Exception ex)
            {
                failed++;
                logger?.LogError(ex, "Baglanti dizesi yeniden sifrelenemedi (proje {ProjectId}); anahtar listede mi?", row.ProjectId);
            }
        }
        logger?.LogInformation("Yeniden sifreleme: {Scanned} tarandi, {Done} guncellendi, {Failed} basarisiz.",
            rows.Count, reencrypted, failed);
        return new Result(rows.Count, reencrypted, failed);
    }
}
