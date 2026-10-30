using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Controllers;
using Namines.API.Services;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Core.Models.Hub;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Security;

/// <summary>
/// Namines Hub sertleştirmesi: ayar yetkisi (Editor vs Admin), sahip-bazlı çözümleme
/// (çapraz sahip slug sızıntısı yok), e-posta ifşası, silme yetkisi, doğrulama sınırları,
/// issue yetkisi, rozet metni ve vitrin tohumlayıcısı.
/// </summary>
public sealed class HubHardeningTests : IAsyncLifetime
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

    private static HubController Controller(AuthDbContext db, string? userId = null)
    {
        var controller = new HubController(db, NewUserManager(db), Namines.Tests.Services.ProjectDeletionTestKit.Create(db));
        var http = new DefaultHttpContext();
        if (!string.IsNullOrEmpty(userId))
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, userId),
            }, "TestAuth"));
        }
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static ApplicationUser User(string id, string name, string? email = null) =>
        new() { Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = email };

    private static string SchemaJson() => JsonSerializer.Serialize(new DatabaseSchema
    {
        Name = "App",
        Tables = new List<SchemaTable>
        {
            new() { Id = "t1", Name = "users", Columns = new List<SchemaColumn> { new() { Id = "c1", Name = "id", Type = "uuid", IsPK = true } } },
        },
    }, SchemaJsonOptions.Default);

    private static CloudProject Project(
        string id, string slug, string ownerId, string? orgId = null,
        ProjectVisibility visibility = ProjectVisibility.Public) => new()
        {
            Id = id,
            Name = "Project " + id,
            Slug = slug,
            DbType = "PostgreSQL",
            Visibility = visibility,
            SchemaJson = SchemaJson(),
            NodePositionsJson = "{}",
            UserId = ownerId,
            OrganizationId = orgId,
            CreatedAt = DateTime.UtcNow,
        };

    /// <summary>Org + üyeler (owner/admin/editor/viewer) ve org'a bağlı bir proje.</summary>
    private async Task SeedOrgProjectAsync()
    {
        await using var db = new AuthDbContext(_options);
        db.Users.AddRange(User("owner", "olive"), User("admin", "adam"), User("editor", "edna"), User("viewer", "vera"));
        db.Organizations.Add(new Organization { Id = "org1", Name = "Acme", CreatedByUserId = "owner" });
        db.OrganizationMembers.AddRange(
            new OrganizationMember { OrganizationId = "org1", UserId = "owner", Role = OrgRole.Owner },
            new OrganizationMember { OrganizationId = "org1", UserId = "admin", Role = OrgRole.Admin },
            new OrganizationMember { OrganizationId = "org1", UserId = "editor", Role = OrgRole.Editor },
            new OrganizationMember { OrganizationId = "org1", UserId = "viewer", Role = OrgRole.Viewer });
        db.CloudProjects.Add(Project("proj", "acme-db", "owner", "org1", ProjectVisibility.Private));
        await db.SaveChangesAsync();
    }

    // ── UpdateSettings: Editor ↔ Admin ────────────────────────────────────────

    [Fact]
    public async Task UpdateSettings_Editor_gorunurluk_ve_otomatik_onayi_DEGISTIREMIYOR()
    {
        await SeedOrgProjectAsync();
        await using var db = new AuthDbContext(_options);
        var editor = Controller(db, "editor");

        Assert.IsType<ForbidResult>(await editor.UpdateSettings(
            "proj", new UpdateRepoSettingsRequest(null, null, null, ProjectVisibility.Public, null, null, null, null, null),
            CancellationToken.None));

        Assert.IsType<ForbidResult>(await editor.UpdateSettings(
            "proj", new UpdateRepoSettingsRequest(null, null, null, null, null, null, null, true, null),
            CancellationToken.None));

        Assert.IsType<ForbidResult>(await editor.UpdateSettings(
            "proj", new UpdateRepoSettingsRequest(null, "yeni-slug", null, null, null, null, null, null, null),
            CancellationToken.None));

        Assert.IsType<ForbidResult>(await editor.UpdateSettings(
            "proj", new UpdateRepoSettingsRequest(null, null, null, null, null, null, "acme/repo", null, null),
            CancellationToken.None));

        await using var verify = new AuthDbContext(_options);
        var stored = await verify.CloudProjects.AsNoTracking().SingleAsync(p => p.Id == "proj");
        Assert.Equal(ProjectVisibility.Private, stored.Visibility);
        Assert.False(stored.AutoApproveSafeChanges);
        Assert.Equal("acme-db", stored.Slug);
    }

    [Fact]
    public async Task UpdateSettings_Editor_ad_ve_aciklamayi_degistirebiliyor_ve_degismeyen_alanlar_403_uretmiyor()
    {
        await SeedOrgProjectAsync();
        await using var db = new AuthDbContext(_options);

        // Ayarlar formu değişmemiş görünürlük/slug değerlerini de geri gönderir.
        var result = await Controller(db, "editor").UpdateSettings(
            "proj",
            new UpdateRepoSettingsRequest("Yeni Ad", "acme-db", "Açıklama", ProjectVisibility.Private, null, null, null, false, null),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        await using var verify = new AuthDbContext(_options);
        Assert.Equal("Yeni Ad", (await verify.CloudProjects.AsNoTracking().SingleAsync(p => p.Id == "proj")).Name);
    }

    [Fact]
    public async Task UpdateSettings_Admin_gorunurluk_ve_slug_degistirebiliyor_Viewer_hicbir_sey()
    {
        await SeedOrgProjectAsync();
        await using var db = new AuthDbContext(_options);

        var ok = await Controller(db, "admin").UpdateSettings(
            "proj",
            new UpdateRepoSettingsRequest(null, "acme-public", null, ProjectVisibility.Public, null, null, "acme/repo", true, "postgres"),
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(ok);

        await using var verify = new AuthDbContext(_options);
        var stored = await verify.CloudProjects.AsNoTracking().SingleAsync(p => p.Id == "proj");
        Assert.Equal(ProjectVisibility.Public, stored.Visibility);
        Assert.Equal("acme-public", stored.Slug);
        Assert.Equal("acme/repo", stored.LinkedGithubRepo);
        Assert.True(stored.AutoApproveSafeChanges);
        Assert.Equal("PostgreSQL", stored.DbType);

        await using var db2 = new AuthDbContext(_options);
        Assert.IsType<ForbidResult>(await Controller(db2, "viewer").UpdateSettings(
            "proj", new UpdateRepoSettingsRequest("x", null, null, null, null, null, null, null, null),
            CancellationToken.None));
    }

    [Theory]
    [InlineData("ab")]                      // çok kısa
    [InlineData("Büyük Harf")]              // geçersiz karakter
    [InlineData("-baslangic")]              // tireyle başlıyor
    [InlineData("cift--tire")]              // ardışık tire
    [InlineData("issues")]                  // ayrılmış
    [InlineData("explore")]                 // ayrılmış
    public async Task UpdateSettings_gecersiz_slug_400(string slug)
    {
        await SeedOrgProjectAsync();
        await using var db = new AuthDbContext(_options);

        var result = await Controller(db, "admin").UpdateSettings(
            "proj", new UpdateRepoSettingsRequest(null, slug, null, null, null, null, null, null, null),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData("Db2")]
    [InlineData("not-a-database")]
    public async Task UpdateSettings_bilinmeyen_DbType_400(string dbType)
    {
        await SeedOrgProjectAsync();
        await using var db = new AuthDbContext(_options);

        var result = await Controller(db, "admin").UpdateSettings(
            "proj", new UpdateRepoSettingsRequest(null, null, null, null, null, null, null, null, dbType),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData("not a repo")]
    [InlineData("https://github.com/a/b")]
    [InlineData("a/b/c")]
    public async Task UpdateSettings_gecersiz_GitHub_deposu_400(string repo)
    {
        await SeedOrgProjectAsync();
        await using var db = new AuthDbContext(_options);

        var result = await Controller(db, "admin").UpdateSettings(
            "proj", new UpdateRepoSettingsRequest(null, null, null, null, null, null, repo, null, null),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ── GetRepo / Badge: sahip bazlı çözümleme, e-posta yok ──────────────────

    private async Task SeedTwoOwnersAsync()
    {
        await using var db = new AuthDbContext(_options);
        db.Users.AddRange(User("a", "alice", "alice@secret.example"), User("b", "bob", "bob@secret.example"));
        db.CloudProjects.Add(Project("bobs", "fintech", "b"));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetRepo_baska_sahibin_slugu_owner_altinda_BULUNMUYOR()
    {
        await SeedTwoOwnersAsync();
        await using var db = new AuthDbContext(_options);

        // alice'in "fintech" reposu yok; bob'unki alice/fintech olarak gösterilmemeli.
        var cross = await Controller(db).GetRepo("alice", "fintech", CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(cross);

        // Bilinmeyen sahip de bulunamaz (eskiden global slug araması yapılırdı).
        Assert.IsType<NotFoundObjectResult>(await Controller(db).GetRepo("kimse", "fintech", CancellationToken.None));

        // Doğru sahip çalışır.
        Assert.IsType<OkObjectResult>(await Controller(db).GetRepo("bob", "fintech", CancellationToken.None));
    }

    [Fact]
    public async Task GetRepo_alt_cizgi_sahibi_yalnizca_proje_Id_ile_cozulur_slug_ile_degil()
    {
        await SeedTwoOwnersAsync();
        await using var db = new AuthDbContext(_options);

        Assert.IsType<NotFoundObjectResult>(await Controller(db).GetRepo("_", "fintech", CancellationToken.None));
        Assert.IsType<OkObjectResult>(await Controller(db).GetRepo("_", "bobs", CancellationToken.None));
    }

    [Fact]
    public async Task GetRepo_anonim_cevap_sahibin_e_postasini_icermiyor()
    {
        await SeedTwoOwnersAsync();
        await using var db = new AuthDbContext(_options);

        var ok = Assert.IsType<OkObjectResult>(await Controller(db).GetRepo("bob", "fintech", CancellationToken.None));
        var json = JsonSerializer.Serialize(ok.Value);

        Assert.DoesNotContain("secret.example", json);
        Assert.DoesNotContain("\"Email\"", json);
    }

    [Fact]
    public async Task GetRepo_private_repo_uye_olmayana_ve_anonime_ayni_404()
    {
        await using (var seed = new AuthDbContext(_options))
        {
            seed.Users.AddRange(User("a", "alice"), User("b", "bob"), User("c", "carl"));
            seed.CloudProjects.Add(Project("priv", "gizli", "b", visibility: ProjectVisibility.Private));
            await seed.SaveChangesAsync();
        }

        await using var db = new AuthDbContext(_options);
        Assert.IsType<NotFoundObjectResult>(await Controller(db).GetRepo("bob", "gizli", CancellationToken.None));
        Assert.IsType<NotFoundObjectResult>(await Controller(db, "c").GetRepo("bob", "gizli", CancellationToken.None));
        Assert.IsType<OkObjectResult>(await Controller(db, "b").GetRepo("bob", "gizli", CancellationToken.None));
    }

    [Fact]
    public async Task Badge_baska_sahibin_reposu_icin_rozet_uretmiyor()
    {
        await SeedTwoOwnersAsync();
        await using var db = new AuthDbContext(_options);

        var cross = Assert.IsType<ContentResult>(await Controller(db).Badge("alice", "fintech", CancellationToken.None));
        Assert.Contains("private / not found", cross.Content);

        var own = Assert.IsType<ContentResult>(await Controller(db).Badge("bob", "fintech", CancellationToken.None));
        Assert.Contains("1 tables", own.Content);
    }

    // ── DeleteRepo ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteRepo_org_uyeligi_dusen_olusturan_SILEMIYOR_org_Owner_silebiliyor()
    {
        await using (var seed = new AuthDbContext(_options))
        {
            seed.Users.AddRange(User("creator", "cem"), User("boss", "bora"));
            seed.Organizations.Add(new Organization { Id = "org2", Name = "Acme2", CreatedByUserId = "boss" });
            // creator artık yalnızca Viewer; proje onun adına kayıtlı.
            seed.OrganizationMembers.AddRange(
                new OrganizationMember { OrganizationId = "org2", UserId = "creator", Role = OrgRole.Viewer },
                new OrganizationMember { OrganizationId = "org2", UserId = "boss", Role = OrgRole.Owner });
            seed.CloudProjects.Add(Project("p2", "acme2", "creator", "org2"));
            await seed.SaveChangesAsync();
        }

        await using var db = new AuthDbContext(_options);
        Assert.IsType<ForbidResult>(await Controller(db, "creator").DeleteRepo("p2", CancellationToken.None));
        Assert.True(await db.CloudProjects.AnyAsync(p => p.Id == "p2"));

        Assert.IsType<NoContentResult>(await Controller(db, "boss").DeleteRepo("p2", CancellationToken.None));
        Assert.False(await db.CloudProjects.AnyAsync(p => p.Id == "p2"));
    }

    // ── Doğrulama sınırları ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateIssue_kolon_sinirlarini_asan_degerler_500_degil_400()
    {
        await using (var seed = new AuthDbContext(_options))
        {
            seed.Users.Add(User("u", "uma"));
            seed.CloudProjects.Add(Project("pi", "issue-repo", "u"));
            await seed.SaveChangesAsync();
        }

        await using var db = new AuthDbContext(_options);
        var c = Controller(db, "u");
        var ct = CancellationToken.None;

        Assert.IsType<BadRequestObjectResult>(await c.CreateIssue("pi", new CreateIssueRequest(new string('t', 257), null, null, null, null), ct));
        Assert.IsType<BadRequestObjectResult>(await c.CreateIssue("pi", new CreateIssueRequest("ok", null, null, null, new string('l', 257)), ct));
        Assert.IsType<BadRequestObjectResult>(await c.CreateIssue("pi", new CreateIssueRequest("ok", null, new string('r', 129), null, null), ct));
        Assert.IsType<BadRequestObjectResult>(await c.CreateIssue("pi", new CreateIssueRequest("ok", null, null, new string('n', 33), null), ct));
        Assert.IsType<BadRequestObjectResult>(await c.CreateIssue("pi", new CreateIssueRequest("ok", new string('b', 20_001), null, null, null), ct));

        // Sınırdaki değerler geçer.
        Assert.IsType<CreatedAtActionResult>(await c.CreateIssue(
            "pi", new CreateIssueRequest(new string('t', 256), null, new string('r', 128), new string('n', 32), new string('l', 256)), ct));
    }

    [Fact]
    public async Task CreateComment_ve_Fork_adi_uzunluk_siniri()
    {
        await using (var seed = new AuthDbContext(_options))
        {
            seed.Users.Add(User("u", "uma"));
            seed.CloudProjects.Add(Project("pc", "comment-repo", "u"));
            seed.RepoIssues.Add(new RepoIssue { Id = "i1", ProjectId = "pc", IssueNumber = 1, Title = "t", CreatedByUserId = "u" });
            await seed.SaveChangesAsync();
        }

        await using var db = new AuthDbContext(_options);
        var c = Controller(db, "u");

        Assert.IsType<BadRequestObjectResult>(
            await c.CreateComment("i1", new CreateCommentRequest(new string('x', 10_001)), CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(
            await c.Fork("pc", new ForkRepoRequest(new string('n', 129)), CancellationToken.None));
    }

    [Fact]
    public async Task CreateIssue_numaralar_art_arda_benzersiz()
    {
        await using (var seed = new AuthDbContext(_options))
        {
            seed.Users.Add(User("u", "uma"));
            seed.CloudProjects.Add(Project("pn", "num-repo", "u"));
            await seed.SaveChangesAsync();
        }

        // Her çağrı kendi bağlamında (kendi isteği gibi): numara MAX+1.
        var numbers = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            await using var db = new AuthDbContext(_options);
            var res = Assert.IsType<CreatedAtActionResult>(
                await Controller(db, "u").CreateIssue("pn", new CreateIssueRequest("t" + i, null, null, null, null), CancellationToken.None));
            numbers.Add((int)res.Value!.GetType().GetProperty("IssueNumber")!.GetValue(res.Value)!);
        }

        Assert.Equal(new[] { 1, 2, 3 }, numbers);
    }

    // ── UpdateIssueState ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateIssueState_private_repoyu_artik_goremeyen_yazar_issue_i_degistiremiyor()
    {
        await using (var seed = new AuthDbContext(_options))
        {
            seed.Users.AddRange(User("o", "olga"), User("x", "xena"));
            seed.Organizations.Add(new Organization { Id = "org3", Name = "O3", CreatedByUserId = "o" });
            seed.OrganizationMembers.Add(new OrganizationMember { OrganizationId = "org3", UserId = "o", Role = OrgRole.Owner });
            seed.CloudProjects.Add(Project("p3", "private3", "o", "org3", ProjectVisibility.Private));
            // xena issue'yu yazmış ama org üyeliği yok (çıkarılmış).
            seed.RepoIssues.Add(new RepoIssue { Id = "iss", ProjectId = "p3", IssueNumber = 1, Title = "t", CreatedByUserId = "x" });
            await seed.SaveChangesAsync();
        }

        await using var db = new AuthDbContext(_options);
        Assert.IsType<NotFoundObjectResult>(
            await Controller(db, "x").UpdateIssueState("iss", new UpdateIssueStateRequest(true), CancellationToken.None));

        Assert.IsType<OkObjectResult>(
            await Controller(db, "o").UpdateIssueState("iss", new UpdateIssueStateRequest(true), CancellationToken.None));
    }

    // ── Yıldız sayacı ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ToggleStar_iki_kullanici_sayaci_dogru_arttiriyor_ve_kayitlari_tekil()
    {
        await using (var seed = new AuthDbContext(_options))
        {
            seed.Users.AddRange(User("o", "olga"), User("s1", "sam"), User("s2", "sue"));
            seed.CloudProjects.Add(Project("ps", "star-repo", "o"));
            await seed.SaveChangesAsync();
        }

        await using (var db1 = new AuthDbContext(_options))
            Assert.IsType<OkObjectResult>(await Controller(db1, "s1").ToggleStar("ps", CancellationToken.None));
        await using (var db2 = new AuthDbContext(_options))
            Assert.IsType<OkObjectResult>(await Controller(db2, "s2").ToggleStar("ps", CancellationToken.None));

        await using var verify = new AuthDbContext(_options);
        Assert.Equal(2, (await verify.CloudProjects.AsNoTracking().SingleAsync(p => p.Id == "ps")).StarCount);
        Assert.Equal(2, await verify.RepoStars.CountAsync(s => s.ProjectId == "ps"));
    }

    // ── Hız sınırı ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(nameof(HubController.UpdateSettings), "sensitive")]
    [InlineData(nameof(HubController.DeleteRepo), "sensitive")]
    [InlineData(nameof(HubController.Fork), "sensitive")]
    [InlineData(nameof(HubController.ToggleStar), "hub-write")]
    [InlineData(nameof(HubController.CreateIssue), "hub-write")]
    [InlineData(nameof(HubController.UpdateIssueState), "hub-write")]
    [InlineData(nameof(HubController.CreateComment), "hub-write")]
    public void Hub_yazma_uclari_hiz_sinirina_bagli(string action, string policy)
    {
        var method = typeof(HubController).GetMethod(action)!;
        var attribute = method.GetCustomAttribute<EnableRateLimitingAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(policy, attribute!.PolicyName);
    }

    // ── Vitrin tohumlayıcısı ──────────────────────────────────────────────────

    [Fact]
    public async Task Seeder_vitrin_kullanicisi_admin_degil_ve_sayaclar_sifir()
    {
        await using var db = new AuthDbContext(_options);
        await HubBlueprintSeeder.SeedAsync(db, NewUserManager(db), NullLogger.Instance);

        await using var verify = new AuthDbContext(_options);
        var architect = await verify.Users.AsNoTracking().SingleAsync(u => u.Id == "namines-architect");
        Assert.False(architect.IsDev);

        var projects = await verify.CloudProjects.AsNoTracking().Where(p => p.UserId == "namines-architect").ToListAsync();
        Assert.NotEmpty(projects);
        Assert.All(projects, p =>
        {
            Assert.Equal(0, p.StarCount);
            Assert.Equal(0, p.ForkCount);
        });
    }

    [Fact]
    public async Task Seeder_adi_baska_hesap_kapmissa_tohumlamayi_atliyor_ve_o_hesabi_benimsemiyor()
    {
        await using (var seed = new AuthDbContext(_options))
        {
            seed.Users.Add(User("real-user", "namines", "real@example.com"));
            await seed.SaveChangesAsync();
        }

        await using var db = new AuthDbContext(_options);
        await HubBlueprintSeeder.SeedAsync(db, NewUserManager(db), NullLogger.Instance);

        await using var verify = new AuthDbContext(_options);
        Assert.False(await verify.CloudProjects.AnyAsync());
        Assert.False(await verify.Users.AnyAsync(u => u.Id == "namines-architect"));
    }

    [Fact]
    public async Task Seeder_mevcut_IsDev_vitrin_hesabini_dusuruyor()
    {
        await using (var seed = new AuthDbContext(_options))
        {
            var legacy = User("namines-architect", "namines");
            legacy.IsDev = true;
            seed.Users.Add(legacy);
            await seed.SaveChangesAsync();
        }

        await using var db = new AuthDbContext(_options);
        await HubBlueprintSeeder.SeedAsync(db, NewUserManager(db), NullLogger.Instance);

        await using var verify = new AuthDbContext(_options);
        Assert.False((await verify.Users.AsNoTracking().SingleAsync(u => u.Id == "namines-architect")).IsDev);
    }
}
