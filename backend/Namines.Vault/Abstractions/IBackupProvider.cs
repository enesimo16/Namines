using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Namines.Vault.Abstractions;

/// <summary>
/// Bir yedeğin hedefi ve kimliği.
/// </summary>
/// <param name="ConnectionString">
/// ÇÖZÜLMÜŞ bağlantı dizesi. Yalnızca bu isteğin ömrü boyunca bellekte durur;
/// hiçbir yere loglanmaz, hiçbir yere yazılmaz.
/// </param>
/// <param name="DatabaseName">Kullanıcıya gösterilecek ad — yalnızca bilgi amaçlı.</param>
public sealed record BackupSpec(string ConnectionString, string DatabaseName);

/// <param name="ConnectionString">Geri yüklenecek HEDEF veritabanı.</param>
/// <param name="Tables">
/// KISMİ geri yükleme (B-43): yalnızca bu tabloları geri yükle, gerisine
/// dokunma. Boş/null ise TAM geri yükleme (eski davranış, varsayılan).
///
/// <b>Yalnızca özel biçim (custom-format) dump'larda desteklenir</b> —
/// `pg_restore -t` bunu gerektiriyor; düz SQL dökümünde (ör. MySQL'in
/// `mysqldump` çıktısı) seçici geri yükleme yapmanın tek yolu dump metnini
/// tablo tablo ayrıştırmaktır, ki bu ayrı ve daha büyük bir iştir. `v1`'de
/// tek sağlayıcı PostgreSQL olduğu için bu sınır şimdilik görünmüyor.
/// </param>
public sealed record RestoreSpec(string ConnectionString, IReadOnlyList<string>? Tables = null);

/// <summary>
/// Bir motorun yedek/geri yükleme yeteneği.
///
/// <b>Motor başına bir uygulama.</b> v1'de yalnızca PostgreSQL var ve bu
/// bilinçli: <c>namines-vault/00-GENEL-BAKIS.md</c> §7'nin kuralı —
/// yazılıp canlı doğrulanmayan bir motor "destekleniyor" sayılmaz.
/// </summary>
public interface IBackupProvider
{
    /// <summary>Bu sağlayıcının hangi motoru konuştuğu ("PostgreSQL").</summary>
    string Engine { get; }

    /// <summary>
    /// Yedeği üretir ve <paramref name="destination"/>'a AKITIR.
    ///
    /// <b>Akış, bellekte tampon DEĞİL:</b> birkaç GB'lık bir dump'ı belleğe
    /// almak, tek bir yedeğin sunucuyu düşürmesi demekti.
    /// </summary>
    Task BackupAsync(BackupSpec spec, Stream destination, CancellationToken ct);

    /// <summary>
    /// <paramref name="source"/>'taki dump'ı hedefe uygular.
    ///
    /// <b>Bu işlem hedefin mevcut nesnelerini SİLER</b> (pg_restore --clean).
    /// Çağıranın onay ve ön yedek sorumluluğu vardır; bu katman onu doğrulamaz.
    /// </summary>
    Task RestoreAsync(RestoreSpec spec, Stream source, CancellationToken ct);

    /// <summary>
    /// Dump'ın gerçekten geri yüklenebildiğini kanıtlar.
    ///
    /// <b>Kullanıcının veritabanına DOKUNMAZ.</b> Geri yükleme, o iş için
    /// ayağa kaldırılan boş ve geçici bir sunucuya yapılır; iş bitince sunucu
    /// da silinir.
    ///
    /// <b>Neden dosyayı okumak yetmiyor:</b> bir dump'ın başlığı geçerli olup
    /// içeriği bozuk olabilir. Tek gerçek kanıt, onu baştan sona uygulamaktır —
    /// yani felaket anında yapılacak işi, felaketten önce yapmak.
    /// </summary>
    /// <returns>Hata metni; <c>null</c> ise doğrulama başarılı.</returns>
    Task<string?> VerifyAsync(Stream source, CancellationToken ct);

    /// <summary>
    /// Sağlayıcının çalışabilecek durumda olup olmadığını söyler.
    ///
    /// <b>Neden var:</b> Vault'un çalışması Docker daemon'una erişime bağlı ve
    /// bu erişim dağıtıma göre değişiyor (bkz. <c>02-ERISIM-VE-DEPLOY.md</c>).
    /// Bu olmadan, eksikliğin anlaşıldığı ilk an kullanıcının ilk yedek
    /// denemesi olurdu — yani en kötü an. Sağlık ucu bunu deploy'dan hemen
    /// sonra tek bir istekle söylüyor.
    /// </summary>
    /// <returns>Engelin açıklaması; <c>null</c> ise hazır.</returns>
    Task<string?> ProbeAsync(CancellationToken ct);
}
