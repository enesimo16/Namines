using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Namines.API.Controllers;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Xunit;

namespace Namines.Tests.Controllers;

/// <summary>
/// Ekipten ÇIKARILAN bir üyenin elinde kalan erişim yolları — docs/last-review H-4.
///
/// <b>Bulunan açık (a):</b> <c>GatewayAccess.AuthenticateAsync</c> anahtarı ÜRETENİN
/// güncel üyeliğine hiç bakmıyordu ve üyelik kaldırmak hiçbir anahtarı iptal
/// etmiyordu. Ekipten çıkarılan bir Admin'in yazma/ham-SQL anahtarı süresiz
/// çalışmaya devam ediyordu — üstelik istekler hâlâ onun hesabına faturalanıyordu.
///
/// <b>Bulunan açık (b):</b> <c>ShareController</c> create/revoke yetkisi
/// <c>p.UserId == CurrentUserId</c> eşitliğiyle kuruluyordu, org kapsamlı değil.
/// Çıkarılmış kurucu hâlâ paylaşım linki üretebiliyordu (şema ANONİM yayınlanıyor)
/// ve projenin gerçek Owner'ı kurucu değilse linki iptal edemiyordu.
/// </summary>
public sealed class RemovedMemberKeepsAccessTests : IAsyncLifetime
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
    /// Owner (founder) + Admin üyesi olan bir org projesi. Admin'in bir API
    /// anahtarı var. Ham anahtar döner.
    /// </summary>
    private static async Task<string> SeedAsync(AuthDbContext db)
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

        var (key, raw) = GatewayAccess.CreateKey(
            "proj-1", "admin key", "admin-1", canWrite: true, expiresAt: null);
        db.GatewayApiKeys.Add(key);

        await db.SaveChangesAsync();
        return raw;
    }

    private static ProjectMemberController NewMemberController(AuthDbContext db, string userId) =>
        // ProjectId doluyken ResolveOrgIdAsync UserManager'a dokunmuyor.
        new(db, userManager: null!)
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

    // ── (a) anahtarlar ────────────────────────────────────────────────────────

    [Fact]
    public async Task Uyelik_kaldirilinca_anahtar_ARTIK_dogrulanmiyor()
    {
        await using var db = NewContext();
        var rawKey = await SeedAsync(db);

        // Once calisiyor.
        Assert.NotNull(await db.AuthenticateAsync(rawKey));

        // Uyeligi elle kaldir (uc kullanmadan) — asil iddia AuthenticateAsync'in
        // kendisi, cikarma ucunun iptali degil.
        var member = await db.OrganizationMembers.SingleAsync(m => m.UserId == "admin-1");
        db.OrganizationMembers.Remove(member);
        await db.SaveChangesAsync();

        await using var verify = NewContext();
        Assert.Null(await verify.AuthenticateAsync(rawKey));
    }

    [Fact]
    public async Task Uye_cikarma_ucu_o_uyenin_anahtarlarini_ACIKCA_iptal_ediyor()
    {
        // AuthenticateAsync kontrolü tek başına erişimi keser, ama anahtar
        // listesinde "aktif" görünmeye devam ederdi — ve üye geri eklendiğinde
        // kimsenin niyet etmediği bir anda yeniden canlanırdı.
        await using var db = NewContext();
        await SeedAsync(db);

        var controller = NewMemberController(db, "owner-1");
        var result = await controller.Remove("proj-1", "admin-1", default);

        Assert.IsType<OkObjectResult>(result);

        await using var verify = NewContext();
        var key = Assert.Single(await verify.GatewayApiKeys.ToListAsync());
        Assert.NotNull(key.RevokedAt);
    }

    [Fact]
    public async Task Kalan_uyelerin_anahtarlari_ETKILENMIYOR()
    {
        // Düzeltme fazla geniş olmamalı: yalnızca çıkarılan üyenin anahtarları.
        await using var db = NewContext();
        await SeedAsync(db);

        var (ownerKey, _) = GatewayAccess.CreateKey(
            "proj-1", "owner key", "owner-1", canWrite: true, expiresAt: null);
        db.GatewayApiKeys.Add(ownerKey);
        await db.SaveChangesAsync();

        var controller = NewMemberController(db, "owner-1");
        await controller.Remove("proj-1", "admin-1", default);

        await using var verify = NewContext();
        var survivor = await verify.GatewayApiKeys
            .SingleAsync(k => k.CreatedByUserId == "owner-1");
        Assert.Null(survivor.RevokedAt);
    }

    // ── (b) paylaşım linkleri ────────────────────────────────────────────────

    private static ShareController NewShareController(AuthDbContext db, string userId) =>
        new(db, linter: null!, configuration: null!)
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
    public async Task Cikarilmis_kurucu_ARTIK_paylasim_linki_uretemiyor()
    {
        // `UserId` kurucuyu gösteriyor ve DEĞİŞMİYOR; üyelik kaldırılsa bile
        // eski eşitlik kontrolü geçiyordu.
        await using var db = NewContext();
        await SeedAsync(db);

        var founder = await db.OrganizationMembers.SingleAsync(m => m.UserId == "owner-1");
        db.OrganizationMembers.Remove(founder);
        await db.SaveChangesAsync();

        await using var fresh = NewContext();
        var controller = NewShareController(fresh, "owner-1");
        var result = await controller.CreateShareLink("proj-1");

        Assert.IsType<NotFoundResult>(result);

        await using var verify = NewContext();
        var project = await verify.CloudProjects.SingleAsync();
        Assert.Null(project.ShareToken);
    }

    [Fact]
    public async Task Kurucu_OLMAYAN_Owner_paylasim_linkini_IPTAL_EDEBILIYOR()
    {
        // Eski kod bunu imkânsız kılıyordu: yayınlanmış bir şemayı geri çekme
        // yetkisi hiç kimsede yoktu.
        await using var db = NewContext();
        await SeedAsync(db);

        var project = await db.CloudProjects.SingleAsync();
        project.ShareToken = "already-published";

        db.Users.Add(new ApplicationUser { Id = "new-owner", UserName = "new-owner" });
        db.OrganizationMembers.Add(new OrganizationMember
        { OrganizationId = "org-1", UserId = "new-owner", Role = OrgRole.Owner });
        await db.SaveChangesAsync();

        await using var fresh = NewContext();
        var controller = NewShareController(fresh, "new-owner");
        var result = await controller.RevokeShareLink("proj-1");

        Assert.IsType<NoContentResult>(result);

        await using var verify = NewContext();
        Assert.Null((await verify.CloudProjects.SingleAsync()).ShareToken);
    }

    [Fact]
    public async Task Editor_paylasim_linki_URETEMIYOR()
    {
        // Şemayı İNTERNETE açmak bir yönetim işi: Editor şema yazabilir ama
        // onu yayınlamak ayrı bir karar.
        await using var db = NewContext();
        await SeedAsync(db);

        db.Users.Add(new ApplicationUser { Id = "editor-1", UserName = "editor-1" });
        db.OrganizationMembers.Add(new OrganizationMember
        { OrganizationId = "org-1", UserId = "editor-1", Role = OrgRole.Editor });
        await db.SaveChangesAsync();

        await using var fresh = NewContext();
        var controller = NewShareController(fresh, "editor-1");
        var result = await controller.CreateShareLink("proj-1");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Admin_paylasim_linki_uretmeye_DEVAM_EDIYOR()
    {
        // Düzeltme meşru kullanımı kırmamalı.
        await using var db = NewContext();
        await SeedAsync(db);

        var controller = NewShareController(db, "admin-1");
        var result = await controller.CreateShareLink("proj-1");

        Assert.IsType<OkObjectResult>(result);
    }
}
