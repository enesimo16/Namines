using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Contracts;

namespace Namines.Identity.Application;

/// <summary>Yönetim paneli kimlik okumaları. Plan ve kota Billing'den sözleşmeyle birleştirilir; burada yalnızca kimlik.</summary>
internal sealed class IdentityAdminService : IIdentityAdmin
{
    private readonly IIdentityData _db;

    public IdentityAdminService(IIdentityData db) => _db = db;

    public Task<int> CountUsersAsync(CancellationToken ct = default) => _db.Users.AsNoTracking().CountAsync(ct);

    public Task<int> CountDevUsersAsync(CancellationToken ct = default) => _db.Users.AsNoTracking().CountAsync(u => u.IsDev, ct);

    private static AdminUserRow Row(Namines.Core.Models.Auth.ApplicationUser u) =>
        new(u.Id, u.UserName, u.Email, u.IsDev, u.Type, u.CompanyName, u.CreatedAt, u.EmailConfirmed,
            u.LockoutEnd != null && u.LockoutEnd > DateTimeOffset.UtcNow);

    public async Task<AdminUserPage> QueryUsersAsync(AdminUserQuery query, CancellationToken ct = default)
    {
        var users = _db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            users = users.Where(u =>
                (u.Email != null && u.Email.ToLower().Contains(search)) ||
                (u.UserName != null && u.UserName.ToLower().Contains(search)) ||
                (u.CompanyName != null && u.CompanyName.ToLower().Contains(search)));
        }

        if (query.DevOnly == true) users = users.Where(u => u.IsDev);
        if (query.ExcludeDev) users = users.Where(u => !u.IsDev);
        if (query.RestrictToIds is not null) users = users.Where(u => query.RestrictToIds.Contains(u.Id));
        if (query.ExcludeIds is { Count: > 0 }) users = users.Where(u => !query.ExcludeIds.Contains(u.Id));

        var total = await users.CountAsync(ct);
        var page = await users.OrderByDescending(u => u.CreatedAt).Skip(query.Skip).Take(query.Take).ToListAsync(ct);
        return new AdminUserPage(total, page.Select(Row).ToList());
    }

    public async Task<AdminUserRow?> GetUserAsync(string userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? null : Row(user);
    }

    public async Task<IReadOnlyList<AdminUserRow>> ListRecentUsersAsync(int take, CancellationToken ct = default) =>
        (await _db.Users.AsNoTracking().OrderByDescending(u => u.CreatedAt).Take(take).ToListAsync(ct)).Select(Row).ToList();

    public async Task<IReadOnlyDictionary<string, string?>> GetUserNamesAsync(IReadOnlyCollection<string> userIds, CancellationToken ct = default) =>
        await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName, ct);
}
