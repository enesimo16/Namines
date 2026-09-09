using System.Threading;
using System.Threading.Tasks;

namespace Namines.Ground.Neon;

/// <summary>
/// Neon'da açılmış bir proje.
/// </summary>
/// <param name="ProjectId">Neon'un proje kimliği.</param>
/// <param name="BranchId">Varsayılan dalın kimliği.</param>
/// <param name="RegionId">Kaynağın gerçekte açıldığı bölge.</param>
/// <param name="ConnectionUri">
/// Kullanıma hazır bağlantı adresi. <b>Parola içerir</b> — loglanmaz, istemciye
/// gönderilmez, yalnızca şifrelenip saklanır.
/// </param>
public sealed record NeonProject(string ProjectId, string BranchId, string RegionId, string ConnectionUri);

/// <summary>Neon'un bir proje için bildirdiği kullanım.</summary>
public sealed record NeonUsage(long? StorageBytes);

/// <summary>
/// Neon HTTP API'sinin ince sarmalayıcısı — <b>yalnızca ham HTTP</b>.
///
/// <b>Neden <c>IDatabaseProvider</c>'dan ayrı bir arayüz</b>
/// (<c>01-INSA-PLANI.md</c> §G0): iş mantığı (adlandırma, hata çevirme,
/// yarım kalan kaynağı temizleme) ile ağ çağrısı ayrılmazsa, o mantığı test
/// etmek için gerçek Neon'a çağrı yapmak gerekirdi — yani <b>her test
/// çalıştırmasında para harcamak ve gerçek kaynak açmak</b>. Ayrılınca
/// <c>NeonProvider</c> sahte bir istemciyle tamamen test edilebiliyor.
/// </summary>
public interface INeonClient
{
    /// <summary>API anahtarı yapılandırılmış mı — ağa çıkmadan bakılabilir.</summary>
    bool IsConfigured { get; }

    /// <summary>Yeni bir Neon projesi açar.</summary>
    Task<NeonProject> CreateProjectAsync(string name, string? regionId, CancellationToken ct);

    /// <summary>
    /// Projeyi siler. <b>Zaten yoksa hata vermez</b> (idempotent): silme arka
    /// planda ve yeniden denenebilir bir işten çağrılıyor, "yok" cevabı
    /// başarısızlık değil istenen son durumdur.
    /// </summary>
    Task DeleteProjectAsync(string projectId, CancellationToken ct);

    /// <summary>Kullanım bilgisi. Neon vermiyorsa alanlar null.</summary>
    Task<NeonUsage> GetUsageAsync(string projectId, CancellationToken ct);

    /// <summary>Erişilebilirlik kontrolü; engel varsa açıklaması, yoksa null.</summary>
    Task<string?> ProbeAsync(CancellationToken ct);
}
