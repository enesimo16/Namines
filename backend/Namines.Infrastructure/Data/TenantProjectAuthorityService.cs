using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Modules;
using Namines.BuildingBlocks.Security;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Infrastructure.Services;

namespace Namines.Infrastructure.Data;

/// <summary>
/// <see cref="ITenantProjectAuthority"/> ve <see cref="IProjectConnectionSecretProvider"/>
/// sözleşmelerinin <see cref="AuthDbContext"/> tabanlı sunucu-otoriteli uygulaması.
///
/// <b>Neden gerekli:</b> Modüller (Vault, Ground, Flow, Desk, Hub vb.) kendi şemalarındaki
/// tablolardan transitif <c>OrganizationId</c> ve <c>EncryptedConnectionString</c>
/// sütunlarını çıkardığında (3NF) ve kendi <c>DbContext</c>'lerine geçtiğinde,
/// <see cref="AuthDbContext"/>'e doğrudan bağımlı olmadan tenant izolasyonunu ve
/// bağlantı sırrı erişimini bu dar sözleşmeler üzerinden doğrular.
/// </summary>
public sealed class TenantProjectAuthorityService : ITenantProjectAuthority, IProjectConnectionSecretProvider
{
    private readonly AuthDbContext _context;
    private readonly IConnectionSecretProtector _protector;
    private readonly IDbHostAccessPolicy? _hostPolicy;
    private readonly IStudioModule? _studioModule;

    public TenantProjectAuthorityService(
        AuthDbContext context,
        IConnectionSecretProtector protector,
        IDbHostAccessPolicy? hostPolicy = null,
        IStudioModule? studioModule = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _hostPolicy = hostPolicy;
        _studioModule = studioModule;
    }

