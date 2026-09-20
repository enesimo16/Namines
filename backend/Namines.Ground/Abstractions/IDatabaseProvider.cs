using System.Threading;
using System.Threading.Tasks;

namespace Namines.Ground.Abstractions;

/// <summary>
/// Bir veritabanı açma isteği.
/// </summary>
/// <param name="ProjectId">
/// Namines projesinin kimliği. Sağlayıcı tarafındaki adlandırmaya girer —
/// bir kaynağa panelden bakan kişi onun HANGİ projeye ait olduğunu görebilmeli.
/// </param>
/// <param name="ProjectName">İnsan tarafından okunacak ad; yalnızca etiket.</param>
/// <param name="Region">
/// Sağlayıcının bölge kodu. v1'de arayüzden seçilmiyor (yapılandırmadan gelen
/// varsayılan), ama sözleşmede baştan var: sonradan eklemek her uygulamayı
/// ve her çağrı yerini değiştirmek demek olurdu.
/// </param>
public sealed record ProvisionSpec(string ProjectId, string ProjectName, string? Region = null);

/// <summary>
/// Açılmış bir veritabanı.
/// </summary>
/// <param name="ProviderProjectId">Sağlayıcı tarafındaki üst kaynak kimliği.</param>
/// <param name="ProviderBranchId">
/// Varsa dal/alt kaynak kimliği. Yerel sağlayıcıda karşılığı yok — null.
/// </param>
/// <param name="Region">Kaynağın gerçekte açıldığı bölge.</param>
/// <param name="ConnectionString">
/// DÜZ METİN bağlantı dizesi. <b>Yalnızca bu çağrının dönüş değerinde yaşar;</b>
/// çağıran onu şifreleyip saklamak zorunda, loglamak ya da istemciye göndermek
/// hiçbir koşulda kabul edilemez.
/// </param>
public sealed record ProvisionedDatabase(
    string ProviderProjectId,
    string? ProviderBranchId,
    string Region,
    string ConnectionString);

/// <summary>
/// Açılmış bir veritabanının ölçümleri.
///
/// <b>Sayılar sağlayıcıdan OKUNUR, tahmin edilmez</b>
/// (<c>00-GENEL-BAKIS.md</c> §9 madde 4). Sağlayıcı bir değeri vermiyorsa
/// alan <c>null</c> kalır — sıfır yazmak "ölçüldü ve sıfır çıktı" demek olurdu.
/// </summary>
public sealed record DatabaseMetrics(long? StorageBytes, int? ActiveConnections);

