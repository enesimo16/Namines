using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Controllers;
using Namines.Core.Analysis;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Microsoft.AspNetCore.Identity;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Architecture;

public class AdminAiRoutingAndFeatureFlagsTests : IAsyncLifetime
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

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private AuthDbContext CreateContext() => new(_options);

    private IServiceScopeFactory CreateScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AuthDbContext>(options => options.UseSqlite(_connection));
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IServiceScopeFactory>();
    }

    private sealed class MockModelCatalog : IModelCatalog
    {
        public string UpstreamModel(NaiModel model) => "mock-model";
        public int MaxCompletionTokens(NaiModel model) => 4096;
        public int ClampToModelLimit(string? upstreamModel, int requestedMaxTokens) => requestedMaxTokens;
    }

    private sealed class MockChatProvider : IChatCompletionProvider
    {
        public string Name { get; }
        public string DisplayName => Name;
        public Uri BaseAddress => new Uri("https://api.mock.com/v1/");
        public bool IsConfigured => true;
        public IModelCatalog Models => new MockModelCatalog();
        public void Authenticate(HttpRequestMessage request) { }

        public MockChatProvider(string name) => Name = name;
    }

    [Fact]
    public async Task AiToolRouter_ReturnsDefaultRoutes_WhenNoDatabaseOverridesExist()
    {
        var scopeFactory = CreateScopeFactory();
        var config = new ConfigurationBuilder().Build();
        var providers = new List<IChatCompletionProvider>
        {
            new MockChatProvider("groq"),
            new MockChatProvider("deepseek")
        };

        var router = new AiToolRouter(providers, scopeFactory, NullLogger<AiToolRouter>.Instance, config);

        var schemaRoute = await router.GetRouteAsync("schema_generator");
        Assert.NotNull(schemaRoute);
        Assert.Equal("schema_generator", schemaRoute.ToolKey);
        Assert.Equal("groq", schemaRoute.Provider);
        Assert.Equal("qwen-2.5-coder-32b", schemaRoute.ModelName);
        Assert.Equal(0.2, schemaRoute.Temperature);

        var allRoutes = await router.GetAllRoutesAsync();
        Assert.True(allRoutes.Count >= 6);
        Assert.Contains(allRoutes, r => r.ToolKey == "ai_dba");
        Assert.Contains(allRoutes, r => r.ToolKey == "flow_automation");
        Assert.Contains(allRoutes, r => r.ToolKey == "smart_seed");
    }

    [Fact]
    public async Task AiToolRouter_CanUpdateRoute_AndReflectDynamically()
    {
        var scopeFactory = CreateScopeFactory();
        var config = new ConfigurationBuilder().Build();
        var providers = new List<IChatCompletionProvider>
        {
            new MockChatProvider("groq"),
            new MockChatProvider("deepseek")
        };

        var router = new AiToolRouter(providers, scopeFactory, NullLogger<AiToolRouter>.Instance, config);

        // Update route from groq to deepseek
        await router.UpdateRouteAsync(
            "schema_generator",
            provider: "deepseek",
            modelName: "deepseek-coder",
            temperature: 0.1,
            fallbackProvider: "groq");

        var updated = await router.GetRouteAsync("schema_generator");
        Assert.Equal("deepseek", updated.Provider);
        Assert.Equal("deepseek-coder", updated.ModelName);
        Assert.Equal(0.1, updated.Temperature);
        Assert.Equal("groq", updated.FallbackProvider);

        // Verify persisted to DB using fresh context
        using var verifyCtx = CreateContext();
        var dbEntity = await verifyCtx.AiToolRoutes.FindAsync("schema_generator");
        Assert.NotNull(dbEntity);
        Assert.Equal("deepseek", dbEntity.Provider);
        Assert.Equal("deepseek-coder", dbEntity.ModelName);
    }

    [Fact]
    public void AiToolRouter_Extensibility_NewProviderImplementingIChatCompletionProvider_IsResolved()
    {
        var scopeFactory = CreateScopeFactory();
        var config = new ConfigurationBuilder().Build();

        // Simulate future additions: "claude", "openai"
        var providers = new List<IChatCompletionProvider>
        {
            new MockChatProvider("groq"),
            new MockChatProvider("deepseek"),
            new MockChatProvider("claude"),
            new MockChatProvider("openai")
        };

        var router = new AiToolRouter(providers, scopeFactory, NullLogger<AiToolRouter>.Instance, config);

        var available = router.GetAvailableProviders();
        Assert.Contains("claude", available);
        Assert.Contains("openai", available);

        var claudeProvider = router.GetProvider("claude");
        Assert.NotNull(claudeProvider);
        Assert.Equal("claude", claudeProvider.Name);
    }

    [Fact]
    public async Task FeatureFlagService_ReturnsDefaultFlags_WithBothModesConfigured()
    {
        var scopeFactory = CreateScopeFactory();
        var flagService = new FeatureFlagService(scopeFactory, NullLogger<FeatureFlagService>.Instance);

        var flags = await flagService.GetAllFlagsAsync();
        Assert.NotEmpty(flags);

        var canvasFlag = flags.FirstOrDefault(f => f.FeatureKey == "canvas");
        Assert.NotNull(canvasFlag);
        Assert.True(canvasFlag.IsEnabled);
        Assert.Equal("DisabledNotice", canvasFlag.Mode);
    }

    [Fact]
    public async Task FeatureFlagService_FastInMemoryCheck_ReflectsDisabledNotice_AndHidden()
    {
        var scopeFactory = CreateScopeFactory();
        var flagService = new FeatureFlagService(scopeFactory, NullLogger<FeatureFlagService>.Instance);

        // Initially enabled
        var isEnabled = flagService.IsFeatureEnabled("canvas", out var mode, out var notice);
        Assert.True(isEnabled);

        // Disable with "DisabledNotice"
        await flagService.UpdateFlagAsync(
            "canvas",
            isEnabled: false,
            mode: "DisabledNotice",
            noticeMessage: "Tuval bakımda!");

        isEnabled = flagService.IsFeatureEnabled("canvas", out mode, out notice);
        Assert.False(isEnabled);
        Assert.Equal("DisabledNotice", mode);
        Assert.Equal("Tuval bakımda!", notice);

        // Switch to "Hidden"
        await flagService.UpdateFlagAsync(
            "canvas",
            isEnabled: false,
            mode: "Hidden",
            noticeMessage: null);

        isEnabled = flagService.IsFeatureEnabled("canvas", out mode, out notice);
        Assert.False(isEnabled);
        Assert.Equal("Hidden", mode);

        // Verify database entry
        using var verifyCtx = CreateContext();
        var dbFlag = await verifyCtx.FeatureFlags.FindAsync("canvas");
        Assert.NotNull(dbFlag);
        Assert.False(dbFlag.IsEnabled);
        Assert.Equal("Hidden", dbFlag.Mode);
    }

    [Fact]
    public async Task FeaturesController_PublicEndpoint_ReturnsAllActiveFlags()
    {
        var scopeFactory = CreateScopeFactory();
        var flagService = new FeatureFlagService(scopeFactory, NullLogger<FeatureFlagService>.Instance);

        var controller = new FeaturesController(flagService);
        var result = await controller.GetFlags();

        var okResult = Assert.IsType<OkObjectResult>(result);
        var items = Assert.IsAssignableFrom<System.Collections.IEnumerable>(okResult.Value);
        var count = items.Cast<object>().Count();
        Assert.True(count >= 7);
    }

    [Fact]
    public async Task AdminController_FeatureFlagEndpoints_AuthorizedAdmin_CanGetAndUpdateFlags()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AuthDbContext>(opt => opt.UseSqlite(_connection));
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<AuthDbContext>();
        var sp = services.BuildServiceProvider();

        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var scopeFactory = CreateScopeFactory();
        var flagService = new FeatureFlagService(scopeFactory, NullLogger<FeatureFlagService>.Instance);
        var config = new ConfigurationBuilder().Build();
        var telemetry = new Namines.API.Services.AdminTelemetryService();

        using var db = CreateContext();
        var controller = new AdminController(
            userManager,
            db,
            NullLogger<AdminController>.Instance,
            telemetry,
            config,
            featureFlags: flagService,
            aiRouter: null);

        var testUser = new ApplicationUser
        {
            Id = "admin-user-1",
            UserName = "admin1",
            Email = "admin1@example.com",
            IsDev = true
        };
        await userManager.CreateAsync(testUser);

        var adminUser = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, testUser.Id),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, testUser.Email)
            }, "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = adminUser }
        };

        // Get features
        var getRes = await controller.GetFeatures();
        var okGet = Assert.IsType<OkObjectResult>(getRes);
        var flags = Assert.IsAssignableFrom<IEnumerable<AdminFeatureFlag>>(okGet.Value);
        Assert.NotEmpty(flags);

        // Update feature
        var updateRes = await controller.UpdateFeature("flow", new UpdateFeatureFlagRequest
        {
            IsEnabled = false,
            Mode = "Hidden",
            NoticeMessage = "Flow bakımda"
        });
        var okUpdate = Assert.IsType<OkObjectResult>(updateRes);
        var updatedFlag = Assert.IsType<AdminFeatureFlag>(okUpdate.Value);
        Assert.False(updatedFlag.IsEnabled);
        Assert.Equal("Hidden", updatedFlag.Mode);
    }

    [Fact]
    public async Task AdminController_AiRouteEndpoints_AuthorizedAdmin_CanGetAndUpdateRoutes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AuthDbContext>(opt => opt.UseSqlite(_connection));
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<AuthDbContext>();
        var sp = services.BuildServiceProvider();

        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var scopeFactory = CreateScopeFactory();
        var config = new ConfigurationBuilder().Build();
        var providers = new List<IChatCompletionProvider>
        {
            new MockChatProvider("groq"),
            new MockChatProvider("deepseek")
        };
        var router = new AiToolRouter(providers, scopeFactory, NullLogger<AiToolRouter>.Instance, config);
        var telemetry = new Namines.API.Services.AdminTelemetryService();

        using var db = CreateContext();
        var controller = new AdminController(
            userManager,
            db,
            NullLogger<AdminController>.Instance,
            telemetry,
            config,
            featureFlags: null,
            aiRouter: router);

        var testUser = new ApplicationUser
        {
            Id = "admin-user-2",
            UserName = "admin2",
            Email = "admin2@example.com",
            IsDev = true
        };
        await userManager.CreateAsync(testUser);

        var adminUser = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, testUser.Id),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, testUser.Email)
            }, "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = adminUser }
        };

        // Get AI Routes
        var getRes = await controller.GetAiRoutes();
        var okGet = Assert.IsType<OkObjectResult>(getRes);
        Assert.NotNull(okGet.Value);

        // Update AI Route
        var updateRes = await controller.UpdateAiRoute("ai_dba", new UpdateAiRouteRequest
        {
            Provider = "deepseek",
            ModelName = "deepseek-reasoner",
            Temperature = 0.05,
            FallbackProvider = "groq"
        });
        var okUpdate = Assert.IsType<OkObjectResult>(updateRes);
        var updatedRoute = Assert.IsType<AiToolRoute>(okUpdate.Value);
        Assert.Equal("deepseek", updatedRoute.Provider);
        Assert.Equal("deepseek-reasoner", updatedRoute.ModelName);
        Assert.Equal(0.05, updatedRoute.Temperature);
    }
    [Fact]
    public async Task AdminController_UnauthorizedUser_CannotAccessEndpoints()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AuthDbContext>(opt => opt.UseSqlite(_connection));
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<AuthDbContext>();
        var sp = services.BuildServiceProvider();

        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var scopeFactory = CreateScopeFactory();
        var flagService = new FeatureFlagService(scopeFactory, NullLogger<FeatureFlagService>.Instance);
        var config = new ConfigurationBuilder().Build();
        var telemetry = new Namines.API.Services.AdminTelemetryService();

        var normalUser = new ApplicationUser
        {
            Id = "normal-user",
            UserName = "normal",
            Email = "normal@example.com",
            IsDev = false
        };
        await userManager.CreateAsync(normalUser);

        using var db = CreateContext();
        var controller = new AdminController(
            userManager,
            db,
            NullLogger<AdminController>.Instance,
            telemetry,
            config,
            featureFlags: flagService,
            aiRouter: null);

        var claims = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, normalUser.Id),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, normalUser.Email)
            }, "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = claims }
        };

        var getRes = await controller.GetFeatures();
        Assert.IsType<UnauthorizedResult>(getRes);
    }
}