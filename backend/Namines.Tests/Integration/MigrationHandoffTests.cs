using Namines.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Namines.BuildingBlocks.Persistence;
using Namines.Infrastructure.Data;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Namines.Tests.Integration;

/// <summary>
/// Faz 14.2D-1 migration sahipliği, gerçek PostgreSQL'de: boş veritabanı ve 14.2C seviyesinde dolu
/// veritabanı aynı komutla (orkestratör) son hâle gelir, satırlar kaybolmaz, geri dönüş veriyi korur,
/// eşlenen her kolon gerçekten vardır, Auth devir migration'ı veri silmez.
/// </summary>
[Collection("Docker")]
public sealed class MigrationHandoffTests : IAsyncLifetime
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
        if (!DockerAvailable.Value) return;
        await _container.DisposeAsync();
    }

    private async Task<string> NewDatabaseAsync(string name)
    {
        await using var admin = new NpgsqlConnection(_container.GetConnectionString());
        await admin.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
        await cmd.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }

    private static async Task ExecAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<HashSet<string>> SetAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var set = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync()) set.Add(reader.GetString(0));
        return set;
    }

    private static async Task MigrateAuthToAsync(string cs, string target)
    {
        await using var ctx = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(cs).Options);
        await ctx.GetService<IMigrator>().MigrateAsync(target);
    }

    /// <summary>Yetkisiz roller ve FK'ler dahil tüm kısıtlar DB'de: EnsureCreated değil, üretim yolu.</summary>
    [RequiresDockerFact]
    public async Task Fresh_database_reaches_final_state_with_one_command()
    {
        var cs = await NewDatabaseAsync("fresh");
        var results = await ModuleMigrationOrchestrator.MigrateAllAsync(TestModules.Descriptors, cs, TimeSpan.FromMinutes(5));

        Assert.Equal("Auth", results[0].Module);
        Assert.Contains(results[0].Applied, m => m.EndsWith("_Phase14_2C_NormalizeCollections"));
        foreach (var (schema, table) in TestModules.MovedTables)
            Assert.Equal(1, await CountAsync(cs, $"SELECT count(*) FROM pg_tables WHERE schemaname='{schema}' AND tablename='{table}'"));

        Assert.Empty(await ModuleMigrationOrchestrator.GetPendingAsync(TestModules.Descriptors, cs));

        // İkinci çalıştırma hiçbir şey yapmaz (idempotent).
        var again = await ModuleMigrationOrchestrator.MigrateAllAsync(TestModules.Descriptors, cs, TimeSpan.FromMinutes(5));
        Assert.All(again, r => Assert.Empty(r.Applied));
    }

    [RequiresDockerFact]
    public async Task Every_mapped_column_of_every_context_exists_in_migrated_database()
    {
        var cs = await NewDatabaseAsync("columns");
        await TestModules.MigrateAllAsync(cs);

        var existing = await SetAsync(cs,
            "SELECT table_schema || '.' || table_name || '.' || column_name FROM information_schema.columns");
        // xmin gibi sistem kolonları information_schema'da görünmez.
        var missing = new List<string>();
        foreach (var factory in TestModules.AllContextFactories)
        {
            await using var ctx = factory(cs);
            foreach (var entity in ctx.GetService<IDesignTimeModel>().Model.GetEntityTypes())
            {
                var tableName = entity.GetTableName();
                if (tableName is null) continue;
                var schema = entity.GetSchema() ?? "public";
                var store = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
                foreach (var property in entity.GetProperties())
                {
                    var column = property.GetColumnName(store);
                    if (column is null || column == "xmin") continue;
                    var key = $"{schema}.{tableName}.{column}";
                    if (!existing.Contains(key)) missing.Add($"{ctx.GetType().Name}: {key}");
                }
            }
        }
        Assert.Empty(missing);
    }

    [RequiresDockerFact]
    public async Task Model_names_match_catalog_for_every_owning_context()
    {
        var cs = await NewDatabaseAsync("names");
        await TestModules.MigrateAllAsync(cs);

        var indexes = await SetAsync(cs, "SELECT schemaname || '.' || indexname FROM pg_indexes");
        var constraints = await SetAsync(cs,
            "SELECT n.nspname || '.' || c.conname FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace");

        var missing = new List<string>();
        foreach (var factory in TestModules.ModuleOwnerContextFactories)
        {
            await using var ctx = factory(cs);
            foreach (var entity in ctx.GetService<IDesignTimeModel>().Model.GetEntityTypes())
            {
                var tableName = entity.GetTableName();
                if (tableName is null) continue;
                var schema = entity.GetSchema() ?? "public";
                var store = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
                foreach (var index in entity.GetIndexes())
                {
                    var name = index.GetDatabaseName(store);
                    if (name is not null && !indexes.Contains($"{schema}.{name}") && !constraints.Contains($"{schema}.{name}"))
                        missing.Add($"index {schema}.{name}");
                }
                foreach (var fk in entity.GetForeignKeys())
                {
                    var name = fk.GetConstraintName(store, StoreObjectIdentifier.Table(fk.PrincipalEntityType.GetTableName()!, fk.PrincipalEntityType.GetSchema()));
                    if (name is not null && !constraints.Contains($"{schema}.{name}"))
                        missing.Add($"fk {schema}.{name}");
                }
                var pk = entity.FindPrimaryKey()?.GetName(store);
                if (pk is not null && !constraints.Contains($"{schema}.{pk}"))
                    missing.Add($"pk {schema}.{pk}");
                foreach (var check in entity.GetCheckConstraints())
                {
                    if (!constraints.Contains($"{schema}.{check.Name}"))
                        missing.Add($"check {schema}.{check.Name}");
                }
            }
        }
        // Tam liste yazılıyor: Assert.Empty koleksiyonu ilk beş öğede keserdi.
        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    [RequiresDockerFact]
    public async Task Auth_handoff_migrations_keep_tables_and_rows()
    {
        var cs = await NewDatabaseAsync("authfirst");
        await MigrateAuthToAsync(cs, TestModules.Level14_2C);
        await TestModules.SeedLiveAsync(cs);

        // Yalnızca Auth son hâline: modül zincirleri henüz çalışmadı, tablolar yerinde ve dolu.
        await using (var ctx = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(cs).Options))
            await ctx.Database.MigrateAsync();

        foreach (var (table, expected) in TestModules.SeededCountsBeforeMove)
            Assert.Equal(expected, await CountAsync(cs, $"SELECT count(*) FROM {table}"));
    }

    [Fact]
    public void Auth_handoff_migrations_have_no_operations()
    {
        // "DROP olmaması" yetmez: bu migration'lar yalnızca modelden çıkıştır, DB'ye hiç dokunmaz.
        using var ctx = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options);
        var assembly = ctx.GetService<IMigrationsAssembly>();
        var handoffs = assembly.Migrations.Where(m => m.Key.Contains("_Phase14_2D1_Handoff")).ToList();
        Assert.NotEmpty(handoffs);
        foreach (var (id, type) in handoffs)
        {
            var migration = assembly.CreateMigration(type, "Npgsql.EntityFrameworkCore.PostgreSQL");
            Assert.Empty(migration.UpOperations);
            Assert.Empty(migration.DownOperations);
        }
    }

    [RequiresDockerFact]
    public async Task Live_database_at_14_2C_moves_without_losing_rows_and_rolls_back()
    {
        var cs = await NewDatabaseAsync("live");
        await MigrateAuthToAsync(cs, TestModules.Level14_2C);
        await TestModules.SeedLiveAsync(cs);

        await TestModules.MigrateAllAsync(cs);
        foreach (var (table, expected) in TestModules.SeededCountsAfterMove)
            Assert.Equal(expected, await CountAsync(cs, $"SELECT count(*) FROM {table}"));
        await TestModules.AssertSeedIntactAsync(cs);

        // Geri: önce modül zincirleri ters sırada, Auth en son. Veri 14.2C konumuna döner.
        foreach (var module in TestModules.Descriptors.Where(d => d.Module != "Auth").OrderByDescending(d => d.Order))
        {
            await using var ctx = module.CreateContext(cs);
            await ctx.GetService<IMigrator>().MigrateAsync("0");
        }
        await MigrateAuthToAsync(cs, TestModules.Level14_2C);
        foreach (var (table, expected) in TestModules.SeededCountsBeforeMove)
            Assert.Equal(expected, await CountAsync(cs, $"SELECT count(*) FROM {table}"));

        // Tekrar ileri: geçmiş tabloları tutarlı, aynı komut yeniden çalışır.
        await TestModules.MigrateAllAsync(cs);
        foreach (var (table, expected) in TestModules.SeededCountsAfterMove)
            Assert.Equal(expected, await CountAsync(cs, $"SELECT count(*) FROM {table}"));
        await TestModules.AssertSeedIntactAsync(cs);
    }

    [RequiresDockerFact]
    public async Task Dropped_organization_copies_come_back_from_projects_on_rollback()
    {
        var cs = await NewDatabaseAsync("orgcopies");
        await MigrateAuthToAsync(cs, TestModules.Level14_2C);
        await TestModules.SeedLiveAsync(cs);
        await ExecAsync(cs, """
            INSERT INTO ground.ground_databases ("Id","ProjectId","OrganizationId","Provider","Status","CreatedAt")
            VALUES ('g1','p1','stale','Fake',0,now());
            """);
        await TestModules.MigrateAllAsync(cs);
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'ground' AND table_name = 'ground_databases' AND column_name = 'OrganizationId'"));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM ground.ground_databases"));

        foreach (var module in TestModules.Descriptors.Where(d => d.Module != "Auth").OrderByDescending(d => d.Order))
        {
            await using var ctx = module.CreateContext(cs);
            await ctx.GetService<IMigrator>().MigrateAsync("0");
        }
        await MigrateAuthToAsync(cs, TestModules.Level14_2C);
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM ground.ground_databases WHERE \"OrganizationId\" = 'o1'"));
    }

    [RequiresDockerFact]
    public async Task Interrupted_run_resumes_from_the_failed_module()
    {
        var cs = await NewDatabaseAsync("resume");
        await TestModules.MigrateAllAsync(cs, upToAuthOnly: true);

        // Bir modül zinciri çalışmadan önce engel: hedef şemada aynı adlı tablo. Orkestratör düşmeli.
        var (schema, table) = ("studio", "Branches");
        await ExecAsync(cs, $"CREATE SCHEMA IF NOT EXISTS \"{schema}\"; CREATE TABLE \"{schema}\".\"{table}\" (x int);");
        await Assert.ThrowsAnyAsync<PostgresException>(() => TestModules.MigrateAllAsync(cs));

        // Tablo kaybolmadı, işlem geri alındı. Engel kalkınca aynı komut tamamlar.
        Assert.Equal(1, await CountAsync(cs, $"SELECT count(*) FROM pg_tables WHERE tablename='{table}' AND schemaname='public'"));
        await ExecAsync(cs, $"DROP TABLE \"{schema}\".\"{table}\";");
        await TestModules.MigrateAllAsync(cs);
        Assert.Empty(await ModuleMigrationOrchestrator.GetPendingAsync(TestModules.Descriptors, cs));
    }

    [RequiresDockerFact]
    public async Task Concurrent_orchestrators_do_not_collide()
    {
        var cs = await NewDatabaseAsync("race");
        var runs = await Task.WhenAll(
            Task.Run(() => ModuleMigrationOrchestrator.MigrateAllAsync(TestModules.Descriptors, cs, TimeSpan.FromMinutes(5))),
            Task.Run(() => ModuleMigrationOrchestrator.MigrateAllAsync(TestModules.Descriptors, cs, TimeSpan.FromMinutes(5))));

        // Kilit sayesinde biri tümünü uygular, öteki bekler ve hiçbir şey bulmaz.
        var totals = runs.Select(r => r.Sum(m => m.Applied.Count)).OrderBy(n => n).ToList();
        Assert.Equal(0, totals[0]);
        Assert.True(totals[1] > 50);
    }

    [RequiresDockerFact]
    public async Task App_role_can_use_module_schema_and_read_history_after_migration()
    {
        var cs = await NewDatabaseAsync("roles");
        // roles.sql yeniden çalıştırılmamış kurulum: rol ile D-1 sürümünde var olan şemaların (public, flow,
        // namines_vault, ground, desk) yetkileri var; yeni şemalar (billing, governance) yok, onları modül
        // zincirlerinin kendi GrantAppRole adımı açmak zorunda.
        await ExecAsync(cs, "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'namines_app') THEN CREATE ROLE namines_app LOGIN PASSWORD 'app_pw'; END IF; END $$;");
        foreach (var existing in new[] { "public", "flow", "namines_vault", "ground", "desk" })
            await ExecAsync(cs, $"CREATE SCHEMA IF NOT EXISTS \"{existing}\"; GRANT USAGE ON SCHEMA \"{existing}\" TO namines_app; ALTER DEFAULT PRIVILEGES IN SCHEMA \"{existing}\" GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO namines_app; ALTER DEFAULT PRIVILEGES IN SCHEMA \"{existing}\" GRANT USAGE, SELECT ON SEQUENCES TO namines_app;");
        await TestModules.MigrateAllAsync(cs);

        var appCs = new NpgsqlConnectionStringBuilder(cs) { Username = "namines_app", Password = "app_pw" }.ConnectionString;
        foreach (var (schema, table) in TestModules.MovedTables)
            Assert.True(await CountAsync(appCs, $"SELECT count(*) FROM \"{schema}\".\"{table}\"") >= 0);
        Assert.Empty(await ModuleMigrationOrchestrator.GetPendingAsync(TestModules.Descriptors, appCs));
    }
}
