using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Models.Auth;

namespace Namines.BuildingBlocks.Application;

/// <summary>
/// Hub Bounded Context Uygulama Servis Sözleşmesi (Phase 14.2B).
/// <c>hub.repositories</c> ve <c>hub.project_tags</c> tabloları üzerindeki tüm
/// vitrin, keşif, etiketleme, yıldızlama ve fork işlemlerini yönetir.
/// </summary>
public interface IHubApplicationService
{
    Task<ProjectRepository?> GetRepositoryAsync(
        string projectId,
        CancellationToken ct = default);

    Task<ProjectRepository?> GetRepositoryBySlugAsync(
        string slug,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetTagsForProjectAsync(
        string projectId,
        CancellationToken ct = default);

    Task<ProjectRepository> UpsertRepositoryAsync(
        ProjectRepository repository,
        IEnumerable<string>? tags = null,
        CancellationToken ct = default);

    Task<bool> DeleteRepositoryAsync(
        string projectId,
        CancellationToken ct = default);

    Task<(int starCount, bool starred)> ToggleStarAsync(
        string projectId,
        string userId,
        CancellationToken ct = default);

    Task<bool> IsStarredByUserAsync(
        string projectId,
        string userId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ProjectRepository>> GetExploreRepositoriesAsync(
        string? query,
        string? category,
        string? tag,
        string? dbType,
        string sort,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<int> CountExploreRepositoriesAsync(
        string? query,
        string? category,
        string? tag,
        string? dbType,
        CancellationToken ct = default);

    Task<ProjectRepository> ForkRepositoryAsync(
        string sourceProjectId,
        string targetProjectId,
        CancellationToken ct = default);
}
