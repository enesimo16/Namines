using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Controllers;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Security;

/// <summary>
/// Auth turu sertleştirmeleri: OAuth dev simülasyonu, hesap devralma kapıları,
/// desk-handoff jeton kapsamı, sync'in örtük ekip paylaşımı ve slug'ı, "kurucu"
/// kısayolunun kalkması, raw-SQL anahtarlarının Owner şartı.
/// </summary>
public sealed class AuthHardeningTests : IAsyncLifetime
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

    private sealed class FakeEnv : IWebHostEnvironment
    {
        public FakeEnv(string name) => EnvironmentName = name;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Namines.Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static UserManager<ApplicationUser> NewUserManager(AuthDbContext db) =>
        new(new UserStore<ApplicationUser>(db),
            optionsAccessor: null!,
            passwordHasher: new PasswordHasher<ApplicationUser>(),
            userValidators: new List<IUserValidator<ApplicationUser>>(),
            passwordValidators: new List<IPasswordValidator<ApplicationUser>>(),
            keyNormalizer: new UpperInvariantLookupNormalizer(),
            errors: new IdentityErrorDescriber(),
            services: null!,
            logger: NullLogger<UserManager<ApplicationUser>>.Instance);

    private static AuthController NewAuth(
        AuthDbContext db, string envName, bool simulationFlag, string? userId = null, params Claim[] extraClaims)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "SuperSecretDevJwtKeyThatIsLongEnough12345!",
                ["Auth:AllowOAuthDevSimulation"] = simulationFlag ? "true" : "false",
            })
            .Build();

        var claims = new List<Claim>(extraClaims);
        if (userId is not null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));

        return new AuthController(
            NewUserManager(db), db, config, new MigrationService(null!), new AutomationJobQueue(),
            NullLogger<AuthController>.Instance, new FakeEnv(envName))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
                },
            },
        };
    }

    private static OAuthCallbackRequestDto Sim(string provider = "google") =>
        new() { Provider = provider, IsDevSimulation = true };

    // ── C-1: OAuth dev simülasyonu ───────────────────────────────────────────

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    [InlineData("Development", false)]
    [InlineData("Production", false)]
    public async Task OAuth_dev_simulasyonu_Development_ve_bayrak_yoksa_reddedilir(string env, bool flag)
    {
        await using var db = NewContext();
        var controller = NewAuth(db, env, flag);

        var result = await controller.OAuthCallback(Sim());

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task OAuth_dev_simulasyonu_Development_ve_bayrakla_calisir()
    {
        await using var db = NewContext();
        var controller = NewAuth(db, "Development", simulationFlag: true);

        var result = await controller.OAuthCallback(Sim("github"));

        Assert.IsType<OkObjectResult>(result);
        Assert.Single(await db.Users.ToListAsync(), u => u.Email == "github.dev@namines.app");
    }

    // ── C-3 / B-3: mevcut hesaba e-posta ile bağlanma kapıları ──────────────

    private async Task SeedSimUserAsync(Action<ApplicationUser> configure)
    {
        await using var db = NewContext();
        var user = new ApplicationUser
        {
            UserName = "google-developer", Email = "google.dev@namines.app",
            NormalizedEmail = "GOOGLE.DEV@NAMINES.APP", NormalizedUserName = "GOOGLE-DEVELOPER",
            SecurityStamp = "old-stamp",
        };
        configure(user);
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task OAuth_2FA_acik_hesaba_otomatik_giris_yapmaz()
    {
        await SeedSimUserAsync(u => { u.TwoFactorEnabled = true; u.EmailConfirmed = true; });
        await using var db = NewContext();

        var result = await NewAuth(db, "Development", true).OAuthCallback(Sim());

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, obj.StatusCode);
    }

    [Fact]
    public async Task OAuth_kilitli_hesaba_giris_yapmaz()
    {
        await SeedSimUserAsync(u =>
        {
            u.EmailConfirmed = true;
            u.LockoutEnabled = true;
            u.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1);
        });
        await using var db = NewContext();

        var result = await NewAuth(db, "Development", true).OAuthCallback(Sim());

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task OAuth_dogrulanmamis_hesapta_onceki_parola_ve_damga_sifirlanir()
    {
        await SeedSimUserAsync(u => { u.EmailConfirmed = false; u.PasswordHash = "attacker-hash"; });
        await using var db = NewContext();

        var result = await NewAuth(db, "Development", true).OAuthCallback(Sim());

        Assert.IsType<OkObjectResult>(result);
        await using var verify = NewContext();
        var user = await verify.Users.SingleAsync();
        Assert.True(user.EmailConfirmed);
        Assert.Null(user.PasswordHash);
        Assert.NotEqual("old-stamp", user.SecurityStamp);
    }

    // ── A-9: desk-handoff jetonu kendini yenileyemez ─────────────────────────

    [Fact]
    public async Task Desk_handoff_jetonu_yeni_handoff_jetonu_uretemez_ve_MFA_degistiremez()
    {
        await using var db = NewContext();
        var purpose = new Claim(AuthController.TokenPurposeClaimType, "desk-handoff");
        var controller = NewAuth(db, "Production", false, "someone", purpose);

        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.CreateDeskHandoffToken(default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.RevokeAllSessions()).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.StartMfaSetup()).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.EnableMfa(new MfaCodeRequest("1"))).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.DisableMfa(new MfaCodeRequest("1"))).StatusCode);
    }

    // ── B-6 / C-8: sync ──────────────────────────────────────────────────────

    private async Task SeedTeamMemberAsync(string userId)
    {
        await using var db = NewContext();
        db.Users.Add(new ApplicationUser { Id = userId, UserName = userId, Email = $"{userId}@example.com" });
        db.Users.Add(new ApplicationUser { Id = "team-owner", UserName = "team-owner", Email = "t@example.com" });
        db.Organizations.Add(new Organization
        { Id = "team-org", Name = "team", IsPersonal = false, CreatedByUserId = "team-owner" });
        db.OrganizationMembers.Add(new OrganizationMember
        { OrganizationId = "team-org", UserId = "team-owner", Role = OrgRole.Owner });
        db.OrganizationMembers.Add(new OrganizationMember
        { OrganizationId = "team-org", UserId = userId, Role = OrgRole.Editor });
        await db.SaveChangesAsync();
    }

    private static SyncProjectDto Dto(string id, string name) => new()
    {
        Id = id, Name = name, DbType = "PostgreSQL", SchemaJson = "{\"tables\":[]}", NodePositionsJson = "{}",
    };

    [Fact]
    public async Task Sync_yeni_projeyi_ekip_org_una_degil_kisisel_org_a_koyar()
    {
        await SeedTeamMemberAsync("member-1");
        await using var db = NewContext();
        var controller = NewAuth(db, "Production", false, "member-1");

        Assert.IsType<OkObjectResult>(await controller.SyncProjects(new() { Dto("new-1", "Orders") }));

        await using var verify = NewContext();
        var project = await verify.CloudProjects.SingleAsync();
        var org = await verify.Organizations.SingleAsync(o => o.Id == project.OrganizationId);
        Assert.True(org.IsPersonal);
        Assert.Equal("member-1", org.CreatedByUserId);
    }

    [Fact]
    public async Task Sync_bos_slug_uretmez_ve_ayni_kullanicida_slug_benzersizdir()
    {
        await SeedTeamMemberAsync("member-2");
        await using var db = NewContext();
        var controller = NewAuth(db, "Production", false, "member-2");

        Assert.IsType<OkObjectResult>(await controller.SyncProjects(new()
        {
            Dto("aaaaaa-1", "!!!"), Dto("bbbbbb-2", "Orders"), Dto("cccccc-3", "Orders"), Dto("dddddd-4", "Orders"),
        }));

        await using var verify = NewContext();
        var slugs = await verify.CloudProjects.Select(p => p.Slug).ToListAsync();
        Assert.Equal(4, slugs.Count);
        Assert.All(slugs, s => Assert.False(string.IsNullOrWhiteSpace(s)));
        Assert.Equal(4, slugs.Distinct().Count());
        Assert.Contains("project-aaaaaa", slugs);
        Assert.Contains("orders", slugs);
        Assert.Contains("orders-2", slugs);
    }

    // ── A-4: kurucu kısayolu kalktı ──────────────────────────────────────────

    private async Task SeedFormerFounderAsync()
    {
        await using var db = NewContext();
        db.Users.Add(new ApplicationUser { Id = "founder", UserName = "founder", Email = "f@example.com" });
        db.Users.Add(new ApplicationUser { Id = "team-owner", UserName = "team-owner", Email = "t@example.com" });
        db.Organizations.Add(new Organization
        { Id = "team-org", Name = "team", IsPersonal = false, CreatedByUserId = "team-owner" });
        db.OrganizationMembers.Add(new OrganizationMember
        { OrganizationId = "team-org", UserId = "team-owner", Role = OrgRole.Owner });
        // Kurucu org'da ÜYE DEĞİL.
        db.CloudProjects.Add(new CloudProject
        {
            Id = "shared-1", Name = "Team Project", DbType = "PostgreSQL", OrganizationId = "team-org",
            SchemaJson = "{\"tables\":[]}", NodePositionsJson = "{}", UserId = "founder",
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Cikarilmis_kurucu_sync_ile_ekip_projesini_ezemez_ve_listede_goremez()
    {
        await SeedFormerFounderAsync();
        await using var db = NewContext();
        var controller = NewAuth(db, "Production", false, "founder");

        var dto = Dto("shared-1", "Hijacked");
        Assert.IsType<OkObjectResult>(await controller.SyncProjects(new() { dto }));

        await using var verify = NewContext();
        var original = await verify.CloudProjects.SingleAsync(p => p.Id == "shared-1");
        Assert.Equal("Team Project", original.Name);
        Assert.Equal("team-org", original.OrganizationId);

        var list = Assert.IsType<OkObjectResult>(await controller.GetProjects());
        var ids = ((IEnumerable<object>)list.Value!).Select(o => (string)o.GetType().GetProperty("Id")!.GetValue(o)!);
        Assert.DoesNotContain("shared-1", ids);
    }

    private static TeamController NewTeam(AuthDbContext db, string userId) =>
        new(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Name, userId) }, "TestAuth")),
                },
            },
        };

    [Fact]
    public async Task Cikarilmis_kurucu_projeyi_share_veya_unshare_ile_tasiyamaz()
    {
        await SeedFormerFounderAsync();
        await using var db = NewContext();
        var team = NewTeam(db, "founder");

        Assert.IsType<NotFoundObjectResult>(await team.ShareProject("shared-1", CancellationToken.None));
        Assert.IsType<NotFoundObjectResult>(await team.UnshareProject("shared-1", CancellationToken.None));

        await using var verify = NewContext();
        Assert.Equal("team-org", (await verify.CloudProjects.SingleAsync()).OrganizationId);
    }

    // ── A-8: raw-SQL / export anahtarları Owner ister ────────────────────────

    private async Task<(string RawSql, string RawPlain)> SeedKeysAsync(OrgRole creatorRole)
    {
        await using var db = NewContext();
        db.Users.Add(new ApplicationUser { Id = "creator", UserName = "creator", Email = "c@example.com" });
        db.Users.Add(new ApplicationUser { Id = "boss", UserName = "boss", Email = "b@example.com" });
        db.Organizations.Add(new Organization { Id = "o1", Name = "o", IsPersonal = false, CreatedByUserId = "boss" });
        db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = "o1", UserId = "boss", Role = OrgRole.Owner });
        db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = "o1", UserId = "creator", Role = creatorRole });
        db.CloudProjects.Add(new CloudProject
        {
            Id = "p1", Name = "p", DbType = "PostgreSQL", OrganizationId = "o1",
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = "boss",
        });

        var (sqlKey, rawSql) = GatewayAccess.CreateKey("p1", "sql", "creator", canWrite: false, expiresAt: null);
        sqlKey.CanExecuteSql = true;
        var (plainKey, rawPlain) = GatewayAccess.CreateKey("p1", "plain", "creator", canWrite: true, expiresAt: null);
        db.GatewayApiKeys.AddRange(sqlKey, plainKey);
        await db.SaveChangesAsync();
        return (rawSql, rawPlain);
    }

    [Fact]
    public async Task Raw_sql_anahtari_uretici_Owner_degilse_dogrulanmaz_ama_normal_anahtar_calisir()
    {
        var (rawSql, rawPlain) = await SeedKeysAsync(OrgRole.Admin);
        await using var db = NewContext();

        Assert.Null(await db.AuthenticateAsync(rawSql));
        Assert.NotNull(await db.AuthenticateAsync(rawPlain));
    }

    [Fact]
    public async Task Raw_sql_anahtari_uretici_Owner_ise_dogrulanir()
    {
        var (rawSql, _) = await SeedKeysAsync(OrgRole.Owner);
        await using var db = NewContext();

        Assert.NotNull(await db.AuthenticateAsync(rawSql));
    }

    [Fact]
    public async Task Owner_dusurulunce_raw_sql_anahtarlari_iptal_edilir_diger_anahtarlar_kalir()
    {
        await SeedKeysAsync(OrgRole.Owner);
        await using var db = NewContext();
        var controller = new ProjectMemberController(db, userManager: null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, "boss") }, "TestAuth")),
                },
            },
        };

        Assert.IsType<OkObjectResult>(
            await controller.ChangeRole("p1", "creator", new ChangeMemberRoleRequest(OrgRole.Editor), default));

        await using var verify = NewContext();
        var keys = await verify.GatewayApiKeys.ToListAsync();
        Assert.NotNull(keys.Single(k => k.CanExecuteSql).RevokedAt);
        Assert.Null(keys.Single(k => !k.CanExecuteSql).RevokedAt);
    }
}
