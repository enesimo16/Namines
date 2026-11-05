using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Application;
using Namines.Core.Models.Auth;
using Namines.Studio.Domain;

namespace Namines.Studio.Application;

internal sealed class StudioApplicationService : IStudioApplicationService
{
    /// <summary>
    /// Proje başına etkin bağlantı tavanı. Her paylaşım tıklaması yeni satır ürettiği için bir
    /// üst sınır şart; aşılınca en eskisi kapanır (hata vermek arayüzde paylaşımı kırardı).
    /// </summary>
    public const int MaxActiveSharesPerProject = 50;

    private readonly IStudioData _dbContext;

    public StudioApplicationService(IStudioData dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<IReadOnlyDictionary<string, StudioProjectSummary>> GetProjectSummariesAsync(
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct = default)
    {
        if (projectIds is not { Count: > 0 })
            return new Dictionary<string, StudioProjectSummary>();

        return await _dbContext.Projects
            .AsNoTracking()
            .Where(p => projectIds.Contains(p.Id))
            .Select(p => new StudioProjectSummary(p.Id, p.Name, p.DbType, p.UserId, p.CreatedAt, p.UpdatedAt))
            .ToDictionaryAsync(p => p.Id, ct);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetSchemaJsonAsync(
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct = default)
    {
        if (projectIds is not { Count: > 0 })
            return new Dictionary<string, string>();

        return await _dbContext.Projects
            .AsNoTracking()
            .Where(p => projectIds.Contains(p.Id))
            .Select(p => new { p.Id, p.SchemaJson })
            .ToDictionaryAsync(p => p.Id, p => p.SchemaJson, ct);
    }

    public async Task<ProjectConnectionMetadata> GetConnectionMetadataAsync(
        string projectId,
        CancellationToken ct = default)
    {
        var map = await GetConnectionMetadataAsync(new[] { projectId }, ct);
        return map.TryGetValue(projectId, out var meta)
            ? meta
            : new ProjectConnectionMetadata(projectId, false, null, false);
    }

    public async Task<IReadOnlyDictionary<string, ProjectConnectionMetadata>> GetConnectionMetadataAsync(
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct = default)
    {
        if (projectIds is not { Count: > 0 })
            return new Dictionary<string, ProjectConnectionMetadata>();

        return await _dbContext.ProjectConnections
            .AsNoTracking()
            .Where(c => projectIds.Contains(c.ProjectId))
            .Select(c => new ProjectConnectionMetadata(
                c.ProjectId,
                c.EncryptedConnectionString != null,
                c.ConnectionDbType,
                c.AllowDeskSql))
            .ToDictionaryAsync(c => c.ProjectId, ct);
    }

    public async Task<string> CreateShareLinkAsync(string projectId, CancellationToken ct = default) =>
        (await CreateShareLinkWithCapAsync(projectId, ct)).Token;

    /// <summary>Tavan aşıldıysa kapatılan en eski bağlantı sayısını da söyler: kapanma sessiz kalmasın (BL-7).</summary>
    internal async Task<(string Token, int ClosedOldestLinks)> CreateShareLinkWithCapAsync(string projectId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        var projectExists = await _dbContext.Projects.AnyAsync(p => p.Id == projectId, ct);
        if (!projectExists)
            throw new KeyNotFoundException("Project not found.");

        // Kriptografik rastgele 24-byte (192-bit entropi) URL-safe token
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var now = DateTime.UtcNow;
        _dbContext.ChangeTracker.Clear();
        _dbContext.ProjectShares.Add(new ProjectShare
        {
            ProjectId = projectId,
            ShareTokenHash = ShareTokenHasher.Hash(rawToken),
            CreatedAt = now,
        });
        await _dbContext.SaveChangesAsync(ct);

        var overflow = await _dbContext.ProjectShares
            .Where(s => s.ProjectId == projectId && s.RevokedAt == null)
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .Skip(MaxActiveSharesPerProject)
            .Select(s => s.Id)
            .ToListAsync(ct);
        if (overflow.Count > 0)
        {
            await _dbContext.ProjectShares
                .Where(s => overflow.Contains(s.Id))
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, (DateTime?)now), ct);
        }

        return (rawToken, overflow.Count);
    }

    public async Task<int> RevokeShareLinksAsync(string projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return 0;

        var now = DateTime.UtcNow;
        return await _dbContext.ProjectShares
            .Where(s => s.ProjectId == projectId && s.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, (DateTime?)now), ct);
    }

    public Task<int> PurgeRevokedSharesAsync(DateTime revokedBefore, CancellationToken ct = default) =>
        _dbContext.ProjectShares
            .Where(s => s.RevokedAt != null && s.RevokedAt < revokedBefore)
            .ExecuteDeleteAsync(ct);

    public async Task<SharedProjectView?> GetSharedProjectByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
            return null;

        // Tek modül içi join: paylaşım ve proje aynı bağlamda.
        var query =
            from s in _dbContext.ProjectShares.AsNoTracking()
            join p in _dbContext.Projects.AsNoTracking() on s.ProjectId equals p.Id
            where s.ShareTokenHash == tokenHash && s.RevokedAt == null
            select new SharedProjectView(p.Name, p.DbType, p.SchemaJson, p.NodePositionsJson);

        return await query.FirstOrDefaultAsync(ct);
    }
}