    public async Task<ProjectAuthoritySnapshot?> GetProjectSnapshotAsync(
        string projectId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return null;

        var project = await _context.CloudProjects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.DbType,
                p.UserId,
                p.OrganizationId,
                p.AllowDeskSql,
                p.AutoApproveSafeChanges,
                p.SchemaJson
            })
            .FirstOrDefaultAsync(ct);

        if (project is null)
            return null;

        return new ProjectAuthoritySnapshot(
            project.Id,
            project.Name,
            project.DbType,
            project.UserId,
            project.OrganizationId,
            project.AllowDeskSql,
            project.AutoApproveSafeChanges,
            project.SchemaJson);
    }

    public async Task<UserPlanSnapshot?> GetUserPlanSnapshotAsync(
        string userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.SubscriptionStatus,
                u.PlanCode,
                u.IsDev
            })
            .FirstOrDefaultAsync(ct);

        return user is null
            ? null
            : new UserPlanSnapshot(user.Id, user.SubscriptionStatus, user.PlanCode, user.IsDev);
    }

    public async Task<OrgRole?> GetRoleAsync(
        string projectId,
        string userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(userId))
            return null;

        return await _context.GetRoleAsync(projectId, userId, ct);
    }

    public async Task<bool> CanViewAsync(
        string projectId,
        string userId,
        CancellationToken ct = default)
        => await GetRoleAsync(projectId, userId, ct) is not null;

    public async Task<bool> CanEditAsync(
        string projectId,
        string userId,
        CancellationToken ct = default)
    {
        var role = await GetRoleAsync(projectId, userId, ct);
        return role is OrgRole.Editor or OrgRole.Admin or OrgRole.Owner;
    }

    public async Task<bool> IsOwnerAsync(
        string projectId,
        string userId,
        CancellationToken ct = default)
    {
        var role = await GetRoleAsync(projectId, userId, ct);
        return role is OrgRole.Owner;
    }

    public async Task<bool> CanManageMembersAsync(
        string projectId,
        string userId,
        CancellationToken ct = default)
    {
        var role = await GetRoleAsync(projectId, userId, ct);
        return role is OrgRole.Admin or OrgRole.Owner;
    }

    public async Task<int> CountVotingMembersAsync(
        string projectId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return 0;

        return await _context.CountVotingMembersAsync(projectId, ct);
    }

    public string? FindDisallowedHost(string connectionString, string connectionDbType)
    {
        if (_hostPolicy is null)
            return null;

        return DbIntrospectionService.FindDisallowedHost(_hostPolicy, connectionString, connectionDbType);
    }

    public async Task<ResolvedProjectConnection?> ResolveAsync(
        string projectId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return null;

        // 1. Önce 3NF normalize studio.project_connections tablosunu dene
        if (_studioModule is not null)
        {
            var pc = await _studioModule.DbContext.ProjectConnections
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ProjectId == projectId, ct);

            if (pc is not null && !string.IsNullOrWhiteSpace(pc.EncryptedConnectionString))
            {
                var plaintextFrom3Nf = _protector.Unprotect(pc.EncryptedConnectionString, associatedData: projectId);
                var denyReasonFrom3Nf = FindDisallowedHost(plaintextFrom3Nf, pc.ConnectionDbType);
                var projectName = await _context.CloudProjects
                    .Where(p => p.Id == projectId)
                    .Select(p => p.Name)
                    .FirstOrDefaultAsync(ct) ?? projectId;

                return new ResolvedProjectConnection(
                    projectId,
                    projectName,
                    plaintextFrom3Nf,
                    pc.ConnectionDbType,
                    pc.AllowDeskSql,
                    denyReasonFrom3Nf);
            }
        }

        // 2. Geriye dönük uyumluluk: CloudProjects üzerindeki mirası oku
        var project = await _context.CloudProjects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.EncryptedConnectionString,
                p.ConnectionDbType,
                p.AllowDeskSql
            })
            .FirstOrDefaultAsync(ct);

        if (project is null ||
            string.IsNullOrWhiteSpace(project.EncryptedConnectionString) ||
            string.IsNullOrWhiteSpace(project.ConnectionDbType))
        {
            return null;
        }

        var plaintext = _protector.Unprotect(project.EncryptedConnectionString, associatedData: projectId);
        var denyReason = FindDisallowedHost(plaintext, project.ConnectionDbType);

        return new ResolvedProjectConnection(
            project.Id,
            project.Name,
            plaintext,
            project.ConnectionDbType,
            project.AllowDeskSql,
            denyReason);
    }

    public async Task<ResolvedProjectConnection?> ResolveForUserAsync(
        string projectId,
        string userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(userId))
            return null;

        var canView = await CanViewAsync(projectId, userId, ct);
        if (!canView)
            return null;

        return await ResolveAsync(projectId, ct);
    }

    public async Task<bool> SetConnectionAsync(
        string projectId,
        string rawConnectionString,
        string connectionDbType,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId) ||
            string.IsNullOrWhiteSpace(rawConnectionString) ||
            string.IsNullOrWhiteSpace(connectionDbType))
        {
            return false;
        }

        var project = await _context.CloudProjects
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

        if (project is null)
            return false;

        var protectedConn = _protector.Protect(rawConnectionString, associatedData: projectId);
        project.EncryptedConnectionString = protectedConn;
        project.ConnectionDbType = connectionDbType;
        project.UpdatedAt = DateTime.UtcNow;

        if (_studioModule is not null)
        {
            var existingConn = await _studioModule.DbContext.ProjectConnections
                .FirstOrDefaultAsync(c => c.ProjectId == projectId, ct);

            if (existingConn is not null)
            {
                existingConn.EncryptedConnectionString = protectedConn;
                existingConn.ConnectionDbType = connectionDbType;
                existingConn.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _studioModule.DbContext.ProjectConnections.Add(new ProjectConnection
                {
                    ProjectId = projectId,
                    EncryptedConnectionString = protectedConn,
                    ConnectionDbType = connectionDbType,
                    AllowDeskSql = project.AllowDeskSql,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            await _studioModule.DbContext.SaveChangesAsync(ct);
        }

        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ClearConnectionAsync(
        string projectId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return false;

        var project = await _context.CloudProjects
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

        if (project is null)
            return false;

        project.EncryptedConnectionString = null;
        project.ConnectionDbType = null;
        project.UpdatedAt = DateTime.UtcNow;

        if (_studioModule is not null)
        {
            var existingConn = await _studioModule.DbContext.ProjectConnections
                .FirstOrDefaultAsync(c => c.ProjectId == projectId, ct);

            if (existingConn is not null)
            {
                _studioModule.DbContext.ProjectConnections.Remove(existingConn);
                await _studioModule.DbContext.SaveChangesAsync(ct);
            }
        }

        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetDeskSqlEnabledAsync(
        string projectId,
        bool enabled,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return false;

        var project = await _context.CloudProjects
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

        if (project is null)
            return false;

        project.AllowDeskSql = enabled;
        project.UpdatedAt = DateTime.UtcNow;

        if (_studioModule is not null)
        {
            var existingConn = await _studioModule.DbContext.ProjectConnections
                .FirstOrDefaultAsync(c => c.ProjectId == projectId, ct);

            if (existingConn is not null)
            {
                existingConn.AllowDeskSql = enabled;
                existingConn.UpdatedAt = DateTime.UtcNow;
                await _studioModule.DbContext.SaveChangesAsync(ct);
            }
        }

        await _context.SaveChangesAsync(ct);
        return true;
    }
}
