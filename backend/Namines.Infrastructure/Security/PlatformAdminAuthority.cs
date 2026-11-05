using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Namines.BuildingBlocks.Security;
using Namines.Core.Models.Auth;

namespace Namines.Infrastructure.Security;

/// <summary>
/// Platform yöneticisi kuralının Identity tablolarına bakan uygulaması; <c>AdminController.EnsureAdminAsync</c>
/// ile aynı kural (IsDev, yapılandırılmış geliştirici e-postası, Admin rolü). D-2'de Identity modülüne taşınır.
/// </summary>
public sealed class PlatformAdminAuthority : IPlatformAdminAuthority
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IConfiguration _configuration;

    public PlatformAdminAuthority(UserManager<ApplicationUser> users, IConfiguration configuration)
    {
        _users = users;
        _configuration = configuration;
    }

    public async Task<bool> IsPlatformAdminAsync(string? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return false;

        var user = await _users.FindByIdAsync(userId);
        if (user is null) return false;
        if (user.IsDev) return true;

        var devEmail = _configuration["Dev:Email"];
        if (!string.IsNullOrWhiteSpace(devEmail) && string.Equals(user.Email, devEmail, StringComparison.OrdinalIgnoreCase))
            return true;

        return _users.SupportsUserRole && await _users.IsInRoleAsync(user, "Admin");
    }
}
