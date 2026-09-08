using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Namines.Vault.Abstractions;

namespace Namines.Vault.Storage;

/// <summary>
/// Yedekleri sunucunun diskinde tutan depo.
///
/// <b>Bu, v1'in bilinçli ve GEÇİCİ seçimi.</b> Plan (<c>00-GENEL-BAKIS.md</c>
/// §4.2) yedeklerin nesne depoda (S3/MinIO) durmasını söylüyor ve bu doğru
/// hedef; ama sağlayıcı seçimi hâlâ açık bir karar (§9 madde 1) ve o karar
/// beklenirken yürüyen iskeleti bloklamak yanlış olurdu.
///
/// <b>Sınırları açıkça:</b> API sunucusunun diski yedek boyutuyla doluyor,
/// birden fazla instance aynı dosyayı göremiyor ve sunucu yeniden kurulursa
/// yedekler gider. Üretimde S3 uygulaması gelmeden kullanılmamalı — bu yüzden
/// <see cref="Description"/> kullanıcıya nerede durduğunu AÇIKÇA söylüyor.
///
/// Yol geçişi (<c>../</c>) engelleniyor: anahtar sunucuda üretiliyor ama tek
/// bir gelecekteki hata, bu sınıfı dosya sistemine keyfi yazma aracına çevirirdi.
/// </summary>
public sealed class FileSystemBackupStore : IBackupStore
{
    private readonly string _root;

    public FileSystemBackupStore(IConfiguration configuration)
    {
        _root = Path.GetFullPath(
            configuration["Vault:StoragePath"]
            ?? Path.Combine(AppContext.BaseDirectory, "vault-backups"));
        Directory.CreateDirectory(_root);
    }

    public string Description => $"Local disk ({_root})";

    public async Task<long> PutAsync(string key, Stream content, CancellationToken ct)
    {
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Önce geçici bir dosyaya, sonra yerine taşı: yazma ortasında kesilen
        // bir işlem, geçerli görünen YARIM bir yedek bırakmasın.
        var temp = path + ".partial";
        try
        {
            long written;
            await using (var file = File.Create(temp))
            {
                await content.CopyToAsync(file, ct);
                written = file.Length;
            }
            File.Move(temp, path, overwrite: true);
            return written;
        }
        catch
        {
            if (File.Exists(temp)) File.Delete(temp);
            throw;
        }
    }

    public Task<Stream?> OpenAsync(string key, CancellationToken ct)
    {
        var path = Resolve(key);
        Stream? stream = File.Exists(path) ? File.OpenRead(path) : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = Resolve(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key is required.", nameof(key));

        var full = Path.GetFullPath(Path.Combine(_root, key));
        if (!full.StartsWith(_root, StringComparison.Ordinal))
            throw new ArgumentException("Key escapes the storage root.", nameof(key));

        return full;
    }
}
