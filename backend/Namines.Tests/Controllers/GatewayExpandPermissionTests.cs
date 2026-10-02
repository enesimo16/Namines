using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Controllers;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.AI;
using Namines.Infrastructure.Data;
using Xunit;

namespace Namines.Tests.Controllers;

/// <summary>
/// <c>POST /api/gateway/list</c> üzerindeki <c>expand</c> (ilişki gömme) izni.
///
/// <b>Bulunan açık (docs/last-review H-2):</b> <c>AuthorizeAsync</c> yalnızca
/// <c>request.TableName</c> için <c>IsTableAllowedAsync</c>'i çağırıyordu.
/// <c>GatewayService.ApplyExpandsAsync</c> ise <c>expand.Table</c>'ın yalnızca
/// SÖZ DİZİMİNİ doğrulayıp (<c>ValidateIdentifierOrThrow</c>) o tablodan
/// <c>SELECT *</c> çekiyordu. Sonuç: <c>orders</c> okuma izni verilmiş bir API
/// anahtarı, izni HİÇ OLMAYAN <c>users</c> tablosunu <c>expand</c> ile satır
/// satır okuyabiliyordu — tablo izin modelinin tamamı ilişkinin diğer ucundan
/// aşılabiliyordu.
///
/// Bu testler bağlantı çözülmeden ÖNCE reddedilen yolu sınıyor, bu yüzden
/// gerçek bir veritabanı gerekmiyor (<c>GatewayQueryAuthorizationTests</c> ile
/// aynı desen: SQLite in-memory + <c>gateway: null!</c>).
/// </summary>
public sealed class GatewayExpandPermissionTests : IAsyncLifetime
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

    private AuthDbContext NewContext() => new(_options);

    /// <summary>
    /// Proje + API anahtarı + verilen tablo izinleri. Ham anahtar döner;
    /// kayıtta yalnızca özeti durur.
    /// </summary>
    private static async Task<string> SeedAsync(AuthDbContext db, params string[] readableTables) =>
        await SeedAsync(db, storeConnection: true, readableTables);

    private static async Task<string> SeedAsync(
        AuthDbContext db, bool storeConnection, params string[] readableTables)
    {
        db.Users.Add(new ApplicationUser { Id = "owner-1", UserName = "owner-1" });
        db.CloudProjects.Add(new CloudProject
        {
            Id = "proj-1", Name = "p", DbType = "PostgreSQL",
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = "owner-1",
            // storeConnection false: izin kapisini GECEN cagri baglantiyi cozmeye
            // gitmesin. Saklanan sifreli dize gercek bir koruyucu ister; bu testin
            // konusu izin, sifre cozme degil.
            EncryptedConnectionString = storeConnection ? "irrelevant-for-this-test" : null,
            ConnectionDbType = "PostgreSQL",
        });

        var (key, raw) = GatewayAccess.CreateKey(
            "proj-1", "test key", "owner-1", canWrite: false, expiresAt: null);
        db.GatewayApiKeys.Add(key);

        foreach (var table in readableTables)
            db.GatewayTablePermissions.Add(new GatewayTablePermission
            {
                ProjectId = "proj-1", TableName = table, CanRead = true, CanWrite = false,
            });

        await db.SaveChangesAsync();
        return raw;
    }

    private static GatewayController NewController(AuthDbContext db, string rawApiKey)
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var groq = new GroqAIService(
            new HttpClient(), new ConfigurationBuilder().Build(), httpContextAccessor,
            new MemoryCache(new MemoryCacheOptions()));
        var quota = new AiQuotaService(db, new ConfigurationBuilder().Build());

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Namines-Key"] = rawApiKey;

        return new GatewayController(
            gateway: null!, context: db, configuration: new ConfigurationBuilder().Build(),
            groq: groq, logger: NullLogger<GatewayController>.Instance, quota: quota,
            protector: null!, introspection: null!, workbench: null!)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private static GatewayListRequest ListWithExpand(string table, params GatewayExpand[] expands) =>
        new(ConnectionString: "", DbType: "", TableName: table, Expand: expands);

    [Fact]
    public async Task Izni_OLMAYAN_tabloyu_expand_ile_okumak_REDDEDILIYOR()
    {
        // Asıl açık: anahtar yalnızca `orders`a okuma izni taşıyor, ama
        // `users`ı gömmeye çalışıyor.
        await using var db = NewContext();
        var rawKey = await SeedAsync(db, readableTables: "orders");

        var controller = NewController(db, rawKey);
        var result = await controller.List(
            ListWithExpand("orders", new GatewayExpand("user_id", "users", "id")),
            default);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
    }

    [Fact]
    public async Task Izni_OLMAYAN_tablo_takma_ad_ARKASINA_saklanamiyor()
    {
        // `As` yalnızca çıktıdaki alan adını değiştiriyor; okunan tablo hâlâ
        // `users`. Kontrol takma ada değil TABLOYA bakmalı.
        await using var db = NewContext();
        var rawKey = await SeedAsync(db, readableTables: "orders");

        var controller = NewController(db, rawKey);
        var result = await controller.List(
            ListWithExpand("orders", new GatewayExpand("user_id", "users", "id", As: "orders")),
            default);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
    }

    [Fact]
    public async Task Birden_fazla_expandda_TEK_izinsiz_olan_bile_reddediyor()
    {
        // İlk expand izinli, ikincisi değil. Döngünün ilk geçerli öğede
        // "tamam" deyip çıkmadığı doğrulanıyor.
        await using var db = NewContext();
        var rawKey = await SeedAsync(db, "orders", "customers");

        var controller = NewController(db, rawKey);
        var result = await controller.List(
            ListWithExpand("orders",
                new GatewayExpand("customer_id", "customers", "id"),
                new GatewayExpand("user_id", "users", "id")),
            default);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
    }

    /// <summary>
    /// İzinli expand bu kapıdan GEÇİYOR — düzeltme meşru kullanımı kırmamalı.
    ///
    /// Projede saklı bağlantı yok, bu yüzden izin kapısını geçen çağrı
    /// "bağlantı yok" diyen <c>BadRequest</c>'e varıyor. Beklenen sonucun 403
    /// OLMAMASI asıl iddia: 403 dönerse düzeltme izinli kullanımı da kapatmış olur.
    /// </summary>
    [Fact]
    public async Task Izinli_expand_izin_kapisini_GECIYOR()
    {
        await using var db = NewContext();
        var rawKey = await SeedAsync(db, storeConnection: false, "orders", "users");

        var controller = NewController(db, rawKey);
        var result = await controller.List(
            ListWithExpand("orders", new GatewayExpand("user_id", "users", "id")),
            default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Bos_expand_tablosu_BadRequest()
    {
        await using var db = NewContext();
        var rawKey = await SeedAsync(db, "orders");

        var controller = NewController(db, rawKey);
        var result = await controller.List(
            ListWithExpand("orders", new GatewayExpand("user_id", "   ", "id")),
            default);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
