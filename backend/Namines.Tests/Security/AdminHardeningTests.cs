using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Controllers;
using Namines.API.Filters;
using Namines.API.Middleware;
using Namines.API.Services;
using Namines.Core.Interfaces;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Security;

/// <summary>
/// Admin / kill-switch / hata ara yazılımı sertleştirmeleri (B-4, B-5, B-7..B-11, B-18..B-24).
/// </summary>
public class AdminHardeningTests : IAsyncLifetime
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

    private IServiceScopeFactory ScopeFactory(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AuthDbContext>(o => o.UseSqlite(connection));
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    // ── B-4: FeatureGate ────────────────────────────────────────────────────

    private static async Task<(bool nextCalled, IActionResult? result)> RunGateAsync(string key, IFeatureFlagService? flags)
    {
        var services = new ServiceCollection();
        if (flags is not null) services.AddSingleton(flags);
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var executing = new ActionExecutingContext(actionContext, new System.Collections.Generic.List<IFilterMetadata>(),
            new System.Collections.Generic.Dictionary<string, object?>(), controller: new object());

        var nextCalled = false;
        await new FeatureGateAttribute(key).OnActionExecutionAsync(executing, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new System.Collections.Generic.List<IFilterMetadata>(), new object()));
        });
        return (nextCalled, executing.Result);
    }

    [Fact]
    public async Task FeatureGate_DisabledFlag_Returns503_WithNoticeMessage()
    {
        var flags = new FeatureFlagService(ScopeFactory(_connection), NullLogger<FeatureFlagService>.Instance);
        await flags.UpdateFlagAsync("vault", false, "DisabledNotice", "Vault under maintenance");

        var (nextCalled, result) = await RunGateAsync("vault", flags);

        Assert.False(nextCalled);
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, obj.StatusCode);
        Assert.Contains("Vault under maintenance", JsonSerializer.Serialize(obj.Value));
    }

    [Fact]
    public async Task FeatureGate_EnabledOrUnknownFlag_PassesThrough()
    {
        var flags = new FeatureFlagService(ScopeFactory(_connection), NullLogger<FeatureFlagService>.Instance);

        Assert.True((await RunGateAsync("vault", flags)).nextCalled);
        Assert.True((await RunGateAsync("no_such_flag", flags)).nextCalled);
    }

    // ── B-11: FeatureFlagService ────────────────────────────────────────────

    [Fact]
    public async Task FeatureFlagService_UnknownKey_IsRejected()
    {
        var flags = new FeatureFlagService(ScopeFactory(_connection), NullLogger<FeatureFlagService>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() => flags.UpdateFlagAsync("typo_flag", false, "Hidden"));
        Assert.Null(await flags.GetFlagAsync("typo_flag"));
    }

    [Fact]
    public async Task FeatureFlagService_OmittedMode_KeepsExistingMode()
    {
        var flags = new FeatureFlagService(ScopeFactory(_connection), NullLogger<FeatureFlagService>.Instance);
        await flags.UpdateFlagAsync("canvas", false, "Hidden");

        await flags.UpdateFlagAsync("canvas", false, string.Empty, "yeni mesaj");

        var flag = await flags.GetFlagAsync("canvas");
        Assert.Equal("Hidden", flag!.Mode);
        Assert.Equal("yeni mesaj", flag.NoticeMessage);
    }

    [Fact]
    public async Task FeatureFlagService_FailedDbWrite_DoesNotChangeCache()
    {
        // Şeması oluşturulmamış bir DB: hem yükleme hem yazma patlar.
        await using var broken = new SqliteConnection("DataSource=:memory:");
        await broken.OpenAsync();
        var flags = new FeatureFlagService(ScopeFactory(broken), NullLogger<FeatureFlagService>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(() => flags.UpdateFlagAsync("canvas", false, "DisabledNotice"));

        Assert.True(flags.IsFeatureEnabled("canvas", out _, out _));
    }

    [Fact]
    public async Task FeatureFlagService_LoadFailure_RetriesInsteadOfStayingUninitialized()
    {
        // Yükleme başarısız olsa bile servis varsayılanlarla çalışır ve hata fırlatmaz.
        await using var broken = new SqliteConnection("DataSource=:memory:");
        await broken.OpenAsync();
        var flags = new FeatureFlagService(ScopeFactory(broken), NullLogger<FeatureFlagService>.Instance);

        var all = await flags.GetAllFlagsAsync();

        Assert.NotEmpty(all);
    }

    // ── B-5 / B-7 / B-8 / B-20: AdminController ─────────────────────────────

    private async Task<(AdminController controller, UserManager<ApplicationUser> users, AuthDbContext db)> CreateAdminAsync(AdminTelemetryService? telemetry = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AuthDbContext>(o => o.UseSqlite(_connection));
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<AuthDbContext>();
        var sp = services.BuildServiceProvider();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var db = new AuthDbContext(_options);

        var controller = new AdminController(
            users, db, NullLogger<AdminController>.Instance, telemetry ?? new AdminTelemetryService(),
            new ConfigurationBuilder().Build());

        var admin = new ApplicationUser { Id = "adm", UserName = "adm", Email = "adm@example.com", IsDev = true };
        await users.CreateAsync(admin);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, admin.Id),
                    new Claim(ClaimTypes.Email, admin.Email!)
                }, "TestAuth"))
            }
        };
        return (controller, users, db);
    }

    [Fact]
    public async Task UpdateUserPlan_DevPlan_IsRejected_AndNeverSetsIsDev()
    {
        var (controller, users, db) = await CreateAdminAsync();
        using var _ = db;
        var target = new ApplicationUser { Id = "u1", UserName = "u1", Email = "u1@example.com" };
        await users.CreateAsync(target);

        var res = await controller.UpdateUserPlan("u1", new AdminController.UpdatePlanDto { PlanCode = "dev" });

        Assert.IsType<BadRequestObjectResult>(res);
        var reloaded = await users.FindByIdAsync("u1");
        Assert.False(reloaded!.IsDev);
    }

    [Fact]
    public async Task UpdateUserPlan_Pro_WithoutSubscription_ReportsEffectiveFree_AndKeepsIsDev()
    {
        var (controller, users, db) = await CreateAdminAsync();
        using var _ = db;
        var target = new ApplicationUser { Id = "u2", UserName = "u2", Email = "u2@example.com" };
        await users.CreateAsync(target);

        var res = await controller.UpdateUserPlan("u2", new AdminController.UpdatePlanDto { PlanCode = "pro" });

        var ok = Assert.IsType<OkObjectResult>(res);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"planCode\":\"pro\"", json);
        Assert.Contains("\"effectivePlan\":\"free\"", json);
        Assert.Contains("\"isDev\":false", json);
    }

    [Fact]
    public async Task TrackClientEvent_Anonymous_CannotForgeIdentity_AndFieldsAreCapped()
    {
        var telemetry = new AdminTelemetryService();
        var (controller, _, db) = await CreateAdminAsync(telemetry);
        using var _ = db;
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var res = controller.TrackClientEvent(new TelemetryEventItem
        {
            SessionId = "s1",
            UserId = "victim-id",
            UserEmail = "victim@example.com",
            Action = new string('a', 5000),
            Metadata = new string('m', 5000),
            DurationSeconds = int.MaxValue
        });

        Assert.IsType<OkObjectResult>(res);
        var session = telemetry.GetRecentSessions().Single();
        Assert.Null(session.UserId);
        Assert.Equal("Anonim", session.UserEmail);
        Assert.Equal(256, session.RecentActions.Single().Action.Length);
        Assert.Equal(2048, session.RecentActions.Single().Metadata!.Length);
    }

    [Fact]
    public async Task AdminEndpoints_NonAdmin_Get403_NotUnauthorized()
    {
        var (controller, _, db) = await CreateAdminAsync();
        using var _ = db;
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "nobody") }, "TestAuth"))
            }
        };

        var res = await controller.GetAiRoutes();

        Assert.Equal(403, Assert.IsType<ObjectResult>(res).StatusCode);
    }

    [Fact]
    public void Telemetry_NoEvents_ReturnsNoFabricatedSessionsOrModuleClicks()
    {
        var telemetry = new AdminTelemetryService();

        Assert.Empty(telemetry.GetRecentSessions());
        var overview = telemetry.GetAnalyticsOverview();
        Assert.Equal(0, overview.totalEvents);
        Assert.Empty(overview.moduleClicks);
        Assert.Empty(overview.startModes);
        Assert.Empty(telemetry.GetLogs());
    }

    [Fact]
    public void Telemetry_PerIpLimit_RejectsExcess()
    {
        var telemetry = new AdminTelemetryService();
        var accepted = Enumerable.Range(0, 200).Count(_ => telemetry.TryAcceptFrom("1.2.3.4"));

        Assert.Equal(120, accepted);
        Assert.True(telemetry.TryAcceptFrom("5.6.7.8"));
    }

    // ── B-10: gerçek tablo sayısı ───────────────────────────────────────────

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("not json", 0)]
    [InlineData("{\"tables\":[]}", 0)]
    [InlineData("{\"Tables\":[{\"name\":\"a\"},{\"name\":\"b\"}]}", 2)]
    public void CountTables_ReadsRealTablesArray(string? json, int expected)
    {
        Assert.Equal(expected, AdminController.CountTables(json));
    }

    // ── B-18: CSV injection ────────────────────────────────────────────────

    [Theory]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData("+cmd", "\"'+cmd\"")]
    [InlineData("-2+3", "\"'-2+3\"")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
    [InlineData("\tx", "\"'\tx\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("plain", "\"plain\"")]
    public void AdminCsv_Field_EscapesFormulasAndQuotes(string input, string expected)
    {
        Assert.Equal(expected, AdminCsv.Field(input));
    }

    [Fact]
    public void AdminCsv_Row_NumbersAreNotPrefixed_NullsAreEmptyCells()
    {
        Assert.Equal("\"a\",\"-3\",\"\",\"True\"", AdminCsv.Row("a", -3, null, true));
    }

    // ── B-19: ExceptionMiddleware ──────────────────────────────────────────

    private sealed class Env : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Namines.API";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public async Task ExceptionMiddleware_ClientAbort_Is499_NotA500()
    {
        using var cts = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cts.Token };
        context.Response.Body = new MemoryStream();
        cts.Cancel();

        var middleware = new ExceptionMiddleware(
            _ => throw new OperationCanceledException(cts.Token),
            NullLogger<ExceptionMiddleware>.Instance,
            new Env());

        await middleware.InvokeAsync(context);

        Assert.Equal(499, context.Response.StatusCode);
    }

    [Fact]
    public async Task ExceptionMiddleware_KeyNotFound_DoesNotEchoMessageInProduction()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = new ExceptionMiddleware(
            _ => throw new System.Collections.Generic.KeyNotFoundException("secret-internal-key-42"),
            NullLogger<ExceptionMiddleware>.Instance,
            new Env());

        await middleware.InvokeAsync(context);

        Assert.Equal(404, context.Response.StatusCode);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain("secret-internal-key-42", body);
    }
}
