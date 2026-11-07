using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;

namespace Namines.Identity.Application;

public sealed record TeamMemberView(string UserId, string? Username, string? Email, string Role, DateTime JoinedAt, bool IsYou);

public sealed record PendingInviteView(string Id, string Role, DateTime CreatedAt, DateTime ExpiresAt);

public sealed record SeatsView(int Total, int Used, int Available);

public sealed record TeamOverview(
    string Plan, bool TeamEnabled, SeatsView Seats, string OrganizationId,
    IReadOnlyList<TeamMemberView> Members, IReadOnlyList<PendingInviteView> PendingInvites);

public sealed record CreatedInvite(string Id, string Token, string Role, DateTime ExpiresAt);

public sealed record InvitePreview(string Organization, string Role, DateTime ExpiresAt);

public sealed record InviteAccepted(string Joined, string Role);

/// <summary>Ekip, davet ve aktif organizasyon. Plan Billing'den sözleşmeyle okunur, kimlik tablosundan değil.</summary>
public interface ITeamService
{
    /// <summary>Kişisel organizasyonu yoksa oluşturur; katıldığı ilk (kişisel olmayan) organizasyon varsa o aktiftir.</summary>
    Task<string> GetActiveOrganizationIdAsync(string userId, CancellationToken ct = default);

    Task<TeamOverview> GetTeamAsync(string userId, CancellationToken ct = default);

    Task<ModuleOutcome<CreatedInvite>> CreateInviteAsync(string userId, OrgRole role, int expiresInDays, CancellationToken ct = default);

    Task<ModuleOutcome<string>> RevokeInviteAsync(string userId, string inviteId, CancellationToken ct = default);

    Task<ModuleOutcome<InvitePreview>> PreviewInviteAsync(string token, CancellationToken ct = default);

    Task<ModuleOutcome<InviteAccepted>> AcceptInviteAsync(string userId, string token, CancellationToken ct = default);
}

internal sealed class TeamService : ITeamService
{
    private readonly IIdentityData _context;
    private readonly IBillingAccounts _billing;

    public TeamService(IIdentityData context, IBillingAccounts billing)
    {
        _context = context;
        _billing = billing;
    }

