using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Contracts;
using Namines.BuildingBlocks.Security;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Studio.Domain;

namespace Namines.Studio.Application;

/// <summary>
/// <see cref="ITenantProjectAuthority"/> ve <see cref="IProjectConnectionSecretProvider"/> sözleşmelerinin
/// tek uygulaması. Proje satırı Studio'nun, üyelik kimlik sözleşmesinden gelir: bağlantı sırrını çözmenin
/// ve proje rolünü hesaplamanın tek kapısı burası.
/// </summary>
internal sealed class TenantProjectAuthorityService : ITenantProjectAuthority, IProjectConnectionSecretProvider
{
    private readonly IIdentityDirectory _identity;
    private readonly IBillingAccounts _billing;
    private readonly IConnectionSecretProtector _protector;
    private readonly IConnectionHostInspector? _hostInspector;
    private readonly IStudioData _studio;

    public TenantProjectAuthorityService(
        IStudioData studio,
        IIdentityDirectory identity,
        IBillingAccounts billing,
        IConnectionSecretProtector protector,
        IConnectionHostInspector? hostInspector = null)
    {
        _studio = studio ?? throw new ArgumentNullException(nameof(studio));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _billing = billing ?? throw new ArgumentNullException(nameof(billing));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _hostInspector = hostInspector;
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

        return await _billing.GetPlanSnapshotAsync(userId, ct);
    }

    public async Task<OrgRole?> GetRoleAsync(
        string projectId,
        string userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(userId))
            return null;

        var project = await _studio.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.UserId, p.OrganizationId })
            .FirstOrDefaultAsync(ct);
        if (project is null) return null;

        // Org'a taşınmamış eski satır: erişimi sessizce kesmemek için sahibi Owner sayılır.
        if (string.IsNullOrEmpty(project.OrganizationId))
            return project.UserId == userId ? OrgRole.Owner : null;

        return await _identity.GetRoleAsync(project.OrganizationId, userId, ct);
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

        var orgId = await _studio.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => p.OrganizationId)
            .FirstOrDefaultAsync(ct);

        // Org'a taşınmamış eski satır: yalnızca oluşturan var sayılır.
        if (string.IsNullOrEmpty(orgId)) return 1;
        return await _identity.CountVotingMembersAsync(orgId, ct);
    }

    public string? FindDisallowedHost(string connectionString, string connectionDbType)
    {
        return _hostInspector?.FindDisallowedHost(connectionString, connectionDbType);
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
