using System.Threading;
using System.Threading.Tasks;

namespace Namines.Vault.Abstractions;

/// <summary>
/// Yedek dosyalarının durduğu yer.
///
/// <b>Neden bir arayüz:</b> dump içeriği control-plane veritabanına ASLA
/// girmez (<c>namines-vault/00-GENEL-BAKIS.md</c> §4.2) ve nesne depo
/// sağlayıcısı hâlâ açık bir karar (§9 madde 1). Arayüz, o karar verilene
/// kadar geliştirmeyi bloklamıyor: v1 dosya sistemine yazıyor, S3/MinIO
/// aynı arayüzün ikinci uygulaması olarak gelecek.
/// </summary>
public interface IBackupStore
{
    /// <summary>Adı — kullanıcıya "yedekler nerede duruyor" sorusunu cevaplamak için.</summary>
    string Description { get; }

    /// <summary>İçeriği yazar ve yazılan bayt sayısını döndürür.</summary>
    Task<long> PutAsync(string key, Stream content, CancellationToken ct);

    /// <summary>Okuma akışı. Anahtar yoksa <c>null</c>.</summary>
    Task<Stream?> OpenAsync(string key, CancellationToken ct);

    /// <summary>Siler. Zaten yoksa sessizce başarılı sayılır (idempotent).</summary>
    Task DeleteAsync(string key, CancellationToken ct);
}
