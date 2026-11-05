using System;
using System.Collections.Generic;
using System.Linq;

namespace Namines.Hub.Domain;

public enum ProjectVisibility
{
    Private = 0,
    Team = 1,
    Public = 2
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

    /// <summary>
    /// <see cref="CloudProject.Name"/> ve <see cref="CloudProject.DbType"/> kopyaları: Explore'un q ve
    /// motor filtresi tek SQL'de, modüller arası join olmadan çalışsın. Proje yazımıyla aynı işlemde güncellenir.
    /// </summary>
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectDbType { get; set; } = string.Empty;

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
