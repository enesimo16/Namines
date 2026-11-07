using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Models.Auth;
using Namines.Identity.Application;
using Namines.Identity.Infrastructure.Persistence;
using Namines.Tests.TestSupport;
using Xunit;

namespace Namines.Tests.Architecture;

/// <summary>
/// 14.2D-2 denetim M-4: kimlik yönetimi (UserManager/SignInManager/RoleManager) yalnızca Identity modülünde.
/// Denetleyiciler Identity Application servislerini çağırır; HTTP eşlemesinden başka bir şey yapmaz.
/// </summary>
public sealed class Phase14_2D2IdentityBoundaryTests
{
    private static readonly Type[] IdentityManagers =
    {
        typeof(UserManager<>), typeof(SignInManager<>), typeof(RoleManager<>),
    };

    private static bool IsManager(Type t)
    {
        if (t.IsByRef || t.IsPointer) t = t.GetElementType()!;
        if (t.IsGenericType && IdentityManagers.Contains(t.GetGenericTypeDefinition())) return true;
        return t.IsGenericType && t.GetGenericArguments().Any(IsManager);
    }

    [Fact]
    public void No_controller_takes_UserManager_SignInManager_or_RoleManager()
    {
        var controllers = typeof(Namines.API.Controllers.AuthController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract).ToList();
        Assert.True(controllers.Count > 20, "Denetleyici taraması boş kalmış olamaz.");

        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var offenders = new List<string>();
        foreach (var c in controllers)
        {
            offenders.AddRange(c.GetConstructors(all).SelectMany(k => k.GetParameters()).Where(p => IsManager(p.ParameterType)).Select(p => $"{c.Name} ctor {p.Name}"));
            offenders.AddRange(c.GetFields(all).Where(f => IsManager(f.FieldType)).Select(f => $"{c.Name} field {f.Name}"));
            offenders.AddRange(c.GetProperties(all).Where(p => IsManager(p.PropertyType)).Select(p => $"{c.Name} property {p.Name}"));
            // Eylem metotlarının [FromServices] parametreleri de aynı yoldan sızabilir.
            offenders.AddRange(c.GetMethods(all).SelectMany(m => m.GetParameters()).Where(p => IsManager(p.ParameterType)).Select(p => $"{c.Name} method parameter {p.Name}"));
        }
        Assert.Empty(offenders);
    }

    private static UserManager<ApplicationUser> NewUserManager(AuthDbContext db) =>
        new(new UserStore<ApplicationUser>(db),
            optionsAccessor: null!,
            passwordHasher: new PasswordHasher<ApplicationUser>(),
            userValidators: new List<IUserValidator<ApplicationUser>>(),
            passwordValidators: new List<IPasswordValidator<ApplicationUser>>(),
            keyNormalizer: new UpperInvariantLookupNormalizer(),
            errors: new IdentityErrorDescriber(),
            services: null!,
            logger: NullLogger<UserManager<ApplicationUser>>.Instance);

    [Fact]
    public async Task Admin_resolution_is_read_only_for_the_configured_developer_account()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(connection).Options;
        await using (var setup = new AuthDbContext(options)) await setup.EnsureModulesCreatedAsync();

        await using var db = new AuthDbContext(options);
        var users = NewUserManager(db);
        await users.CreateAsync(new ApplicationUser { Id = "dev", UserName = "dev", Email = "dev@example.com", IsDev = false });
        await users.CreateAsync(new ApplicationUser { Id = "plain", UserName = "plain", Email = "plain@example.com" });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Dev:Email"] = "DEV@example.com" }).Build();
        var service = new IdentityAccountService(users, config);

        // Yapılandırılmış hesap yönetici sayılır (e-postadan), ama bir okuma kalıcı değişiklik yapmaz.
        Assert.NotNull(await service.ResolveAdminAsync("dev", null));
        Assert.NotNull(await service.ResolveAdminAsync(null, "dev@example.com"));
        Assert.Null(await service.ResolveAdminAsync("plain", null));
        Assert.Null(await service.ResolveAdminAsync("missing", null));

        await using var verify = new AuthDbContext(options);
        Assert.False((await verify.Users.AsNoTracking().SingleAsync(u => u.Id == "dev")).IsDev);
        Assert.False((await verify.Users.AsNoTracking().SingleAsync(u => u.Id == "plain")).IsDev);

        // Modüller arası soru da aynı kuralı kullanır.
        var authority = new PlatformAdminAuthority(service);
        Assert.True(await authority.IsPlatformAdminAsync("dev"));
        Assert.False(await authority.IsPlatformAdminAsync("plain"));
        Assert.False(await authority.IsPlatformAdminAsync(null));
    }
}
