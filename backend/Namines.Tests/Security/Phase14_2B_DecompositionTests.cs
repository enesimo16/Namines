using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Modules;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Namines.Tests.Integration;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Namines.Tests.Security;

/// <summary>
/// Phase 14.2B: CloudProjects God Entity Decomposition Bütünleştirme ve Birim Testleri.
///
/// Doğrulanan Alanlar:
/// 1. Expand -> Backfill -> Verify -> Contract PostgreSQL migration pipeline
/// 2. studio.projects ve studio.project_shares varlık ve veri sahipliği
/// 3. hub.repositories ve hub.project_tags normalize etiket arama ve sosyal sayaçlar
/// 4. IStudioApplicationService ve IHubApplicationService sözleşme ve iş kuralları
/// 5. Proje silme esnasında modüller arası cascade lifecycle davranışı
/// </summary>
public sealed class Phase14_2B_DecompositionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer? _pgContainer;

    public Phase14_2B_DecompositionTests()
    {
        _pgContainer = DockerAvailable.Value
            ? new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("namines_test_14_2b")
                .WithUsername("namines")
                .WithPassword("test_secret_pass")
                .Build()
            : null;
    }

    public async Task InitializeAsync()
    {
        if (_pgContainer is not null)
        {
            await _pgContainer.StartAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_pgContainer is not null)
        {
            await _pgContainer.DisposeAsync();
        }
    }

    [Fact]
    public async Task Postgres_Expand_Backfill_Verify_Contract_Pipeline_Decomposes_CloudProjects()
    {
        if (_pgContainer is null)
            return; // Docker yoksa atla

        var connStr = _pgContainer.GetConnectionString();
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        // 1. Legacy CloudProjects tablosunu ve test verilerini oluştur
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS "CloudProjects" (
                    "Id" character varying(64) PRIMARY KEY,
                    "Name" character varying(512) NOT NULL,
                    "DbType" character varying(32) NOT NULL,
                    "SchemaJson" text NOT NULL,
                    "NodePositionsJson" text NOT NULL,
                    "UserId" character varying(64) NOT NULL,
                    "OrganizationId" character varying(64) NULL,
                    "EncryptedConnectionString" text NULL,
                    "ConnectionDbType" character varying(32) NULL,
                    "AllowDeskSql" boolean NOT NULL DEFAULT false,
                    "ShareToken" text NULL,
                    "ShareTokenHash" character varying(64) NULL,
                    "Slug" character varying(128) NULL,
                    "Description" text NULL,
                    "Visibility" integer NOT NULL DEFAULT 0,
                    "Category" character varying(64) NOT NULL DEFAULT 'General',
                    "Tags" text NULL,
                    "StarCount" integer NOT NULL DEFAULT 0,
                    "ForkCount" integer NOT NULL DEFAULT 0,
                    "ForkedFromProjectId" character varying(64) NULL,
                    "LinkedGithubRepo" character varying(140) NULL,
                    "AutoApproveSafeChanges" boolean NOT NULL DEFAULT false,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT (now() AT TIME ZONE 'utc')
                );

                INSERT INTO "CloudProjects" (
                    "Id", "Name", "DbType", "SchemaJson", "NodePositionsJson", "UserId", "OrganizationId",
                    "EncryptedConnectionString", "ConnectionDbType", "AllowDeskSql",
                    "ShareTokenHash", "Slug", "Description", "Visibility", "Category", "Tags", "StarCount", "ForkCount"
                ) VALUES
                ('proj-1', 'FinTech DB', 'PostgreSQL', '{"tables":[]}', '{"nodes":[]}', 'user-1', 'org-1',
                 'enc:conn:secret1', 'PostgreSQL', true,
                 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855', 'fintech-core', 'Core banking schema', 2, 'FinTech', 'postgresql,3nf,banking', 5, 2),
                ('proj-2', 'E-Commerce', 'MySQL', '{"tables":[]}', '{"nodes":[]}', 'user-2', 'org-2',
                 'enc:conn:secret2', 'MySQL', false,
                 NULL, 'ecommerce-store', 'Online shop schema', 0, 'E-Commerce', 'mysql,ecommerce', 0, 0),
                ('proj-3', 'Blog Engine', 'PostgreSQL', '{"tables":[]}', '{"nodes":[]}', 'user-3', NULL,
                 NULL, NULL, false,
                 'a591a6d40bf420404a011733cfb7b190d62c65bf0bcda32b57b277d9ad9f146e', 'blog-engine', 'Content platform', 2, 'General', NULL, 1, 0);
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Expand: Yeni hedef tabloları oluştur
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = Phase14_2B_DecompositionSql.BuildExpandSql();
            await cmd.ExecuteNonQueryAsync();
        }

        // 3. Backfill: Verileri idempotent aktar
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = Phase14_2B_DecompositionSql.BuildBackfillSql();
            await cmd.ExecuteNonQueryAsync();
        }

        // 4. Verify: Satır sayılarını ve verileri doğrula
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = Phase14_2B_DecompositionSql.BuildVerifySql();
            await cmd.ExecuteNonQueryAsync();
        }

        // Detaylı kontroller:
        // (a) studio.projects satır sayısı = 3
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM studio.projects;";
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            Assert.Equal(3, count);
        }

        // (b) studio.project_connections satır sayısı = 2
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM studio.project_connections;";
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            Assert.Equal(2, count);
        }

        // (c) studio.project_shares satır sayısı = 2
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM studio.project_shares;";
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            Assert.Equal(2, count);
        }

        // (d) hub.repositories satır sayısı = 3
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM hub.repositories;";
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            Assert.Equal(3, count);
        }

        // (e) hub.project_tags: proj-1 için 3 etiket ('postgresql', '3nf', 'banking')
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT \"Tag\" FROM hub.project_tags WHERE \"ProjectId\" = 'proj-1' ORDER BY \"Tag\";";
            var tags = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tags.Add(reader.GetString(0));
            }
            Assert.Equal(["3nf", "banking", "postgresql"], tags);
        }

        // 5. Contract: Eski kolonları nullify et ve eski kısıtları temizle
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = Phase14_2B_DecompositionSql.BuildContractSql();
            await cmd.ExecuteNonQueryAsync();
        }

        // Contract sonrası CloudProjects üzerindeki taşınmış alanların sıfırlandığını doğrula
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT COUNT(*) FROM "CloudProjects"
                WHERE "EncryptedConnectionString" IS NOT NULL
                   OR "ShareTokenHash" IS NOT NULL
                   OR "Tags" IS NOT NULL;
                """;
            var dirtyCount = (long)(await cmd.ExecuteScalarAsync())!;
            Assert.Equal(0, dirtyCount);
        }
    }

    [Fact]
    public async Task StudioApplicationService_Manages_Projects_And_Cryptographic_Shares()
    {
        await using var sqliteConn = new SqliteConnection("DataSource=:memory:");
        await sqliteConn.OpenAsync();

        var options = new DbContextOptionsBuilder<StudioDbContext>()
            .UseSqlite(sqliteConn)
            .Options;

        await using var studioDb = new StudioDbContext(options);
        await studioDb.Database.EnsureCreatedAsync();

        var studioService = new StudioApplicationService(studioDb);

        // 1. Proje oluşturma
        var project = new CloudProject
        {
            Id = "studio-proj-1",
            Name = "Order Fulfillment",
            DbType = "PostgreSQL",
            SchemaJson = "{\"tables\":[{\"name\":\"orders\"}]}",
            NodePositionsJson = "{\"nodes\":[]}",
            UserId = "user-alice",
            OrganizationId = "org-corp",
            AutoApproveSafeChanges = true
        };

        var created = await studioService.CreateOrUpdateProjectAsync(project);
        Assert.NotNull(created);
        Assert.Equal("Order Fulfillment", created.Name);

        // 2. Proje listeleme (organizasyon sınırında)
        var projects = await studioService.GetProjectsForUserOrOrgAsync("user-alice", new[] { "org-corp" });
        Assert.Single(projects);
        Assert.Equal("studio-proj-1", projects[0].Id);

        // 3. Kriptografik Paylaşım Bağlantısı Oluşturma
        var rawToken = await studioService.CreateShareLinkAsync("studio-proj-1");
        Assert.False(string.IsNullOrWhiteSpace(rawToken));

        var expectedHash = ShareTokenHasher.Hash(rawToken);
        var shareRecord = await studioService.GetShareByTokenHashAsync(expectedHash);
        Assert.NotNull(shareRecord);
        Assert.Equal("studio-proj-1", shareRecord!.ProjectId);
        Assert.Equal(expectedHash, shareRecord.ShareTokenHash);

        // Ham token veritabanında saklanmaz
        var shares = await studioDb.ProjectShares.ToListAsync();
        Assert.DoesNotContain(shares, s => s.ShareTokenHash == rawToken);

        // 4. Paylaşılan projeyi getirme
        var sharedProject = await studioService.GetSharedProjectByTokenHashAsync(expectedHash);
        Assert.NotNull(sharedProject);
        Assert.Equal("Order Fulfillment", sharedProject!.Name);

        // 5. Paylaşımı iptal etme
        var revoked = await studioService.RevokeShareLinkAsync("studio-proj-1");
        Assert.True(revoked);

        var revokedShare = await studioService.GetShareByTokenHashAsync(expectedHash);
        Assert.Null(revokedShare);

        var sharedAfterRevoke = await studioService.GetSharedProjectByTokenHashAsync(expectedHash);
        Assert.Null(sharedAfterRevoke);

        // 6. Projeyi silme (Cascade)
        var deleted = await studioService.DeleteProjectAsync("studio-proj-1");
        Assert.True(deleted);

        var projectAfterDelete = await studioService.GetProjectAsync("studio-proj-1");
        Assert.Null(projectAfterDelete);
    }

    [Fact]
    public async Task HubApplicationService_Manages_Repositories_Tags_And_ExploreFeed()
    {
        await using var sqliteConn = new SqliteConnection("DataSource=:memory:");
        await sqliteConn.OpenAsync();

        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseSqlite(sqliteConn)
            .Options;

        await using var hubDb = new HubDbContext(options);
        await hubDb.Database.EnsureCreatedAsync();

        var hubService = new HubApplicationService(hubDb);

        // 1. Repo oluşturma ve etiket kaydetme
        var repo1 = new ProjectRepository
        {
            ProjectId = "hub-proj-1",
            Slug = "fintech-core",
            Description = "Banking core schema with double-entry ledger",
            Visibility = ProjectVisibility.Public,
            Category = "FinTech",
            StarCount = 10,
            PublishedAt = DateTime.UtcNow.AddDays(-2)
        };

        await hubService.UpsertRepositoryAsync(repo1, new[] { "postgresql", "3nf", "ledger" });

        var repo2 = new ProjectRepository
        {
            ProjectId = "hub-proj-2",
            Slug = "ecommerce-lite",
            Description = "Simple online store schema",
            Visibility = ProjectVisibility.Public,
            Category = "E-Commerce",
            StarCount = 3,
            PublishedAt = DateTime.UtcNow.AddDays(-1)
        };

        await hubService.UpsertRepositoryAsync(repo2, new[] { "mysql", "ecommerce" });

        // 2. Etiketleri sorgulama
        var tags1 = await hubService.GetTagsForProjectAsync("hub-proj-1");
        Assert.Equal(3, tags1.Count);
        Assert.Contains("ledger", tags1);

        // 3. Explore Feed: Kategori filtresi
        var fintechRepos = await hubService.GetExploreRepositoriesAsync(
            query: null, category: "FinTech", tag: null, dbType: null, sort: "popular", page: 1, pageSize: 10);
        Assert.Single(fintechRepos);
        Assert.Equal("fintech-core", fintechRepos[0].Slug);

        // 4. Explore Feed: Normalize Etiket filtresi (LIKE yerine B-Tree index lookup)
        var ledgerRepos = await hubService.GetExploreRepositoriesAsync(
            query: null, category: null, tag: "ledger", dbType: null, sort: "popular", page: 1, pageSize: 10);
        Assert.Single(ledgerRepos);
        Assert.Equal("hub-proj-1", ledgerRepos[0].ProjectId);

        // 5. Yıldızlama (Toggle)
        var (starCountAfterAdd, starred) = await hubService.ToggleStarAsync("hub-proj-1", "user-bob");
        Assert.True(starred);
        Assert.Equal(11, starCountAfterAdd);
        Assert.True(await hubService.IsStarredByUserAsync("hub-proj-1", "user-bob"));

        var (starCountAfterRemove, unstarred) = await hubService.ToggleStarAsync("hub-proj-1", "user-bob");
        Assert.False(unstarred);
        Assert.Equal(10, starCountAfterRemove);
        Assert.False(await hubService.IsStarredByUserAsync("hub-proj-1", "user-bob"));

        // 6. Fork işlemi
        var forkedRepo = await hubService.ForkRepositoryAsync("hub-proj-1", "hub-proj-forked");
        Assert.NotNull(forkedRepo);
        Assert.Equal("hub-proj-1", forkedRepo.ForkedFromProjectId);

        var updatedSource = await hubService.GetRepositoryAsync("hub-proj-1");
        Assert.Equal(1, updatedSource!.ForkCount);

        // Fork'un etiketleri kaynaktan kopyalanmış olmalı
        var forkedTags = await hubService.GetTagsForProjectAsync("hub-proj-forked");
        Assert.Equal(3, forkedTags.Count);

        // 7. Silme
        var deleted = await hubService.DeleteRepositoryAsync("hub-proj-1");
        Assert.True(deleted);

        var repoAfterDelete = await hubService.GetRepositoryAsync("hub-proj-1");
        Assert.Null(repoAfterDelete);

        var tagsAfterDelete = await hubService.GetTagsForProjectAsync("hub-proj-1");
        Assert.Empty(tagsAfterDelete);
    }

    [Fact]
    public void StudioDbContext_And_HubDbContext_Have_Correct_Schemas_And_Ignore_Legacy_Columns()
    {
        var studioBuilder = new DbContextOptionsBuilder<StudioDbContext>()
            .UseNpgsql("Host=localhost;Database=test;Username=postgres;Password=postgres");
        using var studioDb = new StudioDbContext(studioBuilder.Options);

        var hubBuilder = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql("Host=localhost;Database=test;Username=postgres;Password=postgres");
        using var hubDb = new HubDbContext(hubBuilder.Options);

        // Şema kontrolleri
        Assert.Equal("studio", studioDb.Model.GetDefaultSchema());
        Assert.Equal("hub", hubDb.Model.GetDefaultSchema());

        // Studio varlıkları
        var projectEntity = studioDb.Model.FindEntityType(typeof(CloudProject));
        Assert.NotNull(projectEntity);
        Assert.Equal("projects", projectEntity!.GetTableName());

        // Legacy kolonların unmap edildiğini doğrula
        Assert.Null(projectEntity.FindProperty(nameof(CloudProject.EncryptedConnectionString)));
        Assert.Null(projectEntity.FindProperty(nameof(CloudProject.ConnectionDbType)));
        Assert.Null(projectEntity.FindProperty(nameof(CloudProject.AllowDeskSql)));
        Assert.Null(projectEntity.FindProperty(nameof(CloudProject.ShareTokenHash)));
        Assert.Null(projectEntity.FindProperty(nameof(CloudProject.Slug)));
        Assert.Null(projectEntity.FindProperty(nameof(CloudProject.Tags)));
        Assert.Null(projectEntity.FindProperty(nameof(CloudProject.StarCount)));
        Assert.Null(projectEntity.FindProperty(nameof(CloudProject.ForkCount)));

        // Core canvas alanlarının map edildiğini doğrula
        Assert.NotNull(projectEntity.FindProperty(nameof(CloudProject.Id)));
        Assert.NotNull(projectEntity.FindProperty(nameof(CloudProject.Name)));
        Assert.NotNull(projectEntity.FindProperty(nameof(CloudProject.DbType)));
        Assert.NotNull(projectEntity.FindProperty(nameof(CloudProject.SchemaJson)));
        Assert.NotNull(projectEntity.FindProperty(nameof(CloudProject.NodePositionsJson)));
        Assert.NotNull(projectEntity.FindProperty(nameof(CloudProject.UserId)));
        Assert.NotNull(projectEntity.FindProperty(nameof(CloudProject.OrganizationId)));

        // Studio project_shares
        var shareEntity = studioDb.Model.FindEntityType(typeof(ProjectShare));
        Assert.NotNull(shareEntity);
        Assert.Equal("project_shares", shareEntity!.GetTableName());

        // Hub repositories & project_tags
        var repoEntity = hubDb.Model.FindEntityType(typeof(ProjectRepository));
        Assert.NotNull(repoEntity);
        Assert.Equal("repositories", repoEntity!.GetTableName());

        var tagEntity = hubDb.Model.FindEntityType(typeof(ProjectTag));
        Assert.NotNull(tagEntity);
        Assert.Equal("project_tags", tagEntity!.GetTableName());
    }
}
