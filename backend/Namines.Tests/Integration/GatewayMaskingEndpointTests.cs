using Namines.Identity.Infrastructure.Persistence;
using Namines.Billing.Application;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Controllers;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Infrastructure.AI;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
using Namines.Tests.TestSupport;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Namines.Tests.Integration;

/// <summary>
/// Maskeleme her okuma ucunda gerçek PostgreSQL ve gerçek veriyle: <c>list</c>, <c>detail</c>,
/// <c>export</c> (csv ve json). Kaynak artık gateway.table_masked_columns; karar kodu aynı kalsa da
/// listenin her uca ulaştığı doğrudan sınanır.
/// </summary>
[Collection("Docker")]
public sealed class GatewayMaskingEndpointTests : IAsyncLifetime
{
    private const string Secret = "ada@secret.example";
    private const string Clear = "Ada";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private DbContextOptions<AuthDbContext> _options = null!;
    private string _rawKey = null!;

    private string Target => _container.GetConnectionString();

    private sealed class AllowAllHosts : IDbHostAccessPolicy
    {
        public bool IsHostAllowed(string? host, out string denyReason)
        {
            denyReason = string.Empty;
            return true;
        }
    }

    public async Task InitializeAsync()
    {
        if (!DockerAvailable.Value) return;
        await _container.StartAsync();
        PostgresTestPools.Reset();

        _options = new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(Target).Options;
        await using var db = new AuthDbContext(_options);
        await db.MigrateAllModulesAsync();

        await using (var conn = new NpgsqlConnection(Target))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"""
                CREATE TABLE musteri (id int PRIMARY KEY, eposta text, ad text);
                INSERT INTO musteri VALUES (1, '{Secret}', '{Clear}');
                CREATE TABLE acik (id int PRIMARY KEY, eposta text);
                INSERT INTO acik VALUES (1, '{Secret}');
                """, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        db.Users.Add(new ApplicationUser { Id = "owner", UserName = "owner", NormalizedUserName = "OWNER" });
        db.Studio().Projects.Add(new CloudProject { Id = "proj", Name = "p", DbType = "PostgreSQL", SchemaJson = "{}", NodePositionsJson = "{}", UserId = "owner" });

        var (key, raw) = GatewayKeys.CreateKey("proj", "k", "owner", canWrite: false, expiresAt: null);
        key.CanExport = true;
        db.Gateway().GatewayApiKeys.Add(key);
        _rawKey = raw;

        db.Gateway().GatewayTablePermissions.Add(new GatewayTablePermission
        {
            ProjectId = "proj", TableName = "musteri", CanRead = true, CanExport = true,
            // Büyük/küçük harf farkı: maskeleme kolon adını harf duyarsız eşler.
            MaskedColumnRules = new List<GatewayTableMaskedColumn> { new() { ColumnName = "EPOSTA", Position = 0 } },
        });
        db.Gateway().GatewayTablePermissions.Add(new GatewayTablePermission
        {
            ProjectId = "proj", TableName = "acik", CanRead = true, CanExport = true,
        });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => DockerAvailable.Value ? _container.DisposeAsync().AsTask() : Task.CompletedTask;

    private GatewayController Controller(AuthDbContext db)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        http.Request.Headers[GatewayController.ApiKeyHeader] = _rawKey;

        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var groq = new GroqAIService(new HttpClient(), new ConfigurationBuilder().Build(), accessor, new MemoryCache(new MemoryCacheOptions()));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = "masking-test-key" })
            .Build();

        return new GatewayController(
            gateway: new GatewayService(new AllowAllHosts()), gate: TestModules.For(db).GatewayGate(configuration), audit: TestModules.For(db).GatewayAudit, usage: TestModules.For(db).Usage, configuration: configuration,
            groq: groq, logger: NullLogger<GatewayController>.Instance, quota: TestModules.For(db).AiQuota(),
            secrets: TestModules.For(db).Authority(new ThrowingProtector()), studio: TestModules.For(db).StudioService, introspection: null!, workbench: null!, authority: TestModules.For(db).DefaultAuthority, projects: TestModules.For(db).Projects, branches: TestModules.For(db).Branches)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private static string Body(IActionResult result)
    {
        if (result is FileContentResult file) return Encoding.UTF8.GetString(file.FileContents);
        var ok = Assert.IsType<OkObjectResult>(result);
        return System.Text.Json.JsonSerializer.Serialize(ok.Value);
    }

    private static void AssertMasked(string body)
    {
        Assert.DoesNotContain(Secret, body);
        Assert.Contains(Clear, body);
    }

    [RequiresDockerFact]
    public async Task List_maskeliyor()
    {
        await using var db = new AuthDbContext(_options);
        AssertMasked(Body(await Controller(db).List(
            new GatewayListRequest(Target, "PostgreSQL", "musteri", OrderByColumn: "id"), default)));
    }

    [RequiresDockerFact]
    public async Task Detail_maskeliyor()
    {
        await using var db = new AuthDbContext(_options);
        AssertMasked(Body(await Controller(db).Detail(
            new GatewayDetailRequest(Target, "PostgreSQL", "musteri", "id", "1"), default)));
    }

    [RequiresDockerFact]
    public async Task Export_csv_maskeliyor()
    {
        await using var db = new AuthDbContext(_options);
        AssertMasked(Body(await Controller(db).Export(
            new GatewayExportRequest(Target, "PostgreSQL", "musteri", Format: "csv"), default)));
    }

    [RequiresDockerFact]
    public async Task Export_json_maskeliyor()
    {
        await using var db = new AuthDbContext(_options);
        AssertMasked(Body(await Controller(db).Export(
            new GatewayExportRequest(Target, "PostgreSQL", "musteri", Format: "json"), default)));
    }

    [RequiresDockerFact]
    public async Task Expand_ile_gomulen_tablo_da_maskeleniyor()
    {
        await using var db = new AuthDbContext(_options);
        var body = Body(await Controller(db).List(
            new GatewayListRequest(Target, "PostgreSQL", "acik", OrderByColumn: "id",
                Select: new[] { "id" },
                Expand: new[] { new GatewayExpand("id", "musteri", "id") }), default));
        Assert.DoesNotContain(Secret, body);
    }

    [RequiresDockerFact]
    public async Task Maskesiz_tabloda_veri_acik_yani_yukaridaki_kontroller_bos_degil()
    {
        // Kontrol: maske yokken gerçek değer yanıtta. Aksi hâlde "içermiyor" iddiası hiçbir şey kanıtlamaz.
        await using var db = new AuthDbContext(_options);
        var controller = Controller(db);
        Assert.Contains(Secret, Body(await controller.List(new GatewayListRequest(Target, "PostgreSQL", "acik", OrderByColumn: "id"), default)));
        Assert.Contains(Secret, Body(await Controller(db).Detail(new GatewayDetailRequest(Target, "PostgreSQL", "acik", "id", "1"), default)));
        Assert.Contains(Secret, Body(await Controller(db).Export(new GatewayExportRequest(Target, "PostgreSQL", "acik", Format: "csv"), default)));
    }
}
