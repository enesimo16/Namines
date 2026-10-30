using System.Security.Claims;
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
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
using Xunit;
using static Namines.Tests.Services.ProjectDeletionTestKit;

namespace Namines.Tests.Services;

/// <summary>
/// Hesap silme (KVKK/GDPR, DB-08): ekip projeleri CASCADE ile gitmemeli, kişisel veriler temizlenmeli,
/// yeniden doğrulama zorunlu olmalı.
/// </summary>
public sealed class AccountDeletionTests : IAsyncLifetime
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

    private sealed class FakeCanceller(bool result) : ISubscriptionCanceller
    {
        public List<string> Canceled { get; } = new();
        public Task<bool> CancelAsync(string subscriptionId, CancellationToken ct)
        {
            Canceled.Add(subscriptionId);
            return Task.FromResult(result);
        }
    }

    private static AccountDeletionService NewService(
        AuthDbContext db, FakeStore store, FakeProvider provider, ISubscriptionCanceller? canceller = null) =>
        new(db, NewUserManager(db), Create(db, store, provider), NullLogger<AccountDeletionService>.Instance, canceller);

    private static CloudProject Project(string id, string userId, string? orgId) => new()
    {
        Id = id, Name = "proj " + id, DbType = "PostgreSQL", SchemaJson = "{}", NodePositionsJson = "{}",
        UserId = userId, OrganizationId = orgId,
    };

    private static void Member(AuthDbContext db, string orgId, string userId, OrgRole role, int minutesAgo = 0) =>
        db.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = orgId, UserId = userId, Role = role, JoinedAt = DateTime.UtcNow.AddMinutes(-minutesAgo),
        });

    /// <summary>u1 = silinecek kullanıcı. P = kişisel org (yalnız u1), T = ekip org (u2 Owner, u1 Editor).</summary>
    private static async Task SeedAsync(AuthDbContext db, FakeStore store)
    {
        db.Users.AddRange(
            new ApplicationUser { Id = "u1", UserName = "alice", Email = "alice@example.com", StripeSubscriptionId = "sub_123" },
            new ApplicationUser { Id = "u2", UserName = "bob", Email = "bob@example.com" });
        db.Organizations.AddRange(
            new Organization { Id = "P", Name = "Alice personal", IsPersonal = true, CreatedByUserId = "u1" },
            new Organization { Id = "T", Name = "Team", CreatedByUserId = "u2" });
        await db.SaveChangesAsync();

        Member(db, "P", "u1", OrgRole.Owner);
        Member(db, "T", "u2", OrgRole.Owner, 60);
        Member(db, "T", "u1", OrgRole.Editor, 30);
        db.CloudProjects.AddRange(Project("pp", "u1", "P"), Project("tp", "u1", "T"));
        await db.SaveChangesAsync();

        store.Blobs["pp/b0"] = new byte[] { 1 };
        db.VaultBackups.Add(new VaultBackup
        {
            ProjectId = "pp", OrganizationId = "P", CreatedByUserId = "u1", DatabaseName = "d", Engine = "PostgreSQL",
            StorageKey = "pp/b0", StoreDescription = "fake", Status = VaultBackupStatus.Succeeded,
        });
        db.GroundDatabases.Add(new GroundDatabase
        {
            ProjectId = "pp", OrganizationId = "P", Provider = "FakeRemote", CreatedByUserId = "u1",
            Status = GroundStatus.Active, ProviderProjectId = "remote-pp",
        });
        db.GatewayApiKeys.Add(new GatewayApiKey
        {
            ProjectId = "tp", Name = "k", Prefix = "nk_", KeyHash = "h", CreatedByUserId = "u1",
        });
        db.Feedbacks.Add(new Feedback { UserId = "u1", Email = "alice@example.com", Message = "great" });
        db.SqlQueryHistory.Add(new SqlQueryHistoryEntry
        {
            UserId = "u1", ProjectId = "tp", Sql = "select secret from t",
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Tek_Owner_olup_baska_uyesi_olan_org_varsa_409_ve_org_listesi_hicbir_sey_silinmez()
    {
        var store = new FakeStore();
        await using var db = NewContext();
        await SeedAsync(db, store);
        // u1, T'de tek Owner olsun: u2'yi Editor'a indir.
        await db.OrganizationMembers.Where(m => m.OrganizationId == "T" && m.UserId == "u2")
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Role, OrgRole.Editor));
        await db.OrganizationMembers.Where(m => m.OrganizationId == "T" && m.UserId == "u1")
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Role, OrgRole.Owner));

        db.ChangeTracker.Clear();
        var user = await db.Users.SingleAsync(u => u.Id == "u1");
        var result = await NewService(db, store, new FakeProvider()).DeleteAsync(user, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("TRANSFER_OWNERSHIP_REQUIRED", result.Code);
        var blocking = Assert.Single(result.Organizations!);
        Assert.Equal("T", blocking.Id);

        await using var verify = NewContext();
        Assert.True(await verify.Users.AnyAsync(u => u.Id == "u1"));
        Assert.Equal(2, await verify.CloudProjects.CountAsync());
        Assert.Single(store.Blobs);
    }

    [Fact]
    public async Task Kisisel_org_projeleri_ProjectDeletionService_ile_silinir_blob_ve_saglayici_dahil()
    {
        var store = new FakeStore();
        var provider = new FakeProvider();
        await using var db = NewContext();
        await SeedAsync(db, store);

        var result = await NewService(db, store, provider)
            .DeleteAsync(await db.Users.SingleAsync(u => u.Id == "u1"), CancellationToken.None);

        Assert.True(result.Ok, result.Code + " " + result.Error);
        Assert.Empty(store.Blobs);
        Assert.Equal(new[] { "remote-pp" }, provider.Deleted);

        await using var verify = NewContext();
        Assert.False(await verify.CloudProjects.AnyAsync(p => p.Id == "pp"));
        Assert.False(await verify.Organizations.AnyAsync(o => o.Id == "P"));
        Assert.False(await verify.VaultBackups.AnyAsync());
        Assert.False(await verify.GroundDatabases.AnyAsync());
        Assert.False(await verify.Users.AnyAsync(u => u.Id == "u1"));
    }

    [Fact]
    public async Task Ekip_projesi_kullanici_silinmeden_Owner_a_devredilir_CASCADE_silemez()
    {
        var store = new FakeStore();
        await using var db = NewContext();
        await SeedAsync(db, store);

        var result = await NewService(db, store, new FakeProvider())
            .DeleteAsync(await db.Users.SingleAsync(u => u.Id == "u1"), CancellationToken.None);

        Assert.True(result.Ok, result.Code + " " + result.Error);
        await using var verify = NewContext();
        var team = await verify.CloudProjects.SingleAsync(p => p.Id == "tp");
        Assert.Equal("u2", team.UserId);
        Assert.False(await verify.OrganizationMembers.AnyAsync(m => m.UserId == "u1"));
        Assert.True(await verify.Organizations.AnyAsync(o => o.Id == "T"));
    }

    [Fact]
    public async Task Uyeligi_dusmus_kurucunun_ekip_projesi_de_devredilir()
    {
        var store = new FakeStore();
        await using var db = NewContext();
        await SeedAsync(db, store);
        // u1 artık T'nin üyesi değil ama projeyi o oluşturmuş.
        await db.OrganizationMembers.Where(m => m.OrganizationId == "T" && m.UserId == "u1").ExecuteDeleteAsync();

        var result = await NewService(db, store, new FakeProvider())
            .DeleteAsync(await db.Users.SingleAsync(u => u.Id == "u1"), CancellationToken.None);

        Assert.True(result.Ok, result.Code + " " + result.Error);
        await using var verify = NewContext();
        Assert.Equal("u2", (await verify.CloudProjects.SingleAsync(p => p.Id == "tp")).UserId);
    }

    [Fact]
    public async Task Stripe_aboneligi_iptal_edilir()
    {
        var store = new FakeStore();
        var canceller = new FakeCanceller(true);
        await using var db = NewContext();
        await SeedAsync(db, store);

        var result = await NewService(db, store, new FakeProvider(), canceller)
            .DeleteAsync(await db.Users.SingleAsync(u => u.Id == "u1"), CancellationToken.None);

        Assert.True(result.Ok, result.Code + " " + result.Error);
        Assert.Equal(new[] { "sub_123" }, canceller.Canceled);
        await using var verify = NewContext();
        var audit = await verify.ChangeRequestAuditLogs.SingleAsync(a => a.Action == ChangeRequestAuditAction.AccountDeleted);
        Assert.Contains("\"stripe\":\"canceled\"", audit.Details);
    }

    [Fact]
    public async Task Stripe_iptal_edilemezse_silme_bloklanmaz_denetim_kaydinda_elle_iptal_yazar()
    {
        var store = new FakeStore();
        await using var db = NewContext();
        await SeedAsync(db, store);

        var result = await NewService(db, store, new FakeProvider(), canceller: null)
            .DeleteAsync(await db.Users.SingleAsync(u => u.Id == "u1"), CancellationToken.None);

        Assert.True(result.Ok, result.Code + " " + result.Error);
        await using var verify = NewContext();
        var audit = await verify.ChangeRequestAuditLogs.SingleAsync(a => a.Action == ChangeRequestAuditAction.AccountDeleted);
        Assert.Contains("manual_cancellation_required", audit.Details);
    }

    [Fact]
    public async Task Api_anahtarlari_iptal_PII_anonimlesir_SQL_gecmisi_gider_denetim_kaydi_PII_icermez()
    {
        var store = new FakeStore();
        await using var db = NewContext();
        await SeedAsync(db, store);

        var result = await NewService(db, store, new FakeProvider())
            .DeleteAsync(await db.Users.SingleAsync(u => u.Id == "u1"), CancellationToken.None);

        Assert.True(result.Ok, result.Code + " " + result.Error);
        await using var verify = NewContext();

        var key = await verify.GatewayApiKeys.SingleAsync();
        Assert.NotNull(key.RevokedAt);
        Assert.Null(key.CreatedByUserId);

        var feedback = await verify.Feedbacks.SingleAsync();
        Assert.Null(feedback.Email);
        Assert.Null(feedback.UserId);
        Assert.Equal("great", feedback.Message);

        Assert.False(await verify.SqlQueryHistory.AnyAsync());

        var audit = await verify.ChangeRequestAuditLogs.SingleAsync(a => a.Action == ChangeRequestAuditAction.AccountDeleted);
        Assert.Equal("u1", audit.ChangeRequestId);
        Assert.Null(audit.ActorUserId);
        Assert.Null(audit.ChangeRequestTitle);
        foreach (var pii in new[] { "alice", "alice@example.com", "Alice personal", "proj pp" })
            Assert.DoesNotContain(pii, audit.Details ?? string.Empty);

        // Kişisel projenin silme denetim satırı da ad/aktör taşımıyor.
        var projectAudit = await verify.ChangeRequestAuditLogs
            .Where(a => a.ProjectId == "pp" && a.Action == ChangeRequestAuditAction.ProjectDeleted).SingleAsync();
        Assert.Null(projectAudit.ActorUserId);
        Assert.Null(projectAudit.ChangeRequestTitle);
    }

    [Fact]
    public async Task Saglayici_basarisizsa_hesap_silinmez()
    {
        var store = new FakeStore();
        await using var db = NewContext();
        await SeedAsync(db, store);

        var result = await NewService(db, store, new FakeProvider { FailDelete = true })
            .DeleteAsync(await db.Users.SingleAsync(u => u.Id == "u1"), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(503, result.StatusCode);
        await using var verify = NewContext();
        Assert.True(await verify.Users.AnyAsync(u => u.Id == "u1"));
        Assert.True(await verify.CloudProjects.AnyAsync(p => p.Id == "pp"));
    }

    // ── Uç nokta: yeniden doğrulama ──────────────────────────────────────────

    private sealed class FakeEnv : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Namines.Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private AuthController NewAuth(AuthDbContext db, params Claim[] claims)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "SuperSecretDevJwtKeyThatIsLongEnough12345!",
        }).Build();
        var all = new List<Claim>(claims) { new(ClaimTypes.NameIdentifier, "u1") };
        var um = NewUserManager(db);
        return new AuthController(um, db, config, new MigrationService(null!), new AutomationJobQueue(),
            NullLogger<AuthController>.Instance, new FakeEnv(), null,
            new AccountDeletionService(db, um, Create(db), NullLogger<AccountDeletionService>.Instance))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(all, "TestAuth")) },
            },
        };
    }

    private static async Task<AuthDbContext> SeedWithPasswordAsync(DbContextOptions<AuthDbContext> options, bool withPassword)
    {
        var db = new AuthDbContext(options);
        await SeedAsync(db, new FakeStore());
        if (withPassword)
        {
            var user = await db.Users.SingleAsync(u => u.Id == "u1");
            user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, "Correct-Horse-9");
            await db.SaveChangesAsync();
        }
        return db;
    }

    [Fact]
    public async Task Yanlis_parola_403_hesap_yerinde()
    {
        await using var db = await SeedWithPasswordAsync(_options, withPassword: true);
        var result = await NewAuth(db).DeleteAccount(
            new DeleteAccountRequest("wrong", null, "alice@example.com"), CancellationToken.None);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
        await using var verify = NewContext();
        Assert.True(await verify.Users.AnyAsync(u => u.Id == "u1"));
    }

    [Fact]
    public async Task Onay_metni_uyusmazsa_400()
    {
        await using var db = await SeedWithPasswordAsync(_options, withPassword: true);
        var result = await NewAuth(db).DeleteAccount(
            new DeleteAccountRequest("Correct-Horse-9", null, "someone@else.com"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Dogru_parola_ve_onay_ile_hesap_silinir()
    {
        await using var db = await SeedWithPasswordAsync(_options, withPassword: true);
        var result = await NewAuth(db).DeleteAccount(
            new DeleteAccountRequest("Correct-Horse-9", null, "ALICE@example.com"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        await using var verify = NewContext();
        Assert.False(await verify.Users.AnyAsync(u => u.Id == "u1"));
    }

    [Fact]
    public async Task Desk_devir_teslim_jetonu_hesap_silemez()
    {
        await using var db = await SeedWithPasswordAsync(_options, withPassword: true);
        var result = await NewAuth(db, new Claim(AuthController.TokenPurposeClaimType, "desk-handoff"))
            .DeleteAccount(new DeleteAccountRequest("Correct-Horse-9", null, "alice@example.com"), CancellationToken.None);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
        await using var verify = NewContext();
        Assert.True(await verify.Users.AnyAsync(u => u.Id == "u1"));
    }

    [Fact]
    public async Task Parolasiz_OAuth_hesabi_eski_jetonla_silemez_taze_jetonla_siler()
    {
        await using var db = await SeedWithPasswordAsync(_options, withPassword: false);

        var stale = DateTimeOffset.UtcNow.AddHours(-3).ToUnixTimeSeconds().ToString();
        var denied = await NewAuth(db, new Claim("iat", stale))
            .DeleteAccount(new DeleteAccountRequest(null, null, "alice@example.com"), CancellationToken.None);
        var deniedObj = Assert.IsType<ObjectResult>(denied);
        Assert.Equal(403, deniedObj.StatusCode);
        Assert.Contains("REAUTH_REQUIRED", System.Text.Json.JsonSerializer.Serialize(deniedObj.Value));

        // iat'ı hiç olmayan eski jeton da taze sayılmaz.
        var noIat = await NewAuth(db).DeleteAccount(new DeleteAccountRequest(null, null, "alice@example.com"), CancellationToken.None);
        Assert.Equal(403, Assert.IsType<ObjectResult>(noIat).StatusCode);

        var fresh = DateTimeOffset.UtcNow.AddMinutes(-2).ToUnixTimeSeconds().ToString();
        var ok = await NewAuth(db, new Claim("iat", fresh))
            .DeleteAccount(new DeleteAccountRequest(null, null, "alice@example.com"), CancellationToken.None);
        Assert.IsType<OkObjectResult>(ok);
    }
}
