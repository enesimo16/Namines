using System;
using System.Net.Http;
using System.Security.Claims;
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
using Namines.Infrastructure.Data;
using Xunit;

namespace Namines.Tests.Controllers;

/// <summary>
/// <c>POST /api/gateway/query</c> — oturum (JWT) yoluyla PROJEYE KAYITLI
/// bağlantı üzerinde ham SQL çalıştırma yetkisi.
///
/// <b>Neden bu test var:</b> ucun kendi yorumu "oturum yolunda kullanıcının
/// zaten DB erişimi var, çünkü bağlantı dizesini kendisi giriyor" diyordu.
/// Bu, uç yalnızca gövdedeki dizeyi kabul ederken doğruydu. Uç PROJEYE KAYITLI
/// şifreli bağlantıyı da çözecek şekilde genişletildiğinde (list/detail'le
/// aynı yol) öncül sessizce çürüdü: herhangi bir Editor, Desk SQL konsolunun
/// (yalnızca Owner + açık <c>AllowDeskSql</c>) uyguladığı iki kısıtı da
/// atlayıp saklı bağlantı üzerinden keyfi DDL/DML çalıştırabiliyordu.
///
/// SQLite in-memory deseni — AutomationControllerTests ile aynı: EF InMemory
/// sağlayıcısı bu projede referanslı değil, CloudProject → ApplicationUser
/// FK'sı ZORUNLU.
/// </summary>
public sealed class GatewayQueryAuthorizationTests : IAsyncLifetime
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
    /// Owner + org üyeleri (Editor/Viewer) olan bir proje kurar. `GetRoleAsync`
    /// yalnızca `OrganizationId` DOLU projelerde org üyeliğine bakıyor — boşsa
    /// tek başına `UserId` sahibi Owner sayılıyor, başka kimse erişemiyor.
    /// </summary>
    private async Task<CloudProject> SeedProjectAsync(
        AuthDbContext db, string ownerId, bool allowDeskSql,
        (string userId, OrgRole role)[]? members = null, string projectId = "proj-1")
    {
        db.Users.Add(new ApplicationUser { Id = ownerId, UserName = ownerId });
        foreach (var (userId, _) in members ?? Array.Empty<(string, OrgRole)>())
            db.Users.Add(new ApplicationUser { Id = userId, UserName = userId });

        var org = new Organization
        {
            Id = "org-1", Name = "org", IsPersonal = false, CreatedByUserId = ownerId,
        };
        db.Organizations.Add(org);
        db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = org.Id, UserId = ownerId, Role = OrgRole.Owner });
        foreach (var (userId, role) in members ?? Array.Empty<(string, OrgRole)>())
            db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = org.Id, UserId = userId, Role = role });

        var project = new CloudProject
        {
            Id = projectId, Name = "p", DbType = "PostgreSQL", OrganizationId = org.Id,
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = ownerId,
            AllowDeskSql = allowDeskSql,
            EncryptedConnectionString = "irrelevant-for-this-test",
            ConnectionDbType = "PostgreSQL",
        };
        db.CloudProjects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    /// <summary>
    /// Yalnızca test edilen yolun (bağlantı çözülmeden ÖNCEKİ yetki reddi)
    /// hiç dokunmadığı bağımlılıklar için minimal, gerçek nesneler — hepsi
    /// `null!` olsaydı DI değil de gerçek kurucular çağrıldığı için patlardı.
    /// </summary>
    private static GatewayController NewController(AuthDbContext db, string userId)
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var groq = new GroqAIService(
            new HttpClient(), new ConfigurationBuilder().Build(), httpContextAccessor,
            new MemoryCache(new MemoryCacheOptions()));
        var quota = new Namines.Infrastructure.Data.AiQuotaService(db, new ConfigurationBuilder().Build());

        var controller = new GatewayController(
            gateway: null!, context: db, configuration: new ConfigurationBuilder().Build(),
            groq: groq, logger: NullLogger<GatewayController>.Instance, quota: quota,
            protector: null!, introspection: null!, workbench: null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "TestAuth")),
                },
            },
        };
        return controller;
    }

    private static GatewayQueryRequest RawSqlAgainstStoredConnection(string projectId, bool readOnly = false) =>
        new(ConnectionString: "", DbType: "", Sql: "DELETE FROM users", ReadOnly: readOnly, ProjectId: projectId);

    [Fact]
    public async Task Editor_ARTIK_projeye_kayitli_baglanti_uzerinde_ham_SQL_calistiramiyor()
    {
        // Bu, incelemede bulunan asıl açık: Editor rolü CRUD uçlarında yazma
        // yetkisi veriyordu ve o yetki bu ucun saklı-bağlantı yolunu da
        // açıyordu — Desk SQL konsolunun Owner-only eşiğini tamamen atlayarak.
        await using var db = NewContext();
        await SeedProjectAsync(db, ownerId: "owner-1", allowDeskSql: true,
            members: new[] { ("editor-1", OrgRole.Editor) });

        var controller = NewController(db, userId: "editor-1");
        var result = await controller.Query(RawSqlAgainstStoredConnection("proj-1"), default);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
    }

    [Fact]
    public async Task Viewer_projeye_kayitli_baglanti_uzerinde_ham_SQL_calistiramiyor()
    {
        await using var db = NewContext();
        await SeedProjectAsync(db, ownerId: "owner-1", allowDeskSql: true,
            members: new[] { ("viewer-1", OrgRole.Viewer) });

        var controller = NewController(db, userId: "viewer-1");
        // Salt-okunur İDDİASIYLA bile — Viewer bu uca hiç giremiyor.
        var result = await controller.Query(RawSqlAgainstStoredConnection("proj-1", readOnly: true), default);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
    }

    [Fact]
    public async Task AllowDeskSql_kapaliyken_Owner_bile_calistiramiyor()
    {
        await using var db = NewContext();
        await SeedProjectAsync(db, ownerId: "owner-1", allowDeskSql: false);

        var controller = NewController(db, userId: "owner-1");
        var result = await controller.Query(RawSqlAgainstStoredConnection("proj-1"), default);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
    }

    [Fact]
    public async Task Yabanci_kullanici_icin_404_donuyor_projenin_varligi_sizdirilmiyor()
    {
        await using var db = NewContext();
        await SeedProjectAsync(db, ownerId: "owner-1", allowDeskSql: true);

        var controller = NewController(db, userId: "intruder-1");
        var result = await controller.Query(RawSqlAgainstStoredConnection("proj-1"), default);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Kendi_baglanti_dizesini_gonderen_cagri_bu_kapidan_GECMIYOR()
    {
        // Belgelenen öncül burada hâlâ geçerli: kendi dizesini veren bir
        // kullanıcının DB erişimi zaten bu uçtan bağımsız — yeni Owner/
        // AllowDeskSql kapısı yalnızca PROJEYE KAYITLI bağlantıyı koruyor.
        // `AllowDeskSql: false` VE Editor (Owner değil) olmasına rağmen istek
        // 403/404 ile DEĞİL, bağlantı çözümlemesinden SONRAKİ bir hatayla
        // düşüyor (burada `_gateway = null!` olduğu için 500) — yani yeni
        // kapı bu isteği hiç görmedi.
        await using var db = NewContext();
        await SeedProjectAsync(db, ownerId: "owner-1", allowDeskSql: false,
            members: new[] { ("editor-1", OrgRole.Editor) });

        var controller = NewController(db, userId: "editor-1");
        var request = new GatewayQueryRequest(
            ConnectionString: "Host=example.test;Database=x;Username=u;Password=p",
            DbType: "PostgreSQL", Sql: "SELECT 1", ReadOnly: true, ProjectId: "proj-1");

        var result = await controller.Query(request, default);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, status.StatusCode);
    }
}
