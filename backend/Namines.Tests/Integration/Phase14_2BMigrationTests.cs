using Namines.Identity.Infrastructure.Persistence;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Security;
using Namines.Tests.TestSupport;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Namines.Tests.Integration;

/// <summary>Container başına bir kez: her test kendi veritabanını açar.</summary>
public sealed class Phase14_2BPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        if (!DockerAvailable.Value) return;
        await _container.StartAsync();
        PostgresTestPools.Reset();
    }

    public async Task DisposeAsync()
    {
        if (DockerAvailable.Value) await _container.DisposeAsync();
    }

    /// <summary>deploy/sql/roles.sql'i gerçek psql ile, üretimdeki gibi çalıştırır.</summary>
    public async Task ApplyRolesAsync(string database, string script)
    {
        await _container.CopyAsync(System.Text.Encoding.UTF8.GetBytes(script), "/tmp/roles.sql");
        var result = await _container.ExecAsync(new[]
        {
            "psql", "-U", "postgres", "-d", database, "-v", "ON_ERROR_STOP=1",
            "-v", "migrator_password='migrator-pw'", "-v", "app_password='app-pw'", "-v", "dbname=" + database,
            "-f", "/tmp/roles.sql",
        });
        Assert.True(result.ExitCode == 0, result.Stderr);
    }

    public async Task<string> CreateDatabaseAsync(string name)
    {
        await using var admin = new NpgsqlConnection(_container.GetConnectionString());
        await admin.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
        await cmd.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name, CommandTimeout = 300 }.ConnectionString;
    }
}

/// <summary>
/// Faz 14.2B migration'ı GERÇEK PostgreSQL'de: boş veritabanı, gerçekçi eski veriyle yükseltme
/// (alan alan), geri alma + yeniden yükseltme, katalogda fiziksel kanıt, yarım kalmış eski üreteç
/// artığına karşı davranış ve modül bağlamlarının eşlediği her şeyin var olması.
/// </summary>
[Collection("Docker")]
public sealed class Phase14_2BMigrationTests : IClassFixture<Phase14_2BPostgresFixture>
{
    private const string BeforeDecomposition = "20261030215219_AddDeletionAuditActions";

    private static readonly string[] MovedColumns =
    {
        "EncryptedConnectionString", "ConnectionDbType", "AllowDeskSql", "ShareToken", "ShareTokenHash",
        "Slug", "Description", "Visibility", "Category", "Tags", "StarCount", "ForkCount",
        "ForkedFromProjectId", "LinkedGithubRepo",
    };

