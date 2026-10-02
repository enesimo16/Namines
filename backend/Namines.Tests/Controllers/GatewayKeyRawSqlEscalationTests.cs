using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Namines.API.Controllers;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Xunit;

namespace Namines.Tests.Controllers;

/// <summary>
/// <c>POST /api/gateway-keys/{projectId}</c> — anahtara ham SQL / toplu dışa
/// aktarım yetkisi VERMEK.
///
/// <b>Bulunan açık (docs/last-review H-3):</b> bu uç <c>CanManageMembersAsync</c>
/// (Admin|Owner) ile korunuyordu ve <c>CanExecuteSql</c> doğrudan istek gövdesinden
/// kopyalanıyordu — üretenin kendi rolüyle sınırlanmıyordu. Desk SQL konsolunu
/// AÇMAK ise Owner istiyor (<c>SetDeskSqlEnabled</c>). Yani bir Admin kendine
/// <c>CanExecuteSql</c> taşıyan bir anahtar basıp projeye SAKLI üretim bağlantısında
/// keyfi DDL/DML çalıştırabiliyordu: Owner-only eşik anahtar yolundan tamamen
/// aşılıyordu. Klasik yetki yükseltme.
/// </summary>
public sealed class GatewayKeyRawSqlEscalationTests : IAsyncLifetime
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

    /// <summary>Owner + Admin üyesi olan bir org projesi.</summary>
    private static async Task SeedAsync(AuthDbContext db)
    {
        db.Users.Add(new ApplicationUser { Id = "owner-1", UserName = "owner-1" });
        db.Users.Add(new ApplicationUser { Id = "admin-1", UserName = "admin-1" });

        var org = new Organization
        {
            Id = "org-1", Name = "org", IsPersonal = false, CreatedByUserId = "owner-1",
        };
        db.Organizations.Add(org);
        db.OrganizationMembers.Add(new OrganizationMember
        { OrganizationId = org.Id, UserId = "owner-1", Role = OrgRole.Owner });
        db.OrganizationMembers.Add(new OrganizationMember
        { OrganizationId = org.Id, UserId = "admin-1", Role = OrgRole.Admin });

        db.CloudProjects.Add(new CloudProject
        {
            Id = "proj-1", Name = "p", DbType = "PostgreSQL", OrganizationId = org.Id,
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = "owner-1",
        });

        await db.SaveChangesAsync();
    }

    private static GatewayKeyController NewController(AuthDbContext db, string userId) =>
        new(context: db, protector: null!, hostPolicy: null!, introspection: null!)
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

    [Fact]
    public async Task Admin_kendine_ham_SQL_anahtari_BASAMIYOR()
    {
        await using var db = NewContext();
        await SeedAsync(db);

        var controller = NewController(db, "admin-1");
        var result = await controller.Create(
            "proj-1", new CreateGatewayKeyRequest("k", CanExecuteSql: true), default);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);

        // Hiçbir anahtar yazılmamalı: reddedilen bir istek yarım kayıt bırakmamalı.
        await using var verify = NewContext();
        Assert.Empty(await verify.GatewayApiKeys.ToListAsync());
    }

    [Fact]
    public async Task Admin_toplu_disa_aktarim_anahtari_da_BASAMIYOR()
    {
        // Dışa aktarım tek istekle tablonun tamamını indiriyor; aynı gerekçe.
        await using var db = NewContext();
        await SeedAsync(db);

        var controller = NewController(db, "admin-1");
        var result = await controller.Create(
            "proj-1", new CreateGatewayKeyRequest("k", CanExport: true), default);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
    }

    [Fact]
    public async Task Admin_SIRADAN_anahtar_basmaya_DEVAM_EDIYOR()
    {
        // Düzeltme Admin'in normal işini kırmamalı — yalnızca iki yetkiyi
        // Owner'a çekiyor.
        await using var db = NewContext();
        await SeedAsync(db);

        var controller = NewController(db, "admin-1");
        var result = await controller.Create(
            "proj-1", new CreateGatewayKeyRequest("k", CanWrite: true), default);

        Assert.IsNotType<ObjectResult>(result);

        await using var verify = NewContext();
        var key = Assert.Single(await verify.GatewayApiKeys.ToListAsync());
        Assert.True(key.CanWrite);
        Assert.False(key.CanExecuteSql);
        Assert.False(key.CanExport);
    }

    [Fact]
    public async Task Owner_ham_SQL_anahtari_BASABILIYOR()
    {
        await using var db = NewContext();
        await SeedAsync(db);

        var controller = NewController(db, "owner-1");
        var result = await controller.Create(
            "proj-1", new CreateGatewayKeyRequest("k", CanExecuteSql: true, CanExport: true), default);

        Assert.IsNotType<ObjectResult>(result);

        await using var verify = NewContext();
        var key = Assert.Single(await verify.GatewayApiKeys.ToListAsync());
        Assert.True(key.CanExecuteSql);
        Assert.True(key.CanExport);
    }
}
