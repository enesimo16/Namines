using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Namines.Core.Models.Auth;

/// <summary>
/// <c>studio.project_connections</c> (projeyle 1:0..1). Satır, üç alandan biri anlamlıysa vardır:
/// bağlantı kopsa da <see cref="AllowDeskSql"/> onayı korunur, bu yüzden şifreli metin nullable.
/// Anahtar sürümü ayrı kolon değil, şifreli metnin kendi önekinde durur (v1 / kid formatı).
/// </summary>
public sealed class ProjectConnection
{
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// AES-256-GCM şifreli metin (AAD = proje kimliği). Düz metin buraya asla yazılmaz.
    /// Tek çözme kapısı <c>IProjectConnectionSecretProvider</c>.
    /// </summary>
    [JsonIgnore]
    public string? EncryptedConnectionString { get; set; }

    /// <summary>
    /// Gerçekten BAĞLANILAN motor; <see cref="CloudProject.DbType"/>'tan ayrı çünkü o, şemanın
    /// derlendiği hedef motor ve ikisi farklı olabilir.
    /// </summary>
    public string? ConnectionDbType { get; set; }

    /// <summary>
    /// Namines Desk v2 §E4.2: SQL konsolu opt-in'i. Varsayılan kapalı, yalnızca Owner değiştirir.
    /// </summary>
    public bool AllowDeskSql { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// <c>studio.project_shares</c>: anonim salt-okunur paylaşım bağlantısı. Düz metin token
/// saklanmaz; ham değer yalnızca oluşturulurken bir kez döner.
///
/// Proje başına BİRDEN ÇOK etkin satır olabilir: eski API ikinci çağrıda aynı linki geri
/// veriyordu, özet saklayınca bu imkânsız. Tek satırda döndürmek daha önce kopyalanmış URL'leri
/// sessizce öldürürdü; o yüzden her çağrı yeni bağlantı üretir ve iptal hepsini kapatır.
/// </summary>
public sealed class ProjectShare
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string ProjectId { get; set; } = string.Empty;

    /// <summary>Ham token'ın küçük harf hex SHA-256 özeti (UNIQUE), bkz. <see cref="ShareTokenHasher"/>.</summary>
    [JsonIgnore]
    public string ShareTokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Dolu ise bağlantı geçersiz; satır "ne zaman kapatıldı" izi için kalır.</summary>
    public DateTime? RevokedAt { get; set; }
}

/// <summary>
/// <c>hub.repositories</c>: her projenin Hub yüzü (1:1). Slug nullable: bugün slug'sız projeler
/// var ve onlara uydurma bir genel URL verilmez.
/// </summary>
public sealed class ProjectRepository
{
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="CloudProject.UserId"/>'nin kopyası. Tek amacı (sahip, slug) benzersizliğini
    /// modüller arası join olmadan Hub tablosunda tutmak; hesap silmede devirle güncellenir.
    /// </summary>
    public string OwnerUserId { get; set; } = string.Empty;

    /// <summary>URL-dostu kısa ad (ör: "multi-tenant-saas").</summary>
    public string? Slug { get; set; }

    public string? Description { get; set; }

    public ProjectVisibility Visibility { get; set; } = ProjectVisibility.Private;

    /// <summary>Hub kategorisi: "FinTech", "SaaS", "E-Commerce", "ERP", "AI", "Healthcare", "General".</summary>
    public string Category { get; set; } = "General";

    public int StarCount { get; set; }
    public int ForkCount { get; set; }

    /// <summary>Fork kaynağı; kaynak silinince fork kalır, işaretçi boşalır (SET NULL).</summary>
    public string? ForkedFromProjectId { get; set; }

    /// <summary>Bağlı GitHub deposu ("owner/repo").</summary>
    public string? LinkedGithubRepo { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// <c>hub.project_tags</c>. <see cref="Tag"/> kanonik anahtar (küçük harf): (ProjectId, Tag)
/// birincil anahtarı büyük/küçük harf yinelemesini veritabanında engeller ve arama tam eşleşme
/// ile indeksten gider. API eskisi gibi kullanıcının yazımını ve sırasını döndürdüğü için
/// <see cref="DisplayTag"/> ve <see cref="Position"/> ayrı tutulur.
/// </summary>
public sealed class ProjectTag
{
    public string ProjectId { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string DisplayTag { get; set; } = string.Empty;
    public int Position { get; set; }
}

/// <summary>
/// Etiket kuralları tek yerde: kırp, boşları at, büyük/küçük harf duyarsız yinelemede ilk yazımı
/// tut. Migration'daki SQL karşılığı bu kurallarla aynı sonucu üretmek zorunda (testle sınanır).
/// </summary>
public static class ProjectTagRules
{
    /// <summary>Kültürden bağımsız: Türkçe kültürde 'I' → 'ı' dönüşümü aramayı bölerdi.</summary>
    public static string Key(string tag) => tag.Trim().ToLowerInvariant();

    public static IReadOnlyList<ProjectTag> Parse(string projectId, string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<ProjectTag>();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ProjectTag>();
        foreach (var part in csv.Split(','))
        {
            var display = part.Trim();
            if (display.Length == 0) continue;
            var key = Key(display);
            if (!seen.Add(key)) continue;
            result.Add(new ProjectTag { ProjectId = projectId, Tag = key, DisplayTag = display, Position = result.Count });
        }
        return result;
    }

    /// <summary>API'nin tarihsel biçimi; etiket yoksa null (hiç etiket girilmemiş proje gibi).</summary>
    public static string? ToCsv(IEnumerable<ProjectTag> tags)
    {
        var ordered = tags.OrderBy(t => t.Position).Select(t => t.DisplayTag).ToList();
        return ordered.Count == 0 ? null : string.Join(",", ordered);
    }
}