/// <summary>
/// Sağlayıcının neyi yapıp neyi yapamadığı.
/// </summary>
/// <param name="SupportsBranching">Copy-on-write dal açabiliyor mu (v2).</param>
/// <param name="SupportsRegionChoice">Bölge seçtirilebiliyor mu.</param>
/// <param name="IsLiveVerified">
/// Bu sağlayıcı gerçek bir kaynağa karşı CANLI denendi mi.
///
/// <b>Arayüzde gösterilir</b> ve bu bilinçli: denenmemiş bir sağlayıcıyı
/// denenmiş gibi sunmak, kullanıcının verisini kanıtlanmamış bir yola
/// koymasına sessizce izin vermek olurdu (<c>02-V1-KARARLARI.md</c> §4).
/// </param>
/// <param name="ResponsibilityNote">
/// Kullanıcıya gösterilecek sorumluluk notu — "bu kaynağı kim işletiyor".
/// </param>
/// <param name="SupportsBranching">Copy-on-write dal açabiliyor mu (v2).</param>
/// <param name="SupportsRegionChoice">Bölge seçtirilebiliyor mu.</param>
/// <param name="IsLiveVerified">Bu sağlayıcı gerçek bir kaynağa karşı CANLI denendi mi.</param>
/// <param name="ResponsibilityNote">Kullanıcıya gösterilecek sorumluluk notu.</param>
/// <param name="CreateIsIdempotentByProjectId">
/// <see cref="IDatabaseProvider.CreateAsync"/>, AYNI <see cref="ProvisionSpec.ProjectId"/>
/// ile ikinci kez çağrıldığında YENİ bir üst kaynak AÇMADAN var olanı bulup
/// kimlik bilgisini tazeliyor mu.
///
/// <b>Neden gerekli — gerçek bir olaydan çıktı:</b> <c>GroundService.
/// CancelDeleteAsync</c> bekleme penceresi içindeki bir silmeyi geri alırken
/// bağlantıyı yeniden kurmak için <c>CreateAsync</c>'i TEKRAR çağırıyordu ve
/// yorumu "CreateAsync idempotan" diyordu. Bu yalnızca <see cref="LocalPostgresProvider"/>
/// için doğru (veritabanı/rol adları <c>ProjectId</c>'den DETERMİNİSTİK
/// türetiliyor, "IF NOT EXISTS" ile ensure ediliyor). Neon/Supabase sağlayıcıları
/// her çağrıda KOŞULSUZ yeni bir uzak proje açıyor: iptal, KULLANICININ
/// ORİJİNAL VERİSİNİ erişilemez bırakıp yerine boş, ikinci ve sonsuza dek
/// faturalanan bir kaynak koyuyordu — hem veri kaybı hem gizli fatura.
///
/// Bu bayrak <c>false</c> olan bir sağlayıcı için <c>CancelDeleteAsync</c>
/// artık ikinci bir kaynak AÇMIYOR; iptalin bu sağlayıcıda desteklenmediğini
/// açıkça söylüyor. Yanlış ama "çalışıyormuş gibi görünen" bir kurtarmadan,
/// dürüst bir "yapılamıyor" mesajı her zaman daha güvenli.
/// </param>
public sealed record ProviderCapabilities(
    bool SupportsBranching,
    bool SupportsRegionChoice,
    bool IsLiveVerified,
    string ResponsibilityNote,
    bool CreateIsIdempotentByProjectId = false);

/// <summary>
/// Yönetilen bir veritabanı açan/silen sağlayıcı.
///
/// <b>Neden arayüz:</b> v1'de iki uygulama var (yerel PostgreSQL ve Neon) ve
/// bir soyutlamanın doğru çizildiğinin tek gerçek sınavı İKİNCİ uygulamadır —
/// tek uygulamalı bir arayüz yalnızca bir sınıfın uzun adıdır.
/// </summary>
public interface IDatabaseProvider
{
    /// <summary>Sağlayıcının adı ("LocalPostgres", "Neon") — kayıtta saklanır.</summary>
    string Name { get; }

    ProviderCapabilities Capabilities { get; }

    /// <summary>
    /// Yeni bir veritabanı açar.
    ///
    /// <b>Yarım kalmış kaynak bırakmamalı:</b> uygulama, açtıktan sonra bir
    /// adımda hata alırsa açtığını temizlemekle yükümlü. Aksi hâlde kimsenin
    /// bilmediği ama faturalanan bir kaynak kalır.
    /// </summary>
    Task<ProvisionedDatabase> CreateAsync(ProvisionSpec spec, CancellationToken ct);

    /// <summary>
    /// Kaynağı KALICI olarak siler.
    ///
    /// <b>Zaten yoksa hata vermez</b> (idempotent): silme, arka planda ve
    /// yeniden denenebilir bir işten çağrılıyor; "yok" cevabı başarısızlık
    /// değil, istenen son durumdur.
    /// </summary>
    Task DeleteAsync(ProvisionedDatabase database, CancellationToken ct);

    /// <summary>Ölçümler. Sağlayıcı desteklemiyorsa alanlar null döner.</summary>
    Task<DatabaseMetrics> GetMetricsAsync(ProvisionedDatabase database, CancellationToken ct);

    /// <summary>
    /// Sağlayıcının şu an çalışabilir durumda olup olmadığı; engel varsa açıklaması.
    ///
    /// Vault'un <c>ProbeAsync</c>'iyle aynı gerekçe: eksik bir yapılandırmanın
    /// anlaşıldığı ilk an, kullanıcının ilk provizyon denemesi olmamalı.
    /// </summary>
    Task<string?> ProbeAsync(CancellationToken ct);
}
