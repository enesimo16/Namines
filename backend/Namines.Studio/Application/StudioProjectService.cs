using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Contracts;
using Namines.Studio.Domain;

namespace Namines.Studio.Application;

/// <summary><see cref="IStudioProjects"/> ve yönetim okumalarının uygulaması; varlık modül dışına çıkmaz.</summary>
internal sealed class StudioProjectService : IStudioProjects, IStudioAdminQueries
{
    private readonly IStudioData _db;

    public StudioProjectService(IStudioData db) => _db = db;

    private static readonly System.Linq.Expressions.Expression<Func<CloudProject, StudioProjectHeader>> HeaderOf = p =>
        new StudioProjectHeader(p.Id, p.Name, p.DbType, p.UserId, p.OrganizationId, p.CreatedAt, p.UpdatedAt, p.AutoApproveSafeChanges, p.RowVersion);

    private static readonly System.Linq.Expressions.Expression<Func<CloudProject, StudioProject>> ProjectOf = p =>
        new StudioProject(p.Id, p.Name, p.DbType, p.SchemaJson, p.NodePositionsJson, p.UserId, p.OrganizationId, p.CreatedAt, p.UpdatedAt, p.AutoApproveSafeChanges, p.RowVersion);

    public Task<StudioProject?> GetAsync(string projectId, CancellationToken ct = default) =>
        _db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(ProjectOf).FirstOrDefaultAsync(ct);

    public Task<StudioProjectHeader?> GetHeaderAsync(string projectId, CancellationToken ct = default) =>
        _db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(HeaderOf).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<string, StudioProject>> GetManyAsync(IReadOnlyCollection<string> projectIds, CancellationToken ct = default)
    {
        if (projectIds is not { Count: > 0 }) return new Dictionary<string, StudioProject>();
        return await _db.Projects.AsNoTracking().Where(p => projectIds.Contains(p.Id)).Select(ProjectOf).ToDictionaryAsync(p => p.Id, ct);
    }

    public async Task<IReadOnlyDictionary<string, StudioProjectHeader>> GetHeadersAsync(IReadOnlyCollection<string> projectIds, CancellationToken ct = default)
    {
        if (projectIds is not { Count: > 0 }) return new Dictionary<string, StudioProjectHeader>();
        return await _db.Projects.AsNoTracking().Where(p => projectIds.Contains(p.Id)).Select(HeaderOf).ToDictionaryAsync(p => p.Id, ct);
    }