    /// <summary>Davet jetonu çıplak saklanmaz, SHA-256 özeti saklanır: veritabanı sızarsa davet linkleri kullanılamaz.</summary>
    private static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    private async Task<PlanTier> TierAsync(string? userId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId)) return PlanTier.Free;
        var snapshot = await _billing.GetPlanSnapshotAsync(userId, ct);
        return PlanQuotas.Resolve(snapshot?.SubscriptionStatus, snapshot?.PlanCode, snapshot?.IsDev ?? false);
    }

    private async Task<Organization> MyOrgAsync(string userId, CancellationToken ct)
    {
        var name = await _context.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.UserName).FirstOrDefaultAsync(ct);
        return await _context.GetOrCreatePersonalOrgAsync(userId, name ?? "Personal", ct);
    }

    private async Task<Organization> ActiveOrgAsync(string userId, CancellationToken ct)
    {
        var personal = await MyOrgAsync(userId, ct);
        var joined = await _context.OrganizationMembers.AsNoTracking()
            .Where(m => m.UserId == userId && m.OrganizationId != personal.Id)
            .OrderBy(m => m.JoinedAt)
            .Select(m => m.Organization)
            .FirstOrDefaultAsync(ct);
        return joined ?? personal;
    }

    public async Task<string> GetActiveOrganizationIdAsync(string userId, CancellationToken ct = default) =>
        (await ActiveOrgAsync(userId, ct)).Id;

    /// <summary>
    /// Takımın planı kurucunun planından okunur (org kendi aboneliğini taşımıyor); geliştirici hesabı ya kurucuda ya da
    /// çağıranda varsa sınırlar kapalıdır.
    /// </summary>
    private async Task<PlanTier> OrgTierAsync(Organization org, string callerId, CancellationToken ct)
    {
        var ownerTier = await TierAsync(org.CreatedByUserId, ct);
        if (ownerTier == PlanTier.Dev) return PlanTier.Dev;

        if (!string.IsNullOrEmpty(callerId) && callerId != org.CreatedByUserId
            && await TierAsync(callerId, ct) == PlanTier.Dev)
            return PlanTier.Dev;

        return ownerTier;
    }

    public async Task<TeamOverview> GetTeamAsync(string userId, CancellationToken ct = default)
    {
        var org = await ActiveOrgAsync(userId, ct);
        var tier = await OrgTierAsync(org, userId, ct);
        var seats = PlanQuotas.For(tier).TeamSeats;

        var members = await _context.OrganizationMembers.AsNoTracking()
            .Where(m => m.OrganizationId == org.Id)
            .OrderBy(m => m.JoinedAt)
            .Select(m => new TeamMemberView(m.UserId, m.User.UserName, m.User.Email, m.Role.ToString(), m.JoinedAt, m.UserId == userId))
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var pending = await _context.TeamInvites.AsNoTracking()
            .Where(i => i.OrganizationId == org.Id && i.AcceptedByUserId == null && i.RevokedAt == null && i.ExpiresAt > now)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new PendingInviteView(i.Id, i.Role.ToString(), i.CreatedAt, i.ExpiresAt))
            .ToListAsync(ct);

        return new TeamOverview(
            tier.ToString(),
            seats < 0 || seats > 1,
            new SeatsView(seats, members.Count + pending.Count, seats < 0 ? -1 : Math.Max(0, seats - members.Count - pending.Count)),
            org.Id, members, pending);
    }

    public async Task<ModuleOutcome<CreatedInvite>> CreateInviteAsync(string userId, OrgRole role, int expiresInDays, CancellationToken ct = default)
    {
        var org = await ActiveOrgAsync(userId, ct);
        var tier = await OrgTierAsync(org, userId, ct);
        var seats = PlanQuotas.For(tier).TeamSeats;

        if (seats >= 0 && seats <= 1)
            return ModuleOutcome<CreatedInvite>.Failed(402, "Inviting teammates requires the Team plan.", null);

        var myRole = await _context.OrganizationMembers.AsNoTracking()
            .Where(m => m.OrganizationId == org.Id && m.UserId == userId)
            .Select(m => (OrgRole?)m.Role)
            .FirstOrDefaultAsync(ct);
        if (myRole is not (OrgRole.Owner or OrgRole.Admin))
            return ModuleOutcome<CreatedInvite>.Failed(403, "Only team owners and admins can invite people.", null);

        // Owner rolü davet linkiyle verilemez: link ele geçerse tüm organizasyon devralınırdı.
        if (role is OrgRole.Owner)
            return ModuleOutcome<CreatedInvite>.Invalid("Owner role cannot be granted through an invite link.");

        var now = DateTime.UtcNow;
        var memberCount = await _context.OrganizationMembers.CountAsync(m => m.OrganizationId == org.Id, ct);
        var pendingCount = await _context.TeamInvites
            .CountAsync(i => i.OrganizationId == org.Id && i.AcceptedByUserId == null && i.RevokedAt == null && i.ExpiresAt > now, ct);

        // Bekleyen davet de koltuk sayılır: 5 koltuklu planda 5 üye + 3 bekleyen davet 8 kişi demek olurdu.
        if (seats >= 0 && memberCount + pendingCount >= seats)
            return ModuleOutcome<CreatedInvite>.Conflict(
                $"No seats left. The {tier} plan allows {seats} people in total (including you). Revoke a pending invite or remove a member first.");

        var days = Math.Clamp(expiresInDays, 1, 30);
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        var invite = new TeamInvite
        {
            OrganizationId = org.Id,
            TokenHash = Hash(raw),
            CreatedByUserId = userId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(days),
            Role = role,
        };
        _context.TeamInvites.Add(invite);
        await _context.SaveChangesAsync(ct);

        return ModuleOutcome<CreatedInvite>.Ok(new CreatedInvite(invite.Id, raw, invite.Role.ToString(), invite.ExpiresAt));
    }

    public async Task<ModuleOutcome<string>> RevokeInviteAsync(string userId, string inviteId, CancellationToken ct = default)
    {
        var org = await ActiveOrgAsync(userId, ct);

        var invite = await _context.TeamInvites.FirstOrDefaultAsync(i => i.Id == inviteId && i.OrganizationId == org.Id, ct);
        if (invite is null) return ModuleOutcome<string>.NotFound("Invite not found.");

        if (invite.AcceptedByUserId is not null)
            return ModuleOutcome<string>.Conflict("This invite was already used. Remove the member instead.");

        invite.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return ModuleOutcome<string>.Ok(inviteId);
    }

    public async Task<ModuleOutcome<InvitePreview>> PreviewInviteAsync(string token, CancellationToken ct = default)
    {
        var hash = Hash(token);
        var invite = await _context.TeamInvites.AsNoTracking().Include(i => i.Organization).FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (invite is null) return ModuleOutcome<InvitePreview>.NotFound("This invite link is not valid.");

        if (invite.AcceptedByUserId is not null) return ModuleOutcome<InvitePreview>.Conflict("This invite link has already been used.");
        if (invite.RevokedAt is not null) return ModuleOutcome<InvitePreview>.Conflict("This invite link was revoked.");
        if (invite.ExpiresAt <= DateTime.UtcNow) return ModuleOutcome<InvitePreview>.Conflict("This invite link has expired.");

        return ModuleOutcome<InvitePreview>.Ok(new InvitePreview(invite.Organization.Name, invite.Role.ToString(), invite.ExpiresAt));
    }

    /// <summary>
    /// Davet TEK KULLANIMLIK ve atomik: kabul, tek koşullu güncellemeyle (<c>AcceptedByUserId IS NULL</c>) alınır; iki kişi
    /// aynı linke aynı anda tıklarsa yalnızca biri üye olur, öbürü "artık kullanılamaz" alır.
    /// </summary>
    public async Task<ModuleOutcome<InviteAccepted>> AcceptInviteAsync(string userId, string token, CancellationToken ct = default)
    {
        var hash = Hash(token);
        var invite = await _context.TeamInvites.AsNoTracking().FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (invite is null) return ModuleOutcome<InviteAccepted>.NotFound("This invite link is not valid.");

        var now = DateTime.UtcNow;
        if (!invite.IsUsable(now)) return ModuleOutcome<InviteAccepted>.Conflict("This invite link is no longer usable.");

        if (await _context.OrganizationMembers.AnyAsync(m => m.OrganizationId == invite.OrganizationId && m.UserId == userId, ct))
            return ModuleOutcome<InviteAccepted>.Conflict("You are already a member of this team.");

        var strategy = _context.Database.CreateExecutionStrategy();
        var claimed = await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            var rows = await _context.TeamInvites
                .Where(i => i.Id == invite.Id && i.AcceptedByUserId == null && i.RevokedAt == null && i.ExpiresAt > now)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.AcceptedByUserId, userId).SetProperty(i => i.AcceptedAt, now), ct);
            if (rows == 0)
            {
                await tx.RollbackAsync(ct);
                return false;
            }

            _context.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = invite.OrganizationId, UserId = userId, Role = invite.Role, JoinedAt = now,
            });
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        });

        return claimed
            ? ModuleOutcome<InviteAccepted>.Ok(new InviteAccepted(invite.OrganizationId, invite.Role.ToString()))
            : ModuleOutcome<InviteAccepted>.Conflict("This invite link is no longer usable.");
    }
}
