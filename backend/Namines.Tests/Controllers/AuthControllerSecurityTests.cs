using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Controllers;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Controllers;

/// <summary>
/// C-1 Güvenlik Testleri:
/// - Ayrılmış (reserved) sistem e-postalarının (namines@namines.com, admin@namines.com, Dev:Email)
///   kamuya açık kayıt (register) uç noktasından kaydedilemeyeceğinin doğrulanması.
/// </summary>
public sealed class AuthControllerSecurityTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<AuthDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(_connection).Options;

        await using var db = new AuthDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private static UserManager<ApplicationUser> NewUserManager(AuthDbContext db)
    {
        var store = new UserStore<ApplicationUser>(db);
        return new UserManager<ApplicationUser>(
            store,
            optionsAccessor: null!,
            passwordHasher: new PasswordHasher<ApplicationUser>(),
            userValidators: new List<IUserValidator<ApplicationUser>>(),
            passwordValidators: new List<IPasswordValidator<ApplicationUser>>(),
            keyNormalizer: new UpperInvariantLookupNormalizer(),
            errors: new IdentityErrorDescriber(),
            services: null!,
            logger: NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private static AuthController NewController(
        AuthDbContext db,
        UserManager<ApplicationUser> userManager,
        IConfiguration? configuration = null)
    {
        var config = configuration ?? new ConfigurationBuilder().Build();
        var controller = new AuthController(
            userManager,
            db,
            config,
            new MigrationService(null!),
            new AutomationJobQueue(),
            NullLogger<AuthController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    [Theory]
    [InlineData("namines@namines.com")]
    [InlineData("admin@namines.com")]
    [InlineData("NAMINES@NAMINES.COM")]
    [InlineData("Admin@Namines.com")]
    public async Task Register_ReservedEmail_ReturnsBadRequest(string reservedEmail)
    {
        await using var db = new AuthDbContext(_options);
        var userManager = NewUserManager(db);
        var controller = NewController(db, userManager);

        var model = new RegisterRequestDto
        {
            Username = "attacker",
            Email = reservedEmail,
            Password = "SuperSecretPassword123!"
        };

        var result = await controller.Register(model);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequest.Value);
        var json = System.Text.Json.JsonSerializer.Serialize(badRequest.Value);
        Assert.Contains("reserved", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_ConfiguredDevEmail_ReturnsBadRequest()
    {
        await using var db = new AuthDbContext(_options);
        var userManager = NewUserManager(db);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dev:Email"] = "dev-operator@internal.corp"
            })
            .Build();

        var controller = NewController(db, userManager, config);

        var model = new RegisterRequestDto
        {
            Username = "devhacker",
            Email = "dev-operator@internal.corp",
            Password = "SuperSecretPassword123!"
        };

        var result = await controller.Register(model);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequest.Value);
        var json = System.Text.Json.JsonSerializer.Serialize(badRequest.Value);
        Assert.Contains("reserved", json, StringComparison.OrdinalIgnoreCase);
    }
}
