using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Models.Auth;

namespace Namines.BuildingBlocks.Application;

/// <summary>Keşif filtreleri; hepsi tek SQL'de uygulanır (ad ve motor Hub'daki okuma modelinden).</summary>
public sealed record ExploreCriteria(string? Category, string? Tag, string? Query, string? DbType, bool RecentFirst);

/// <summary>PATCH ayarları. null = dokunma; boş dizge alanın anlamına göre (temizle) işlenir.</summary>
public sealed record RepositorySettingsChange(
    string? Slug = null,
    string? Description = null,
    ProjectVisibility? Visibility = null,
    string? Category = null,
    string? Tags = null,
    string? LinkedGithubRepo = null);

/// <summary>
/// Hub modülünün dış sözleşmesi: <c>hub.repositories</c>, <c>hub.project_tags</c> ve yıldızlar.
/// Yetki denetimi ÇAĞIRANIN işi. Yazma yöntemleri <c>ICrossModuleTransaction</c> içinde çağrılınca
/// proje yazımıyla aynı işleme katılır; izleyiciyi kendisi temizler ki yeniden denemede eski
/// durumla çalışmasın.
/// </summary>
public interface IHubApplicationService
{
    Task<ProjectRepository?> GetRepositoryAsync(
        string projectId,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, ProjectRepository>> GetRepositoriesAsync(
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct = default);

    /// <summary>Satır yoksa null: çağıran bunu güvenli tarafa (özel) yorumlar.</summary>
    Task<ProjectVisibility?> GetVisibilityAsync(
        string projectId,
        CancellationToken ct = default);

    /// <summary>API'nin tarihsel "a,b,c" biçimi, ilk yazım ve sıra korunmuş; etiket yoksa null.</summary>
    Task<string?> GetTagsCsvAsync(
        string projectId,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, string?>> GetTagsCsvAsync(
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct = default);

    /// <summary>(sahip, slug veya proje kimliği) → proje kimliği. Başka sahibin reposuna düşmez.</summary>
    Task<string?> ResolveProjectIdAsync(
        string ownerUserId,
        string slugOrId,
        CancellationToken ct = default);

    Task<bool> IsSlugTakenByOwnerAsync(
        string ownerUserId,
        string slug,
        string? excludeProjectId,
        CancellationToken ct = default);

    /// <summary>Ayarlar formundaki tarihsel kontrol: slug tüm sahipler arasında aranır.</summary>
    Task<bool> IsSlugTakenAsync(
        string slug,
        string excludeProjectId,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetSlugsByOwnerAsync(
        string ownerUserId,
        CancellationToken ct = default);

    /// <summary>Yeni repo satırı ve etiketleri ekler (fork/tohum/oluşturma).</summary>
    Task CreateRepositoryAsync(
        ProjectRepository repository,
        string? tagsCsv,
        CancellationToken ct = default);

    /// <summary>
    /// Satır yoksa varsayılanlarla açar; ad/motor kopyasını günceller; boş slug'ı verilen değerle
    /// doldurur, dolu olana dokunmaz.
    /// </summary>
    Task<ProjectRepository> EnsureRepositoryAsync(
        string projectId,
        string ownerUserId,
        string projectName,
        string projectDbType,
        string? slugIfMissing,
        CancellationToken ct = default);

    Task<ProjectRepository> ApplySettingsAsync(
        string projectId,
        string ownerUserId,
        string projectName,
        string projectDbType,
        RepositorySettingsChange change,
        CancellationToken ct = default);

    /// <summary>Yıldız satırı ve sayaç tek işlemde; eşzamanlı çift tıklama sayacı şişirmez.</summary>
    Task<(int starCount, bool starred)> ToggleStarAsync(
        string projectId,
        string userId,
        CancellationToken ct = default);

    Task<bool> IsStarredByUserAsync(
        string projectId,
        string userId,
        CancellationToken ct = default);

    /// <summary>Public + açıklamalı repolar, istenen sırada: tek sorgu, en fazla <paramref name="take"/> satır.</summary>
    Task<IReadOnlyList<ProjectRepository>> GetExploreCandidatesAsync(
        ExploreCriteria criteria,
        int take,
        CancellationToken ct = default);

    Task IncrementForkCountAsync(
        string sourceProjectId,
        CancellationToken ct = default);

    /// <summary>
    /// Hesap silmede kurucu devri. Yeni sahipte aynı slug varsa <c>slug-2</c>, <c>slug-3</c>... seçilir;
    /// slug değiştiyse yenisini döndürür (URL değiştiği için çağıran loglar), değilse null.
    /// </summary>
    Task<string?> ReassignOwnerAsync(
        string projectId,
        string newOwnerUserId,
        CancellationToken ct = default);

    /// <summary>Proje silinmeden hemen önce: fork ise kaynağın sayacını düşürür (satırlar cascade).</summary>
    Task DetachFromForkSourceAsync(
        string projectId,
        CancellationToken ct = default);

    /// <summary>Hesap silmede: kullanıcının yıldızlarını kaldırır, ilgili sayaçları düşürür.</summary>
    Task RemoveStarsByUserAsync(
        string userId,
        CancellationToken ct = default);
}
