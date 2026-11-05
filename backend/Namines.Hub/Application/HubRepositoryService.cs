using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.BuildingBlocks.Persistence;
using Namines.BuildingBlocks.Security;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Core.Nsl;
using Namines.Hub.Domain;

namespace Namines.Hub.Application;

public sealed record RepoSettingsCommand(
    string? Name,
    string? Slug,
    string? Description,
    ProjectVisibility? Visibility,
    string? Category,
    string? Tags,
    string? LinkedGithubRepo,
    bool? AutoApproveSafeChanges,
    string? DbType);

public sealed record ExploreOwner(string Id, string Username);
public sealed record ExploreStats(int TableCount, int RelationCount);
public sealed record ExploreItem(
    string Id, string Name, string? Slug, string? Description, string DbType, string Category, string? Tags,
    int StarCount, int ForkCount, string? ForkedFromProjectId, DateTime CreatedAt, DateTime UpdatedAt,
    ExploreOwner Owner, ExploreStats Stats);
public sealed record ExplorePage(int Total, int Page, int PageSize, IReadOnlyList<ExploreItem> Items);

public sealed record RepoBranchView(string Id, string Name, bool IsDefault, DateTime CreatedAt);
public sealed record RepoForkedFrom(string Id, string Name, string? Slug, string OwnerUsername);
public sealed record RepoCaller(bool IsStarred, string? Role, bool CanEdit);
public sealed record RepoStats(int TableCount, int RelationCount, int OpenIssuesCount, int ClosedIssuesCount, int OpenPrCount, int ClosedPrCount);
public sealed record RepoView(
    string Id, string Name, string? Slug, string? Description, ProjectVisibility Visibility, string Category, string? Tags, string DbType,
    int StarCount, int ForkCount, string? ForkedFromProjectId, RepoForkedFrom? ForkedFrom, string? LinkedGithubRepo,
    bool AutoApproveSafeChanges, DateTime CreatedAt, DateTime UpdatedAt, ExploreOwner Owner, RepoCaller Caller, RepoStats Stats,
    IReadOnlyList<RepoBranchView> Branches, DatabaseSchema Schema, string NodePositionsJson);

public sealed record RepoSettingsView(
    string Id, string Name, string? Slug, string? Description, ProjectVisibility Visibility, string Category, string? Tags,
    string DbType, bool AutoApproveSafeChanges, string? LinkedGithubRepo, DateTime UpdatedAt);

public sealed record ForkView(string Id, string Name, string Slug, string? ForkedFromProjectId, ProjectVisibility Visibility, DateTime CreatedAt, string OwnerName);

public sealed record RepoBadgeView(DatabaseSchema Schema);

/// <summary>
/// Hub'ın son kullanıcı yüzeyi: görünürlük ve üyelik kuralları burada, controller yalnızca çevirir.
/// Özel repo varlığı yetkisiz taraftan görünmez (NotFound). Studio verisine yalnızca sözleşmeyle erişir.
/// </summary>
public interface IHubRepositoryService
{
    Task<ExplorePage> ExploreAsync(ExploreCriteria criteria, int page, int pageSize, CancellationToken ct = default);

    Task<ModuleOutcome<RepoView>> GetRepoAsync(string? actorUserId, string owner, string slugOrId, CancellationToken ct = default);

    Task<ModuleOutcome<RepoSettingsView>> UpdateSettingsAsync(string actorUserId, string projectId, RepoSettingsCommand command, CancellationToken ct = default);

    Task<ModuleOutcome<bool>> DeleteRepoAsync(string actorUserId, string projectId, CancellationToken ct = default);

    Task<ModuleOutcome<(bool IsStarred, int StarCount)>> ToggleStarAsync(string actorUserId, string projectId, CancellationToken ct = default);

    Task<ModuleOutcome<ForkView>> ForkAsync(string actorUserId, string projectId, string? name, CancellationToken ct = default);

    /// <summary>Yalnızca herkese açık repo için şema; rozet özel repoyu ele vermez.</summary>
    Task<RepoBadgeView?> GetBadgeAsync(string? actorUserId, string owner, string slug, CancellationToken ct = default);
}

internal sealed class HubRepositoryService : IHubRepositoryService
{
    private const string RepositoryNotFound = "Repository not found.";

