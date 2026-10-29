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

public sealed class OAuthTests : IAsyncLifetime
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

    private sealed class FakeEnv(string name) : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Namines.Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static AuthController NewController(AuthDbContext db, IConfiguration? config = null, string environment = "Development")
    {
        config ??= new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "SuperSecretDevJwtKeyThatIsLongEnough12345!",
                ["Jwt:Issuer"] = "NaminesServer",
                ["Jwt:Audience"] = "NaminesClient",
                ["Auth:AllowOAuthDevSimulation"] = "true"
            })
            .Build();

        var controller = new AuthController(
            NewUserManager(db),
            db,
            config,
            new MigrationService(null!),
            new AutomationJobQueue(),
            NullLogger<AuthController>.Instance,
            new FakeEnv(environment))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        return controller;
    }

    [Fact]
    public void GetOAuthProviders_ReturnsConfiguredStatus()
    {
        using var db = new AuthDbContext(_options);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Google:ClientId"] = "google-id-123",
                ["Authentication:GitHub:ClientId"] = "github-id-456"
            })
            .Build();

        var controller = NewController(db, config);
        var result = controller.GetOAuthProviders() as OkObjectResult;

        Assert.NotNull(result);
        dynamic val = result.Value!;
        Assert.True(val.google.enabled);
        Assert.Equal("google-id-123", val.google.clientId);
        Assert.True(val.github.enabled);
        Assert.Equal("github-id-456", val.github.clientId);
    }

    [Fact]
    public async Task OAuthCallback_WithNullOrInvalidProvider_ReturnsBadRequest()
    {
        using var db = new AuthDbContext(_options);
        var controller = NewController(db);

        var nullResult = await controller.OAuthCallback(null!) as BadRequestObjectResult;
        Assert.NotNull(nullResult);

        var unknownResult = await controller.OAuthCallback(new OAuthCallbackRequestDto
        {
            Provider = "facebook",
            IsDevSimulation = true
        }) as BadRequestObjectResult;
        Assert.NotNull(unknownResult);
    }

    [Fact]
    public async Task OAuthCallback_DevSimulation_IsRejectedOutsideDevelopment()
    {
        using var db = new AuthDbContext(_options);
        // Bayrak açık olsa bile Production'da simülasyon reddedilmeli.
        var controller = NewController(db, environment: "Production");

        var result = await controller.OAuthCallback(new OAuthCallbackRequestDto
        {
            Provider = "google",
            IsDevSimulation = true
        });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task OAuthCallback_DevSimulation_CreatesUserAndReturnsToken()
    {
        using var db = new AuthDbContext(_options);
        var controller = NewController(db);

        var result = await controller.OAuthCallback(new OAuthCallbackRequestDto
        {
            Provider = "google",
            IsDevSimulation = true
        }) as OkObjectResult;

        Assert.NotNull(result);
        dynamic val = result.Value!;
        Assert.NotNull(val.Token);
        Assert.Equal("google.dev@namines.app", val.User.Email);

        // Verify policy and quota created in DB
        var userInDb = await db.Users.FirstOrDefaultAsync(u => u.Email == "google.dev@namines.app");
        Assert.NotNull(userInDb);

        var policy = await db.UserAIPolicies.FirstOrDefaultAsync(p => p.UserId == userInDb.Id);
        Assert.NotNull(policy);

        var quota = await db.UserAIQuotas.FirstOrDefaultAsync(q => q.UserId == userInDb.Id);
        Assert.NotNull(quota);
        Assert.Equal(100, quota.DailyLimit);

        // Second callback should log in the same user without duplicate error
        var secondResult = await controller.OAuthCallback(new OAuthCallbackRequestDto
        {
            Provider = "google",
            IsDevSimulation = true
        }) as OkObjectResult;

        Assert.NotNull(secondResult);
        dynamic secondVal = secondResult.Value!;
        Assert.Equal("google.dev@namines.app", secondVal.User.Email);
    }
}
