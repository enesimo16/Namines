using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Contracts;
using Namines.Core.Models.Auth;
using Namines.Identity.Infrastructure.Persistence;

namespace Namines.Identity.Application;

/// <summary>
/// Kimlik tablolarının modüllere açılan dar görünümü: üyelik, kullanıcı adı, oturum durumu, geliştirici bayrağı.
/// Plan ve abonelik burada DEĞİL (Billing'in); Stripe alanları kullanıcı tablosundan çıktı.
/// </summary>
internal sealed class IdentityDirectoryService : IIdentityDirectory
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

    public async Task<UserSessionState> GetSessionStateAsync(string userId, CancellationToken ct = default)
    {
        var stamp = await _context.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.SecurityStamp })
            .FirstOrDefaultAsync(ct);
        return new UserSessionState(stamp is not null, stamp?.SecurityStamp);
    }

    public async Task<IdentityUserFacts?> FindUserByEmailAsync(string email, CancellationToken ct = default)
    {
        var user = _users is not null
            ? await _users.FindByEmailAsync(email)
            : await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpperInvariant(), ct);
        return user is null ? null : new IdentityUserFacts(user.Id, user.UserName, user.Email, user.IsDev, user.Type);
    }

    public async Task<IdentityUserFacts?> GetUserFactsAsync(string userId, CancellationToken ct = default) =>
        await _context.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new IdentityUserFacts(u.Id, u.UserName, u.Email, u.IsDev, u.Type))
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Yalnızca <c>Type</c> yazılır, SecurityStamp'a dokunulmaz: ödeme olayı kullanıcının açık oturumlarını düşürmemeli.
    /// </summary>
    public async Task SetUserTypeAsync(string userId, UserType type, CancellationToken ct = default) =>
        await _context.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Type, type)
                .SetProperty(u => u.ConcurrencyStamp, Guid.NewGuid().ToString()), ct);

    public async Task<string?> GetPoolOrganizationIdAsync(string userId, CancellationToken ct = default) =>
        await _context.OrganizationMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.Organization.IsPersonal ? 1 : 0)
            .ThenBy(m => m.JoinedAt)
            .Select(m => m.OrganizationId)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<string>> ListDevUserIdsAsync(CancellationToken ct = default) =>
        await _context.Users.AsNoTracking().Where(u => u.IsDev).Select(u => u.Id).ToListAsync(ct);

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
