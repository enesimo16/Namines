using System.Net;
using System.Net.Http;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Controllers;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.AI;
using Namines.Infrastructure.Data;
using Xunit;

namespace Namines.Tests.Controllers;

/// <summary>
/// <c>GET /api/gateway/tables</c> ve <c>/openapi.json</c> (ve <c>/schema</c>) — API anahtarının
/// bildirdiği kaynak kısıtları.
///
/// <b>Bulunan açık:</b> bu üç uç anahtarı yalnızca KİMLİK olarak doğruluyordu. Diğer tüm
/// anahtar uçları ek olarak <c>AllowedOrigins</c>/<c>AllowedIps</c> ve dakikalık sınırı
/// uyguluyordu. Yani IP'ye kilitlenmiş bir anahtar sızarsa, saldırgan herhangi bir adresten
/// müşterinin canlı üretim şemasını (<c>/schema</c> introspection yapıyor) okuyabiliyordu.
/// </summary>
public sealed class GatewayKeyScopeOnMetadataEndpointsTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<AuthDbContext> _options = null!;
    private string _rawKey = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(_connection).Options;

        await using var db = new AuthDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        db.Users.Add(new ApplicationUser { Id = "owner", UserName = "owner" });
        db.CloudProjects.Add(new CloudProject
        {
            Id = "proj-1", Name = "p", DbType = "PostgreSQL", SchemaJson = "{}", NodePositionsJson = "{}", UserId = "owner",
        });

        var (entity, raw) = GatewayAccess.CreateKey("proj-1", "k", "owner", canWrite: false, expiresAt: null);
        // Yalnızca belirli bir uygulama sunucusundan kullanılabilen bir anahtar.
        entity.AllowedIps = "203.0.113.7";
        entity.AllowedOrigins = "https://app.example.com";
        db.GatewayApiKeys.Add(entity);
        await db.SaveChangesAsync();
        _rawKey = raw;
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private GatewayController NewController(AuthDbContext db, IPAddress remote, string? origin, bool withKey = true)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = remote;
        if (withKey) http.Request.Headers[GatewayController.ApiKeyHeader] = _rawKey;
        if (origin is not null) http.Request.Headers.Origin = origin;

        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var groq = new GroqAIService(new HttpClient(), new ConfigurationBuilder().Build(), accessor,
            new MemoryCache(new MemoryCacheOptions()));
        var quota = new AiQuotaService(db, new ConfigurationBuilder().Build());

        // ForwardedHeaders:KnownNetworks TANIMLI: istemci adresi güvenilir sayılıyor, IP kısıtı
        // gerçekten değerlendirilebiliyor.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/8" })
            .Build();

        return new GatewayController(
            gateway: null!, context: db, configuration: configuration,
            groq: groq, logger: NullLogger<GatewayController>.Instance, quota: quota,
            protector: null!, introspection: null!, workbench: null!)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private static int? Status(IActionResult result) => (result as ObjectResult)?.StatusCode;

    [Fact]
    public async Task Tables_is_refused_from_an_address_the_key_is_not_locked_to()
    {
        await using var db = new AuthDbContext(_options);

        var result = await NewController(db, IPAddress.Parse("198.51.100.99"), "https://app.example.com")
            .Tables(default);

        Assert.Equal(403, Status(result));
    }

    [Fact]
    public async Task OpenApi_is_refused_from_an_address_the_key_is_not_locked_to()
    {
        await using var db = new AuthDbContext(_options);

        var result = await NewController(db, IPAddress.Parse("198.51.100.99"), "https://app.example.com")
            .OpenApi(default);

        Assert.Equal(403, Status(result));
    }

    [Fact]
    public async Task Schema_is_refused_from_an_address_the_key_is_not_locked_to()
    {
        // /schema decrypts the stored connection string and INTROSPECTS the live database:
        // the most sensitive of the three.
        await using var db = new AuthDbContext(_options);

        var result = await NewController(db, IPAddress.Parse("198.51.100.99"), "https://app.example.com")
            .Schema(projectId: null, default);

        Assert.Equal(403, Status(result));
    }

    [Fact]
    public async Task Tables_is_refused_from_a_foreign_origin_even_at_the_right_address()
    {
        await using var db = new AuthDbContext(_options);

        var result = await NewController(db, IPAddress.Parse("203.0.113.7"), "https://evil.example.net")
            .Tables(default);

        Assert.Equal(403, Status(result));
    }

    [Fact]
    public async Task Tables_still_works_from_the_allowed_address_and_origin()
    {
        await using var db = new AuthDbContext(_options);

        var result = await NewController(db, IPAddress.Parse("203.0.113.7"), "https://app.example.com")
            .Tables(default);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Tables_without_any_key_is_still_unauthorized()
    {
        await using var db = new AuthDbContext(_options);

        var result = await NewController(db, IPAddress.Parse("203.0.113.7"), origin: null, withKey: false)
            .Tables(default);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }
}
