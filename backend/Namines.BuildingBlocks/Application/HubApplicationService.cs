using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Modules;
using Namines.Core.Models.Auth;
using Namines.Core.Models.Hub;

namespace Namines.BuildingBlocks.Application;

public sealed class HubApplicationService : IHubApplicationService
{
    private readonly HubDbContext _dbContext;

    public HubApplicationService(HubDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<ProjectRepository?> GetRepositoryAsync(string projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return null;

        return await _dbContext.Repositories
            .FirstOrDefaultAsync(r => r.ProjectId == projectId, ct);
    }

    public async Task<ProjectRepository?> GetRepositoryBySlugAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        var normalized = slug.Trim().ToLowerInvariant();

        return await _dbContext.Repositories
            .FirstOrDefaultAsync(r => r.Slug == slug || r.Slug == normalized, ct);
    }

    public async Task<IReadOnlyList<string>> GetTagsForProjectAsync(string projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return Array.Empty<string>();

        return await _dbContext.ProjectTags
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId)
            .Select(t => t.Tag)
            .ToListAsync(ct);
    }

    public async Task<ProjectRepository> UpsertRepositoryAsync(
        ProjectRepository repository,
        IEnumerable<string>? tags = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var existing = await _dbContext.Repositories
            .FirstOrDefaultAsync(r => r.ProjectId == repository.ProjectId, ct);

        if (existing is null)
        {
            repository.PublishedAt = repository.PublishedAt == default ? DateTime.UtcNow : repository.PublishedAt;
            repository.UpdatedAt = DateTime.UtcNow;
            await _dbContext.Repositories.AddAsync(repository, ct);
            existing = repository;
        }
        else
        {
            existing.Slug = repository.Slug;
            existing.Description = repository.Description;
            existing.Visibility = repository.Visibility;
            existing.Category = repository.Category;
            existing.LinkedGithubRepo = repository.LinkedGithubRepo ?? existing.LinkedGithubRepo;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        if (tags is not null)
        {
            var existingTags = await _dbContext.ProjectTags
                .Where(t => t.ProjectId == repository.ProjectId)
                .ToListAsync(ct);

            if (existingTags.Count > 0)
                _dbContext.ProjectTags.RemoveRange(existingTags);

            var normalizedTags = tags
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(t => new ProjectTag { ProjectId = repository.ProjectId, Tag = t });

            await _dbContext.ProjectTags.AddRangeAsync(normalizedTags, ct);
        }

        await _dbContext.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<bool> DeleteRepositoryAsync(string projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return false;

        var existing = await _dbContext.Repositories
            .FirstOrDefaultAsync(r => r.ProjectId == projectId, ct);

        if (existing is null)
            return false;

        var tags = await _dbContext.ProjectTags
            .Where(t => t.ProjectId == projectId)
            .ToListAsync(ct);
        if (tags.Count > 0)
            _dbContext.ProjectTags.RemoveRange(tags);

        var stars = await _dbContext.RepoStars
            .Where(s => s.ProjectId == projectId)
            .ToListAsync(ct);
        if (stars.Count > 0)
            _dbContext.RepoStars.RemoveRange(stars);

        var issues = await _dbContext.RepoIssues
            .Where(i => i.ProjectId == projectId)
            .ToListAsync(ct);
        if (issues.Count > 0)
        {
            var issueIds = issues.Select(i => i.Id).ToList();
            var comments = await _dbContext.RepoIssueComments
                .Where(c => issueIds.Contains(c.IssueId))
                .ToListAsync(ct);
            if (comments.Count > 0)
                _dbContext.RepoIssueComments.RemoveRange(comments);

            _dbContext.RepoIssues.RemoveRange(issues);
        }

        _dbContext.Repositories.Remove(existing);
        await _dbContext.SaveChangesAsync(ct);
        return true;
    }

    public async Task<(int starCount, bool starred)> ToggleStarAsync(string projectId, string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var repo = await _dbContext.Repositories
            .FirstOrDefaultAsync(r => r.ProjectId == projectId, ct);

        var existingStar = await _dbContext.RepoStars
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.UserId == userId, ct);

        bool starred;
        if (existingStar is not null)
        {
            _dbContext.RepoStars.Remove(existingStar);
            if (repo is not null && repo.StarCount > 0)
                repo.StarCount--;
            starred = false;
        }
        else
        {
            await _dbContext.RepoStars.AddAsync(new RepoStar
            {
                ProjectId = projectId,
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            }, ct);

            if (repo is not null)
                repo.StarCount++;
            starred = true;
        }

        await _dbContext.SaveChangesAsync(ct);
        var currentCount = repo?.StarCount ?? 0;
        return (currentCount, starred);
    }

    public async Task<bool> IsStarredByUserAsync(string projectId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(userId))
            return false;

        return await _dbContext.RepoStars
            .AsNoTracking()
            .AnyAsync(s => s.ProjectId == projectId && s.UserId == userId, ct);
    }

    public async Task<IReadOnlyList<ProjectRepository>> GetExploreRepositoriesAsync(
        string? query,
        string? category,
        string? tag,
        string? dbType,
        string sort,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var q = BuildExploreQuery(query, category, tag);

        if (string.Equals(sort, "recent", StringComparison.OrdinalIgnoreCase))
            q = q.OrderByDescending(r => r.PublishedAt);
        else
            q = q.OrderByDescending(r => r.StarCount).ThenByDescending(r => r.PublishedAt);

        return await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<int> CountExploreRepositoriesAsync(
        string? query,
        string? category,
        string? tag,
        string? dbType,
        CancellationToken ct = default)
    {
        var q = BuildExploreQuery(query, category, tag);
        return await q.CountAsync(ct);
    }

    public async Task<ProjectRepository> ForkRepositoryAsync(
        string sourceProjectId,
        string targetProjectId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceProjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetProjectId);

        var sourceRepo = await _dbContext.Repositories
            .FirstOrDefaultAsync(r => r.ProjectId == sourceProjectId, ct);

        if (sourceRepo is not null)
        {
            sourceRepo.ForkCount++;
            sourceRepo.UpdatedAt = DateTime.UtcNow;
        }

        var forkedRepo = new ProjectRepository
        {
            ProjectId = targetProjectId,
            Slug = $"{sourceRepo?.Slug ?? "project"}-fork-{Guid.NewGuid():N}"[..Math.Min(120, 20)],
            Description = sourceRepo?.Description,
            Visibility = ProjectVisibility.Private,
            Category = sourceRepo?.Category ?? "General",
            StarCount = 0,
            ForkCount = 0,
            ForkedFromProjectId = sourceProjectId,
            PublishedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _dbContext.Repositories.AddAsync(forkedRepo, ct);

        // Kaynak etiketleri varsa kopyala
        var sourceTags = await _dbContext.ProjectTags
            .AsNoTracking()
            .Where(t => t.ProjectId == sourceProjectId)
            .ToListAsync(ct);

        if (sourceTags.Count > 0)
        {
            var forkedTags = sourceTags.Select(t => new ProjectTag
            {
                ProjectId = targetProjectId,
                Tag = t.Tag
            });
            await _dbContext.ProjectTags.AddRangeAsync(forkedTags, ct);
        }

        await _dbContext.SaveChangesAsync(ct);
        return forkedRepo;
    }

    private IQueryable<ProjectRepository> BuildExploreQuery(string? query, string? category, string? tag)
    {
        var q = _dbContext.Repositories
            .AsNoTracking()
            .Where(r => r.Visibility == ProjectVisibility.Public && !string.IsNullOrEmpty(r.Description));

        if (!string.IsNullOrWhiteSpace(category) && !string.Equals(category, "all", StringComparison.OrdinalIgnoreCase))
        {
            q = q.Where(r => r.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var normalizedTag = tag.Trim();
            var projectIdsWithTag = _dbContext.ProjectTags
                .AsNoTracking()
                .Where(t => t.Tag == normalizedTag)
                .Select(t => t.ProjectId);

            q = q.Where(r => projectIdsWithTag.Contains(r.ProjectId));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(r => r.Slug.Contains(term) || (r.Description != null && r.Description.Contains(term)));
        }

        return q;
    }
}
