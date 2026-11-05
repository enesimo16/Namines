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
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ProjectId == projectId, ct);
    }

    public async Task<IReadOnlyDictionary<string, ProjectRepository>> GetRepositoriesAsync(
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct = default)
    {
        if (projectIds is not { Count: > 0 })
            return new Dictionary<string, ProjectRepository>();

        return await _dbContext.Repositories
            .AsNoTracking()
            .Where(r => projectIds.Contains(r.ProjectId))
            .ToDictionaryAsync(r => r.ProjectId, ct);
    }

    public async Task<ProjectVisibility?> GetVisibilityAsync(string projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return null;

        return await _dbContext.Repositories
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .Select(r => (ProjectVisibility?)r.Visibility)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<string?> GetTagsCsvAsync(string projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return null;

        var tags = await _dbContext.ProjectTags
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId)
            .ToListAsync(ct);
        return ProjectTagRules.ToCsv(tags);
    }

    public async Task<IReadOnlyDictionary<string, string?>> GetTagsCsvAsync(
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct = default)
    {
        if (projectIds is not { Count: > 0 })
            return new Dictionary<string, string?>();

        var tags = await _dbContext.ProjectTags
            .AsNoTracking()
            .Where(t => projectIds.Contains(t.ProjectId))
            .ToListAsync(ct);

        return tags
            .GroupBy(t => t.ProjectId)
            .ToDictionary(g => g.Key, g => ProjectTagRules.ToCsv(g));
    }

    public async Task<string?> ResolveProjectIdAsync(string ownerUserId, string slugOrId, CancellationToken ct = default)
    {
        var raw = (slugOrId ?? string.Empty).Trim();
        if (raw.Length == 0 || string.IsNullOrWhiteSpace(ownerUserId))
            return null;
        var normalized = raw.ToLowerInvariant();

        return await _dbContext.Repositories
            .AsNoTracking()
            .Where(r => r.OwnerUserId == ownerUserId &&
                        (r.ProjectId == raw || r.Slug == raw || r.Slug == normalized))
            .Select(r => r.ProjectId)
            .FirstOrDefaultAsync(ct);
    }

    public Task<bool> IsSlugTakenByOwnerAsync(string ownerUserId, string slug, string? excludeProjectId, CancellationToken ct = default) =>
        _dbContext.Repositories
            .AsNoTracking()
            .AnyAsync(r => r.OwnerUserId == ownerUserId && r.Slug == slug && r.ProjectId != excludeProjectId, ct);

    public Task<bool> IsSlugTakenAsync(string slug, string excludeProjectId, CancellationToken ct = default) =>
        _dbContext.Repositories
            .AsNoTracking()
            .AnyAsync(r => r.ProjectId != excludeProjectId && r.Slug == slug, ct);

    public async Task<IReadOnlyList<string>> GetSlugsByOwnerAsync(string ownerUserId, CancellationToken ct = default)
    {
        return await _dbContext.Repositories
            .AsNoTracking()
            .Where(r => r.OwnerUserId == ownerUserId && r.Slug != null)
            .Select(r => r.Slug!)
            .ToListAsync(ct);
    }

    public async Task CreateRepositoryAsync(ProjectRepository repository, string? tagsCsv, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _dbContext.ChangeTracker.Clear();
        _dbContext.Repositories.Add(repository);
        _dbContext.ProjectTags.AddRange(ProjectTagRules.Parse(repository.ProjectId, tagsCsv));
        await _dbContext.SaveChangesAsync(ct);
        _dbContext.ChangeTracker.Clear();
    }

    public async Task<ProjectRepository> EnsureRepositoryAsync(
        string projectId,
        string ownerUserId,
        string projectName,
        string projectDbType,
        string? slugIfMissing,
        CancellationToken ct = default)
    {
        _dbContext.ChangeTracker.Clear();
        var repo = await _dbContext.Repositories.FirstOrDefaultAsync(r => r.ProjectId == projectId, ct);
        if (repo is null)
        {
            repo = new ProjectRepository
            {
                ProjectId = projectId,
                OwnerUserId = ownerUserId,
                ProjectName = projectName,
                ProjectDbType = projectDbType,
                Slug = slugIfMissing,
            };
            _dbContext.Repositories.Add(repo);
        }
        else
        {
            repo.ProjectName = projectName;
            repo.ProjectDbType = projectDbType;
            if (string.IsNullOrWhiteSpace(repo.Slug) && !string.IsNullOrWhiteSpace(slugIfMissing))
                repo.Slug = slugIfMissing;
            if (_dbContext.Entry(repo).State == EntityState.Modified)
                repo.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(ct);
        _dbContext.ChangeTracker.Clear();
        return repo;
    }

    public async Task<ProjectRepository> ApplySettingsAsync(
        string projectId,
        string ownerUserId,
        string projectName,
        string projectDbType,
        RepositorySettingsChange change,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        _dbContext.ChangeTracker.Clear();
        var repo = await _dbContext.Repositories.FirstOrDefaultAsync(r => r.ProjectId == projectId, ct);
        if (repo is null)
        {
            repo = new ProjectRepository { ProjectId = projectId, OwnerUserId = ownerUserId };
            _dbContext.Repositories.Add(repo);
        }

        repo.ProjectName = projectName;
        repo.ProjectDbType = projectDbType;
        if (change.Slug != null) repo.Slug = change.Slug;
        if (change.Description != null) repo.Description = change.Description;
        if (change.Visibility.HasValue) repo.Visibility = change.Visibility.Value;
        if (change.Category != null) repo.Category = change.Category;
        if (change.LinkedGithubRepo != null) repo.LinkedGithubRepo = change.LinkedGithubRepo;
        repo.UpdatedAt = DateTime.UtcNow;

        if (change.Tags != null)
        {
            // Sil + ekle: Position ve DisplayTag birlikte değiştiği için satır satır karşılaştırmak
            // kazanç sağlamıyor; ikisi aynı SaveChanges içinde.
            var existing = await _dbContext.ProjectTags.Where(t => t.ProjectId == projectId).ToListAsync(ct);
            _dbContext.ProjectTags.RemoveRange(existing);
            await _dbContext.SaveChangesAsync(ct);
            _dbContext.ProjectTags.AddRange(ProjectTagRules.Parse(projectId, change.Tags));
        }

        await _dbContext.SaveChangesAsync(ct);
        _dbContext.ChangeTracker.Clear();
        return repo;
    }

    public async Task<(int starCount, bool starred)> ToggleStarAsync(string projectId, string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        // Yıldız satırı + sayaç TEK işlemde: biri başarısız olursa diğeri de geri alınır.
        // Elle açılan işlem yürütme stratejisinin içinde olmak zorunda (bkz. Program.cs retry).
        bool starred;
        try
        {
            var strategy = _dbContext.Database.CreateExecutionStrategy();
            starred = await strategy.ExecuteAsync(async () =>
            {
                _dbContext.ChangeTracker.Clear();
                await using var tx = await _dbContext.Database.BeginTransactionAsync(ct);

                // Önce silmeyi dene: "var mı" okuması ayrı olsaydı eşzamanlı iki kaldırma sayacı
                // iki kez düşürürdü. Etkilenen satır sayısı tek doğruluk kaynağı.
                var removed = await _dbContext.RepoStars
                    .Where(s => s.ProjectId == projectId && s.UserId == userId)
                    .ExecuteDeleteAsync(ct);
                if (removed > 0)
                {
                    await AdjustStarCountAsync(projectId, -removed, ct);
                    await tx.CommitAsync(ct);
                    return false;
                }

                _dbContext.RepoStars.Add(new RepoStar
                {
                    ProjectId = projectId,
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow
                });
                await _dbContext.SaveChangesAsync(ct);
                await AdjustStarCountAsync(projectId, +1, ct);
                await tx.CommitAsync(ct);
                return true;
            });
        }
        catch (DbUpdateException)
        {
            // Aynı kullanıcının eşzamanlı ikinci isteği: (ProjectId, UserId) PK ikinci eklemeyi
            // reddetti, işlem geri alındı, sayaç ARTMADI. Zaten yıldızlıysa 500 değil o durum döner.
            _dbContext.ChangeTracker.Clear();
            if (!await IsStarredByUserAsync(projectId, userId, ct))
                throw;
            starred = true;
        }

        var starCount = await _dbContext.Repositories
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .Select(r => r.StarCount)
            .FirstOrDefaultAsync(ct);
        return (starCount, starred);
    }

    // Tek SQL ifadesi: okuma-değiştirme-yazma yarışı yok, sıfırın altına inmez.
    private Task<int> AdjustStarCountAsync(string projectId, int delta, CancellationToken ct) =>
        _dbContext.Repositories
            .Where(r => r.ProjectId == projectId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                r => r.StarCount,
                r => r.StarCount + delta < 0 ? 0 : r.StarCount + delta), ct);

    public async Task<bool> IsStarredByUserAsync(string projectId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(userId))
            return false;

        return await _dbContext.RepoStars
            .AsNoTracking()
            .AnyAsync(s => s.ProjectId == projectId && s.UserId == userId, ct);
    }

    public async Task<IReadOnlyList<ProjectRepository>> GetExploreCandidatesAsync(
        ExploreCriteria criteria,
        int take,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var q = _dbContext.Repositories
            .AsNoTracking()
            .Where(r => r.Visibility == ProjectVisibility.Public &&
                        r.Description != null && r.Description != "");

        if (!string.IsNullOrWhiteSpace(criteria.Category))
        {
            var cat = criteria.Category.Trim().ToLower();
            q = q.Where(r => r.Category.ToLower() == cat);
        }

        if (!string.IsNullOrWhiteSpace(criteria.DbType))
        {
            var engine = criteria.DbType.Trim().ToLower();
            q = q.Where(r => r.ProjectDbType.ToLower() == engine);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Tag))
        {
            // Kanonik anahtarla tam eşleşme: CSV üzerinde LIKE yok, IX_project_tags_Tag_ProjectId.
            var key = ProjectTagRules.Key(criteria.Tag);
            q = q.Where(r => _dbContext.ProjectTags.Any(t => t.Tag == key && t.ProjectId == r.ProjectId));
        }

        if (!string.IsNullOrWhiteSpace(criteria.Query))
        {
            // Ad ve açıklamada alt dizge; etikette tam eşleşme (sahibin kuralı: etikette LIKE yok).
            var term = criteria.Query.Trim().ToLowerInvariant();
            var tagKey = ProjectTagRules.Key(criteria.Query);
            q = q.Where(r =>
                r.ProjectName.ToLower().Contains(term) ||
                r.Description!.ToLower().Contains(term) ||
                _dbContext.ProjectTags.Any(t => t.Tag == tagKey && t.ProjectId == r.ProjectId));
        }

        q = criteria.RecentFirst
            ? q.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.ProjectId)
            : q.OrderByDescending(r => r.StarCount).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.ProjectId);

        return await q.Take(take).ToListAsync(ct);
    }

    public Task IncrementForkCountAsync(string sourceProjectId, CancellationToken ct = default) =>
        _dbContext.Repositories
            .Where(r => r.ProjectId == sourceProjectId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ForkCount, r => r.ForkCount + 1), ct);

    public async Task<string?> ReassignOwnerAsync(string projectId, string newOwnerUserId, CancellationToken ct = default)
    {
        var slug = await _dbContext.Repositories
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .Select(r => r.Slug)
            .FirstOrDefaultAsync(ct);

        // Slug yalnızca sahip içinde benzersiz, aynı adlı projeler olağan: varis tarafında çakışırsa
        // (sahip, slug) UNIQUE hesap silmeyi kalıcı olarak düşürürdü.
        var finalSlug = slug;
        if (!string.IsNullOrWhiteSpace(slug))
        {
            for (var suffix = 2; await IsSlugTakenByOwnerAsync(newOwnerUserId, finalSlug!, projectId, ct); suffix++)
                finalSlug = $"{slug}-{suffix}";
        }

        await _dbContext.Repositories
            .Where(r => r.ProjectId == projectId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.OwnerUserId, newOwnerUserId)
                .SetProperty(r => r.Slug, finalSlug)
                .SetProperty(r => r.UpdatedAt, DateTime.UtcNow), ct);

        return finalSlug == slug ? null : finalSlug;
    }

    public async Task DetachFromForkSourceAsync(string projectId, CancellationToken ct = default)
    {
        var sourceId = await _dbContext.Repositories
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .Select(r => r.ForkedFromProjectId)
            .FirstOrDefaultAsync(ct);
        if (sourceId is null) return;

        await _dbContext.Repositories
            .Where(r => r.ProjectId == sourceId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                r => r.ForkCount,
                r => r.ForkCount > 0 ? r.ForkCount - 1 : 0), ct);
    }

    public async Task RemoveStarsByUserAsync(string userId, CancellationToken ct = default)
    {
        var starred = await _dbContext.RepoStars
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => s.ProjectId)
            .ToListAsync(ct);
        if (starred.Count == 0) return;

        await _dbContext.RepoStars.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
        await _dbContext.Repositories
            .Where(r => starred.Contains(r.ProjectId))
            .ExecuteUpdateAsync(s => s.SetProperty(
                r => r.StarCount,
                r => r.StarCount > 0 ? r.StarCount - 1 : 0), ct);
    }
}
