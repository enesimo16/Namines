using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Models.Auth;

namespace Namines.BuildingBlocks.Contracts;

/// <summary>Oturumun geçerliliği için gereken en az bilgi: kullanıcı var mı, güvenlik damgası ne.</summary>
public sealed record UserSessionState(bool Exists, string? SecurityStamp);

public sealed record MemberRow(string UserId, string? Username, string? Email, OrgRole Role, DateTime JoinedAt);

public enum MemberChange
{
    Ok,
    NotFound,
    LastOwner,
}

/// <summary>
/// Organizasyon üyeliğinin tek yazım kapısı. "Son sahip" kuralı organizasyon başına seri işlenir (PostgreSQL'de
/// işlem kapsamlı danışma kilidi): iki eşzamanlı düşürme ikisi de "iki sahip var" görüp organizasyonu sahipsiz bırakamaz.
/// Çağıranın işlemine katılır (<see cref="Namines.BuildingBlocks.Persistence.ICrossModuleTransaction"/>).
/// </summary>
public interface IOrganizationMembers
{
    Task<IReadOnlyList<MemberRow>> ListAsync(string organizationId, CancellationToken ct = default);

    Task<OrgRole?> GetRoleAsync(string organizationId, string userId, CancellationToken ct = default);

    /// <summary>Zaten üyeyse false (birleşik birincil anahtar son savunma).</summary>
    Task<bool> AddAsync(string organizationId, string userId, OrgRole role, CancellationToken ct = default);

    Task<MemberChange> ChangeRoleAsync(string organizationId, string userId, OrgRole role, CancellationToken ct = default);

    Task<MemberChange> RemoveAsync(string organizationId, string userId, CancellationToken ct = default);
}

public sealed record MembershipInfo(string OrganizationId, string OrganizationName, OrgRole Role);

public sealed record OtherMember(string UserId, OrgRole Role);

/// <summary>Hesap silme ve devir planı için organizasyon okumaları ve yazımları. Çağıranın işleminde çalışır.</summary>
public interface IAccountOrganizations
{
    Task<IReadOnlyList<MembershipInfo>> GetMembershipsAsync(string userId, CancellationToken ct = default);

    /// <summary>Kullanıcı hariç üyeler, katılım sırasıyla.</summary>
    Task<IReadOnlyList<OtherMember>> GetOtherMembersAsync(string organizationId, string excludeUserId, CancellationToken ct = default);

    /// <summary>Varis: önce Owner, sonra en eski üye. Yoksa null.</summary>
    Task<string?> FindHeirAsync(string organizationId, string excludeUserId, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListCreatedOrganizationIdsAsync(string userId, IReadOnlyCollection<string> exclude, CancellationToken ct = default);

    Task SetOrganizationCreatorAsync(string organizationId, string newCreatorUserId, CancellationToken ct = default);

    Task DeleteOrganizationsAsync(IReadOnlyCollection<string> organizationIds, CancellationToken ct = default);

    /// <summary>
    /// Kullanıcıyı siler (üyelikler, davet atıfları, jeton ve talepler veritabanı kısıtlarıyla gider). Silinen kullanıcının
    /// oturumları bir sonraki güvenlik damgası denetiminde düşer. Başarısızsa hata kodları döner.
    /// </summary>
    Task<(bool Succeeded, IReadOnlyList<string> ErrorCodes)> DeleteUserAsync(string userId, CancellationToken ct = default);
}

/// <summary>Saklama süresi işleri.</summary>
public interface IIdentityMaintenance
{
    /// <summary>Kabul edilmemiş ve süresi dolmuş davetler; kabul edilenler üyelik geçmişi olarak kalır.</summary>
    Task<int> PurgeExpiredInvitesAsync(DateTime cutoffUtc, TimeSpan pause, CancellationToken ct = default);
}

/// <summary>Yönetim panelinin kullanıcı satırı (kimlik alanları; plan ve kota Billing'den birleştirilir).</summary>
public sealed record AdminUserRow(
    string Id, string? UserName, string? Email, bool IsDev, UserType Type, string? CompanyName, DateTime CreatedAt,
    bool EmailConfirmed, bool LockedOut);

public sealed record AdminUserPage(int Total, IReadOnlyList<AdminUserRow> Users);

public sealed record AdminUserQuery(string? Search, IReadOnlyCollection<string>? RestrictToIds, IReadOnlyCollection<string>? ExcludeIds, bool? DevOnly, bool ExcludeDev, int Skip, int Take);

/// <summary>Yönetim paneli için kimlik okumaları. Yetki AdminController + <c>IPlatformAdminAuthority</c>.</summary>
public interface IIdentityAdmin
{
    Task<int> CountUsersAsync(CancellationToken ct = default);

    Task<int> CountDevUsersAsync(CancellationToken ct = default);

    Task<AdminUserPage> QueryUsersAsync(AdminUserQuery query, CancellationToken ct = default);

    Task<AdminUserRow?> GetUserAsync(string userId, CancellationToken ct = default);

    Task<IReadOnlyList<AdminUserRow>> ListRecentUsersAsync(int take, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, string?>> GetUserNamesAsync(IReadOnlyCollection<string> userIds, CancellationToken ct = default);
}
