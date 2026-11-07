using Namines.Identity.Infrastructure.Persistence;
using Namines.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Namines.Tests.Integration;

/// <summary>
/// Modül sınırları gerçek PostgreSQL'de: şemalar arası FK davranışı, bileşik etiket FK'si, doğrulama
/// bloğunun negatif yolu, modüller arası işlem ve gerçek eşzamanlılık. Her test ayrı bağlantılarla (üretim düzeni).
/// </summary>
[Collection("Docker")]
public sealed class Phase14_2D1FlowTests : IAsyncLifetime
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

    /// <summary>14.2C seviyesinde tohumla, tüm zinciri çalıştır: canlı sistemin yükseltilmiş hâli.</summary>
    private async Task<string> UpgradedAsync(string name)
    {
        var cs = await NewDatabaseAsync(name);
        await using (var ctx = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(cs).Options))
            await ctx.GetService<IMigrator>().MigrateAsync(TestModules.Level14_2C);
        await TestModules.SeedLiveAsync(cs);
        await TestModules.MigrateAllAsync(cs);
        return cs;
    }

    private static ModuleKit Kit(string cs)
    {
        var auth = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(cs).Options);
        return new ModuleKit(auth, cs);
    }

    // ── Şemalar arası FK'ler ────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task Deleting_project_cascades_across_schemas_and_keeps_gateway_audit()
    {
        var cs = await UpgradedAsync("cascade");
        await ExecAsync(cs, "DELETE FROM studio.\"CrossDatabaseRelations\"");
        await ExecAsync(cs, "DELETE FROM studio.projects WHERE \"Id\" = 'p1'");

        foreach (var table in new[] { "studio.\"Branches\"", "studio.\"SchemaVersions\"", "studio.project_connections", "studio.project_shares", "hub.repositories", "hub.project_tags", "hub.\"RepoIssues\"", "hub.\"RepoStars\"", "gateway.\"GatewayApiKeys\"", "gateway.\"GatewayTablePermissions\"", "public.\"ChangeRequests\"" })
        {
            var where = table.Contains("project_tags") || table.Contains("repositories") || table.Contains("project_") || table.Contains("Branches") || table.Contains("SchemaVersions") || table.Contains("RepoIssues") || table.Contains("RepoStars") || table.Contains("Gateway") || table.Contains("ChangeRequests")
                ? "\"ProjectId\" = 'p1'" : "true";
            Assert.Equal(0, await CountAsync(cs, $"SELECT count(*) FROM {table} WHERE {where}"));
        }
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM gateway.api_key_allowed_origins"));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM gateway.table_masked_columns"));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM hub.repo_issue_labels"));
        // Denetim izi projeden uzun yaşar.
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM gateway.\"GatewayAuditEntries\""));
    }

    [RequiresDockerFact]
    public async Task Deleting_project_cascades_to_flow_and_desk_rows_with_ddl_in_auth_chain()
    {
        var cs = await UpgradedAsync("flowcascade");
        await ExecAsync(cs, """
            INSERT INTO flow.automation_rules ("Id","ProjectId","Name","TriggerType","ActionType","ActionConfigJson","Enabled","ConditionsJson","CreatedAt")
            VALUES ('r1','p2','n','TableAdded','Webhook','{}',true,'[]',now());
            INSERT INTO flow.automation_run_logs ("Id","RuleId","ActionType","Status","IsTest","TriggeredAt")
            VALUES ('l1','r1','Webhook','Success',false,now());
            INSERT INTO desk.saved_queries ("Id","UserId","ProjectId","Name","Sql","CreatedAt","UpdatedAt")
            VALUES ('q1','u1','p2','n','select 1',now(),now());
            """);
        await ExecAsync(cs, "DELETE FROM studio.\"CrossDatabaseRelations\"");
        await ExecAsync(cs, "DELETE FROM studio.projects WHERE \"Id\" = 'p2'");
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM flow.automation_rules"));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM flow.automation_run_logs"));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM desk.saved_queries"));
    }

    [RequiresDockerFact]
    public async Task Restrict_keeps_vault_and_ground_rows_from_being_orphaned_by_a_direct_delete()
    {
        var cs = await UpgradedAsync("restrictvg");
        await ExecAsync(cs, """
            INSERT INTO ground.ground_databases ("Id","ProjectId","Provider","Status","CreatedAt")
            VALUES ('g1','p2','Fake',0,now());
            """);
        await ExecAsync(cs, "DELETE FROM studio.\"CrossDatabaseRelations\"");
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(cs, "DELETE FROM studio.projects WHERE \"Id\" = 'p2'"));
        Assert.Equal("23503", ex.SqlState);
    }

    [RequiresDockerFact]
    public async Task Project_with_relation_cannot_be_deleted_directly()
    {
        var cs = await UpgradedAsync("restrict");
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(cs, "DELETE FROM studio.projects WHERE \"Id\" = 'p2'"));
        Assert.Equal("23503", ex.SqlState);
    }

    [RequiresDockerFact]
    public async Task Deleting_user_sets_null_across_schemas_and_removes_stars()
    {
        var cs = await UpgradedAsync("user");
        await ExecAsync(cs, "DELETE FROM \"AspNetUsers\" WHERE \"Id\" = 'u2'");
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM hub.\"RepoIssues\" WHERE \"Id\" = 'i1' AND \"CreatedByUserId\" IS NULL"));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM hub.\"RepoIssueComments\" WHERE \"AuthorUserId\" IS NULL"));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM studio.\"SchemaVersions\" WHERE \"AuthorUserId\" IS NULL"));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM hub.\"RepoStars\""));
    }

    [RequiresDockerFact]
    public async Task Orphan_rows_are_rejected_by_cross_schema_fks()
    {
        var cs = await UpgradedAsync("orphan");
        foreach (var sql in new[]
        {
            "INSERT INTO hub.\"RepoStars\" (\"ProjectId\",\"UserId\",\"CreatedAt\") VALUES ('nope','u1',now())",
            "INSERT INTO gateway.\"GatewayTablePermissions\" (\"Id\",\"ProjectId\",\"TableName\",\"CanRead\",\"CanWrite\",\"CanExport\",\"UpdatedAt\") VALUES ('z','nope','t',true,false,false,now())",
            "INSERT INTO studio.\"Branches\" (\"Id\",\"ProjectId\",\"Name\",\"IsDefault\",\"CreatedAt\") VALUES ('z','nope','x',false,now())",
            "INSERT INTO studio.projects (\"Id\",\"Name\",\"DbType\",\"SchemaJson\",\"NodePositionsJson\",\"UserId\",\"CreatedAt\",\"UpdatedAt\",\"AutoApproveSafeChanges\") VALUES ('z','n','d','{}','{}','no-user',now(),now(),false)",
            "INSERT INTO public.\"ChangeRequests\" (\"Id\",\"ProjectId\",\"BranchId\",\"HeadVersionId\",\"Status\",\"RiskLevel\",\"ImpactReportJson\",\"CreatedAt\",\"VotingMembersAtCreation\") VALUES ('z','p1','b1','no-version',0,0,'{}',now(),1)",
        })
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(cs, sql));
            Assert.Equal("23503", ex.SqlState);
        }
    }

    // ── Bileşik etiket FK'si (BL-C1) ────────────────────────────────────────

    [RequiresDockerFact]
    public async Task Label_of_another_project_is_rejected_by_composite_fk()
    {
        var cs = await UpgradedAsync("labelfk");
        // i1 p1'in, l2 p2'nin. Hem kendi ProjectId'siyle hem etiketin ProjectId'siyle reddedilmeli.
        foreach (var project in new[] { "p1", "p2" })
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(cs,
                $"INSERT INTO hub.repo_issue_labels (\"RepoIssueId\",\"IssueLabelId\",\"ProjectId\",\"DisplayName\",\"Position\") VALUES ('i1','l2','{project}','x',1)"));
            Assert.Equal("23503", ex.SqlState);
        }

        // Var olan bağı başka projeye kaydırmak da olmaz; issue'nun projesi değişemez.
        var move = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(cs, "UPDATE hub.\"RepoIssues\" SET \"ProjectId\" = 'p2', \"IssueNumber\" = 99 WHERE \"Id\" = 'i1'"));
        Assert.Equal("23503", move.SqlState);
    }

    [RequiresDockerFact]
    public async Task Migration_stops_when_a_cross_project_label_link_exists()
    {
        var cs = await NewDatabaseAsync("labelverify");
        await using (var ctx = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(cs).Options))
            await ctx.GetService<IMigrator>().MigrateAsync(TestModules.Level14_2C);
        await TestModules.SeedLiveAsync(cs);
        await ExecAsync(cs, "INSERT INTO hub.repo_issue_labels (\"RepoIssueId\",\"IssueLabelId\",\"DisplayName\",\"Position\") VALUES ('i1','l2','x',1)");

        var ex = await Assert.ThrowsAsync<PostgresException>(() => TestModules.MigrateAllAsync(cs));
        Assert.Contains("etiket bagi", ex.MessageText);

        // İşlem geri alındı: tablolar yerinde, Hub zinciri uygulanmadı, sorun giderilince aynı komut tamamlar.
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM pg_tables WHERE schemaname='public' AND tablename='RepoIssues'"));
        await ExecAsync(cs, "DELETE FROM hub.repo_issue_labels WHERE \"IssueLabelId\" = 'l2' AND \"RepoIssueId\" = 'i1'");
        await TestModules.MigrateAllAsync(cs);
        Assert.Empty(await ModuleMigrationOrchestrator.GetPendingAsync(TestModules.Descriptors, cs));
    }

    // ── Modüller arası işlem ────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task Cross_module_transaction_rolls_back_every_module_on_failure()
    {
        var cs = await UpgradedAsync("txn");
        var kit = Kit(cs);
        await Assert.ThrowsAnyAsync<Exception>(() => kit.Transaction.ExecuteAsync(async c =>
        {
            await kit.Projects.SaveAsync(new[] { new ProjectWrite("newp", "New", "PostgreSQL", "{}", "{}", "u1", "o1") }, c);
            await kit.HubService.EnsureRepositoryAsync("newp", "u1", "New", "PostgreSQL", "new-slug", c);
            await kit.Feedback.SubmitAsync(new FeedbackSubmission("u1", null, "bug", "işlem içinde"), c);
            await kit.GatewayMaintenance.SeedTablePermissionAsync("newp", "t", true, false, Array.Empty<string>(), c);
            throw new InvalidOperationException("boom");
        }));

        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM studio.projects WHERE \"Id\" = 'newp'"));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM hub.repositories WHERE \"ProjectId\" = 'newp'"));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM gateway.\"GatewayTablePermissions\" WHERE \"ProjectId\" = 'newp'"));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM ops.\"Feedbacks\" WHERE \"Message\" = 'işlem içinde'"));

        // Bağlamlar yeniden kullanılabilir: geri alma sonrası bağlantı doğru.
        await kit.Transaction.ExecuteAsync(async c =>
            await kit.Projects.SaveAsync(new[] { new ProjectWrite("newp", "New", "PostgreSQL", "{}", "{}", "u1", "o1") }, c));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM studio.projects WHERE \"Id\" = 'newp'"));
    }

    // ── Gerçek eşzamanlılık ─────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task Concurrent_commits_all_succeed_with_unique_versions()
    {
        var cs = await UpgradedAsync("commits");
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
            await Kit(cs).Branches.AddVersionAsync("b1", "{\"n\":" + i + "}", null, 0, "u1"))));
        Assert.Equal(8, results.Select(r => r.Version).Distinct().Count());
        Assert.Equal(9, await CountAsync(cs, "SELECT count(*) FROM studio.\"SchemaVersions\" WHERE \"BranchId\" = 'b1'"));
    }

    [RequiresDockerFact]
    public async Task Concurrent_default_branch_requests_meet_on_one_row()
    {
        var cs = await UpgradedAsync("branches");
        var ids = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
            (await Kit(cs).Branches.GetOrCreateDefaultAsync("p2", "u1")).Id)));
        Assert.Single(ids.Distinct());
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM studio.\"Branches\" WHERE \"ProjectId\" = 'p2'"));
    }

    [RequiresDockerFact]
    public async Task Stale_row_version_is_rejected_on_project_save()
    {
        var cs = await UpgradedAsync("xmin");
        var kit = Kit(cs);
        var current = (await kit.Projects.GetAsync("p1"))!;
        await kit.Projects.SaveAsync(new[] { new ProjectWrite("p1", "A", current.DbType, current.SchemaJson, "{}", current.UserId, current.OrganizationId, current.RowVersion) });
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            kit.Projects.SaveAsync(new[] { new ProjectWrite("p1", "B", current.DbType, current.SchemaJson, "{}", current.UserId, current.OrganizationId, current.RowVersion) }));
        Assert.Equal("A", (await Kit(cs).Projects.GetAsync("p1"))!.Name);
    }

    [RequiresDockerFact]
    public async Task Concurrent_stars_keep_counter_equal_to_rows()
    {
        var cs = await UpgradedAsync("stars");
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            try { await Kit(cs).HubService.ToggleStarAsync("p2", "u1"); }
            catch (DbUpdateException) { /* eşzamanlı tıklama reddedilebilir; sayaç tutarlı kalmalı */ }
        })));
        var rows = await CountAsync(cs, "SELECT count(*) FROM hub.\"RepoStars\" WHERE \"ProjectId\" = 'p2'");
        var counter = await CountAsync(cs, "SELECT \"StarCount\" FROM hub.repositories WHERE \"ProjectId\" = 'p2'");
        Assert.Equal(rows, counter);
    }

    [RequiresDockerFact]
    public async Task Concurrent_issues_get_unique_numbers_and_one_label_row()
    {
        var cs = await UpgradedAsync("issues");
        await ExecAsync(cs, "INSERT INTO \"OrganizationMembers\" (\"OrganizationId\",\"UserId\",\"Role\",\"JoinedAt\") VALUES ('o1','u2',1,now())");
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Task.Run(async () =>
            await Kit(cs).HubIssues.CreateAsync("u2", "p2", new CreateIssueCommand("t" + i, null, null, null, "Race"))) ));
        Assert.All(results, r => Assert.True(r.IsOk, r.Error));
        Assert.Equal(6, results.Select(r => r.Value!.IssueNumber).Distinct().Count());
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM hub.issue_labels WHERE \"ProjectId\" = 'p2' AND \"NormalizedName\" = 'race'"));
    }

    [RequiresDockerFact]
    public async Task Concurrent_permission_puts_end_with_one_row_and_no_error()
    {
        var cs = await UpgradedAsync("perm");
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Task.Run(async () =>
            await Kit(cs).GatewayKeys.SetPermissionAsync("u1", "p2", new PermissionCommand("t", true, false, false, "a,b")))));
        Assert.All(results, r => Assert.True(r.IsOk, r.Error));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM gateway.\"GatewayTablePermissions\" WHERE \"ProjectId\" = 'p2'"));
        Assert.Equal(2, await CountAsync(cs, "SELECT count(*) FROM gateway.table_masked_columns m JOIN gateway.\"GatewayTablePermissions\" p ON p.\"Id\" = m.\"TablePermissionId\" WHERE p.\"ProjectId\" = 'p2'"));
    }

    [RequiresDockerFact]
    public async Task Seeded_key_authenticates_after_the_move()
    {
        var cs = await UpgradedAsync("key");
        var kit = Kit(cs);
        var key = await kit.GatewayAccess.AuthenticateAsync(TestModules.SeededKeyRaw);
        Assert.NotNull(key);
        Assert.True(await kit.GatewayAccess.IsTableAllowedAsync(key!, "musteri", forWrite: false));
        Assert.Equal(new[] { "eposta" }, await kit.GatewayAccess.MaskedColumnsAsync("p1", "musteri"));
    }

    [RequiresDockerFact]
    public async Task Governance_reads_studio_rows_through_the_contract_after_the_move()
    {
        var cs = await UpgradedAsync("governance");
        var kit = Kit(cs);
        var cr = await kit.Db.Governance().ChangeRequests.AsNoTracking().SingleAsync();
        Assert.Equal("main", (await kit.Branches.GetAsync(cr.BranchId))!.Name);
        Assert.Equal(1, (await kit.Branches.GetVersionAsync(cr.HeadVersionId))!.Version);
    }

    [RequiresDockerFact]
    public async Task Retention_runs_against_moved_tables()
    {
        var cs = await UpgradedAsync("retention");
        await ExecAsync(cs, "UPDATE gateway.\"GatewayAuditEntries\" SET \"CreatedAt\" = now() - interval '400 days'");
        await ExecAsync(cs, "UPDATE ops.\"SqlExecutionAudits\" SET \"CreatedAt\" = now() - interval '400 days'");
        var kit = Kit(cs);
        Assert.Equal(1, await kit.GatewayMaintenance.PurgeAuditAsync(365, DateTime.UtcNow, TimeSpan.Zero));
        Assert.Equal(1, await kit.OpsRetention.PurgeSqlExecutionAuditsAsync(365, DateTime.UtcNow, TimeSpan.Zero));
    }
}
