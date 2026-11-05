using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Contracts;
using Namines.BuildingBlocks.Security;
using Namines.Core.Models.Auth;

namespace Namines.Infrastructure.Data;

/// <summary>
/// Kimlik tablolarının modüllere açılan dar görünümü. D-2'de Identity modülüne taşınır; o zamana kadar
/// modüller AuthDbContext'i tanımadan üyelik ve kullanıcı adı sorabilsin diye burada.
/// </summary>
public sealed class IdentityDirectoryService : IIdentityDirectory
{
    private readonly AuthDbContext _context;
    private readonly UserManager<ApplicationUser>? _users;

    public IdentityDirectoryService(AuthDbContext context, UserManager<ApplicationUser>? users = null)
    {
        _context = context;
        _users = users;
    }

    public async Task<OrgRole?> GetRoleAsync(string organizationId, string userId, CancellationToken ct = default) =>
        (await _context.OrganizationMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId, ct))?.Role;

    public async Task<IReadOnlyList<OrgMembership>> GetMembershipsAsync(string userId, CancellationToken ct = default) =>
        await _context.OrganizationMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new OrgMembership(m.OrganizationId, m.Role))
            .ToListAsync(ct);

    public Task<int> CountVotingMembersAsync(string organizationId, CancellationToken ct = default) =>
        _context.OrganizationMembers.AsNoTracking()
            .CountAsync(m => m.OrganizationId == organizationId &&
                             (m.Role == OrgRole.Editor || m.Role == OrgRole.Admin || m.Role == OrgRole.Owner), ct);

    public async Task<IReadOnlyDictionary<string, string?>> GetUserNamesAsync(IReadOnlyCollection<string> userIds, CancellationToken ct = default)
    {
        if (userIds is not { Count: > 0 }) return new Dictionary<string, string?>();
        return await _context.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName })
            .ToDictionaryAsync(u => u.Id, u => u.UserName, ct);
    }

    /// <summary>E-posta alanı şifreli depolanıyor olabilir: UserManager yolu çözer, doğrudan sorgu çözmez.</summary>
    public async Task<IReadOnlyDictionary<string, string?>> GetUserEmailsAsync(IReadOnlyCollection<string> userIds, CancellationToken ct = default)
    {
        if (userIds is not { Count: > 0 }) return new Dictionary<string, string?>();
        return await _context.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email })
            .ToDictionaryAsync(u => u.Id, u => u.Email, ct);
    }

    public async Task<UserPlanSnapshot?> GetUserPlanSnapshotAsync(string userId, CancellationToken ct = default) =>
        await _context.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserPlanSnapshot(u.Id, u.SubscriptionStatus, u.PlanCode, u.IsDev))
            .FirstOrDefaultAsync(ct);

    public async Task<string?> ResolveUserIdAsync(string userNameOrId, CancellationToken ct = default)
    {
        if (_users is not null)
        {
            var user = await _users.FindByNameAsync(userNameOrId) ?? await _users.FindByIdAsync(userNameOrId);
            return user?.Id;
        }
        return await _context.Users.AsNoTracking()
            .Where(u => u.UserName == userNameOrId || u.Id == userNameOrId)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<string> GetOrCreatePersonalOrganizationAsync(string userId, string displayName, CancellationToken ct = default) =>
        (await _context.GetOrCreatePersonalOrgAsync(userId, displayName, ct)).Id;
}
