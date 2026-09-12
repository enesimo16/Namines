using System.Threading;
using System.Threading.Tasks;

namespace Namines.Ground.Supabase;

/// <summary>
/// Supabase'de açılmış bir proje.
/// </summary>
/// <param name="ProjectRef">
/// Supabase'in proje referansı (<c>ref</c>). Bağlantı adresinin içine giriyor:
/// <c>db.&lt;ref&gt;.supabase.co</c>.
/// </param>
/// <param name="Region">Kaynağın gerçekte açıldığı bölge.</param>
/// <param name="ConnectionString">
/// Anahtar=değer biçiminde, kullanıma hazır bağlantı. <b>Parola içerir</b> —
/// loglanmaz, istemciye gönderilmez, yalnızca şifrelenip saklanır.
/// </param>
public sealed record SupabaseProjectInfo(string ProjectRef, string Region, string ConnectionString);

/// <summary>
/// Supabase Management API'sinin ince sarmalayıcısı — <b>yalnızca ham HTTP</b>.
///
/// <b>Neden <see cref="Namines.Ground.Abstractions.IDatabaseProvider"/>'dan
/// ayrı:</b> <c>INeonClient</c> ile aynı gerekçe. İş mantığı (adlandırma,
/// hata çevirme, yarım kalan kaynağı temizleme) ağ çağrısından ayrılmazsa, o
/// mantığı test etmek için gerçek Supabase'e çağrı yapmak gerekirdi — yani
/// her test koşusunda <b>gerçek proje açmak ve para harcamak</b>.
/// </summary>
public interface ISupabaseClient
{
    /// <summary>Erişim jetonu yapılandırılmış mı — ağa çıkmadan bakılabilir.</summary>
    bool IsConfigured { get; }

    /// <summary>Yeni bir Supabase projesi açar.</summary>
    Task<SupabaseProjectInfo> CreateProjectAsync(string name, string? region, CancellationToken ct);

    /// <summary>
    /// Projeyi siler. <b>Zaten yoksa hata vermez</b> (idempotent): silme arka
    /// planda ve yeniden denenebilir bir işten çağrılıyor, "yok" cevabı
    /// başarısızlık değil istenen son durumdur.
    /// </summary>
    Task DeleteProjectAsync(string projectRef, CancellationToken ct);

    /// <summary>Erişilebilirlik kontrolü; engel varsa açıklaması, yoksa null.</summary>
    Task<string?> ProbeAsync(CancellationToken ct);
}
