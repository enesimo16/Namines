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
public sealed record RestoreSpec(string ConnectionString);

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
}
