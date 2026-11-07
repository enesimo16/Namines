using Namines.BuildingBlocks.Security;

namespace Namines.Identity.Application;

/// <summary>Diğer modüllere (Ops) açılan platform yöneticisi sorusu; kural <see cref="IAdminUserService"/> ile tek yerde.</summary>
public sealed class PlatformAdminAuthority : IPlatformAdminAuthority
{
    private readonly IAdminUserService _admins;

    public PlatformAdminAuthority(IAdminUserService admins) => _admins = admins;

    public async Task<bool> IsPlatformAdminAsync(string? userId, CancellationToken ct = default)
        => !string.IsNullOrWhiteSpace(userId) && await _admins.ResolveAdminAsync(userId, null) is not null;
}
