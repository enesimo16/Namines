using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.BuildingBlocks.Persistence;
using Namines.BuildingBlocks.Security;
using Namines.Core.Models.Auth;
using Namines.Gateway.Application;
using Namines.Studio.Application;

namespace Namines.Infrastructure.Services;

public sealed record AddedMember(string Id, string? UserName, string? Email, OrgRole Role);

public sealed record ChangedMember(string UserId, OrgRole Role, int RevokedKeys);

public sealed record RemovedMember(string Removed, int RevokedKeys);

/// <summary>
/// Proje üyeliği yönetimi. Yetki Studio'dan, üyelik yazımı Identity'den, anahtar iptali Gateway'den sözleşmeyle gelir;
/// rol düşürme ve çıkarma, üyelik değişikliği ile Gateway anahtar iptalini TEK işlemde yapar (yarım kalırsa ikisi de geri
/// alınır: yetkisi düşen üyenin anahtarı çalışmaya devam etmesin).
/// </summary>
public interface IProjectMembershipService
{
    Task<ModuleOutcome<IReadOnlyList<MemberRow>>> ListAsync(string userId, string projectId, CancellationToken ct = default);

    Task<ModuleOutcome<AddedMember>> AddAsync(string userId, string projectId, string email, OrgRole role, CancellationToken ct = default);

    Task<ModuleOutcome<ChangedMember>> ChangeRoleAsync(string userId, string projectId, string memberUserId, OrgRole role, CancellationToken ct = default);

    Task<ModuleOutcome<RemovedMember>> RemoveAsync(string userId, string projectId, string memberUserId, CancellationToken ct = default);
}

public sealed class ProjectMembershipService : IProjectMembershipService
{
    private const string OwnerOnlyError = "Only an owner can grant, change or remove the owner role.";
    private const string NoPermission = "Project not found, or you do not have permission to manage members.";

    private readonly ITenantProjectAuthority _authority;
    private readonly IProjectLifecycleService _lifecycle;
    private readonly IOrganizationMembers _members;
    private readonly IIdentityDirectory _identity;
    private readonly IGatewayMaintenance _gateway;
    private readonly ICrossModuleTransaction _transaction;

    public ProjectMembershipService(
        ITenantProjectAuthority authority, IProjectLifecycleService lifecycle, IOrganizationMembers members,
        IIdentityDirectory identity, IGatewayMaintenance gateway, ICrossModuleTransaction transaction)
    {
        _authority = authority;
        _lifecycle = lifecycle;
        _members = members;
        _identity = identity;
        _gateway = gateway;
        _transaction = transaction;
    }

    private async Task<bool> IsCallerOwnerAsync(string projectId, string userId, CancellationToken ct) =>
        await _authority.GetRoleAsync(projectId, userId, ct) == OrgRole.Owner;

    public async Task<ModuleOutcome<IReadOnlyList<MemberRow>>> ListAsync(string userId, string projectId, CancellationToken ct = default)
    {
        if (!await _authority.CanViewAsync(projectId, userId, ct))
            return ModuleOutcome<IReadOnlyList<MemberRow>>.NotFound("Project not found.");

        var orgId = await _lifecycle.ResolveOrganizationAsync(userId, projectId, ct);
        if (orgId is null) return ModuleOutcome<IReadOnlyList<MemberRow>>.NotFound("Project not found.");

        return ModuleOutcome<IReadOnlyList<MemberRow>>.Ok(await _members.ListAsync(orgId, ct));
    }

    public async Task<ModuleOutcome<AddedMember>> AddAsync(string userId, string projectId, string email, OrgRole role, CancellationToken ct = default)
    {
        if (!await _authority.CanManageMembersAsync(projectId, userId, ct))
            return ModuleOutcome<AddedMember>.NotFound(NoPermission);

        if (string.IsNullOrWhiteSpace(email))
            return ModuleOutcome<AddedMember>.Invalid("An email address is required.");

        if (!Enum.IsDefined(role))
            return ModuleOutcome<AddedMember>.Invalid("Unknown role.");

        // Owner rolünü yalnızca bir Owner verebilir: Admin kendini Owner'a yükseltemesin.
        if (role == OrgRole.Owner && !await IsCallerOwnerAsync(projectId, userId, ct))
            return ModuleOutcome<AddedMember>.Forbidden(OwnerOnlyError);

        var invitee = await _identity.FindUserByEmailAsync(email.Trim(), ct);
        if (invitee is null)
            return ModuleOutcome<AddedMember>.NotFound("No user is registered with this email. (Invites are not available yet - the user must sign up first.)");

        var orgId = await _lifecycle.ResolveOrganizationAsync(userId, projectId, ct);
        if (orgId is null) return ModuleOutcome<AddedMember>.NotFound("Project not found.");

        if (!await _members.AddAsync(orgId, invitee.Id, role, ct))
            return ModuleOutcome<AddedMember>.Conflict("This user is already on the team.");

        return ModuleOutcome<AddedMember>.Ok(new AddedMember(invitee.Id, invitee.UserName, invitee.Email, role));
    }

