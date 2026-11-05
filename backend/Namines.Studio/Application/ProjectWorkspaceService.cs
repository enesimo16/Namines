using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.BuildingBlocks.Security;
using Namines.Core.Models.Auth;

namespace Namines.Studio.Application;

/// <summary>Kullanıcının proje listesindeki satır; rol ve kurucu adı kimlik sözleşmesinden birleştirilir.</summary>
public sealed record WorkspaceProject(StudioProject Project, string? OwnerName, bool IsOwner);

/// <summary>Proje sahipliği ve görünürlüğü: üyelik sınırı burada uygulanır.</summary>
public interface IProjectWorkspaceService
{
    /// <summary>Üyesi olunan organizasyonlardaki projeler; org'a taşınmamış eski satırlar yalnızca kurucusuna.</summary>
    Task<IReadOnlyList<WorkspaceProject>> ListForUserAsync(string userId, CancellationToken ct = default);

    /// <summary>Yetki yoksa NotFound; projeyi başka org'a taşıyabilmek CanManageMembers ister.</summary>
    Task<ModuleOutcome<string>> MoveToOrganizationAsync(string actorUserId, string projectId, string organizationId, CancellationToken ct = default);

    Task<ModuleOutcome<bool>> SetAutoApproveAsync(string actorUserId, string projectId, bool enabled, CancellationToken ct = default);
}

internal sealed class ProjectWorkspaceService : IProjectWorkspaceService
{
    private readonly IStudioData _db;
    private readonly ITenantProjectAuthority _authority;
    private readonly IIdentityDirectory _identity;
    private readonly IStudioProjects _projects;

    public ProjectWorkspaceService(IStudioData db, ITenantProjectAuthority authority, IIdentityDirectory identity, IStudioProjects projects)
    {
        _db = db;
        _authority = authority;
        _identity = identity;
        _projects = projects;
    }

    public async Task<IReadOnlyList<WorkspaceProject>> ListForUserAsync(string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var memberships = await _identity.GetMembershipsAsync(userId, ct);
        var orgIds = memberships.Select(m => m.OrganizationId).ToList();
        var roles = memberships.ToDictionary(m => m.OrganizationId, m => m.Role);

        // "Kurucu" kısayolu yok: org'dan çıkarılan kurucu projeyi listesinde görmez.
        var rows = await _db.Projects.AsNoTracking()
            .Where(p => (p.OrganizationId == null && p.UserId == userId) ||
                        (p.OrganizationId != null && orgIds.Contains(p.OrganizationId)))
            .OrderByDescending(p => p.UpdatedAt)
            .Select(p => new StudioProject(p.Id, p.Name, p.DbType, p.SchemaJson, p.NodePositionsJson, p.UserId, p.OrganizationId, p.CreatedAt, p.UpdatedAt, p.AutoApproveSafeChanges, p.RowVersion))
            .ToListAsync(ct);

        var names = await _identity.GetUserNamesAsync(rows.Select(p => p.UserId).Distinct().ToList(), ct);

        return rows.Select(p => new WorkspaceProject(
                p,
                names.GetValueOrDefault(p.UserId),
                // Org'a taşınmamış satırda sahip Owner sayılır (yetki kuralıyla aynı).
                p.OrganizationId == null ? p.UserId == userId : roles.TryGetValue(p.OrganizationId, out var role) && role == OrgRole.Owner))
            .ToList();
    }

    public async Task<ModuleOutcome<string>> MoveToOrganizationAsync(string actorUserId, string projectId, string organizationId, CancellationToken ct = default)
    {
        if (!await _authority.CanManageMembersAsync(projectId, actorUserId, ct))
            return ModuleOutcome<string>.NotFound();
        return await _projects.SetOrganizationAsync(projectId, organizationId, ct)
            ? ModuleOutcome<string>.Ok(organizationId)
            : ModuleOutcome<string>.NotFound();
    }

    public async Task<ModuleOutcome<bool>> SetAutoApproveAsync(string actorUserId, string projectId, bool enabled, CancellationToken ct = default)
    {
        if (!await _authority.CanManageMembersAsync(projectId, actorUserId, ct))
            return ModuleOutcome<bool>.NotFound();
        return await _projects.SetAutoApproveSafeChangesAsync(projectId, enabled, ct)
            ? ModuleOutcome<bool>.Ok(enabled)
            : ModuleOutcome<bool>.NotFound();
    }
}
