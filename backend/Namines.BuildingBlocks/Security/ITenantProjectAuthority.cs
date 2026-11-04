using System;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Models.Auth;

namespace Namines.BuildingBlocks.Security;

/// <summary>
/// Bir projenin kiracı (organizasyon) ve sahiplik üst verisi.
/// Modüller `CloudProject` tablosuna doğrudan erişmek yerine bu salt-okunur
/// sözleşmeyi kullanır (3NF + Modül İzolasyonu).
/// </summary>
public sealed record ProjectAuthoritySnapshot(
    string ProjectId,
    string ProjectName,
    string TargetDbType,
    string OwnerUserId,
    string? OrganizationId,
    bool AllowDeskSql,
    bool AutoApproveSafeChanges,
    string? SchemaJson = null);

/// <summary>
/// Kullanıcının faturalama/plan üst verisi (Ground ve diğer modüllerin `Users` tablosuna
/// doğrudan dokunmadan kota hesaplayabilmesi için).
/// </summary>
public sealed record UserPlanSnapshot(
    string UserId,
    string? SubscriptionStatus,
    string? PlanCode,
    bool IsDev);

/// <summary>
/// Tüm modüller için merkezi kiracı ve proje yetki doğrulama sözleşmesi (Kritik Kontrol #1: Tenant Isolation).
/// </summary>
public interface ITenantProjectAuthority
{
    Task<ProjectAuthoritySnapshot?> GetProjectSnapshotAsync(string projectId, CancellationToken ct = default);

    Task<UserPlanSnapshot?> GetUserPlanSnapshotAsync(string userId, CancellationToken ct = default);

    Task<OrgRole?> GetRoleAsync(string projectId, string userId, CancellationToken ct = default);

    Task<bool> CanViewAsync(string projectId, string userId, CancellationToken ct = default);

    Task<bool> CanEditAsync(string projectId, string userId, CancellationToken ct = default);

    Task<bool> IsOwnerAsync(string projectId, string userId, CancellationToken ct = default);

    Task<bool> CanManageMembersAsync(string projectId, string userId, CancellationToken ct = default);

    Task<int> CountVotingMembersAsync(string projectId, CancellationToken ct = default);
}
