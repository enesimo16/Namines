using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Security;

namespace Namines.Tests.TestSupport;

/// <summary>Ops yönetim servisini gerçek yetki kuralıyla kurar: testler kuralı atlamasın.</summary>
public static class TestOps
{
    public static IOpsAdminService Admin(IServiceProvider sp, IFeatureFlagService? flags = null)
    {
        var scopes = sp.GetRequiredService<IServiceScopeFactory>();
        var db = sp.CreateScope().ServiceProvider.GetRequiredService<OpsDbContext>();
        return new OpsAdminService(
            db,
            new PlatformAdminAuthority(sp.GetRequiredService<UserManager<ApplicationUser>>(), new ConfigurationBuilder().Build()),
            flags ?? new FeatureFlagService(scopes, NullLogger<FeatureFlagService>.Instance),
            new AiRouteCatalog(scopes, NullLogger<AiRouteCatalog>.Instance));
    }
}