    // Quality Gate şemayı ayrıştırıyor; bu pahalı olduğu için istek başına aday tavanı var.
    private const int ExploreCandidateLimit = 200;

    private const int MaxNameLength = 128;
    private const int MaxDescriptionLength = 1000;
    private const int MaxCategoryLength = 64;
    private const int MaxTagsLength = 256;
    private const int MaxGithubRepoLength = 140;

    // Slug biçimi: küçük harf/rakam, tek tire ile ayrılmış parçalar. Hub URL'sinin parçası
    // olduğu için bir sayfa yoluyla çakışan adlar ayrılmış.
    private static readonly Regex SlugPattern = new(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);

    private static readonly HashSet<string> ReservedSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "issues", "settings", "explore", "new", "api", "admin", "star", "fork", "badge", "me", "_",
    };

    private static readonly Regex GithubRepoPattern = new(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

    private readonly IHubApplicationService _hub;
    private readonly IHubIssueQueries _issues;
    private readonly IStudioProjects _projects;
    private readonly IStudioBranches _branches;
    private readonly ITenantProjectAuthority _authority;
    private readonly IIdentityDirectory _identity;
    private readonly IProjectDeletion _deletion;
    private readonly IChangeRequestStats _changeRequests;
    private readonly ICrossModuleTransaction _transaction;

    public HubRepositoryService(
        IHubApplicationService hub,
        IHubIssueQueries issues,
        IStudioProjects projects,
        IStudioBranches branches,
        ITenantProjectAuthority authority,
        IIdentityDirectory identity,
        IProjectDeletion deletion,
        IChangeRequestStats changeRequests,
        ICrossModuleTransaction transaction)
    {
        _hub = hub;
        _issues = issues;
        _projects = projects;
        _branches = branches;
        _authority = authority;
        _identity = identity;
        _deletion = deletion;
        _changeRequests = changeRequests;
        _transaction = transaction;
    }

    private async Task<StudioProject?> FindProjectByOwnerAsync(string? actorUserId, string owner, string slugOrId, CancellationToken ct)
    {
        var raw = (slugOrId ?? string.Empty).Trim();
        if (raw.Length == 0) return null;

        if (owner == "_") return await _projects.GetAsync(raw, ct);

        var ownerId = owner == "me" ? actorUserId : await _identity.ResolveUserIdAsync(owner, ct);
        if (string.IsNullOrEmpty(ownerId)) return null;

        var projectId = await _hub.ResolveProjectIdAsync(ownerId, raw, ct);
        return projectId is null ? null : await _projects.GetAsync(projectId, ct);
    }

    public async Task<ExplorePage> ExploreAsync(ExploreCriteria criteria, int page, int pageSize, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var repositories = await _hub.GetExploreCandidatesAsync(criteria, ExploreCandidateLimit, ct);
        var ids = repositories.Select(r => r.ProjectId).ToList();
        var projects = await _projects.GetManyAsync(ids, ct);
        var tags = await _hub.GetTagsCsvAsync(ids, ct);

        // Quality Gate: en az 4 tablo, 2 ilişki, 0 NSL hatası. Bozuk şema akışa girmez.
        var qualified = new List<(ProjectRepository Repository, StudioProject Project, DatabaseSchema Schema)>();
        foreach (var repository in repositories)
        {
            if (!projects.TryGetValue(repository.ProjectId, out var project)) continue;
            try
            {
                var schema = JsonSerializer.Deserialize<DatabaseSchema>(project.SchemaJson, SchemaJsonOptions.Default);
                if (schema is { Tables.Count: >= 4, Relations.Count: >= 2 } &&
                    !NslValidator.Validate(schema).Any(f => f.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)))
                {
                    qualified.Add((repository, project, schema));
                }
            }
            catch (JsonException)
            {
                // Bozuk şemalar keşif akışına dahil edilmez.
            }
        }

        var visible = qualified.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        // Yalnızca görünen sayfanın sahipleri: tek sorgu, N+1 yok.
        var owners = await _identity.GetUserNamesAsync(visible.Select(x => x.Project.UserId).Distinct().ToList(), ct);

        var items = visible.Select(x => new ExploreItem(
                x.Project.Id,
                x.Project.Name,
                x.Repository.Slug,
                x.Repository.Description,
                x.Project.DbType,
                x.Repository.Category,
                tags.GetValueOrDefault(x.Project.Id),
                x.Repository.StarCount,
                x.Repository.ForkCount,
                x.Repository.ForkedFromProjectId,
                x.Project.CreatedAt,
                x.Project.UpdatedAt,
                new ExploreOwner(x.Project.UserId, owners.GetValueOrDefault(x.Project.UserId) ?? "unknown"),
                new ExploreStats(x.Schema.Tables.Count, x.Schema.Relations.Count)))
            .ToList();

        return new ExplorePage(qualified.Count, page, pageSize, items);
    }

    public async Task<ModuleOutcome<RepoView>> GetRepoAsync(string? actorUserId, string owner, string slugOrId, CancellationToken ct = default)
    {
        var project = await FindProjectByOwnerAsync(actorUserId, owner, slugOrId, ct);
        if (project is null) return ModuleOutcome<RepoView>.NotFound(RepositoryNotFound);

        var repository = await _hub.GetRepositoryAsync(project.Id, ct);
        var visibility = repository?.Visibility ?? ProjectVisibility.Private;

        // Özel repoda giriş yapmamışa 401, üye olmayana 404 dönmek "bu repo var" bilgisini sızdıran
        // bir oracle'dı; ikisi de 404.
        if (visibility != ProjectVisibility.Public &&
            (string.IsNullOrEmpty(actorUserId) || !await _authority.CanViewAsync(project.Id, actorUserId, ct)))
            return ModuleOutcome<RepoView>.NotFound(RepositoryNotFound);

        OrgRole? callerRole = null;
        var isStarred = false;
        if (!string.IsNullOrEmpty(actorUserId))
        {
            callerRole = await _authority.GetRoleAsync(project.Id, actorUserId, ct);
            isStarred = await _hub.IsStarredByUserAsync(project.Id, actorUserId, ct);
        }
        var canEdit = callerRole is OrgRole.Editor or OrgRole.Admin or OrgRole.Owner;

        var branches = (await _branches.ListAsync(project.Id, ct))
            .Where(b => b.ClosedAt == null)
            .OrderByDescending(b => b.IsDefault)
            .ThenBy(b => b.Name)
            .Select(b => new RepoBranchView(b.Id, b.Name, b.IsDefault, b.CreatedAt))
            .ToList();

        var (openIssues, closedIssues) = await _issues.CountIssuesAsync(project.Id, ct);
        var (openPrs, closedPrs) = await _changeRequests.CountAsync(project.Id, ct);

        RepoForkedFrom? forkedFrom = null;
        if (!string.IsNullOrEmpty(repository?.ForkedFromProjectId))
        {
            var parent = await _projects.GetHeaderAsync(repository.ForkedFromProjectId, ct);
            if (parent is not null)
            {
                var parentRepository = await _hub.GetRepositoryAsync(parent.Id, ct);
                var parentOwner = await _identity.GetUserNamesAsync(new[] { parent.UserId }, ct);
                forkedFrom = new RepoForkedFrom(parent.Id, parent.Name, parentRepository?.Slug, parentOwner.GetValueOrDefault(parent.UserId) ?? "unknown");
            }
        }

        var tags = await _hub.GetTagsCsvAsync(project.Id, ct);

        DatabaseSchema schema;
        try
        {
            schema = JsonSerializer.Deserialize<DatabaseSchema>(project.SchemaJson, SchemaJsonOptions.Default) ?? new DatabaseSchema();
        }
        catch (JsonException)
        {
            schema = new DatabaseSchema { Name = project.Name };
        }

        var ownerNames = await _identity.GetUserNamesAsync(new[] { project.UserId }, ct);

        return ModuleOutcome<RepoView>.Ok(new RepoView(
            project.Id, project.Name, repository?.Slug, repository?.Description, visibility,
            repository?.Category ?? "General", tags, project.DbType,
            repository?.StarCount ?? 0, repository?.ForkCount ?? 0, repository?.ForkedFromProjectId, forkedFrom,
            repository?.LinkedGithubRepo, project.AutoApproveSafeChanges, project.CreatedAt, project.UpdatedAt,
            new ExploreOwner(project.UserId, ownerNames.GetValueOrDefault(project.UserId) ?? "unknown"),
            new RepoCaller(isStarred, callerRole?.ToString(), canEdit),
            new RepoStats(schema.Tables.Count, schema.Relations.Count, openIssues, closedIssues, openPrs, closedPrs),
            branches, schema, project.NodePositionsJson));
    }

    public async Task<ModuleOutcome<RepoSettingsView>> UpdateSettingsAsync(string actorUserId, string projectId, RepoSettingsCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(actorUserId) || !await _authority.CanEditAsync(projectId, actorUserId, ct))
            return ModuleOutcome<RepoSettingsView>.Forbidden();

        // Doğrulamalar kayıt YAPILMADAN önce: yarım uygulanmış bir PATCH bırakmamak için.
        if (command.Name != null && command.Name.Trim().Length > MaxNameLength)
            return ModuleOutcome<RepoSettingsView>.Invalid($"Name must be at most {MaxNameLength} characters.");
        if (command.Description != null && command.Description.Trim().Length > MaxDescriptionLength)
            return ModuleOutcome<RepoSettingsView>.Invalid($"Description must be at most {MaxDescriptionLength} characters.");
        if (command.Category != null && command.Category.Trim().Length > MaxCategoryLength)
            return ModuleOutcome<RepoSettingsView>.Invalid($"Category must be at most {MaxCategoryLength} characters.");
        if (command.Tags != null && command.Tags.Trim().Length > MaxTagsLength)
            return ModuleOutcome<RepoSettingsView>.Invalid($"Tags must be at most {MaxTagsLength} characters.");

        string? newSlug = null;
        if (command.Slug != null)
        {
            var slug = command.Slug.Trim().ToLowerInvariant();
            if (slug.Length < 3 || slug.Length > 64 || !SlugPattern.IsMatch(slug))
                return ModuleOutcome<RepoSettingsView>.Invalid("Slug must be 3-64 characters: lowercase letters, digits and single hyphens.");
            if (ReservedSlugs.Contains(slug))
                return ModuleOutcome<RepoSettingsView>.Invalid("This slug is reserved.");
            newSlug = slug;
        }

        string? newDbType = null;
        if (!string.IsNullOrWhiteSpace(command.DbType))
        {
            // Serbest metin DbType, DDL/eject yollarında bilinmeyen motora düşüyordu.
            var dbTypeText = command.DbType.Trim();
            if (dbTypeText.Equals("postgres", StringComparison.OrdinalIgnoreCase)) dbTypeText = nameof(DatabaseType.PostgreSQL);
            if (dbTypeText.Equals("sqlserver", StringComparison.OrdinalIgnoreCase)) dbTypeText = nameof(DatabaseType.MSSQL);
            if (!Enum.TryParse<DatabaseType>(dbTypeText, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                return ModuleOutcome<RepoSettingsView>.Invalid($"Unknown DbType. Use one of: {string.Join(", ", Enum.GetNames<DatabaseType>())}.");
            newDbType = parsed.ToString();
        }

        string? newGithubRepo = null;
        if (command.LinkedGithubRepo != null)
        {
            newGithubRepo = command.LinkedGithubRepo.Trim();
            // Boş değer bağlantıyı kaldırır; doluysa owner/repo biçimi.
            if (newGithubRepo.Length > 0 && (newGithubRepo.Length > MaxGithubRepoLength || !GithubRepoPattern.IsMatch(newGithubRepo)))
                return ModuleOutcome<RepoSettingsView>.Invalid("LinkedGithubRepo must look like 'owner/repo'.");
        }

        var project = await _projects.GetAsync(projectId, ct);
        if (project is null) return ModuleOutcome<RepoSettingsView>.NotFound(RepositoryNotFound);

        // Görünürlük/slug/GitHub bağlantısı/otomatik onay üye yönetimi yetkisi ister. Yalnızca DEĞİŞEN
        // alanlar sayılır: form değişmemiş değerleri de geri gönderdiğinde Editor'ün ad kaydı 403 almamalı.
        var repository = await _hub.GetRepositoryAsync(projectId, ct);
        var currentVisibility = repository?.Visibility ?? ProjectVisibility.Private;

        var touchesPrivileged =
            (command.Visibility.HasValue && command.Visibility.Value != currentVisibility) ||
            (command.AutoApproveSafeChanges.HasValue && command.AutoApproveSafeChanges.Value != project.AutoApproveSafeChanges) ||
            (newSlug != null && !string.Equals(newSlug, repository?.Slug, StringComparison.Ordinal)) ||
            (newGithubRepo != null && !string.Equals(newGithubRepo, repository?.LinkedGithubRepo ?? string.Empty, StringComparison.Ordinal));

        if (touchesPrivileged && !await _authority.CanManageMembersAsync(projectId, actorUserId, ct))
            return ModuleOutcome<RepoSettingsView>.Forbidden();

        var slugChanged = newSlug != null && !string.Equals(newSlug, repository?.Slug, StringComparison.Ordinal);
        if (slugChanged && await _hub.IsSlugTakenAsync(newSlug!, projectId, ct))
            return ModuleOutcome<RepoSettingsView>.Conflict("This slug is already taken.");

        var name = !string.IsNullOrWhiteSpace(command.Name) ? command.Name.Trim() : project.Name;
        var dbType = newDbType ?? project.DbType;
        var autoApprove = command.AutoApproveSafeChanges ?? project.AutoApproveSafeChanges;

        var change = new RepositorySettingsChange(
            Slug: slugChanged ? newSlug : null,
            Description: command.Description?.Trim(),
            Visibility: command.Visibility,
            Category: command.Category?.Trim(),
            Tags: command.Tags?.Trim(),
            LinkedGithubRepo: newGithubRepo);

        // Proje ve repo tek işlemde: yarım uygulanmış PATCH bırakılmaz.
        ProjectRepository updated = null!;
        await _transaction.ExecuteAsync(async c =>
        {
            // Yalnızca ad ve motor: şema, düğüm konumu ve org Studio'nun, burada okunan kopyaları bayat olabilir.
            await _projects.RenameAsync(project.Id, name, dbType, c);
            if (autoApprove != project.AutoApproveSafeChanges)
                await _projects.SetAutoApproveSafeChangesAsync(project.Id, autoApprove, c);
            updated = await _hub.ApplySettingsAsync(projectId, project.UserId, name, dbType, change, c);
        }, ct);

        var fresh = await _projects.GetHeaderAsync(projectId, ct);
        return ModuleOutcome<RepoSettingsView>.Ok(new RepoSettingsView(
            project.Id, name, updated.Slug, updated.Description, updated.Visibility, updated.Category,
            await _hub.GetTagsCsvAsync(projectId, ct), dbType, autoApprove, updated.LinkedGithubRepo,
            fresh?.UpdatedAt ?? DateTime.UtcNow));
    }

    public async Task<ModuleOutcome<bool>> DeleteRepoAsync(string actorUserId, string projectId, CancellationToken ct = default)
    {
        if (!await _projects.ExistsAsync(projectId, ct))
            return ModuleOutcome<bool>.NotFound(RepositoryNotFound);

        // Rol tek kaynaktan: projeyi oluşturup organizasyondan çıkarılan kullanıcı silemez.
        if (await _authority.GetRoleAsync(projectId, actorUserId, ct) != OrgRole.Owner)
            return ModuleOutcome<bool>.Forbidden();

        var result = await _deletion.DeleteAsync(projectId, actorUserId, ct);
        return result.Ok
            ? ModuleOutcome<bool>.Ok(true)
            : ModuleOutcome<bool>.Failed(result.StatusCode, result.Error, result.Code);
    }

    public async Task<ModuleOutcome<(bool IsStarred, int StarCount)>> ToggleStarAsync(string actorUserId, string projectId, CancellationToken ct = default)
    {
        var visibility = await _hub.GetVisibilityAsync(projectId, ct);
        if (visibility is null) return ModuleOutcome<(bool, int)>.NotFound(RepositoryNotFound);
        if (visibility != ProjectVisibility.Public && !await _authority.CanViewAsync(projectId, actorUserId, ct))
            return ModuleOutcome<(bool, int)>.NotFound(RepositoryNotFound);

        var (starCount, isStarred) = await _hub.ToggleStarAsync(projectId, actorUserId, ct);
        return ModuleOutcome<(bool, int)>.Ok((isStarred, starCount));
    }

    public async Task<ModuleOutcome<ForkView>> ForkAsync(string actorUserId, string projectId, string? name, CancellationToken ct = default)
    {
        if (name != null && name.Trim().Length > MaxNameLength)
            return ModuleOutcome<ForkView>.Invalid($"Name must be at most {MaxNameLength} characters.");

        var project = await _projects.GetAsync(projectId, ct);
        if (project is null) return ModuleOutcome<ForkView>.NotFound(RepositoryNotFound);

        var sourceRepository = await _hub.GetRepositoryAsync(projectId, ct);
        if ((sourceRepository?.Visibility ?? ProjectVisibility.Private) != ProjectVisibility.Public &&
            !await _authority.CanViewAsync(projectId, actorUserId, ct))
            return ModuleOutcome<ForkView>.NotFound(RepositoryNotFound);

        var names = await _identity.GetUserNamesAsync(new[] { actorUserId, project.UserId }, ct);
        var callerName = names.GetValueOrDefault(actorUserId) ?? "User";
        // Fork her zaman çağıranın kişisel organizasyonunda ve özel açılır: kaynağın ekibine sızmaz.
        var personalOrgId = await _identity.GetOrCreatePersonalOrganizationAsync(actorUserId, callerName, ct);

        var forkName = !string.IsNullOrWhiteSpace(name) ? name.Trim() : $"{project.Name}-fork";
        if (forkName.Length > MaxNameLength) forkName = forkName[..MaxNameLength];

        var baseSlug = !string.IsNullOrWhiteSpace(sourceRepository?.Slug) ? sourceRepository.Slug : project.Name.ToLowerInvariant();
        var cleanBaseSlug = Regex.Replace(baseSlug, @"[^a-z0-9\-]", "").Trim('-');
        if (cleanBaseSlug.Length == 0) cleanBaseSlug = "repo";
        var forkSlug = $"{cleanBaseSlug}-{Guid.NewGuid().ToString()[..6]}";

        var forkId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var sourceTags = await _hub.GetTagsCsvAsync(project.Id, ct);

        var forkedRepository = new ProjectRepository
        {
            ProjectId = forkId,
            OwnerUserId = actorUserId,
            ProjectName = forkName,
            ProjectDbType = project.DbType,
            Slug = forkSlug,
            Description = $"Forked from {names.GetValueOrDefault(project.UserId) ?? "unknown"}/{project.Name}",
            Visibility = ProjectVisibility.Private,
            Category = sourceRepository?.Category ?? "General",
            ForkedFromProjectId = project.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Proje, branch, repo satırı ve kaynağın sayacı tek işlemde: fork ya tam olur ya hiç olmaz.
        await _transaction.ExecuteAsync(async c =>
        {
            await _projects.SaveAsync(new[]
            {
                new ProjectWrite(forkId, forkName, project.DbType, project.SchemaJson, project.NodePositionsJson, actorUserId, personalOrgId, null, now, now),
            }, c);
            await _branches.GetOrCreateDefaultAsync(forkId, actorUserId, c);
            // Self-FK kaynak repo satırını ister; satırsız eski proje burada tamamlanır.
            if (sourceRepository is null)
                await _hub.EnsureRepositoryAsync(project.Id, project.UserId, project.Name, project.DbType, null, c);
            await _hub.CreateRepositoryAsync(forkedRepository, sourceTags, c);
            await _hub.IncrementForkCountAsync(project.Id, c);
        }, ct);

        return ModuleOutcome<ForkView>.Ok(new ForkView(forkId, forkName, forkSlug, project.Id, ProjectVisibility.Private, now, callerName));
    }

    public async Task<RepoBadgeView?> GetBadgeAsync(string? actorUserId, string owner, string slug, CancellationToken ct = default)
    {
        // Sahip verildiyse YALNIZCA onun repoları: slug'ı sahipler arasında aramak, başkasının
        // reposuna "owner/slug" rozeti üretmekti.
        var project = await FindProjectByOwnerAsync(actorUserId, owner, slug, ct);
        if (project is null || await _hub.GetVisibilityAsync(project.Id, ct) != ProjectVisibility.Public)
            return null;

        DatabaseSchema schema;
        try
        {
            schema = JsonSerializer.Deserialize<DatabaseSchema>(project.SchemaJson, SchemaJsonOptions.Default) ?? new DatabaseSchema();
        }
        catch (JsonException)
        {
            schema = new DatabaseSchema();
        }
        return new RepoBadgeView(schema);
    }
}
