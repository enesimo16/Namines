using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Modules;
using Namines.Core.Models.Auth;

namespace Namines.BuildingBlocks.Application;

public sealed class StudioApplicationService : IStudioApplicationService
{
    private readonly StudioDbContext _dbContext;

    public StudioApplicationService(StudioDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<CloudProject?> GetProjectAsync(string projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return null;

        return await _dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);
    }

    public async Task<IReadOnlyList<CloudProject>> GetProjectsForUserOrOrgAsync(
        string userId,
        IReadOnlyCollection<string> orgIds,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Array.Empty<CloudProject>();

        var query = _dbContext.Projects.AsNoTracking();

        if (orgIds is { Count: > 0 })
        {
            query = query.Where(p => (p.OrganizationId == null && p.UserId == userId) ||
                                     (p.OrganizationId != null && orgIds.Contains(p.OrganizationId)));
        }
        else
        {
            query = query.Where(p => p.UserId == userId);
        }

        return await query
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync(ct);
    }

    public async Task<CloudProject> CreateOrUpdateProjectAsync(CloudProject project, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var existing = await _dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == project.Id, ct);

        if (existing is null)
        {
            project.CreatedAt = project.CreatedAt == default ? DateTime.UtcNow : project.CreatedAt;
            project.UpdatedAt = DateTime.UtcNow;
            await _dbContext.Projects.AddAsync(project, ct);
            await _dbContext.SaveChangesAsync(ct);
            return project;
        }

        existing.Name = project.Name;
        existing.DbType = project.DbType;
        existing.SchemaJson = project.SchemaJson;
        existing.NodePositionsJson = project.NodePositionsJson;
        existing.AutoApproveSafeChanges = project.AutoApproveSafeChanges;
        existing.OrganizationId = project.OrganizationId ?? existing.OrganizationId;
        existing.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<bool> DeleteProjectAsync(string projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return false;

        var project = await _dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

        if (project is null)
            return false;

        // İlgili 3NF varlıklarını temizle
        var shares = await _dbContext.ProjectShares
            .Where(s => s.ProjectId == projectId)
            .ToListAsync(ct);
        if (shares.Count > 0)
            _dbContext.ProjectShares.RemoveRange(shares);

        var conns = await _dbContext.ProjectConnections
            .Where(c => c.ProjectId == projectId)
            .ToListAsync(ct);
        if (conns.Count > 0)
            _dbContext.ProjectConnections.RemoveRange(conns);

        var gitRepos = await _dbContext.ProjectGitRepositories
            .Where(g => g.ProjectId == projectId)
            .ToListAsync(ct);
        if (gitRepos.Count > 0)
            _dbContext.ProjectGitRepositories.RemoveRange(gitRepos);

        var branches = await _dbContext.Branches
            .Where(b => b.ProjectId == projectId)
            .ToListAsync(ct);
        if (branches.Count > 0)
            _dbContext.Branches.RemoveRange(branches);

        var versions = await _dbContext.SchemaVersions
            .Where(v => v.ProjectId == projectId)
            .ToListAsync(ct);
        if (versions.Count > 0)
            _dbContext.SchemaVersions.RemoveRange(versions);

        _dbContext.Projects.Remove(project);
        await _dbContext.SaveChangesAsync(ct);
        return true;
    }

    public async Task<string> CreateShareLinkAsync(string projectId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        var projectExists = await _dbContext.Projects.AnyAsync(p => p.Id == projectId, ct);
        if (!projectExists)
            throw new KeyNotFoundException($"Project '{projectId}' not found in studio.projects.");

        // Kriptografik rastgele 24-byte (192-bit entropi) URL-safe token
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var tokenHash = ShareTokenHasher.Hash(rawToken);

        var existingShare = await _dbContext.ProjectShares
            .FirstOrDefaultAsync(s => s.ProjectId == projectId, ct);

        if (existingShare is not null)
        {
            existingShare.ShareTokenHash = tokenHash;
            existingShare.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            await _dbContext.ProjectShares.AddAsync(new ProjectShare
            {
                ProjectId = projectId,
                ShareTokenHash = tokenHash,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }, ct);
        }

        await _dbContext.SaveChangesAsync(ct);
        return rawToken;
    }

    public async Task<bool> RevokeShareLinkAsync(string projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return false;

        var share = await _dbContext.ProjectShares
            .FirstOrDefaultAsync(s => s.ProjectId == projectId, ct);

        if (share is null)
            return false;

        _dbContext.ProjectShares.Remove(share);
        await _dbContext.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ProjectShare?> GetShareByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
            return null;

        return await _dbContext.ProjectShares
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ShareTokenHash == tokenHash, ct);
    }

    public async Task<CloudProject?> GetSharedProjectByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
            return null;

        var share = await _dbContext.ProjectShares
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ShareTokenHash == tokenHash, ct);

        if (share is null)
            return null;

        return await _dbContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == share.ProjectId, ct);
    }

    public async Task<bool> SetAutoApproveSafeChangesAsync(string projectId, bool enabled, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return false;

        var project = await _dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

        if (project is null)
            return false;

        project.AutoApproveSafeChanges = enabled;
        project.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);
        return true;
    }
}