    public Task<bool> ExistsAsync(string projectId, CancellationToken ct = default) =>
        _db.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, ct);

    public async Task SaveAsync(IReadOnlyList<ProjectWrite> writes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(writes);
        if (writes.Count == 0) return;

        // Yeniden denemede izleyicide eski durum kalmasın.
        _db.ChangeTracker.Clear();

        var ids = writes.Select(w => w.Id).ToList();
        var existing = await _db.Projects.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var now = DateTime.UtcNow;

        foreach (var write in writes)
        {
            if (existing.TryGetValue(write.Id, out var project))
            {
                // İstemcinin gördüğü sürüm jetonu özgün değer olur: araya biri girdiyse UPDATE 0 satır etkiler.
                if (write.ExpectedRowVersion is { } expected && _db.Database.IsNpgsql())
                    _db.Entry(project).Property(p => p.RowVersion).OriginalValue = expected;

                project.Name = write.Name;
                project.DbType = write.DbType;
                project.SchemaJson = write.SchemaJson;
                project.NodePositionsJson = write.NodePositionsJson;
                project.OrganizationId = write.OrganizationId;
                project.UpdatedAt = write.UpdatedAt ?? now;
            }
            else
            {
                _db.Projects.Add(new CloudProject
                {
                    Id = write.Id,
                    Name = write.Name,
                    DbType = write.DbType,
                    SchemaJson = write.SchemaJson,
                    NodePositionsJson = write.NodePositionsJson,
                    UserId = write.UserId,
                    OrganizationId = write.OrganizationId,
                    CreatedAt = write.CreatedAt ?? now,
                    UpdatedAt = write.UpdatedAt ?? now,
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    public async Task<bool> SetOrganizationAsync(string projectId, string organizationId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);
        return await _db.Projects.Where(p => p.Id == projectId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.OrganizationId, organizationId)
                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow), ct) > 0;
    }

    public async Task<bool> SetAutoApproveSafeChangesAsync(string projectId, bool enabled, CancellationToken ct = default) =>
        await _db.Projects.Where(p => p.Id == projectId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.AutoApproveSafeChanges, enabled), ct) > 0;

    public Task ReassignCreatorAsync(string projectId, string newUserId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newUserId);
        return _db.Projects.Where(p => p.Id == projectId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.UserId, newUserId)
                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow), ct);
    }

    public async Task<bool> DeleteAsync(string projectId, CancellationToken ct = default)
    {
        // Modül içi çocuklar FK cascade ile gider; ExecuteDelete izleyiciyi yanıltmasın diye temizlenir.
        _db.ChangeTracker.Clear();
        return await _db.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync(ct) > 0;
    }

    public async Task<IReadOnlyList<StudioProjectHeader>> ListCreatedByAsync(string userId, CancellationToken ct = default) =>
        await _db.Projects.AsNoTracking().Where(p => p.UserId == userId).Select(HeaderOf).ToListAsync(ct);

    public async Task<IReadOnlyList<string>> ListIdsInOrganizationsAsync(IReadOnlyCollection<string> organizationIds, CancellationToken ct = default)
    {
        if (organizationIds is not { Count: > 0 }) return Array.Empty<string>();
        return await _db.Projects.AsNoTracking()
            .Where(p => p.OrganizationId != null && organizationIds.Contains(p.OrganizationId))
            .Select(p => p.Id)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StudioProjectHeader>> ListInOrganizationAsync(string organizationId, int take, CancellationToken ct = default) =>
        await _db.Projects.AsNoTracking()
            .Where(p => p.OrganizationId == organizationId)
            .OrderByDescending(p => p.UpdatedAt)
            .Take(take)
            .Select(HeaderOf)
            .ToListAsync(ct);

    // ── Yönetim okumaları ───────────────────────────────────────────────────

    public Task<int> CountProjectsAsync(CancellationToken ct = default) => _db.Projects.CountAsync(ct);

    public async Task<IReadOnlyDictionary<string, int>> CountByOwnerAsync(IReadOnlyCollection<string> userIds, CancellationToken ct = default)
    {
        if (userIds is not { Count: > 0 }) return new Dictionary<string, int>();
        return await _db.Projects.AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .GroupBy(p => p.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);
    }

    public async Task<IReadOnlyList<StudioProjectHeader>> ListByOwnerAsync(string userId, CancellationToken ct = default) =>
        await _db.Projects.AsNoTracking().Where(p => p.UserId == userId).Select(HeaderOf).ToListAsync(ct);

    public async Task<IReadOnlyList<string>> ListDbTypesAsync(CancellationToken ct = default) =>
        await _db.Projects.AsNoTracking().Select(p => p.DbType).ToListAsync(ct);

    public async Task<IReadOnlyList<StudioProject>> PageAsync(int skip, int take, CancellationToken ct = default) =>
        await _db.Projects.AsNoTracking()
            .OrderByDescending(p => p.UpdatedAt)
            .Skip(skip)
            .Take(take)
            .Select(ProjectOf)
            .ToListAsync(ct);

    public async IAsyncEnumerable<string> StreamSchemasAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var schemaJson in _db.Projects.AsNoTracking().Select(p => p.SchemaJson).AsAsyncEnumerable().WithCancellation(ct))
            yield return schemaJson;
    }

    public async Task<long> SumSchemaCharsAsync(CancellationToken ct = default) =>
        await _db.Projects.AsNoTracking().SumAsync(p => (long?)(p.SchemaJson == null ? 0 : p.SchemaJson.Length), ct) ?? 0L;

    public async Task<IReadOnlyList<StudioProject>> ExportAsync(int take, CancellationToken ct = default) =>
        await _db.Projects.AsNoTracking().Take(take).Select(ProjectOf).ToListAsync(ct);
}
