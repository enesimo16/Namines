using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Controllers;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Infrastructure;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Security;
using Namines.Infrastructure.Services;
using Namines.Tests.Services;
using Namines.Tests.TestSupport;
using Npgsql;

namespace Namines.Tests.Integration;

/// <summary>
/// Faz 14.2B akışları GERÇEK PostgreSQL'de, üretimle aynı bağlantı düzeniyle: AuthDbContext retry
/// stratejili, Studio/Hub ayrı bağlantılı, ortak işlem SharedTransactionManager ile. SQLite testleri
/// xmin'i, ayrı bağlantıya işlem paylaşımını ve gerçek FK cascade'ini sınamaz.
/// </summary>
[Collection("Docker")]
public sealed class Phase14_2BFlowTests : IClassFixture<Phase14_2BPostgresFixture>
{
    private readonly Phase14_2BPostgresFixture _pg;
    private readonly IConfiguration _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Security:ConnectionEncryptionKey"] = "phase-14-2b-flow-test-key-at-least-32-characters",
        ["Jwt:Key"] = "SuperSecretDevJwtKeyThatIsLongEnough12345!",
    }).Build();
    private readonly IConnectionSecretProtector _protector;

    public Phase14_2BFlowTests(Phase14_2BPostgresFixture pg)
    {
        _pg = pg;
        _protector = new AesGcmConnectionSecretProtector(_config);
    }

    private sealed class Scope : IAsyncDisposable
    {
        public required AuthDbContext Db { get; init; }
        public required ModuleKit Kit { get; init; }
        public required string ConnectionString { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Kit.Studio.DisposeAsync();
            await Kit.Hub.DisposeAsync();
            await Db.DisposeAsync();
        }
    }

    private static Scope Open(string cs, CommandCounter? counter = null)
    {
        var interceptors = counter is null ? Array.Empty<IInterceptor>() : new IInterceptor[] { counter };
        var db = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(cs, o => { o.CommandTimeout(120); o.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null); })
            .AddInterceptors(interceptors).Options);
        return new Scope { Db = db, Kit = new ModuleKit(db, cs, interceptors), ConnectionString = cs };
    }

    /// <summary>Üç bağlamın da gönderdiği SQL sayısı: Explore'un public repo sayısından bağımsız olduğunun kanıtı.</summary>
    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count;

        public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private async Task<string> NewDatabaseAsync(string name)
    {
        var cs = await _pg.CreateDatabaseAsync(name);
        await using var scope = Open(cs);
        await scope.Db.MigrateAllModulesAsync();

        scope.Db.Users.AddRange(
            User("owner"), User("member"), User("stranger"), User("fan1"), User("fan2"));
        scope.Db.Organizations.AddRange(
            new Organization { Id = "org-a", Name = "A", CreatedByUserId = "owner" },
            new Organization { Id = "org-b", Name = "B", CreatedByUserId = "stranger" });
        scope.Db.OrganizationMembers.AddRange(
            new OrganizationMember { OrganizationId = "org-a", UserId = "owner", Role = OrgRole.Owner },
            new OrganizationMember { OrganizationId = "org-a", UserId = "member", Role = OrgRole.Editor },
            new OrganizationMember { OrganizationId = "org-b", UserId = "stranger", Role = OrgRole.Owner });
        await scope.Db.SaveChangesAsync();
        return cs;
    }

    private static ApplicationUser User(string id) =>
        new() { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant(), Email = id + "@x.io", NormalizedEmail = id.ToUpperInvariant() + "@X.IO" };

    private static UserManager<ApplicationUser> Users(AuthDbContext db) => new(
        new Microsoft.AspNetCore.Identity.EntityFrameworkCore.UserStore<ApplicationUser>(db),
        null!, new PasswordHasher<ApplicationUser>(), new List<IUserValidator<ApplicationUser>>(),
        new List<IPasswordValidator<ApplicationUser>>(), new UpperInvariantLookupNormalizer(),
        new IdentityErrorDescriber(), null!, NullLogger<UserManager<ApplicationUser>>.Instance);

    private static DefaultHttpContext Http(string? userId)
    {
        var http = new DefaultHttpContext();
        if (userId != null)
            http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "TestAuth"));
        return http;
    }

    private static HubController Hub(Scope s, string? userId = null) =>
        new(s.Kit.HubRepositories(ProjectDeletionTestKit.Create(s.Db, s.Kit)), s.Kit.HubIssues)
        {
            ControllerContext = new ControllerContext { HttpContext = Http(userId) },
        };

    private static ShareController Share(Scope s, string? userId = null) =>
        new(s.Kit.Shares, new LinterService(), s.Kit.StudioService)
        {
            ControllerContext = new ControllerContext { HttpContext = Http(userId) },
        };

    private AuthController Auth(Scope s, string userId) =>
        new(Users(s.Db), s.Db, _config, new MigrationService(null!), new AutomationJobQueue(),
            NullLogger<AuthController>.Instance, s.Kit.HubService, s.Kit.StudioService, s.Kit.Transaction, s.Kit.DefaultAuthority, s.Kit.Projects, s.Kit.Workspace)
        {
            ControllerContext = new ControllerContext { HttpContext = Http(userId) },
        };

    private static string QualitySchema() => JsonSerializer.Serialize(new DatabaseSchema
    {
        Name = "S",
        Tables = Enumerable.Range(1, 4).Select(i => new SchemaTable
        {
            Id = "t" + i,
            Name = "table" + i,
            Columns = new List<SchemaColumn>
            {
                new() { Id = "c" + i, Name = "id", Type = "uuid", IsPK = true },
                new() { Id = "f" + i, Name = "next_id", Type = "uuid", IsFK = true },
            },
        }).ToList(),
        Relations = new List<SchemaRelation>
        {
            new() { Id = "r1", SourceTableId = "t1", SourceColumnId = "f1", TargetTableId = "t2", TargetColumnId = "c2" },
            new() { Id = "r2", SourceTableId = "t2", SourceColumnId = "f2", TargetTableId = "t3", TargetColumnId = "c3" },
        },
    }, SchemaJsonOptions.Default);

    private static async Task SeedProjectAsync(Scope s, string id, string owner = "owner", string? org = "org-a", string? name = null,
        string? slug = null, ProjectVisibility visibility = ProjectVisibility.Private, string? description = null,
        string category = "General", string? tags = null, int stars = 0, string dbType = "PostgreSQL", DateTime? createdAt = null)
    {
        var at = createdAt ?? DateTime.UtcNow;
        s.Db.Studio().Projects.Add(new CloudProject
        {
            Id = id, Name = name ?? id, DbType = dbType, SchemaJson = QualitySchema(), NodePositionsJson = "{}",
            UserId = owner, OrganizationId = org, CreatedAt = at, UpdatedAt = at,
        });
        await s.Db.SaveChangesAsync();
        await s.Kit.SeedRepositoryAsync(id, owner, slug: slug, visibility: visibility, description: description,
            category: category, tags: tags, starCount: stars, createdAt: at);
    }

    private static JsonElement Json(IActionResult result)
    {
        var value = result switch
        {
            OkObjectResult ok => ok.Value,
            CreatedAtActionResult created => created.Value,
            _ => throw new Xunit.Sdk.XunitException("Unexpected result: " + result.GetType().Name),
        };
        return JsonSerializer.SerializeToElement(value);
    }

    private static SyncProjectDto Dto(string id, string name, uint? rowVersion = null) => new()
    {
        Id = id, Name = name, DbType = "PostgreSQL", SchemaJson = QualitySchema(), NodePositionsJson = "{}", RowVersion = rowVersion,
    };

    // ── Oluşturma / şema güncelleme ──────────────────────────────────────────

    [RequiresDockerFact]
    public async Task Sync_proje_ve_repo_satirini_birlikte_olusturuyor_cakisan_surumu_reddediyor()
    {
        var cs = await NewDatabaseAsync("f_sync");
        await using var s = Open(cs);

        Assert.IsType<OkObjectResult>(await Auth(s, "owner").SyncProjects(new() { Dto("p-a", "Orders"), Dto("p-b", "Orders") }));

        await using var v1 = Open(cs);
        var repos = await v1.Kit.HubService.GetRepositoriesAsync(new[] { "p-a", "p-b" });
        Assert.Equal(2, repos.Count);
        Assert.Equal(new[] { "orders", "orders-2" }, repos.Values.Select(r => r.Slug).OrderBy(x => x).ToArray());
        Assert.All(repos.Values, r => Assert.Equal("owner", r.OwnerUserId));
        Assert.All(repos.Values, r => Assert.Equal(ProjectVisibility.Private, r.Visibility));

        var current = await v1.Db.Studio().Projects.AsNoTracking().SingleAsync(p => p.Id == "p-a");

        // Doğru sürüm kabul, eski sürüm 409 (xmin gerçek motorda).
        await using (var s2 = Open(cs))
            Assert.IsType<OkObjectResult>(await Auth(s2, "owner").SyncProjects(new() { Dto("p-a", "Orders v2", current.RowVersion) }));
        await using (var s3 = Open(cs))
            Assert.IsType<ConflictObjectResult>(await Auth(s3, "owner").SyncProjects(new() { Dto("p-a", "Orders v3", current.RowVersion) }));

        await using var v2 = Open(cs);
        Assert.Equal("Orders v2", (await v2.Db.Studio().Projects.AsNoTracking().SingleAsync(p => p.Id == "p-a")).Name);
    }

    [RequiresDockerFact]
    public async Task Repo_yazimi_patlarsa_proje_de_geri_aliniyor()
    {
        var cs = await NewDatabaseAsync("f_atomic");
        await using var s = Open(cs);
        await SeedProjectAsync(s, "p1", slug: "taken");
        await SeedProjectAsync(s, "p2", slug: "free");

        await using var w = Open(cs);
        var project = await w.Db.Studio().Projects.SingleAsync(p => p.Id == "p2");
        project.Name = "Should roll back";

        // (sahip, slug) UNIQUE ihlali Hub tarafında, proje yazımından SONRA.
        await Assert.ThrowsAsync<DbUpdateException>(() => w.Kit.Transaction.ExecuteAsync(async c =>
        {
            await w.Db.SaveChangesAsync(acceptAllChangesOnSuccess: false, c);
            await w.Kit.HubService.ApplySettingsAsync("p2", "owner", "Should roll back", "PostgreSQL", new(Slug: "taken"), c);
        }));

        await using var v = Open(cs);
        Assert.Equal("p2", (await v.Db.Studio().Projects.AsNoTracking().SingleAsync(p => p.Id == "p2")).Name);
        Assert.Equal("free", (await v.Kit.HubService.GetRepositoryAsync("p2"))!.Slug);
    }

    // ── Repo ayarları / görünürlük / IDOR ────────────────────────────────────

    [RequiresDockerFact]
    public async Task Ayarlar_etiketleri_normallestiriyor_ve_yetkisiz_goremiyor()
    {
        var cs = await NewDatabaseAsync("f_settings");
        await using var s = Open(cs);
        await SeedProjectAsync(s, "p1", slug: "alpha");

        var json = Json(await Hub(s, "owner").UpdateSettings("p1", new UpdateRepoSettingsRequest(
            "Yeni", "alpha-2", "Açıklama", ProjectVisibility.Public, "SaaS", " Foo, foo ,,BAR", "acme/repo", null, null), default));
        Assert.Equal("alpha-2", json.GetProperty("Slug").GetString());
        Assert.Equal("Foo,BAR", json.GetProperty("Tags").GetString());
        Assert.Equal("acme/repo", json.GetProperty("LinkedGithubRepo").GetString());

        await using var v = Open(cs);
        Assert.Equal(2, await v.Kit.Hub.ProjectTags.CountAsync(t => t.ProjectId == "p1"));
        var get = Json(await Hub(v).GetRepo("owner", "alpha-2", default));
        Assert.Equal("Yeni", get.GetProperty("Name").GetString());
        Assert.Equal((int)ProjectVisibility.Public, get.GetProperty("Visibility").GetInt32());

        // Başka sahibin altından aynı slug çözülmez; özel repo yabancıya 404.
        await using var x = Open(cs);
        Assert.IsType<NotFoundObjectResult>(await Hub(x).GetRepo("stranger", "alpha-2", default));
        await SeedProjectAsync(x, "p-private", slug: "secret");
        Assert.IsType<NotFoundObjectResult>(await Hub(x, "stranger").GetRepo("owner", "secret", default));
        Assert.IsType<ForbidResult>(await Hub(x, "stranger").UpdateSettings("p-private", new UpdateRepoSettingsRequest(
            null, null, "ele geçir", null, null, null, null, null, null), default));
        Assert.IsType<NotFoundObjectResult>(await Hub(x, "stranger").ToggleStar("p-private", default));
        Assert.IsType<NotFoundObjectResult>(await Hub(x, "stranger").Fork("p-private", null, default));
    }

    // ── Paylaşım ─────────────────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task Paylasim_yalniz_ozet_saklar_iki_link_de_calisir_iptal_hepsini_kapatir()
    {
        var cs = await NewDatabaseAsync("f_share");
        await using var s = Open(cs);
        await SeedProjectAsync(s, "p1");

        var first = Json(await Share(s, "owner").CreateShareLink("p1")).GetProperty("token").GetString()!;
        var second = Json(await Share(s, "owner").CreateShareLink("p1")).GetProperty("token").GetString()!;
        Assert.NotEqual(first, second);

        // Yetkisiz: yabancı ve Editor üretemez/iptal edemez.
        Assert.IsType<NotFoundResult>(await Share(s, "stranger").CreateShareLink("p1"));
        Assert.IsType<NotFoundResult>(await Share(s, "member").CreateShareLink("p1"));
        Assert.IsType<NotFoundResult>(await Share(s, "stranger").RevokeShareLink("p1"));

        // Düz metin hiçbir kolonda yok.
        await using (var conn = new NpgsqlConnection(cs))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT string_agg(row_to_json(s)::text, '|') FROM studio.project_shares s", conn);
            var text = (string)(await cmd.ExecuteScalarAsync())!;
            Assert.DoesNotContain(first, text);
            Assert.DoesNotContain(second, text);
            Assert.Contains(ShareTokenHasher.Hash(first), text);
        }

        // Anonim uçlar ham token ile.
        await using var anon = Open(cs);
        foreach (var token in new[] { first, second })
        {
            var view = Json(await Share(anon).GetSharedSchema(token));
            Assert.Equal("p1", view.GetProperty("Name").GetString());
            Assert.Equal(4, Json(await Share(anon).Meta(token)).GetProperty("tables").GetInt32());
            Assert.IsType<ContentResult>(await Share(anon).GetDbaBadge(token));
            Assert.IsType<ContentResult>(await Share(anon).OgImage(token));
        }
        // Özet token değildir.
        Assert.IsType<NotFoundResult>(await Share(anon).GetSharedSchema(ShareTokenHasher.Hash(first)));

        Assert.IsType<NoContentResult>(await Share(s, "owner").RevokeShareLink("p1"));
        await using var after = Open(cs);
        Assert.IsType<NotFoundResult>(await Share(after).GetSharedSchema(first));
        Assert.IsType<NotFoundResult>(await Share(after).GetSharedSchema(second));
        Assert.IsType<NotFoundResult>(await Share(after).GetDbaBadge(second));

        // İptalden sonra yeniden paylaşım çalışır, iptal edilen dirilmez.
        var third = Json(await Share(s, "owner").CreateShareLink("p1")).GetProperty("token").GetString()!;
        Assert.IsType<OkObjectResult>(await Share(after).GetSharedSchema(third));
        Assert.IsType<NotFoundResult>(await Share(after).GetSharedSchema(first));
    }

    [RequiresDockerFact]
    public async Task Etkin_baglanti_tavani_asilinca_en_eskisi_kapanir()
    {
        var cs = await NewDatabaseAsync("f_sharecap");
        await using var s = Open(cs);
        await SeedProjectAsync(s, "p1");

        var firstToken = await s.Kit.StudioService.CreateShareLinkAsync("p1");
        await Task.Delay(5);
        for (var i = 0; i < StudioApplicationService.MaxActiveSharesPerProject; i++)
            await s.Kit.StudioService.CreateShareLinkAsync("p1");

        Assert.Equal(StudioApplicationService.MaxActiveSharesPerProject,
            await s.Kit.Studio.ProjectShares.CountAsync(x => x.ProjectId == "p1" && x.RevokedAt == null));
        Assert.Null(await s.Kit.StudioService.GetSharedProjectByTokenHashAsync(ShareTokenHasher.Hash(firstToken)));
    }

    // ── Yıldız ───────────────────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task Eszamanli_yildizlarda_sayac_satir_sayisina_esit_cift_tiklama_sisirmiyor()
    {
        var cs = await NewDatabaseAsync("f_star");
        await using (var s = Open(cs)) await SeedProjectAsync(s, "p1", slug: "star", visibility: ProjectVisibility.Public);

        // Beş kullanıcı aynı anda, her biri kendi bağlantısıyla.
        await Task.WhenAll(new[] { "owner", "member", "stranger", "fan1", "fan2" }.Select(async user =>
        {
            await using var s = Open(cs);
            Assert.IsType<OkObjectResult>(await Hub(s, user).ToggleStar("p1", default));
        }));

        async Task AssertCounterMatchesAsync(int expected)
        {
            await using var v = Open(cs);
            var repo = await v.Kit.HubService.GetRepositoryAsync("p1");
            Assert.Equal(expected, repo!.StarCount);
            Assert.Equal(expected, await v.Db.Hub().RepoStars.CountAsync(x => x.ProjectId == "p1"));
        }
        await AssertCounterMatchesAsync(5);

        // Çift tıklama: aynı kullanıcı aynı anda iki kez. Son durum ne olursa olsun sayaç = satır.
        await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var s = Open(cs);
            await Hub(s, "fan1").ToggleStar("p1", default);
        }));
        await using (var v = Open(cs))
        {
            var rows = await v.Db.Hub().RepoStars.CountAsync(x => x.ProjectId == "p1");
            Assert.Equal(rows, (await v.Kit.HubService.GetRepositoryAsync("p1"))!.StarCount);
            Assert.True(await v.Db.Hub().RepoStars.CountAsync(x => x.ProjectId == "p1" && x.UserId == "fan1") <= 1);
        }

        // Yinelenen yıldız veritabanı düzeyinde imkânsız.
        await using (var conn = new NpgsqlConnection(cs))
        {
            await conn.OpenAsync();
            await using var dup = new NpgsqlCommand("""
                INSERT INTO "RepoStars" ("ProjectId","UserId","CreatedAt") VALUES ('p1','dup-user',now());
                """, conn);
            await Assert.ThrowsAsync<PostgresException>(() => dup.ExecuteNonQueryAsync()); // FK: yok olan kullanıcı
            await using var pk = new NpgsqlCommand("""
                INSERT INTO "RepoStars" ("ProjectId","UserId","CreatedAt") VALUES ('p1','owner',now());
                """, conn);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => pk.ExecuteNonQueryAsync());
            Assert.Equal("23505", ex.SqlState);
        }
    }

    // ── Fork + silme ─────────────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task Fork_sayaci_ve_silme_tutarli()
    {
        var cs = await NewDatabaseAsync("f_fork");
        await using var s = Open(cs);
        await SeedProjectAsync(s, "src", slug: "source", visibility: ProjectVisibility.Public, description: "Kaynak",
            category: "FinTech", tags: "ledger,Finance");

        var fork = Json(await Hub(s, "stranger").Fork("src", new ForkRepoRequest("Çatal"), default));
        var forkId = fork.GetProperty("Id").GetString()!;
        Assert.Equal("src", fork.GetProperty("ForkedFromProjectId").GetString());
        Assert.Equal((int)ProjectVisibility.Private, fork.GetProperty("Visibility").GetInt32());

        await using (var v = Open(cs))
        {
            Assert.Equal(1, (await v.Kit.HubService.GetRepositoryAsync("src"))!.ForkCount);
            var forked = (await v.Kit.HubService.GetRepositoryAsync(forkId))!;
            Assert.Equal("src", forked.ForkedFromProjectId);
            Assert.Equal("stranger", forked.OwnerUserId);
            Assert.Equal("FinTech", forked.Category);
            Assert.Equal("ledger,Finance", await v.Kit.HubService.GetTagsCsvAsync(forkId));
            Assert.True(await v.Db.Studio().Branches.AnyAsync(b => b.ProjectId == forkId && b.IsDefault));
        }

        // Fork silinince kaynağın sayacı düşer.
        await using (var d = Open(cs))
            Assert.IsType<NoContentResult>(await Hub(d, "stranger").DeleteRepo(forkId, default));
        await using (var v = Open(cs))
        {
            Assert.Equal(0, (await v.Kit.HubService.GetRepositoryAsync("src"))!.ForkCount);
            Assert.Null(await v.Kit.HubService.GetRepositoryAsync(forkId));
            Assert.False(await v.Kit.Hub.ProjectTags.AnyAsync(t => t.ProjectId == forkId));
        }

        // Kaynak silinince fork kalır, işaretçi boşalır.
        await using (var f = Open(cs))
        {
            var second = Json(await Hub(f, "stranger").Fork("src", null, default)).GetProperty("Id").GetString()!;
            await using var d = Open(cs);
            Assert.IsType<NoContentResult>(await Hub(d, "owner").DeleteRepo("src", default));
            await using var v = Open(cs);
            Assert.Null((await v.Kit.HubService.GetRepositoryAsync(second))!.ForkedFromProjectId);
            Assert.True(await v.Db.Studio().Projects.AnyAsync(p => p.Id == second));
        }
    }

    [RequiresDockerFact]
    public async Task Proje_silme_baglanti_paylasim_repo_etiket_yildizi_goturur_yetkisiz_silemez()
    {
        var cs = await NewDatabaseAsync("f_delete");
        await using var s = Open(cs);
        await SeedProjectAsync(s, "p1", slug: "gone", visibility: ProjectVisibility.Public, tags: "a,b");
        await s.Kit.Authority(_protector).SetConnectionAsync("p1", "Host=x.example.com;Password=p", "PostgreSQL");
        await s.Kit.StudioService.CreateShareLinkAsync("p1");
        await Hub(s, "fan1").ToggleStar("p1", default);

        Assert.IsType<ForbidResult>(await Hub(s, "stranger").DeleteRepo("p1", default));
        Assert.IsType<ForbidResult>(await Hub(s, "member").DeleteRepo("p1", default));

        await using (var d = Open(cs))
            Assert.IsType<NoContentResult>(await Hub(d, "owner").DeleteRepo("p1", default));

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        foreach (var table in new[] { "studio.projects WHERE \"Id\"", "studio.project_connections WHERE \"ProjectId\"", "studio.project_shares WHERE \"ProjectId\"",
                     "hub.repositories WHERE \"ProjectId\"", "hub.project_tags WHERE \"ProjectId\"", "\"RepoStars\" WHERE \"ProjectId\"" })
        {
            await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {table} = 'p1'", conn);
            Assert.Equal(0L, await cmd.ExecuteScalarAsync());
        }
    }

    [RequiresDockerFact]
    public async Task Hesap_silme_sayaci_dusurur_ve_devredilen_reponun_sahibini_gunceller()
    {
        var cs = await NewDatabaseAsync("f_account");
        await using var s = Open(cs);
        await SeedProjectAsync(s, "starred", slug: "starred", visibility: ProjectVisibility.Public);
        await SeedProjectAsync(s, "team", owner: "member", slug: "teamrepo");
        await s.Db.Users.Where(u => u.Id == "owner").ExecuteUpdateAsync(x => x.SetProperty(u => u.IsDev, false));
        Assert.IsType<OkObjectResult>(await Hub(s, "member").ToggleStar("starred", default));
        Assert.Equal(1, (await s.Kit.HubService.GetRepositoryAsync("starred"))!.StarCount);

        await using var d = Open(cs);
        var um = Users(d.Db);
        var service = new AccountDeletionService(d.Db, um, ProjectDeletionTestKit.Create(d.Db, d.Kit),
            d.Kit.HubService, d.Kit.Transaction, NullLogger<AccountDeletionService>.Instance, d.Kit.Feedback, d.Kit.Projects);
        var result = await service.DeleteAsync(await d.Db.Users.SingleAsync(u => u.Id == "member"), default);
        Assert.True(result.Ok, result.Code + " " + result.Error);

        await using var v = Open(cs);
        Assert.Equal(0, (await v.Kit.HubService.GetRepositoryAsync("starred"))!.StarCount);
        var team = (await v.Kit.HubService.GetRepositoryAsync("team"))!;
        Assert.Equal("owner", team.OwnerUserId);
        Assert.Equal("owner", (await v.Db.Studio().Projects.AsNoTracking().SingleAsync(p => p.Id == "team")).UserId);
        Assert.Equal("team", await v.Kit.HubService.ResolveProjectIdAsync("owner", "teamrepo"));
    }

    // ── Explore ──────────────────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task Explore_filtreleri_siralama_ve_etiket_tam_eslesme()
    {
        var cs = await NewDatabaseAsync("f_explore");
        await using var s = Open(cs);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedProjectAsync(s, "a", name: "Ledger Core", slug: "a", visibility: ProjectVisibility.Public, description: "double entry",
            category: "FinTech", tags: "PostgreSQL,Ledger", stars: 5, createdAt: t0);
        await SeedProjectAsync(s, "b", name: "Shop", slug: "b", visibility: ProjectVisibility.Public, description: "orders",
            category: "SaaS", tags: "postgresql-extra", stars: 9, createdAt: t0.AddDays(1), dbType: "MySQL");
        await SeedProjectAsync(s, "c", name: "Hidden", slug: "c", visibility: ProjectVisibility.Private, description: "private",
            tags: "PostgreSQL", stars: 100, createdAt: t0.AddDays(2));
        await SeedProjectAsync(s, "d", name: "No description", slug: "d", visibility: ProjectVisibility.Public, description: "",
            tags: "PostgreSQL", stars: 100, createdAt: t0.AddDays(3));

        async Task<string[]> Ids(string? q = null, string? category = null, string? dbType = null, string? sort = "popular", string? tag = null)
        {
            await using var e = Open(cs);
            var json = Json(await Hub(e).Explore(q, category, dbType, sort, 1, 12, tag, default));
            return json.GetProperty("Items").EnumerateArray().Select(i => i.GetProperty("Id").GetString()!).ToArray();
        }

        Assert.Equal(new[] { "b", "a" }, await Ids());
        Assert.Equal(new[] { "b", "a" }, await Ids(sort: "recent"));
        Assert.Equal(new[] { "a" }, await Ids(category: "fintech"));
        Assert.Equal(new[] { "b" }, await Ids(dbType: "mysql"));
        Assert.Equal(new[] { "a" }, await Ids(q: "ledger"));   // ad + etiket
        Assert.Equal(new[] { "b" }, await Ids(q: "ORDERS"));   // açıklama, büyük/küçük duyarsız

        // Etiket filtresi TAM eşleşme: alt dizge ("postgresql-extra") ve özel/açıklamasız repo gelmez.
        Assert.Equal(new[] { "a" }, await Ids(tag: "postgresql"));
        Assert.Equal(new[] { "a" }, await Ids(tag: "POSTGRESQL"));
        Assert.Empty(await Ids(tag: "postgre"));

        // Yanıtın alanları ve sahip adı.
        await using var one = Open(cs);
        var item = Json(await Hub(one).Explore("ledger", null, null, "popular", 1, 12, null, default))
            .GetProperty("Items").EnumerateArray().Single();
        Assert.Equal("PostgreSQL,Ledger", item.GetProperty("Tags").GetString());
        Assert.Equal(5, item.GetProperty("StarCount").GetInt32());
        Assert.Equal("owner", item.GetProperty("Owner").GetProperty("Username").GetString());
        Assert.Equal(4, item.GetProperty("Stats").GetProperty("TableCount").GetInt32());
    }

    [RequiresDockerFact]
    public async Task Explore_eslesmeyen_q_ve_motor_sabit_sorgu_public_repo_sayisindan_bagimsiz()
    {
        var cs = await NewDatabaseAsync("f_explore_count");
        await using (var s = Open(cs))
            await SeedProjectAsync(s, "real", name: "Needle Service", slug: "real", visibility: ProjectVisibility.Public,
                description: "x", tags: "Alpha", dbType: "MySQL");

        // Eski döngü 200'lük turlarla 3 sorgu atıyordu; 650 public repoda en az 4 tur (12 sorgu) olurdu.
        await using (var conn = new NpgsqlConnection(cs))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO studio.projects ("Id","Name","DbType","SchemaJson","NodePositionsJson","UserId","CreatedAt","UpdatedAt","AutoApproveSafeChanges")
                SELECT 'bulk-' || i, 'Bulk ' || i, 'PostgreSQL', '{}', '{}', 'owner', now(), now(), false FROM generate_series(1, 650) i;
                INSERT INTO hub.repositories ("ProjectId","OwnerUserId","ProjectName","ProjectDbType","Slug","Description","Visibility","Category","StarCount","ForkCount","CreatedAt","UpdatedAt")
                SELECT 'bulk-' || i, 'owner', 'Bulk ' || i, 'PostgreSQL', 'bulk-' || i, 'd', 2, 'General', 0, 0, now(), now() FROM generate_series(1, 650) i;
                """, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        async Task<(int Commands, string[] Ids)> RunAsync(string? q = null, string? dbType = null)
        {
            var counter = new CommandCounter();
            await using var e = Open(cs, counter);
            var json = Json(await Hub(e).Explore(q, null, dbType, "popular", 1, 12, null, default));
            return (counter.Count, json.GetProperty("Items").EnumerateArray().Select(i => i.GetProperty("Id").GetString()!).ToArray());
        }

        // Eşleşmeyen filtre: tek SQL, sonra toplu sorgu yok ya da boş.
        var noMatch = await RunAsync(q: "zzzz-no-such-thing");
        Assert.Empty(noMatch.Ids);
        Assert.True(noMatch.Commands <= 5, "q eslesmesi yokken " + noMatch.Commands + " sorgu");
        var noEngine = await RunAsync(dbType: "Oracle");
        Assert.Empty(noEngine.Ids);
        Assert.True(noEngine.Commands <= 5, "motor eslesmesi yokken " + noEngine.Commands + " sorgu");

        // Eşleşen de aynı: ad, motor, tam etiket tek sorguda bulunuyor; kalan 650 satır taranmaz.
        var byName = await RunAsync(q: "needle");
        Assert.Equal(new[] { "real" }, byName.Ids);
        Assert.Equal(5, byName.Commands); // repo, özet, etiket, şema, sahip adı
        Assert.Equal(new[] { "real" }, (await RunAsync(dbType: "mysql")).Ids);
        Assert.Equal(new[] { "real" }, (await RunAsync(q: "ALPHA")).Ids);
        Assert.DoesNotContain("real", (await RunAsync(q: "alph")).Ids); // etikette alt dizge yok
    }

    [RequiresDockerFact]
    public async Task Yeniden_adlandirma_ve_motor_degisimi_Explore_aramasina_yansiyor()
    {
        var cs = await NewDatabaseAsync("f_explore_rename");
        await using (var s = Open(cs))
            await SeedProjectAsync(s, "p1", name: "Old Name", slug: "p1", visibility: ProjectVisibility.Public, description: "d");

        async Task<string[]> Ids(string? q = null, string? dbType = null)
        {
            await using var e = Open(cs);
            return Json(await Hub(e).Explore(q, null, dbType, "popular", 1, 12, null, default))
                .GetProperty("Items").EnumerateArray().Select(i => i.GetProperty("Id").GetString()!).ToArray();
        }

        Assert.Equal(new[] { "p1" }, await Ids("old name"));

        // Ayarlar ucundan ad + motor.
        await using (var w = Open(cs))
            Assert.IsType<OkObjectResult>(await Hub(w, "owner").UpdateSettings("p1", new UpdateRepoSettingsRequest(
                "Renamed Via Settings", null, null, null, null, null, null, null, "MySQL"), default));
        Assert.Empty(await Ids("old name"));
        Assert.Equal(new[] { "p1" }, await Ids("via settings"));
        Assert.Equal(new[] { "p1" }, await Ids(dbType: "mysql"));

        // Senkron ucundan ad.
        await using (var w = Open(cs))
            Assert.IsType<OkObjectResult>(await Auth(w, "owner").SyncProjects(new() { Dto("p1", "Renamed Via Sync") }));
        Assert.Empty(await Ids("via settings"));
        Assert.Equal(new[] { "p1" }, await Ids("via sync"));

        // Fork kendi adıyla aranır.
        await using (var w = Open(cs))
            Assert.IsType<CreatedAtActionResult>(await Hub(w, "stranger").Fork("p1", new ForkRepoRequest("Forked Copy"), default));
        await using var v = Open(cs);
        Assert.Equal(1, await v.Kit.Hub.Repositories.CountAsync(r => r.ProjectName == "Forked Copy" && r.ProjectDbType == "PostgreSQL"));
    }

    // ── Hesap silme sırası ───────────────────────────────────────────────────

    private sealed class RecordingCanceller : ISubscriptionCanceller
    {
        private readonly object _gate = new();
        public List<string> Canceled { get; } = new();
        public Action? OnCall { get; init; }
        public int FailFirst { get; set; }

        public async Task<bool> CancelAsync(string subscriptionId, CancellationToken ct)
        {
            OnCall?.Invoke();
            // Gerçek çağrı gibi: token iptal edildiyse burada patlar.
            await Task.Delay(1, ct);
            lock (_gate)
            {
                if (FailFirst > 0)
                {
                    FailFirst--;
                    return false;
                }
                Canceled.Add(subscriptionId);
            }
            return true;
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<LogLevel> Levels { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Levels.Add(logLevel);
    }

    private static SubscriptionCancellationProcessor Processor(Scope s, ISubscriptionCanceller? canceller, ILogger? logger = null) =>
        new(s.Db, canceller, logger ?? NullLogger.Instance);

    /// <summary>Aboneliği olan, silinecek bir kullanıcı: "member" ekipte, projesi varise devrolur.</summary>
    private async Task<string> SubscribedMemberDatabaseAsync(string name)
    {
        var cs = await NewDatabaseAsync(name);
        await using var s = Open(cs);
        await SeedProjectAsync(s, "team", owner: "member", slug: "teamrepo");
        await s.Db.Users.Where(u => u.Id == "member").ExecuteUpdateAsync(x => x.SetProperty(u => u.StripeSubscriptionId, "sub_1"));
        return cs;
    }

    private static async Task<AccountDeletionResult> DeleteMemberAsync(string cs, ISubscriptionCanceller? canceller, CancellationToken ct = default)
    {
        await using var d = Open(cs);
        return await AccountDeletion(d, canceller).DeleteAsync(await d.Db.Users.SingleAsync(u => u.Id == "member"), ct);
    }

    [RequiresDockerFact]
    public async Task Surec_commit_ile_Stripe_arasinda_olse_de_isci_iptali_tamamlar()
    {
        var cs = await SubscribedMemberDatabaseAsync("f_outbox_death");

        // canceller yok: commit olduktan sonra hiçbir Stripe çağrısı yapılmamış durum, ölen süreçle aynı.
        var result = await DeleteMemberAsync(cs, canceller: null);
        Assert.True(result.Ok, result.Code + " " + result.Error);

        await using var s = Open(cs);
        Assert.False(await s.Db.Users.AnyAsync(u => u.Id == "member"));
        var row = await s.Db.PendingSubscriptionCancellations.AsNoTracking().SingleAsync();
        Assert.Equal("sub_1", row.SubscriptionId);
        Assert.Null(row.CompletedAt);
        var audit = await s.Db.ChangeRequestAuditLogs.AsNoTracking().SingleAsync(a => a.Action == ChangeRequestAuditAction.AccountDeleted);
        Assert.Contains("\"stripe\":\"queued\"", audit.Details);

        var canceller = new RecordingCanceller();
        // Vadesi gelmeden dokunulmaz (ölen işçinin satırı da böyle kilitli kalmaz, süre dolunca alınır).
        Assert.Equal(0, await Processor(s, canceller).ProcessDueAsync(row.NextAttemptAt.AddSeconds(-1), 10, default));
        Assert.Empty(canceller.Canceled);

        Assert.Equal(1, await Processor(s, canceller).ProcessDueAsync(row.NextAttemptAt.AddSeconds(1), 10, default));
        Assert.Equal(new[] { "sub_1" }, canceller.Canceled);
        Assert.NotNull((await s.Db.PendingSubscriptionCancellations.AsNoTracking().SingleAsync()).CompletedAt);

        // Tamamlanan bir daha denenmez.
        Assert.Equal(0, await Processor(s, canceller).ProcessDueAsync(DateTime.UtcNow.AddYears(1), 10, default));
        Assert.Single(canceller.Canceled);
    }

    [RequiresDockerFact]
    public async Task Istemci_kopsa_da_iptal_ve_denetim_kaydi_tamamlanir()
    {
        var cs = await SubscribedMemberDatabaseAsync("f_outbox_abort");
        using var client = new CancellationTokenSource();
        // İstemci tam Stripe çağrısı sırasında kopuyor. Eski kod bu token'ı geçiriyordu ve çağrı iptal oluyordu.
        var canceller = new RecordingCanceller { OnCall = client.Cancel };

        var result = await DeleteMemberAsync(cs, canceller, client.Token);

        Assert.True(client.IsCancellationRequested);
        Assert.True(result.Ok, result.Code + " " + result.Error);
        Assert.Equal(new[] { "sub_1" }, canceller.Canceled);
        await using var v = Open(cs);
        Assert.NotNull((await v.Db.PendingSubscriptionCancellations.AsNoTracking().SingleAsync()).CompletedAt);
        var audit = await v.Db.ChangeRequestAuditLogs.AsNoTracking().SingleAsync(a => a.Action == ChangeRequestAuditAction.AccountDeleted);
        Assert.Contains("\"stripe\":\"canceled\"", audit.Details);
    }

    [RequiresDockerFact]
    public async Task Stripe_once_basarisiz_sonra_basarili_geri_cekilmeyle_tamamlanir_esik_loglanir()
    {
        var cs = await SubscribedMemberDatabaseAsync("f_outbox_retry");
        var canceller = new RecordingCanceller { FailFirst = SubscriptionCancellationProcessor.AlertAfterAttempts - 1 };

        Assert.True((await DeleteMemberAsync(cs, canceller)).Ok);

        await using var s = Open(cs);
        var row = await s.Db.PendingSubscriptionCancellations.AsNoTracking().SingleAsync();
        Assert.Equal(1, row.Attempts);
        Assert.Null(row.CompletedAt);
        Assert.NotNull(row.LastAttemptAt);
        Assert.True(row.NextAttemptAt > row.LastAttemptAt);

        // Eşiğe kadar başarısız: 7. deneme ERROR değil, 8. uyarı eşiği. Sonra başarılı.
        canceller.FailFirst = SubscriptionCancellationProcessor.AlertAfterAttempts - 1;
        var logger = new CapturingLogger();
        for (var i = 0; i < SubscriptionCancellationProcessor.AlertAfterAttempts - 1; i++)
        {
            var due = (await s.Db.PendingSubscriptionCancellations.AsNoTracking().SingleAsync()).NextAttemptAt;
            Assert.Equal(1, await Processor(s, canceller, logger).ProcessDueAsync(due.AddSeconds(1), 10, default));
        }
        var stuck = await s.Db.PendingSubscriptionCancellations.AsNoTracking().SingleAsync();
        Assert.Equal(SubscriptionCancellationProcessor.AlertAfterAttempts, stuck.Attempts);
        Assert.Null(stuck.CompletedAt);
        Assert.Contains(LogLevel.Error, logger.Levels);
        Assert.True(stuck.NextAttemptAt - stuck.LastAttemptAt!.Value <= TimeSpan.FromHours(6));

        Assert.Equal(1, await Processor(s, canceller).ProcessDueAsync(stuck.NextAttemptAt.AddSeconds(1), 10, default));
        Assert.NotNull((await s.Db.PendingSubscriptionCancellations.AsNoTracking().SingleAsync()).CompletedAt);
        Assert.Equal(new[] { "sub_1" }, canceller.Canceled);
    }

    [RequiresDockerFact]
    public async Task Iki_isci_ayni_satiri_iki_kez_almaz_SKIP_LOCKED()
    {
        var cs = await NewDatabaseAsync("f_outbox_skip");
        await using (var conn = new NpgsqlConnection(cs))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO "PendingSubscriptionCancellations" ("Id","SubscriptionId","CreatedAt","Attempts","NextAttemptAt")
                SELECT 'r' || i, 'sub_' || i, now(), 0, now() - interval '1 minute' FROM generate_series(1, 30) i;
                """, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        var canceller = new RecordingCanceller();
        var counts = await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            await using var s = Open(cs);
            return await Processor(s, canceller).ProcessDueAsync(DateTime.UtcNow, 100, default);
        }));

        Assert.Equal(30, counts.Sum());
        Assert.Equal(30, canceller.Canceled.Distinct().Count());
        Assert.Equal(30, canceller.Canceled.Count);
        await using var v = Open(cs);
        Assert.Equal(30, await v.Db.PendingSubscriptionCancellations.CountAsync(c => c.CompletedAt != null && c.Attempts == 1));
    }

    [RequiresDockerFact]
    public async Task Saklama_acik_iptali_silmez_tamamlanani_siler()
    {
        var cs = await NewDatabaseAsync("f_outbox_retention");
        await using var s = Open(cs);
        var old = DateTime.UtcNow.AddYears(-3);
        s.Db.PendingSubscriptionCancellations.AddRange(
            new PendingSubscriptionCancellation { Id = "open-old", SubscriptionId = "a", CreatedAt = old, NextAttemptAt = old },
            new PendingSubscriptionCancellation { Id = "done-old", SubscriptionId = "b", CreatedAt = old, NextAttemptAt = old, CompletedAt = old },
            new PendingSubscriptionCancellation { Id = "done-new", SubscriptionId = "c", CreatedAt = old, NextAttemptAt = old, CompletedAt = DateTime.UtcNow.AddDays(-1) });
        await s.Db.SaveChangesAsync();

        var result = await DataRetentionService.PurgeAsync(
            s.Db, DataRetentionService.RetentionOptions.From(new ConfigurationBuilder().Build()),
            NullLogger.Instance, DateTime.UtcNow, TimeSpan.Zero, default, s.Kit.OpsRetention);

        Assert.Equal(1, result["PendingSubscriptionCancellations"]);
        var left = await s.Db.PendingSubscriptionCancellations.AsNoTracking().Select(c => c.Id).OrderBy(x => x).ToListAsync();
        Assert.Equal(new[] { "done-new", "open-old" }, left);
    }

    private static AccountDeletionService AccountDeletion(Scope s, ISubscriptionCanceller? canceller = null) =>
        new(s.Db, Users(s.Db), ProjectDeletionTestKit.Create(s.Db, s.Kit), s.Kit.HubService, s.Kit.Transaction,
            NullLogger<AccountDeletionService>.Instance, s.Kit.Feedback, s.Kit.Projects, canceller);

    [RequiresDockerFact]
    public async Task Hesap_silme_varis_slug_cakismasi()
    {
        var cs = await NewDatabaseAsync("f_heir_slug");
        await using var s = Open(cs);
        // Slug addan ve yalnızca sahip içinde benzersiz üretildiği için aynı adlı projeler olağan.
        await SeedProjectAsync(s, "heir-own", owner: "owner", slug: "same-name");
        await SeedProjectAsync(s, "heir-own-2", owner: "owner", slug: "same-name-2");
        await SeedProjectAsync(s, "team", owner: "member", slug: "same-name");
        await SeedProjectAsync(s, "team-free", owner: "member", slug: "other-name");

        await using var d = Open(cs);
        var result = await AccountDeletion(d).DeleteAsync(await d.Db.Users.SingleAsync(u => u.Id == "member"), default);
        Assert.True(result.Ok, result.Code + " " + result.Error);

        await using var v = Open(cs);
        Assert.False(await v.Db.Users.AnyAsync(u => u.Id == "member"));
        var team = (await v.Kit.HubService.GetRepositoryAsync("team"))!;
        Assert.Equal("owner", team.OwnerUserId);
        Assert.Equal("same-name-3", team.Slug);
        Assert.Equal("same-name", (await v.Kit.HubService.GetRepositoryAsync("heir-own"))!.Slug);
        Assert.Equal("same-name-2", (await v.Kit.HubService.GetRepositoryAsync("heir-own-2"))!.Slug);
        Assert.Equal("other-name", (await v.Kit.HubService.GetRepositoryAsync("team-free"))!.Slug);
        Assert.Equal("team", await v.Kit.HubService.ResolveProjectIdAsync("owner", "same-name-3"));
    }

    [RequiresDockerFact]
    public async Task Hesap_silme_islemi_duserse_Stripe_iptal_edilmez_devir_geri_alinir_yeniden_deneme_tamamlar()
    {
        var cs = await NewDatabaseAsync("f_account_order");
        await using (var s = Open(cs))
        {
            await SeedProjectAsync(s, "team", owner: "member", slug: "teamrepo");
            await s.Db.Users.Where(u => u.Id == "member").ExecuteUpdateAsync(x => x.SetProperty(u => u.StripeSubscriptionId, "sub_1"));
        }

        await using (var conn = new NpgsqlConnection(cs))
        {
            await conn.OpenAsync();
            await using var fault = new NpgsqlCommand("""
                CREATE FUNCTION deny_user_delete() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test fault'; END $$;
                CREATE TRIGGER deny_user_delete BEFORE DELETE ON "AspNetUsers" FOR EACH ROW EXECUTE FUNCTION deny_user_delete();
                """, conn);
            await fault.ExecuteNonQueryAsync();
        }

        var canceller = new RecordingCanceller();
        await using (var d = Open(cs))
        {
            var failed = await AccountDeletion(d, canceller).DeleteAsync(await d.Db.Users.SingleAsync(u => u.Id == "member"), default);
            // Genel 500 değil: ne olduğunu ve yeniden denemenin güvenli olduğunu söyleyen 503.
            Assert.False(failed.Ok);
            Assert.Equal(503, failed.StatusCode);
            Assert.Equal("ACCOUNT_DELETE_INCOMPLETE", failed.Code);
            Assert.Contains("safe to try again", failed.Error);
        }

        Assert.Empty(canceller.Canceled);
        await using (var v = Open(cs))
        {
            Assert.True(await v.Db.Users.AnyAsync(u => u.Id == "member"));
            Assert.Equal("member", (await v.Kit.HubService.GetRepositoryAsync("team"))!.OwnerUserId);
            Assert.Equal("member", (await v.Db.Studio().Projects.AsNoTracking().SingleAsync(p => p.Id == "team")).UserId);
            // İşlem düştü: outbox satırı da yok, silinmemiş kullanıcının aboneliği iptal edilmez.
            Assert.False(await v.Db.PendingSubscriptionCancellations.AnyAsync());
        }

        await using (var conn = new NpgsqlConnection(cs))
        {
            await conn.OpenAsync();
            await using var clear = new NpgsqlCommand("""DROP TRIGGER deny_user_delete ON "AspNetUsers";""", conn);
            await clear.ExecuteNonQueryAsync();
        }

        await using (var retry = Open(cs))
        {
            var result = await AccountDeletion(retry, canceller).DeleteAsync(await retry.Db.Users.SingleAsync(u => u.Id == "member"), default);
            Assert.True(result.Ok, result.Code + " " + result.Error);
        }

        Assert.Equal(new[] { "sub_1" }, canceller.Canceled);
        await using var after = Open(cs);
        Assert.Equal("owner", (await after.Kit.HubService.GetRepositoryAsync("team"))!.OwnerUserId);
        var audit = await after.Db.ChangeRequestAuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == ChangeRequestAuditAction.AccountDeleted);
        Assert.Contains("\"stripe\":\"canceled\"", audit.Details);
    }

    [RequiresDockerFact]
    public async Task Iptal_edilmis_paylasim_satirlari_saklama_suresi_sonunda_silinir_etkinler_kalir()
    {
        var cs = await NewDatabaseAsync("f_share_purge");
        await using var s = Open(cs);
        await SeedProjectAsync(s, "p1");
        var live = await s.Kit.StudioService.CreateShareLinkAsync("p1");
        await s.Kit.SeedShareAsync("p1", "old-revoked", DateTime.UtcNow.AddDays(-100));
        await s.Kit.SeedShareAsync("p1", "new-revoked", DateTime.UtcNow.AddDays(-1));

        Assert.Equal(1, await s.Kit.StudioService.PurgeRevokedSharesAsync(DateTime.UtcNow.AddDays(-90)));

        Assert.NotNull(await s.Kit.StudioService.GetSharedProjectByTokenHashAsync(ShareTokenHasher.Hash(live)));
        Assert.Equal(2, await s.Kit.Studio.ProjectShares.CountAsync());
        Assert.False(await s.Kit.Studio.ProjectShares.AnyAsync(x => x.ShareTokenHash == ShareTokenHasher.Hash("old-revoked")));
    }

    // ── Sırlar ───────────────────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task Baglanti_sirri_kiraci_disina_cikmiyor_listede_yok_rotasyon_yeni_tabloda()
    {
        var cs = await NewDatabaseAsync("f_secret");
        await using var s = Open(cs);
        await SeedProjectAsync(s, "p1");
        await SeedProjectAsync(s, "p2", owner: "stranger", org: "org-b");
        var authority = s.Kit.Authority(_protector);

        const string conn = "Host=db.example.com;Database=d;Username=u;Password=very-secret-value";
        Assert.True(await authority.SetConnectionAsync("p1", conn, "PostgreSQL"));

        // Şifreli saklanıyor; kiracı dışına çözülmüyor.
        var stored = await s.Kit.Studio.ProjectConnections.AsNoTracking().SingleAsync(c => c.ProjectId == "p1");
        Assert.DoesNotContain("very-secret-value", stored.EncryptedConnectionString);
        Assert.Equal(conn, (await authority.ResolveForUserAsync("p1", "owner"))!.ConnectionString);
        Assert.Equal(conn, (await authority.ResolveForUserAsync("p1", "member"))!.ConnectionString);
        Assert.Null(await authority.ResolveForUserAsync("p1", "stranger"));
        Assert.Null(await authority.ResolveForUserAsync("p2", "owner"));

        // AAD: şifreli metni başka projeye kopyalamak çözülmez.
        await s.Kit.SeedConnectionAsync("p2", stored.EncryptedConnectionString);
        await Assert.ThrowsAnyAsync<System.Security.Cryptography.CryptographicException>(() => authority.ResolveAsync("p2"));

        // API yanıtları: bağlantı dizesi ve şifreli metin hiçbir biçimde dönmüyor.
        var list = JsonSerializer.Serialize(((OkObjectResult)await Auth(s, "owner").GetProjects()).Value);
        Assert.DoesNotContain(stored.EncryptedConnectionString!, list);
        Assert.DoesNotContain("very-secret-value", list);
        Assert.Contains("\"hasConnection\":true", list);
        Assert.DoesNotContain("\"EncryptedConnectionString\"", JsonSerializer.Serialize(stored));

        // Rotasyon yeni tabloyu yeniden yazar; eski çözülür, yeni anahtar başa geçer.
        var rotatingConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:ConnectionEncryptionKey"] = "phase-14-2b-flow-test-key-at-least-32-characters",
            ["Security:ConnectionEncryptionKeys:0:Id"] = "k2",
            ["Security:ConnectionEncryptionKeys:0:Key"] = "phase-14-2b-second-key-at-least-32-characters-long",
        }).Build();
        var rotating = new AesGcmConnectionSecretProtector(rotatingConfig);
        await s.Kit.Studio.ProjectConnections.Where(c => c.ProjectId == "p2").ExecuteDeleteAsync();
        var r = await ConnectionSecretReencryptor.ReencryptAsync(s.Kit.Studio, rotating);
        Assert.Equal(1, r.Reencrypted);
        var rotated = await s.Kit.Studio.ProjectConnections.AsNoTracking().SingleAsync(c => c.ProjectId == "p1");
        Assert.NotEqual(stored.EncryptedConnectionString, rotated.EncryptedConnectionString);
        Assert.Equal(conn, rotating.Unprotect(rotated.EncryptedConnectionString!, associatedData: "p1"));
        Assert.Equal(0, (await ConnectionSecretReencryptor.ReencryptAsync(s.Kit.Studio, rotating)).Reencrypted);

        // Temizleme: onay korunur, bağlantı gider.
        await authority.SetDeskSqlEnabledAsync("p1", true);
        await authority.ClearConnectionAsync("p1");
        var meta = await s.Kit.StudioService.GetConnectionMetadataAsync("p1");
        Assert.False(meta.HasConnection);
        Assert.True(meta.AllowDeskSql);
    }

    [RequiresDockerFact]
    public async Task Gateway_sakli_baglantiyi_anahtarla_ve_oturumla_cozuyor_SSRF_yine_kapali()
    {
        var cs = await NewDatabaseAsync("f_gateway");
        await using var s = Open(cs);
        await SeedProjectAsync(s, "p1");

        // Hedef: aynı container (Development + AllowPrivateDbHosts ile izinli).
        var devPolicy = new DbHostAccessPolicy(new DevEnv(), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:AllowPrivateDbHosts"] = "true",
        }).Build(), NullLogger<DbHostAccessPolicy>.Instance);
        var authority = s.Kit.Authority(_protector, devPolicy);
        await authority.SetConnectionAsync("p1", cs, "PostgreSQL");

        var (key, raw) = GatewayAccess.CreateKey("p1", "key", "owner", canWrite: false, expiresAt: null);
        s.Db.GatewayApiKeys.Add(key);
        await s.Db.SaveChangesAsync();

        GatewayController Gateway(IDbHostAccessPolicy policy, string? user, string? apiKey)
        {
            var http = Http(user);
            if (apiKey != null) http.Request.Headers["X-Namines-Key"] = apiKey;
            var groq = new Namines.Infrastructure.AI.GroqAIService(new HttpClient(), _config, new HttpContextAccessor { HttpContext = http }, new MemoryCache(new MemoryCacheOptions()));
            return new GatewayController(
                gateway: null!, context: s.Db, configuration: _config, groq: groq,
                logger: NullLogger<GatewayController>.Instance, quota: new AiQuotaService(s.Db, _config),
                secrets: authority, studio: s.Kit.StudioService,
                introspection: new DbIntrospectionService(NullLogger<DbIntrospectionService>.Instance, policy),
                workbench: null!, authority: s.Kit.DefaultAuthority, projects: s.Kit.Projects, branches: s.Kit.Branches)
            {
                ControllerContext = new ControllerContext { HttpContext = http },
            };
        }

        // Oturum yolu: üye şemayı görüyor, yabancı görmüyor. Hiçbir yanıtta bağlantı dizesi yok.
        var ok = Assert.IsType<OkObjectResult>(await Gateway(devPolicy, "owner", null).Schema("p1", default));
        var body = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("AspNetUsers", body);
        Assert.DoesNotContain(new NpgsqlConnectionStringBuilder(cs).Password!, body);
        Assert.DoesNotContain("Password=", body);
        Assert.IsNotType<OkObjectResult>(await Gateway(devPolicy, "stranger", null).Schema("p1", default));

        // Anahtar yolu: projeye bağlı anahtar saklı bağlantıyı kullanır.
        Assert.IsType<OkObjectResult>(await Gateway(devPolicy, null, raw).Schema(null, default));

        // SSRF: sıkı politikada aynı saklı bağlantı (127.0.0.1) reddedilir, 200 dönmez, host sızmaz.
        var strict = new DbHostAccessPolicy(new ProdEnv(), new ConfigurationBuilder().Build(), NullLogger<DbHostAccessPolicy>.Instance);
        var denied = await Gateway(strict, "owner", null).Schema("p1", default);
        Assert.IsNotType<OkObjectResult>(denied);
        Assert.DoesNotContain(new NpgsqlConnectionStringBuilder(cs).Password!, JsonSerializer.Serialize((denied as ObjectResult)?.Value));
        Assert.NotNull((await s.Kit.Authority(_protector, strict).ResolveAsync("p1"))!.HostPolicyDenyReason);

        // SQL konsolu kapısı: bayrak kapalıyken anahtarla ham SQL reddi metadata'dan okunur.
        Assert.False((await s.Kit.StudioService.GetConnectionMetadataAsync("p1")).AllowDeskSql);
    }

    private sealed class DevEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class ProdEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
