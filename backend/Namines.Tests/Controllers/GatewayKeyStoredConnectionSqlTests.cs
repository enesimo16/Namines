using System.Threading.Tasks;
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
using Namines.Core.Security;
using Namines.Infrastructure.Data;
using Xunit;

namespace Namines.Tests.Controllers;

/// <summary>
/// <c>POST /api/gateway/query</c> — API ANAHTARI yoluyla, projeye SAKLI bağlantı
/// üzerinde ham SQL.
///
/// <b>Bulunan açık (docs/last-review H-3, ikinci yarı):</b>
/// <c>CloudProject.AllowDeskSql</c> yalnızca oturum yolunda okunuyordu
/// (<c>AuthorizeSessionRawSqlAsync</c>, <c>key is null</c> dalı). Anahtar yolunda
/// hiç okunmuyordu: Owner projenin ham-SQL anahtarını KAPATIYOR, ama
/// <c>CanExecuteSql</c> taşıyan bir API anahtarı aynı saklanmış bağlantıda ham SQL
/// çalıştırmaya devam ediyordu. Yani proje seviyesindeki kapatma anahtarı, anahtar
/// yolundan anlamsız hâle geliyordu.
/// </summary>
public sealed class GatewayKeyStoredConnectionSqlTests : IAsyncLifetime
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

    private static async Task<string> SeedAsync(AuthDbContext db, bool allowDeskSql)
    {
        db.Users.Add(new ApplicationUser { Id = "owner-1", UserName = "owner-1" });
        db.CloudProjects.Add(new CloudProject
        {
            Id = "proj-1", Name = "p", DbType = "PostgreSQL",
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = "owner-1",
            AllowDeskSql = allowDeskSql,
            // Saklı bağlantı VAR: testin konusu tam olarak bu bağlantının
            // anahtar yolundan kullanılabilmesi.
            EncryptedConnectionString = "irrelevant-for-this-test",
            ConnectionDbType = "PostgreSQL",
        });

        var (key, raw) = GatewayAccess.CreateKey(
            "proj-1", "sql key", "owner-1", canWrite: true, expiresAt: null);
        key.CanExecuteSql = true;
        db.GatewayApiKeys.Add(key);

        await db.SaveChangesAsync();
        return raw;
    }

    /// <summary>
    /// Düz metni olduğu gibi taşıyan sahte koruyucu. Gerekli, çünkü izin kapısını
    /// GEÇEN çağrı saklanan bağlantıyı çözmeye gidiyor.
    /// </summary>
    private sealed class PassthroughProtector : IConnectionSecretProtector
    {
        public string Protect(string plaintext) => "enc:" + plaintext;
        public string Unprotect(string ciphertext) => ciphertext.Replace("enc:", "");
    }

    private static GatewayController NewController(AuthDbContext db, string rawApiKey)
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var groq = new GroqAIService(
            new System.Net.Http.HttpClient(), new ConfigurationBuilder().Build(),
            httpContextAccessor, new MemoryCache(new MemoryCacheOptions()));
        var quota = new AiQuotaService(db, new ConfigurationBuilder().Build());

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Namines-Key"] = rawApiKey;

        return new GatewayController(
            gateway: null!, context: db, configuration: new ConfigurationBuilder().Build(),
            groq: groq, logger: NullLogger<GatewayController>.Instance, quota: quota,
            protector: new PassthroughProtector(), introspection: null!, workbench: null!)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    /// <summary>Gövdede bağlantı dizesi YOK — yani saklanan bağlantı çözülecek.</summary>
    private static GatewayQueryRequest StoredConnectionSql() =>
        new(ConnectionString: "", DbType: "", Sql: "DROP TABLE users", ReadOnly: false, ProjectId: "proj-1");

    [Fact]
    public async Task AllowDeskSql_KAPALIYKEN_anahtar_sakli_baglantida_ham_SQL_calistiramiyor()
    {
        await using var db = NewContext();
        var rawKey = await SeedAsync(db, allowDeskSql: false);

        var controller = NewController(db, rawKey);
        var result = await controller.Query(StoredConnectionSql(), default);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
    }

    /// <summary>
    /// Çağıran KENDİ bağlantı dizesini gönderdiğinde bu kapı çalışmıyor: o zaten
    /// kendi veritabanına erişebiliyor ve Namines ona ekstra bir şey vermiyor.
    /// Beklenen sonuç 403 OLMAMASI (gerçek bağlantı olmadığı için akış ileride
    /// başka bir yerde duruyor).
    /// </summary>
    [Fact]
    public async Task Cagiran_KENDI_baglantisini_verdiyse_AllowDeskSql_kapisi_calismiyor()
    {
        await using var db = NewContext();
        var rawKey = await SeedAsync(db, allowDeskSql: false);

        var controller = NewController(db, rawKey);
        var result = await controller.Query(
            new GatewayQueryRequest(
                ConnectionString: "Host=localhost;Database=mine;Username=u;Password=p",
                DbType: "PostgreSQL", Sql: "SELECT 1", ReadOnly: true, ProjectId: null),
            default);

        var status = result as ObjectResult;
        Assert.NotEqual(403, status?.StatusCode);
    }

    [Fact]
    public async Task AllowDeskSql_ACIKKEN_anahtar_yolu_403_ALMIYOR()
    {
        // Düzeltme meşru kullanımı kırmamalı.
        await using var db = NewContext();
        var rawKey = await SeedAsync(db, allowDeskSql: true);

        var controller = NewController(db, rawKey);
        var result = await controller.Query(StoredConnectionSql(), default);

        var status = result as ObjectResult;
        Assert.NotEqual(403, status?.StatusCode);
    }
}
