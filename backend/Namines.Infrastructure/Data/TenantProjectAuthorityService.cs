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
/// tablolardan transitif <c>OrganizationId</c> sütununu çıkardığında (3NF) ve kendi
/// <c>DbContext</c>'lerine geçtiğinde, <see cref="AuthDbContext"/>'e doğrudan bağımlı olmadan
/// tenant izolasyonunu doğrular. Bağlantı sırrı <c>studio.project_connections</c>'tan okunur;
/// çözme işinin tek kapısı burası.
/// </summary>
public sealed class TenantProjectAuthorityService : ITenantProjectAuthority, IProjectConnectionSecretProvider
{
    private readonly AuthDbContext _context;
    private readonly IConnectionSecretProtector _protector;
    private readonly IDbHostAccessPolicy? _hostPolicy;
    private readonly StudioDbContext _studio;

    public TenantProjectAuthorityService(
        AuthDbContext context,
        StudioDbContext studio,
        IConnectionSecretProtector protector,
        IDbHostAccessPolicy? hostPolicy = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _studio = studio ?? throw new ArgumentNullException(nameof(studio));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _hostPolicy = hostPolicy;
    }

    public async Task<ProjectAuthoritySnapshot?> GetProjectSnapshotAsync(
        string projectId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return null;

        // Bağlantı satırı yoksa SQL konsolu kapalı (false): opt-in'in varsayılanı.
        var project = await _studio.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.DbType,
                p.UserId,
                p.OrganizationId,
                AllowDeskSql = _studio.ProjectConnections
                    .Where(c => c.ProjectId == p.Id)
                    .Select(c => c.AllowDeskSql)
                    .FirstOrDefault(),
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
        var stored = await GetStoredConnectionAsync(projectId, ct);
        if (stored is null || string.IsNullOrWhiteSpace(stored.ConnectionDbType))
            return null;

        return stored with { HostPolicyDenyReason = FindDisallowedHost(stored.ConnectionString, stored.ConnectionDbType) };
    }

    public async Task<ResolvedProjectConnection?> GetStoredConnectionAsync(
        string projectId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return null;

        // Bu dosya, proje bağlantısının çözüldüğü TEK yer (mimari test denetliyor).
        var row = await (
                from c in _studio.ProjectConnections.AsNoTracking()
                join p in _studio.Projects.AsNoTracking() on c.ProjectId equals p.Id
                where c.ProjectId == projectId
                select new { c.EncryptedConnectionString, c.ConnectionDbType, c.AllowDeskSql, p.Name })
            .FirstOrDefaultAsync(ct);

        if (row is null || string.IsNullOrWhiteSpace(row.EncryptedConnectionString))
            return null;

        var plaintext = _protector.Unprotect(row.EncryptedConnectionString, associatedData: projectId);

        return new ResolvedProjectConnection(
            projectId,
            row.Name,
            plaintext,
            row.ConnectionDbType ?? string.Empty,
            row.AllowDeskSql);
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

        return await MutateConnectionAsync(projectId, connection =>
        {
            connection.EncryptedConnectionString = _protector.Protect(rawConnectionString, associatedData: projectId);
            connection.ConnectionDbType = connectionDbType;
        }, ct);
    }

    public Task<bool> ClearConnectionAsync(
        string projectId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return Task.FromResult(false);

        // SQL konsolu onayı bağlantıyla birlikte silinmez: Owner'ın kararı, bağlantı yenilenince
        // aynen geçerli kalmalı. Onay da yoksa satır anlamını yitirir ve silinir.
        return MutateConnectionAsync(projectId, connection =>
        {
            connection.EncryptedConnectionString = null;
            connection.ConnectionDbType = null;
        }, ct);
    }

    public Task<bool> SetDeskSqlEnabledAsync(
        string projectId,
        bool enabled,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return Task.FromResult(false);

        return MutateConnectionAsync(projectId, connection => connection.AllowDeskSql = enabled, ct);
    }

    /// <summary>
    /// Bağlantı satırı ile projenin UpdatedAt'i tek SaveChanges'te (tek işlem) değişir; proje
    /// satırına dokunmak aynı zamanda xmin'i ilerletir, yani eski sürümle kaydetmeye çalışan
    /// istemci yine çakışma görür. Eşzamanlı yazıda <c>DbUpdateConcurrencyException</c> çağırana gider.
    /// </summary>
    private async Task<bool> MutateConnectionAsync(string projectId, Action<ProjectConnection> mutate, CancellationToken ct)
    {
        _studio.ChangeTracker.Clear();

        var project = await _studio.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null)
            return false;

        var now = DateTime.UtcNow;
        var connection = await _studio.ProjectConnections.FirstOrDefaultAsync(c => c.ProjectId == projectId, ct);
        var isNew = connection is null;
        connection ??= new ProjectConnection { ProjectId = projectId, CreatedAt = now };

        mutate(connection);
        connection.UpdatedAt = now;
        project.UpdatedAt = now;

        var meaningful = connection.EncryptedConnectionString is not null ||
                         connection.ConnectionDbType is not null ||
                         connection.AllowDeskSql;
        if (isNew && meaningful)
            _studio.ProjectConnections.Add(connection);
        else if (!isNew && !meaningful)
            _studio.ProjectConnections.Remove(connection);

        await _studio.SaveChangesAsync(ct);
        _studio.ChangeTracker.Clear();
        return true;
    }
}
