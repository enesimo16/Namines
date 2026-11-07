using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Contracts;
using Namines.Core.Models.Auth;
using Namines.Identity.Infrastructure.Persistence;

namespace Namines.Identity.Application;

/// <summary>
/// Organizasyon üyeliğinin tek yazım kapısı. "Son sahip" kuralı organizasyon başına SERİ işlenir: PostgreSQL'de
/// işlem kapsamlı danışma kilidi alınır, yoksa iki eşzamanlı düşürme ikisi de "iki sahip var" görüp organizasyonu
/// sahipsiz bırakırdı (kilitsiz sürümde üyelik başına ayrı denetim vardı).
/// </summary>
internal sealed class OrganizationMembersService : IOrganizationMembers
{
    private readonly AuthDbContext _db;

    public OrganizationMembersService(AuthDbContext db) => _db = db;

    public async Task<IReadOnlyList<MemberRow>> ListAsync(string organizationId, CancellationToken ct = default) =>
        await _db.OrganizationMembers.AsNoTracking()
            .Where(m => m.OrganizationId == organizationId)
            .OrderBy(m => m.JoinedAt)
            .Select(m => new MemberRow(m.UserId, m.User.UserName, m.User.Email, m.Role, m.JoinedAt))
            .ToListAsync(ct);

    public async Task<OrgRole?> GetRoleAsync(string organizationId, string userId, CancellationToken ct = default) =>
        await _db.OrganizationMembers.AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && m.UserId == userId)
            .Select(m => (OrgRole?)m.Role)
            .FirstOrDefaultAsync(ct);

    public async Task<bool> AddAsync(string organizationId, string userId, OrgRole role, CancellationToken ct = default)
    {
        if (await _db.OrganizationMembers.AnyAsync(m => m.OrganizationId == organizationId && m.UserId == userId, ct))
            return false;

        _db.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organizationId, UserId = userId, Role = role, JoinedAt = DateTime.UtcNow,
        });
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex) && _db.Database.CurrentTransaction is null)
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    public Task<MemberChange> ChangeRoleAsync(string organizationId, string userId, OrgRole role, CancellationToken ct = default) =>
        MutateAsync(organizationId, userId, remove: false, role, ct);

    public Task<MemberChange> RemoveAsync(string organizationId, string userId, CancellationToken ct = default) =>
        MutateAsync(organizationId, userId, remove: true, OrgRole.Viewer, ct);

    private async Task<MemberChange> MutateAsync(string organizationId, string userId, bool remove, OrgRole role, CancellationToken ct)
    {
        // Çağıranın işlemi varsa ona katılır (gateway anahtar iptali ile birlikte atomik); yoksa kendi işlemini açar.
        if (_db.Database.CurrentTransaction is not null)
            return await MutateCoreAsync(organizationId, userId, remove, role, ct);

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            var result = await MutateCoreAsync(organizationId, userId, remove, role, ct);
            if (result == MemberChange.Ok) await tx.CommitAsync(ct);
            else await tx.RollbackAsync(ct);
            return result;
        });
    }

    private async Task<MemberChange> MutateCoreAsync(string organizationId, string userId, bool remove, OrgRole role, CancellationToken ct)
    {
        if (_db.Database.IsNpgsql())
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({"org-owners:" + organizationId}, 0))", ct);

        var member = await _db.OrganizationMembers.FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId, ct);
        if (member is null) return MemberChange.NotFound;

        if (member.Role == OrgRole.Owner && (remove || role != OrgRole.Owner))
        {
            var owners = await _db.OrganizationMembers.CountAsync(m => m.OrganizationId == organizationId && m.Role == OrgRole.Owner, ct);
            if (owners <= 1) return MemberChange.LastOwner;
        }

        if (remove) _db.OrganizationMembers.Remove(member);
        else member.Role = role;

        await _db.SaveChangesAsync(acceptAllChangesOnSuccess: false, ct);
        _db.ChangeTracker.AcceptAllChanges();
        return MemberChange.Ok;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException switch
    {
        System.Data.Common.DbException { SqlState: "23505" } => true,
        System.Data.Common.DbException { ErrorCode: 19 } => true,
        _ => false,
    };
}

/// <summary>Hesap silme planının kimlik tarafı: üyelik okumaları, devir, organizasyon ve kullanıcı silme.</summary>
internal sealed class AccountOrganizationsService : IAccountOrganizations
{
    private readonly AuthDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public AccountOrganizationsService(AuthDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    public async Task<IReadOnlyList<MembershipInfo>> GetMembershipsAsync(string userId, CancellationToken ct = default) =>
        await _db.OrganizationMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new MembershipInfo(m.OrganizationId, m.Organization.Name, m.Role))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<OtherMember>> GetOtherMembersAsync(string organizationId, string excludeUserId, CancellationToken ct = default) =>
        await _db.OrganizationMembers.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.UserId != excludeUserId)
            .OrderBy(x => x.JoinedAt)
            .Select(x => new OtherMember(x.UserId, x.Role))
            .ToListAsync(ct);

    public async Task<string?> FindHeirAsync(string organizationId, string excludeUserId, CancellationToken ct = default) =>
        await _db.OrganizationMembers.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.UserId != excludeUserId)
            .OrderByDescending(x => x.Role == OrgRole.Owner)
            .ThenBy(x => x.JoinedAt)
            .Select(x => x.UserId)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<string>> ListCreatedOrganizationIdsAsync(string userId, IReadOnlyCollection<string> exclude, CancellationToken ct = default) =>
        await _db.Organizations.AsNoTracking()
            .Where(o => o.CreatedByUserId == userId && !exclude.Contains(o.Id))
            .Select(o => o.Id)
            .ToListAsync(ct);

    public async Task SetOrganizationCreatorAsync(string organizationId, string newCreatorUserId, CancellationToken ct = default) =>
        await _db.Organizations.Where(o => o.Id == organizationId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.CreatedByUserId, newCreatorUserId), ct);

    public async Task DeleteOrganizationsAsync(IReadOnlyCollection<string> organizationIds, CancellationToken ct = default)
    {
        if (organizationIds.Count == 0) return;
        await _db.Organizations.Where(o => organizationIds.Contains(o.Id)).ExecuteDeleteAsync(ct);
    }

    public async Task<(bool Succeeded, IReadOnlyList<string> ErrorCodes)> DeleteUserAsync(string userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user is null) return (true, Array.Empty<string>());

        // ExecuteUpdate/Delete izlenen varlıkları güncellemez: eski bir örnek takipteyse EF kullanıcıyı silerken
        // devredilmiş satırlarda eşzamanlılık hatası alırdı.
        _db.ChangeTracker.Clear();
        user = await _users.FindByIdAsync(userId);
        var result = await _users.DeleteAsync(user!);
        return (result.Succeeded, result.Errors.Select(e => e.Code).ToList());
    }
}

internal sealed class IdentityMaintenanceService : IIdentityMaintenance
{
    private readonly AuthDbContext _db;

    public IdentityMaintenanceService(AuthDbContext db) => _db = db;

    public Task<int> PurgeExpiredInvitesAsync(DateTime cutoffUtc, TimeSpan pause, CancellationToken ct = default) =>
        Namines.BuildingBlocks.Persistence.BatchedDelete.RunAsync(
            _db.TeamInvites.Where(i => i.AcceptedAt == null && i.ExpiresAt < cutoffUtc), i => i.Id, pause, ct);
}
