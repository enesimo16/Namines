using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Security;

namespace Namines.Studio.Application;

/// <param name="Token">Ham token; yalnızca bu yanıtta görünür, depoda özeti var.</param>
/// <param name="ClosedOldestLinks">Tavan aşıldığı için kapatılan en eski bağlantı sayısı.</param>
public sealed record CreatedShareLink(string Token, int ClosedOldestLinks);

/// <summary>Paylaşım bağlantısının aktörlü yaşam döngüsü: oluşturma ve iptal Admin ve üstüne açık.</summary>
public interface IShareService
{
    Task<ModuleOutcome<CreatedShareLink>> CreateAsync(string actorUserId, string projectId, CancellationToken ct = default);

    Task<ModuleOutcome<int>> RevokeAsync(string actorUserId, string projectId, CancellationToken ct = default);
}

internal sealed class ShareService : IShareService
{
    private readonly ITenantProjectAuthority _authority;
    private readonly StudioApplicationService _shares;

    public ShareService(ITenantProjectAuthority authority, IStudioApplicationService shares)
    {
        _authority = authority;
        _shares = (StudioApplicationService)shares;
    }

    public async Task<ModuleOutcome<CreatedShareLink>> CreateAsync(string actorUserId, string projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId) || !await _authority.CanManageMembersAsync(projectId, actorUserId, ct))
            return ModuleOutcome<CreatedShareLink>.NotFound();

        try
        {
            var (token, closed) = await _shares.CreateShareLinkWithCapAsync(projectId, ct);
            return ModuleOutcome<CreatedShareLink>.Ok(new CreatedShareLink(token, closed));
        }
        catch (KeyNotFoundException)
        {
            return ModuleOutcome<CreatedShareLink>.NotFound();
        }
    }

    public async Task<ModuleOutcome<int>> RevokeAsync(string actorUserId, string projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId) || !await _authority.CanManageMembersAsync(projectId, actorUserId, ct))
            return ModuleOutcome<int>.NotFound();
        return ModuleOutcome<int>.Ok(await _shares.RevokeShareLinksAsync(projectId, ct));
    }
}