    private readonly Phase14_2BPostgresFixture _pg;
    private readonly IConnectionSecretProtector _protector = new AesGcmConnectionSecretProtector(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:ConnectionEncryptionKey"] = "phase-14-2b-migration-test-key-at-least-32-chars",
        }).Build());

    public Phase14_2BMigrationTests(Phase14_2BPostgresFixture pg) => _pg = pg;

    private static AuthDbContext Context(string cs) =>
        new(new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(cs, o => o.CommandTimeout(300)).Options);

    private static async Task ExecAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ScalarAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (await cmd.ExecuteScalarAsync())?.ToString();
    }

    private static async Task<long> CountAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    /// <summary>Sorgunun satırlarını sıralı JSON dizgeleri olarak döndürür: alan alan karşılaştırma için.</summary>
    private static async Task<List<string>> DumpAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT row_to_json(t)::text FROM ({sql}) t ORDER BY 1", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return rows;
    }

    private static string BackendFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.GetFiles("Namines.sln").Any()) dir = dir.Parent;
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }

    private static string Literal(string? value) => value is null ? "NULL" : "'" + value.Replace("'", "''") + "'";

    /// <summary>
    /// Kayıp-riski taşıyan her uç durum: AAD'li ve AAD'siz sır, düz metin/yalnız özet/ikisi birden
    /// token, karışık CSV, Türkçe harfler, fork, yetim fork, slug'sız ve boş slug'lı proje, bağlantısız
    /// SQL konsolu onayı, sahipler arası aynı slug.
    /// </summary>
    private async Task SeedLegacyAsync(string cs)
    {
        await using (var old = Context(cs))
            await old.GetService<IMigrator>().MigrateAsync(BeforeDecomposition);

        const string T = "'2026-01-02 03:04:05.123456+00'";
        await ExecAsync(cs, $"""
            INSERT INTO "AspNetUsers" ("Id","Type","CreatedAt","IsDev","EmailConfirmed","PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount","UserName","NormalizedUserName")
            VALUES ('u1',1,{T},false,true,false,false,true,0,'una','UNA'),
                   ('u2',1,{T},false,true,false,false,true,0,'veli','VELI');
            INSERT INTO "Organizations" ("Id","Name","IsPersonal","CreatedByUserId","CreatedAt") VALUES ('org1','Org',false,'u1',{T});
            INSERT INTO "OrganizationMembers" ("OrganizationId","UserId","Role","JoinedAt") VALUES ('org1','u1',0,{T});
            -- Eski FK varken yetim fork oluşamaz; yine de geldiği yer (elle veri, eski dump) güvenli olmalı.
            ALTER TABLE "CloudProjects" DROP CONSTRAINT "FK_CloudProjects_CloudProjects_ForkedFromProjectId";
            """);

        async Task Project(string id, string owner = "u1", string? org = "org1", string? enc = null, string? connType = null,
            bool desk = false, string? token = null, string? hash = null, string? slug = null, string? desc = null,
            int vis = 0, string cat = "General", string? tags = null, int stars = 0, int forks = 0, string? fork = null, string? github = null)
        {
            await ExecAsync(cs, $"""
                INSERT INTO "CloudProjects" ("Id","Name","DbType","SchemaJson","NodePositionsJson","UserId","OrganizationId","CreatedAt","UpdatedAt",
                    "AutoApproveSafeChanges","EncryptedConnectionString","ConnectionDbType","AllowDeskSql","ShareToken","ShareTokenHash","Slug","Description",
                    "Visibility","Category","Tags","StarCount","ForkCount","ForkedFromProjectId","LinkedGithubRepo")
                VALUES ({Literal(id)},{Literal("Name " + id)},'PostgreSQL','{"{"}"tables":[]{"}"}','{"{"}{"}"}',{Literal(owner)},{Literal(org)},{T},{T},
                    false,{Literal(enc)},{Literal(connType)},{(desk ? "true" : "false")},{Literal(token)},{Literal(hash)},{Literal(slug)},{Literal(desc)},
                    {vis},{Literal(cat)},{Literal(tags)},{stars},{forks},{Literal(fork)},{Literal(github)});
                """);
        }

        await Project("p-secret", enc: _protector.Protect("Host=a.example.com;Password=secret-a", associatedData: "p-secret"),
            connType: "PostgreSQL", desk: true, token: "legacy-plain-token", slug: "alpha", desc: "Alpha desc", vis: 2, cat: "SaaS",
            tags: " PostgreSQL, 3nf ,,postgresql, multi-tenant,  3NF", stars: 7, forks: 2, github: "acme/repo");
        await Project("p-noaad", enc: _protector.Protect("Host=b.example.com;Password=secret-b"), connType: "MSSQL", vis: 1, slug: "bravo");
        await Project("p-hashonly", hash: ShareTokenHasher.Hash("raw-hash-only"));
        await Project("p-both", token: "both-token", hash: ShareTokenHasher.Hash("both-token"), slug: "");
        await Project("p-fork", fork: "p-secret", slug: "alpha-fork", tags: "");
        await Project("p-orphan", fork: "gone-project", slug: "orphan");
        await Project("p-desk-only", desk: true);
        await Project("p-turkish", tags: "İstanbul,ÇAY,çay,Işık, ışık ,ÜÇ");
        // Bağlam duyarlı küçültme (sondaki Yunanca Σ) ve eski gözcü karakterle çakışma: C# ile aynı kalmalı.
        await Project("p-greek", tags: "ΑΣ,ασ,\uE000x,İx");
        await Project("p-other-owner", owner: "u2", org: null, slug: "alpha", vis: 2, desc: "Same slug, other owner");

        await ExecAsync(cs, $"""
            INSERT INTO "RepoStars" ("ProjectId","UserId","CreatedAt") VALUES ('p-secret','u2',{T});
            INSERT INTO "Branches" ("Id","ProjectId","Name","IsDefault","CreatedByUserId","CreatedAt") VALUES ('b1','p-secret','main',true,'u1',{T});
            """);
    }

    private const string RepositoriesDump = """
        SELECT "ProjectId","OwnerUserId","ProjectName","ProjectDbType","Slug","Description","Visibility","Category","StarCount","ForkCount",
               "ForkedFromProjectId","LinkedGithubRepo","CreatedAt" FROM hub.repositories
        """;
    private const string ConnectionsDump = """SELECT "ProjectId","EncryptedConnectionString","ConnectionDbType","AllowDeskSql" FROM studio.project_connections""";
    private const string SharesDump = """SELECT "ProjectId","ShareTokenHash","RevokedAt" FROM studio.project_shares""";
    private const string TagsDump = """SELECT "ProjectId","Tag","DisplayTag","Position" FROM hub.project_tags""";
    private const string ProjectsDump = """SELECT "Id","Name","DbType","SchemaJson","NodePositionsJson","UserId","OrganizationId","CreatedAt","AutoApproveSafeChanges" FROM studio.projects""";

    [RequiresDockerFact]
    public async Task Bos_veritabani_tum_zinciri_uyguluyor_ve_tablolar_gercekten_var()
    {
        var cs = await _pg.CreateDatabaseAsync("b_fresh");
        await using var ctx = Context(cs);
        await ctx.Database.MigrateAsync();

        Assert.Empty(await ctx.Database.GetPendingMigrationsAsync());
        foreach (var table in new[]
            {
                "studio.projects", "studio.project_connections", "studio.project_shares", "hub.repositories", "hub.project_tags",
                "hub.issue_labels", "hub.repo_issue_labels",
                "gateway.api_key_allowed_origins", "gateway.api_key_allowed_ips", "gateway.table_masked_columns",
            })
            Assert.Equal(1, await CountAsync(cs, $"SELECT count(*) FROM (SELECT to_regclass('{table}') r) x WHERE r IS NOT NULL"));
        await AssertLegacyShapeGoneAsync(cs);

        // Tekrar çalıştırmak hiçbir şey yapmaz (EF geçmişi).
        var applied = await CountAsync(cs, "SELECT count(*) FROM \"__EFMigrationsHistory\"");
        await ctx.Database.MigrateAsync();
        Assert.Equal(applied, await CountAsync(cs, "SELECT count(*) FROM \"__EFMigrationsHistory\""));
    }

    /// <summary>Katalog kanıtı: eski tablo, kolonlar, indeksler, kısıtlar FİZİKSEL olarak yok.</summary>
    private static async Task AssertLegacyShapeGoneAsync(string cs)
    {
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM pg_class WHERE relname = 'CloudProjects'"));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM pg_class WHERE relname LIKE '%CloudProjects%'"));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM pg_constraint WHERE conname LIKE '%CloudProjects%'"));

        var columns = await DumpAsync(cs, """
            SELECT a.attname AS name FROM pg_attribute a
            WHERE a.attrelid = 'studio.projects'::regclass AND a.attnum > 0 AND NOT a.attisdropped
            """);
        var names = columns.Select(c => JsonDocument.Parse(c).RootElement.GetProperty("name").GetString()!).ToHashSet();
        Assert.Equal(
            new HashSet<string> { "Id", "Name", "DbType", "SchemaJson", "NodePositionsJson", "UserId", "OrganizationId", "CreatedAt", "UpdatedAt", "AutoApproveSafeChanges" },
            names);
        foreach (var moved in MovedColumns) Assert.DoesNotContain(moved, names);

        // Diğer tablolardan gelen FK'ler yeni tabloyu gösteriyor.
        Assert.True(await CountAsync(cs, "SELECT count(*) FROM pg_constraint WHERE contype = 'f' AND confrelid = 'studio.projects'::regclass") >= 15);
        // Düz metin token kolonu hiçbir tabloda yok.
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM information_schema.columns WHERE column_name = 'ShareToken'"));
    }

    [RequiresDockerFact]
    public async Task Eski_veri_alan_alan_kayipsiz_tasiniyor_ve_duz_metin_token_kalmiyor()
    {
        var cs = await _pg.CreateDatabaseAsync("b_seeded");
        await SeedLegacyAsync(cs);

        var sourceBefore = await DumpAsync(cs, """
            SELECT "Id","Name","DbType","SchemaJson","NodePositionsJson","UserId","OrganizationId","CreatedAt","AutoApproveSafeChanges" FROM "CloudProjects"
            """);

        await using var ctx = Context(cs);
        await ctx.Database.MigrateAsync();
        await AssertLegacyShapeGoneAsync(cs);

        // Çekirdek alanlara dokunulmadı (xmin/UpdatedAt hariç her şey aynı).
        Assert.Equal(sourceBefore, await DumpAsync(cs, ProjectsDump));

        var kit = TestModules.For(ctx);

        // ── Repo: her projeye tam bir satır, alanlar birebir ──
        Assert.Equal(10, await CountAsync(cs, "SELECT count(*) FROM hub.repositories"));
        Assert.Equal(0, await CountAsync(cs, """SELECT count(*) FROM hub.repositories r JOIN studio.projects p ON p."Id" = r."ProjectId" WHERE r."ProjectName" <> p."Name" OR r."ProjectDbType" <> p."DbType" """));
        var alpha = (await kit.HubService.GetRepositoryAsync("p-secret"))!;
        Assert.Equal(("alpha", "Alpha desc", ProjectVisibility.Public, "SaaS", 7, 2, "acme/repo", "u1"),
            (alpha.Slug, alpha.Description, alpha.Visibility, alpha.Category, alpha.StarCount, alpha.ForkCount, alpha.LinkedGithubRepo, alpha.OwnerUserId));
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, 123, 456, DateTimeKind.Utc), alpha.CreatedAt.ToUniversalTime());

        Assert.Null((await kit.HubService.GetRepositoryAsync("p-hashonly"))!.Slug);
        Assert.Equal("", (await kit.HubService.GetRepositoryAsync("p-both"))!.Slug);
        Assert.Equal("p-secret", (await kit.HubService.GetRepositoryAsync("p-fork"))!.ForkedFromProjectId);
        Assert.Null((await kit.HubService.GetRepositoryAsync("p-orphan"))!.ForkedFromProjectId);
        Assert.Equal(ProjectVisibility.Team, (await kit.HubService.GetRepositoryAsync("p-noaad"))!.Visibility);
        // (sahip, slug) benzersizliği: iki farklı sahipte aynı slug yaşıyor, çözüm sahibe göre.
        Assert.Equal("p-secret", await kit.HubService.ResolveProjectIdAsync("u1", "alpha"));
        Assert.Equal("p-other-owner", await kit.HubService.ResolveProjectIdAsync("u2", "alpha"));

        // ── Bağlantı: şifreli metin yeniden şifrelenmeden, AAD proje kimliği olduğu için çözülür ──
        var authority = kit.Authority(_protector);
        var secret = (await authority.ResolveAsync("p-secret"))!;
        Assert.Equal("Host=a.example.com;Password=secret-a", secret.ConnectionString);
        Assert.True(secret.AllowDeskSql);
        Assert.Equal("Host=b.example.com;Password=secret-b", (await authority.ResolveAsync("p-noaad"))!.ConnectionString);
        Assert.Equal("MSSQL", (await authority.ResolveAsync("p-noaad"))!.ConnectionDbType);

        // Bağlantısız onay kaybolmadı; bağlantısı ve onayı olmayan projeye satır açılmadı.
        var deskOnly = await kit.StudioService.GetConnectionMetadataAsync("p-desk-only");
        Assert.True(deskOnly.AllowDeskSql);
        Assert.False(deskOnly.HasConnection);
        Assert.Equal(3, await CountAsync(cs, "SELECT count(*) FROM studio.project_connections"));

        // ── Paylaşım: mevcut URL'ler çalışıyor, düz metin DB'de yok ──
        foreach (var raw in new[] { "legacy-plain-token", "raw-hash-only", "both-token" })
            Assert.NotNull(await kit.StudioService.GetSharedProjectByTokenHashAsync(ShareTokenHasher.Hash(raw)));
        Assert.Equal(3, await CountAsync(cs, "SELECT count(*) FROM studio.project_shares"));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM studio.project_shares WHERE \"ShareTokenHash\" IN ('legacy-plain-token','both-token','raw-hash-only')"));
        Assert.Equal(1, await CountAsync(cs, $"SELECT count(*) FROM studio.project_shares WHERE \"ProjectId\" = 'p-secret' AND \"ShareTokenHash\" = '{ShareTokenHasher.Hash("legacy-plain-token")}'"));

        // ── Etiket: kırpma, boş atma, büyük/küçük yineleme, ilk yazım ve sıra ──
        Assert.Equal("PostgreSQL,3nf,multi-tenant", await kit.HubService.GetTagsCsvAsync("p-secret"));
        Assert.Null(await kit.HubService.GetTagsCsvAsync("p-fork"));
        Assert.Null(await kit.HubService.GetTagsCsvAsync("p-hashonly"));
        Assert.Equal("İstanbul,ÇAY,Işık,ışık,ÜÇ", await kit.HubService.GetTagsCsvAsync("p-turkish"));
        Assert.Equal("ΑΣ,\uE000x,İx", await kit.HubService.GetTagsCsvAsync("p-greek"));

        // SQL'in ürettiği anahtar, uygulamanın ürettiğiyle AYNI: yoksa arama eski satırları bulamaz.
        var tags = await kit.Hub.ProjectTags.AsNoTracking().ToListAsync();
        Assert.NotEmpty(tags);
        Assert.All(tags, t => Assert.Equal(ProjectTagRules.Key(t.DisplayTag), t.Tag));

        // ── Referanslar yeni tablonun peşinden gitti ──
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"RepoStars\" s JOIN studio.projects p ON p.\"Id\" = s.\"ProjectId\""));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"Branches\" b JOIN studio.projects p ON p.\"Id\" = b.\"ProjectId\""));

        // ── Düz metin hiçbir yeni tabloda yok (satır metni üzerinde tarama) ──
        foreach (var table in new[] { "studio.projects", "studio.project_connections", "studio.project_shares", "hub.repositories" })
        {
            var text = string.Join("\n", await DumpAsync(cs, $"SELECT * FROM {table}"));
            Assert.DoesNotContain("legacy-plain-token", text);
            Assert.DoesNotContain("both-token", text);
            Assert.DoesNotContain("secret-a", text);
            Assert.DoesNotContain("secret-b", text);
        }
    }

    [RequiresDockerFact]
    public async Task Geri_alma_veriyi_geri_koyuyor_ve_yeniden_yukseltme_ayni_sonucu_veriyor()
    {
        var cs = await _pg.CreateDatabaseAsync("b_updown");
        await SeedLegacyAsync(cs);

        await using var ctx = Context(cs);
        await ctx.Database.MigrateAsync();

        var firstRepos = await DumpAsync(cs, RepositoriesDump);
        var firstConns = await DumpAsync(cs, ConnectionsDump);
        var firstShares = await DumpAsync(cs, SharesDump);
        var firstTags = await DumpAsync(cs, TagsDump);
        var firstProjects = await DumpAsync(cs, ProjectsDump);

        await ctx.GetService<IMigrator>().MigrateAsync(BeforeDecomposition);

        // Şema ve veri eski hâlinde; düz metin geri gelmez ama link özetle çalışır. Boş şemalar bilerek
        // kalır (roles.sql'in şemaya bağlı varsayılan yetkileri için), tablolar yok.
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM pg_tables WHERE schemaname IN ('studio','hub')"));
        Assert.Equal(10, await CountAsync(cs, "SELECT count(*) FROM \"CloudProjects\""));
        // Temiz veride CHECK'ler doğrulanmış döner.
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM pg_constraint WHERE conname LIKE 'CK_CloudProjects%' AND NOT convalidated"));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM \"CloudProjects\" WHERE \"ShareToken\" IS NOT NULL"));
        Assert.Equal(1, await CountAsync(cs, $"SELECT count(*) FROM \"CloudProjects\" WHERE \"Id\" = 'p-secret' AND \"ShareTokenHash\" = '{ShareTokenHasher.Hash("legacy-plain-token")}'"));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"CloudProjects\" WHERE \"Id\" = 'p-secret' AND \"Slug\" = 'alpha' AND \"Visibility\" = 2 AND \"Category\" = 'SaaS' AND \"StarCount\" = 7 AND \"ForkCount\" = 2 AND \"LinkedGithubRepo\" = 'acme/repo' AND \"AllowDeskSql\" AND \"Tags\" = 'PostgreSQL,3nf,multi-tenant'"));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"CloudProjects\" WHERE \"Id\" = 'p-fork' AND \"ForkedFromProjectId\" = 'p-secret'"));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"CloudProjects\" WHERE \"Id\" = 'p-desk-only' AND \"AllowDeskSql\" AND \"EncryptedConnectionString\" IS NULL"));

        await ctx.Database.MigrateAsync();

        Assert.Equal(firstRepos, await DumpAsync(cs, RepositoriesDump));
        Assert.Equal(firstConns, await DumpAsync(cs, ConnectionsDump));
        Assert.Equal(firstShares, await DumpAsync(cs, SharesDump));
        Assert.Equal(firstTags, await DumpAsync(cs, TagsDump));
        Assert.Equal(firstProjects, await DumpAsync(cs, ProjectsDump));
        await AssertLegacyShapeGoneAsync(cs);
    }

    [RequiresDockerFact]
    public async Task Eski_uretec_artigi_varsa_migration_duruyor_hicbir_sey_degismiyor_temizlenince_geciyor()
    {
        var cs = await _pg.CreateDatabaseAsync("b_leftover");
        await SeedLegacyAsync(cs);

        // Bağlanmamış üretecin bir yerde elle çalıştırıldığı senaryo: farklı şekilli boş tablo.
        await ExecAsync(cs, """
            CREATE SCHEMA studio;
            CREATE TABLE studio.project_connections ("ProjectId" varchar(64) PRIMARY KEY, "EncryptedConnectionString" text NOT NULL);
            """);

        await using var ctx = Context(cs);
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.MigrateAsync());
        Assert.Contains("generator SQL", ex.ToString());

        // Decompose işlemi geri alındı: kaynak yerinde, veri tam.
        Assert.Equal(10, await CountAsync(cs, "SELECT count(*) FROM \"CloudProjects\""));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%DecomposeCloudProjects'"));

        await ExecAsync(cs, "DROP TABLE studio.project_connections;");
        await ctx.Database.MigrateAsync();
        Assert.Empty(await ctx.Database.GetPendingMigrationsAsync());
        Assert.Equal(10, await CountAsync(cs, "SELECT count(*) FROM hub.repositories"));
    }

    private const string ReconcileDrift = "20261105055013_Phase14_2B_ReconcileModelDrift";

    /// <summary>Tek oturumda: hazırlık, bozma, doğrulama. Doğrulama patlamalı, işlem geri alınır.</summary>
    private static async Task AssertVerifyRaisesAsync(string cs, string prepare, string corrupt, string verify, string expected)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        foreach (var sql in new[] { prepare, corrupt })
        {
            await using var cmd = new NpgsqlCommand(sql, conn, tx) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync();
        }
        await using var check = new NpgsqlCommand(verify, conn, tx) { CommandTimeout = 120 };
        var ex = await Assert.ThrowsAsync<PostgresException>(() => check.ExecuteNonQueryAsync());
        Assert.Contains(expected, ex.MessageText);
        await tx.RollbackAsync();
    }

    [RequiresDockerFact]
    public async Task Up_dogrulamasi_bozulmus_veriyi_yakaliyor()
    {
        var cs = await _pg.CreateDatabaseAsync("b_verify_up");
        await SeedLegacyAsync(cs);
        await using (var ctx = Context(cs))
            await ctx.GetService<IMigrator>().MigrateAsync(ReconcileDrift);

        var prepare = Namines.Identity.Infrastructure.Persistence.Migrations.Phase14_2BDecompositionScripts.UpPrepare;
        var verify = Namines.Identity.Infrastructure.Persistence.Migrations.Phase14_2BDecompositionScripts.UpVerify;

        // Bozulmamış hâlde geçer: aşağıdakiler gerçekten bozmanın yakalandığını gösterir.
        await using (var conn = new NpgsqlConnection(cs))
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await using (var p = new NpgsqlCommand(prepare, conn, tx)) await p.ExecuteNonQueryAsync();
            await using (var v = new NpgsqlCommand(verify, conn, tx)) await v.ExecuteNonQueryAsync();
            await tx.RollbackAsync();
        }

        var cases = new (string Corrupt, string Expected)[]
        {
            ("""UPDATE studio.project_connections SET "EncryptedConnectionString" = 'x' WHERE "ProjectId" = 'p-secret'""", "verify connections"),
            ("""UPDATE studio.project_connections SET "AllowDeskSql" = false WHERE "ProjectId" = 'p-desk-only'""", "verify connections"),
            ("""DELETE FROM studio.project_connections WHERE "ProjectId" = 'p-noaad'""", "verify connections"),
            ("""DELETE FROM studio.project_shares WHERE "ProjectId" = 'p-hashonly'""", "verify shares"),
            ("""UPDATE studio.project_shares SET "ShareTokenHash" = repeat('0', 64) WHERE "ProjectId" = 'p-both'""", "verify shares"),
            ("""UPDATE hub.repositories SET "StarCount" = "StarCount" + 1 WHERE "ProjectId" = 'p-secret'""", "verify repositories"),
            ("""UPDATE hub.repositories SET "Slug" = 'changed' WHERE "ProjectId" = 'p-fork'""", "verify repositories"),
            ("""UPDATE hub.repositories SET "ForkedFromProjectId" = NULL WHERE "ProjectId" = 'p-fork'""", "verify repositories"),
            ("""DELETE FROM hub.repositories WHERE "ProjectId" = 'p-hashonly'""", "verify repositories"),
            ("""DELETE FROM hub.project_tags WHERE "ProjectId" = 'p-secret' AND "Position" = 1""", "verify tags"),
            ("""UPDATE hub.project_tags SET "Tag" = 'wrong-key' WHERE "ProjectId" = 'p-secret' AND "Position" = 0""", "verify tags"),
            ("""UPDATE hub.project_tags SET "Position" = 99 WHERE "ProjectId" = 'p-secret' AND "Position" = 1""", "verify tags"),
        };
        foreach (var (corrupt, expected) in cases)
            await AssertVerifyRaisesAsync(cs, prepare, corrupt, verify, expected);
    }

    [RequiresDockerFact]
    public async Task Down_dogrulamasi_bozulmus_veriyi_yakaliyor()
    {
        var cs = await _pg.CreateDatabaseAsync("b_verify_down");
        await SeedLegacyAsync(cs);
        await using (var ctx = Context(cs))
            await ctx.GetService<IMigrator>().MigrateAsync("20261105060611_Phase14_2B_DecomposeCloudProjects");

        var restore = Namines.Identity.Infrastructure.Persistence.Migrations.Phase14_2BDecompositionScripts.DownRestore;
        var verify = Namines.Identity.Infrastructure.Persistence.Migrations.Phase14_2BDecompositionScripts.DownVerify;

        var cases = new (string Corrupt, string Expected)[]
        {
            ("""UPDATE "CloudProjects" SET "StarCount" = "StarCount" + 1 WHERE "Id" = 'p-secret'""", "down verify repositories"),
            ("""UPDATE "CloudProjects" SET "EncryptedConnectionString" = 'x' WHERE "Id" = 'p-secret'""", "down verify connections"),
            ("""UPDATE "CloudProjects" SET "Tags" = 'zz' WHERE "Id" = 'p-secret'""", "down verify tags"),
            ("""UPDATE "CloudProjects" SET "ShareTokenHash" = NULL WHERE "Id" = 'p-both'""", "down verify shares"),
        };
        foreach (var (corrupt, expected) in cases)
            await AssertVerifyRaisesAsync(cs, restore, corrupt, verify, expected);
    }

    [RequiresDockerFact]
    public async Task Superuser_ile_kurulup_sonradan_rollere_gecen_kurulumda_her_modul_semasi_migratora_gecer()
    {
        // Denetim m-2: ops sahiplik listesinde yoktu; tabloları postgres'te kalıyor, Ops migration'ı migrator ile düşüyordu.
        const string database = "b_roles_after";
        var adminCs = await _pg.CreateDatabaseAsync(database);
        await TestModules.MigrateAllAsync(adminCs);
        await _pg.ApplyRolesAsync(database, await File.ReadAllTextAsync(BackendFile("..", "deploy", "sql", "roles.sql")));

        Assert.Equal(0, await CountAsync(adminCs, """
            SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r','S','p') AND n.nspname IN ('public','flow','namines_vault','ground','desk','studio','hub','gateway','ops')
              AND c.relowner::regrole::text <> 'namines_migrator'
            """));
        Assert.True(await CountAsync(adminCs, "SELECT count(*) FROM pg_tables WHERE schemaname = 'ops'") >= 4);

        // Kanıt: migrator, modül zincirini geri alıp yeniden uygulayabiliyor (tablo taşıması sahiplik ister).
        var migratorCs = new NpgsqlConnectionStringBuilder(adminCs) { Username = "namines_migrator", Password = "migrator-pw" }.ConnectionString;
        var ops = TestModules.Descriptors.Single(d => d.Module == "Ops");
        await using (var ctx = ops.CreateContext(migratorCs))
        {
            await ctx.GetService<IMigrator>().MigrateAsync("0");
            await ctx.Database.MigrateAsync();
        }
        Assert.Equal(4, await CountAsync(adminCs, """SELECT count(*) FROM pg_tables WHERE schemaname = 'ops' AND tablename IN ('Feedbacks','SqlExecutionAudits','FeatureFlags','AiToolRoutes')"""));
    }

    [RequiresDockerFact]
    public async Task Suveri_kullaniciyla_14_2C_kurulumu_rollere_gecip_migrator_ile_tum_zincirleri_tamamliyor()
    {
        // Denetim R2-M1: 14.2C'yi süper kullanıcı çalıştırınca hub.issue_label_gc onun malı kalıyordu; roles.sql
        // yalnız tablo ve dizi devrediyordu, Hub_02 migrator ile 42501 alıp Hub/Gateway/Ops'u durduruyordu.
        const string database = "b_roles_after_2c";
        var adminCs = await _pg.CreateDatabaseAsync(database);
        await using (var ctx = Context(adminCs))
            await ctx.GetService<IMigrator>().MigrateAsync(TestModules.Level14_2C);
        await TestModules.SeedLiveAsync(adminCs);
        Assert.Equal("postgres", await ScalarAsync(adminCs, "SELECT proowner::regrole::text FROM pg_proc WHERE oid = 'hub.issue_label_gc()'::regprocedure"));

        await _pg.ApplyRolesAsync(database, await File.ReadAllTextAsync(BackendFile("..", "deploy", "sql", "roles.sql")));
        Assert.Equal("namines_migrator", await ScalarAsync(adminCs, "SELECT proowner::regrole::text FROM pg_proc WHERE oid = 'hub.issue_label_gc()'::regprocedure"));
        Assert.Equal(0, await CountAsync(adminCs, """
            SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname IN ('public','flow','namines_vault','ground','desk','studio','hub','gateway','ops','migration_quarantine')
              AND p.proowner::regrole::text <> 'namines_migrator'
            """));

        // Belgeli geçiş: migration artık migrator ile, tek komut, bütün zincirler.
        var migratorCs = new NpgsqlConnectionStringBuilder(adminCs) { Username = "namines_migrator", Password = "migrator-pw" }.ConnectionString;
        await TestModules.MigrateAllAsync(migratorCs);
        Assert.Empty(await Namines.BuildingBlocks.Persistence.ModuleMigrationOrchestrator.GetPendingAsync(TestModules.Descriptors, migratorCs));
        Assert.Equal("statement", await ScalarAsync(adminCs, "SELECT CASE WHEN tgtype & 1 = 1 THEN 'row' ELSE 'statement' END FROM pg_trigger WHERE tgname = 'trg_repo_issue_labels_gc'"));
        await TestModules.AssertSeedIntactAsync(adminCs);

        // Uygulama rolü taşınan her tabloyu okur ve yazar; etiket GC tetikleyicisi onun yetkisiyle çalışır.
        var appCs = new NpgsqlConnectionStringBuilder(adminCs) { Username = "namines_app", Password = "app-pw" }.ConnectionString;
        foreach (var (schema, table) in TestModules.MovedTables)
            Assert.True(await CountAsync(appCs, $"SELECT count(*) FROM {schema}.\"{table}\"") >= 0);
        await ExecAsync(appCs, """DELETE FROM hub.repo_issue_labels WHERE "RepoIssueId" = 'i1'""");
        Assert.Equal(0, await CountAsync(appCs, """SELECT count(*) FROM hub.issue_labels WHERE "Id" = 'l1'"""));
        await ExecAsync(appCs, """INSERT INTO ops."Feedbacks" ("Message","Category","CreatedAt") VALUES ('m','General',now())""");
        await ExecAsync(appCs, """UPDATE gateway."GatewayApiKeys" SET "LastUsedAt" = now() WHERE "ProjectId" = 'p1'""");
    }

    [RequiresDockerFact]
    public async Task Fonksiyon_sahibi_eksikse_Hub_02_ne_yapilacagini_soyleyerek_durur_ve_roles_sql_sonrasi_devam_eder()
    {
        const string database = "b_hub02_guard";
        var adminCs = await _pg.CreateDatabaseAsync(database);
        await using (var ctx = Context(adminCs))
            await ctx.GetService<IMigrator>().MigrateAsync(TestModules.Level14_2C);
        var roles = await File.ReadAllTextAsync(BackendFile("..", "deploy", "sql", "roles.sql"));
        await _pg.ApplyRolesAsync(database, roles);
        // Eski roles.sql'in bıraktığı durum: fonksiyon hâlâ süper kullanıcıda.
        await ExecAsync(adminCs, "ALTER FUNCTION hub.issue_label_gc() OWNER TO postgres");

        var migratorCs = new NpgsqlConnectionStringBuilder(adminCs) { Username = "namines_migrator", Password = "migrator-pw" }.ConnectionString;
        var ex = await Assert.ThrowsAsync<PostgresException>(() => TestModules.MigrateAllAsync(migratorCs));
        Assert.Equal("42501", ex.SqlState);
        Assert.Contains("Re-run deploy/sql/roles.sql", ex.MessageText);
        Assert.Equal(1, await CountAsync(adminCs, "SELECT count(*) FROM hub.\"__EFMigrationsHistory\""));
        Assert.Equal("row", await ScalarAsync(adminCs, "SELECT CASE WHEN tgtype & 1 = 1 THEN 'row' ELSE 'statement' END FROM pg_trigger WHERE tgname = 'trg_repo_issue_labels_gc'"));

        // Runbook adımı: roles.sql yeniden, sonra aynı komut kaldığı yerden tamamlar.
        await _pg.ApplyRolesAsync(database, roles);
        await TestModules.MigrateAllAsync(migratorCs);
        Assert.Empty(await Namines.BuildingBlocks.Persistence.ModuleMigrationOrchestrator.GetPendingAsync(TestModules.Descriptors, migratorCs));
    }

    [RequiresDockerFact]
    public async Task En_az_yetkili_rolle_bos_kurulum_geri_alma_ve_yeniden_yukseltme_calisiyor()
    {
        const string database = "b_least_privilege";
        var adminCs = await _pg.CreateDatabaseAsync(database);
        await _pg.ApplyRolesAsync(database, await File.ReadAllTextAsync(BackendFile("..", "deploy", "sql", "roles.sql")));

        var migratorCs = new NpgsqlConnectionStringBuilder(adminCs) { Username = "namines_migrator", Password = "migrator-pw" }.ConnectionString;
        var appCs = new NpgsqlConnectionStringBuilder(adminCs) { Username = "namines_app", Password = "app-pw" }.ConnectionString;

        async Task AssertAppPrivilegesAsync()
        {
            foreach (var table in new[] { "studio.projects", "studio.project_connections", "studio.project_shares", "hub.repositories", "hub.project_tags" })
            {
                Assert.Equal(0, await CountAsync(appCs, $"SELECT count(*) FROM {table}"));
                await using var conn = new NpgsqlConnection(appCs);
                await conn.OpenAsync();
                await using var ddl = new NpgsqlCommand($"ALTER TABLE {table} ADD COLUMN x int", conn);
                var denied = await Assert.ThrowsAsync<PostgresException>(() => ddl.ExecuteNonQueryAsync());
                Assert.Equal("42501", denied.SqlState);
            }
        }

        // Boş kurulum: yalnızca migrator ile, tüm zincir.
        await using var ctx = Context(migratorCs);
        await ctx.Database.MigrateAsync();
        Assert.Empty(await ctx.Database.GetPendingMigrationsAsync());
        await AssertAppPrivilegesAsync();

        // Geri al + yeniden yükselt, yine migrator: yetki hatası yok, uygulama rolü erişimini kaybetmiyor.
        await ctx.GetService<IMigrator>().MigrateAsync(BeforeDecomposition);
        Assert.Equal(0, await CountAsync(appCs, "SELECT count(*) FROM \"CloudProjects\""));
        await ctx.Database.MigrateAsync();
        Assert.Empty(await ctx.Database.GetPendingMigrationsAsync());
        await AssertAppPrivilegesAsync();
    }

    [RequiresDockerFact]
    public async Task Modul_baglamlarinin_esledigi_her_tablo_ve_kolon_veritabaninda_var()
    {
        var cs = await _pg.CreateDatabaseAsync("b_mapping");
        await TestModules.MigrateAllAsync(cs);

        await using var studio = new StudioDbContext(new DbContextOptionsBuilder<StudioDbContext>().UseNpgsql(cs).Options);
        await using var hub = new HubDbContext(new DbContextOptionsBuilder<HubDbContext>().UseNpgsql(cs).Options);

        foreach (DbContext context in new DbContext[] { studio, hub })
        {
            var entityTypes = context.Model.GetEntityTypes().ToList();
            Assert.NotEmpty(entityTypes);
            foreach (var entity in entityTypes)
            {
                var table = entity.GetTableName()!;
                var schema = entity.GetSchema() ?? "public";
                var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
                foreach (var property in entity.GetProperties())
                {
                    var column = property.GetColumnName(store)!;
                    if (column == "xmin") continue; // sistem kolonu; information_schema göstermez
                    Assert.True(
                        await CountAsync(cs, $"SELECT count(*) FROM information_schema.columns WHERE table_schema = '{schema}' AND table_name = '{table}' AND column_name = '{column}'") == 1,
                        $"{context.GetType().Name} {schema}.{table}.{column} veritabanında yok");
                }
            }
        }

        // Eşleme yetmez: EF'in ürettiği SQL gerçekten çalışmalı.
        Assert.False(await studio.Projects.AnyAsync());
        Assert.False(await studio.ProjectConnections.AnyAsync());
        Assert.False(await studio.ProjectShares.AnyAsync());
        Assert.False(await hub.Repositories.AnyAsync());
        Assert.False(await hub.ProjectTags.AnyAsync());
        Assert.False(await hub.RepoStars.AnyAsync());
    }
}