    public async Task<ModuleOutcome<ChangedMember>> ChangeRoleAsync(string userId, string projectId, string memberUserId, OrgRole role, CancellationToken ct = default)
    {
        if (!await _authority.CanManageMembersAsync(projectId, userId, ct))
            return ModuleOutcome<ChangedMember>.NotFound(NoPermission);

        var orgId = await _lifecycle.ResolveOrganizationAsync(userId, projectId, ct);
        if (orgId is null) return ModuleOutcome<ChangedMember>.NotFound("Project not found.");

        var current = await _members.GetRoleAsync(orgId, memberUserId, ct);
        if (current is null) return ModuleOutcome<ChangedMember>.NotFound("Member not found.");

        if (!Enum.IsDefined(role))
            return ModuleOutcome<ChangedMember>.Invalid("Unknown role.");

        // Owner rolünü vermek de, bir Owner'ın rolünü değiştirmek de yalnızca Owner'a açık.
        if ((role == OrgRole.Owner || current == OrgRole.Owner) && !await IsCallerOwnerAsync(projectId, userId, ct))
            return ModuleOutcome<ChangedMember>.Forbidden(OwnerOnlyError);

        var revokedKeys = 0;
        var change = MemberChange.Ok;
        await _transaction.ExecuteAsync(async c =>
        {
            change = await _members.ChangeRoleAsync(orgId, memberUserId, role, c);
            if (change != MemberChange.Ok) return;

            // Sahiplikten düşürülen üyenin ayrıcalıklı anahtarları aynı işlemde iptal edilir.
            if (current == OrgRole.Owner && role != OrgRole.Owner)
                revokedKeys = await _gateway.RevokeMemberKeysAsync(projectId, memberUserId, privilegedOnly: true, c);
        }, ct);

        return change switch
        {
            MemberChange.NotFound => ModuleOutcome<ChangedMember>.NotFound("Member not found."),
            MemberChange.LastOwner => ModuleOutcome<ChangedMember>.Conflict("The last owner's role cannot be lowered - assign another owner first."),
            _ => ModuleOutcome<ChangedMember>.Ok(new ChangedMember(memberUserId, role, revokedKeys)),
        };
    }

    public async Task<ModuleOutcome<RemovedMember>> RemoveAsync(string userId, string projectId, string memberUserId, CancellationToken ct = default)
    {
        if (!await _authority.CanManageMembersAsync(projectId, userId, ct))
            return ModuleOutcome<RemovedMember>.NotFound(NoPermission);

        var orgId = await _lifecycle.ResolveOrganizationAsync(userId, projectId, ct);
        if (orgId is null) return ModuleOutcome<RemovedMember>.NotFound("Project not found.");

        var current = await _members.GetRoleAsync(orgId, memberUserId, ct);
        if (current is null) return ModuleOutcome<RemovedMember>.NotFound("Member not found.");

        if (current == OrgRole.Owner && !await IsCallerOwnerAsync(projectId, userId, ct))
            return ModuleOutcome<RemovedMember>.Forbidden(OwnerOnlyError);

        var revoked = 0;
        var change = MemberChange.Ok;
        await _transaction.ExecuteAsync(async c =>
        {
            change = await _members.RemoveAsync(orgId, memberUserId, c);
            if (change != MemberChange.Ok) return;
            revoked = await _gateway.RevokeMemberKeysAsync(projectId, memberUserId, privilegedOnly: false, c);
        }, ct);

        return change switch
        {
            MemberChange.NotFound => ModuleOutcome<RemovedMember>.NotFound("Member not found."),
            MemberChange.LastOwner => ModuleOutcome<RemovedMember>.Conflict("The last owner cannot be removed from the team."),
            _ => ModuleOutcome<RemovedMember>.Ok(new RemovedMember(memberUserId, revoked)),
        };
    }
}
