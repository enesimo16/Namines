using Namines.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Persistence;
using Namines.Infrastructure.Data;
using Namines.Studio.Domain;
using Namines.Studio.Infrastructure.Persistence;
using Namines.Hub.Domain;
using Namines.Hub.Infrastructure.Persistence;
using Namines.Gateway.Application;
using Namines.Gateway.Domain;
using Namines.Gateway.Infrastructure.Persistence;
using Namines.Ops.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace Namines.Tests.TestSupport;

/// <summary>Migration testlerinin ortak el kitabı: taşınan tablolar, tohum, beklenen sayımlar.</summary>
internal static partial class TestModules
{
    /// <summary>D-1'den önceki son Auth migration'ı: canlı sistemin bugünkü seviyesi.</summary>
    public const string Level14_2C = "20261105114438_Phase14_2C_NormalizeCollections";

    public static IReadOnlyList<(string Schema, string Table)> MovedTables { get; } = new[]
    {
        ("studio", "projects"), ("studio", "project_connections"), ("studio", "project_shares"),
        ("studio", "Branches"), ("studio", "SchemaVersions"), ("studio", "CrossDatabaseRelations"),
        ("hub", "repositories"), ("hub", "project_tags"), ("hub", "issue_labels"), ("hub", "repo_issue_labels"),
        ("hub", "RepoIssues"), ("hub", "RepoIssueComments"), ("hub", "RepoStars"),
        ("gateway", "GatewayApiKeys"), ("gateway", "GatewayTablePermissions"), ("gateway", "GatewayAuditEntries"),
        ("gateway", "api_key_allowed_origins"), ("gateway", "api_key_allowed_ips"), ("gateway", "table_masked_columns"),
        ("ops", "Feedbacks"), ("ops", "SqlExecutionAudits"), ("ops", "FeatureFlags"), ("ops", "AiToolRoutes"),
        ("billing", "UsageEvents"), ("billing", "UserAIQuotas"), ("billing", "UserBillingSettings"), ("billing", "GlobalAiUsages"),
        ("billing", "OrgAiUsages"), ("billing", "StripeWebhookEvents"), ("billing", "PendingSubscriptionCancellations"),
        ("billing", "customers"), ("billing", "subscriptions"),
        ("governance", "ChangeRequests"), ("governance", "ChangeRequestApprovals"), ("governance", "ChangeRequestAuditLogs"), ("governance", "UserAIPolicies"),
    };

    public static IReadOnlyList<Func<string, DbContext>> OwnerContextFactories =>
        Descriptors.Select(d => d.CreateContext).ToList();

    /// <summary>
    /// Auth dışındakiler: Auth zincirinin Vault/Ground/Flow/Desk tablolarındaki eski PK adları (tablo adı
    /// değiştirilirken PK'lar yeniden adlandırılmamış) bu fazın kapsamı dışında, ayrı backlog maddesi.
    /// </summary>
    public static IReadOnlyList<Func<string, DbContext>> ModuleOwnerContextFactories =>
        Descriptors.Where(d => d.Module != "Auth").Select(d => d.CreateContext).ToList();

    /// <summary>Çalışma zamanında tablo eşleyen her bağlam: sahip olanlar ve zinciri olmayanlar.</summary>
    public static IReadOnlyList<Func<string, DbContext>> AllContextFactories => OwnerContextFactories;

    public static Task MigrateAllAsync(string connectionString, bool upToAuthOnly) =>
        ModuleMigrationOrchestrator.MigrateAllAsync(
            upToAuthOnly ? Descriptors.Where(d => d.Module == "Auth") : Descriptors,
            connectionString, TimeSpan.FromMinutes(5));

    /// <summary>14.2C seviyesinde, tabloların o günkü yerinde gerçekçi tohum: her taşınan tablo dolu.</summary>
    public static async Task SeedLiveAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(SeedSql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public const string SeededKeyRaw = "nmn_seededlivekey000000000000000000000";

    private static readonly string SeedSql = $$"""
        INSERT INTO "AspNetUsers" ("Id","Type","CreatedAt","IsDev","EmailConfirmed","PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount","UserName","NormalizedUserName")
        VALUES ('u1',1,now(),false,true,false,false,true,0,'una','UNA'), ('u2',1,now(),false,true,false,false,true,0,'ece','ECE');
        INSERT INTO "Organizations" ("Id","Name","IsPersonal","CreatedAt","CreatedByUserId") VALUES ('o1','Org',false,now(),'u1');
        INSERT INTO "OrganizationMembers" ("OrganizationId","UserId","Role","JoinedAt") VALUES ('o1','u1',3,now());

        INSERT INTO studio.projects ("Id","Name","DbType","SchemaJson","NodePositionsJson","UserId","OrganizationId","CreatedAt","UpdatedAt","AutoApproveSafeChanges")
        VALUES ('p1','P1','PostgreSQL','{"tables":[]}','{}','u1','o1',now(),now(),false),
               ('p2','P2','PostgreSQL','{"tables":[]}','{}','u1','o1',now(),now(),false);
        INSERT INTO studio.project_connections ("ProjectId","EncryptedConnectionString","ConnectionDbType","AllowDeskSql","CreatedAt","UpdatedAt")
        VALUES ('p1','cipher','PostgreSQL',true,now(),now());
        INSERT INTO studio.project_shares ("Id","ProjectId","ShareTokenHash","CreatedAt")
        VALUES ('s1','p1',repeat('a',64),now());
        INSERT INTO public."Branches" ("Id","ProjectId","Name","IsDefault","CreatedByUserId","CreatedAt")
        VALUES ('b1','p1','main',true,'u1',now()), ('b2','p1','dev',false,'u2',now());
        INSERT INTO public."SchemaVersions" ("Id","ProjectId","BranchId","Version","Checksum","SchemaJson","TableCount","AuthorUserId","CreatedAt")
        VALUES ('v1','p1','b1',1,'c','{}',0,'u2',now());
        INSERT INTO public."CrossDatabaseRelations" ("Id","SourceProjectId","SourceTableId","SourceColumnId","TargetProjectId","TargetTableId","TargetColumnId","CreatedByUserId","CreatedAt")
        VALUES ('x1','p1','t','c','p2','t','c','u1',now());

        INSERT INTO hub.repositories ("ProjectId","OwnerUserId","ProjectName","ProjectDbType","Slug","Description","Visibility","Category","StarCount","ForkCount","CreatedAt","UpdatedAt")
        VALUES ('p1','u1','P1','PostgreSQL','one','d',2,'General',1,0,now(),now()),
               ('p2','u1','P2','PostgreSQL','two','d',0,'General',0,0,now(),now());
        INSERT INTO hub.project_tags ("ProjectId","Tag","DisplayTag","Position") VALUES ('p1','saas','SaaS',0);
        INSERT INTO public."RepoStars" ("ProjectId","UserId","CreatedAt") VALUES ('p1','u2',now());
        INSERT INTO public."RepoIssues" ("Id","ProjectId","IssueNumber","Title","Body","IsClosed","CreatedByUserId","CreatedAt")
        VALUES ('i1','p1',1,'t','b',false,'u2',now()), ('i2','p2',1,'t2','b',false,'u1',now());
        INSERT INTO public."RepoIssueComments" ("Id","IssueId","Body","AuthorUserId","CreatedAt") VALUES ('m1','i1','c','u2',now());
        INSERT INTO hub.issue_labels ("Id","ProjectId","Name","NormalizedName") VALUES ('l1','p1','Bug','bug'), ('l2','p2','Bug','bug');
        INSERT INTO hub.repo_issue_labels ("RepoIssueId","IssueLabelId","DisplayName","Position") VALUES ('i1','l1','Bug',0), ('i2','l2','bug',0);

        INSERT INTO public."GatewayApiKeys" ("Id","ProjectId","Name","Prefix","KeyHash","CanWrite","CanExecuteSql","CanExport","RestrictOrigins","RestrictIps","RateLimitPerMinute","CreatedByUserId","CreatedAt")
        VALUES ('k1','p1','k',left('{{SeededKeyRaw}}',12),upper(encode(sha256('{{SeededKeyRaw}}'::bytea),'hex')),false,false,false,true,true,600,'u1',now());
        INSERT INTO gateway.api_key_allowed_origins ("Id","ApiKeyId","Origin","Position") VALUES ('oo1','k1','https://a.com',0);
        INSERT INTO gateway.api_key_allowed_ips ("Id","ApiKeyId","Cidr","Position") VALUES ('ii1','k1','10.0.0.0/8',0);
        INSERT INTO public."GatewayTablePermissions" ("Id","ProjectId","TableName","CanRead","CanWrite","CanExport","UpdatedAt")
        VALUES ('tp1','p1','musteri',true,false,false,now());
        INSERT INTO gateway.table_masked_columns ("Id","TablePermissionId","ColumnName","Position") VALUES ('mc1','tp1','eposta',0);
        INSERT INTO public."GatewayAuditEntries" ("Id","ProjectId","ApiKeyId","ApiKeyPrefix","Kind","TableName","AffectedRows","Succeeded","CreatedAt")
        VALUES ('a1','p1','k1','nmn_',0,'musteri',1,true,now());

        INSERT INTO public."ChangeRequests" ("Id","ProjectId","BranchId","HeadVersionId","Status","RiskLevel","ImpactReportJson","CreatedAt","VotingMembersAtCreation")
        VALUES ('cr1','p1','b1','v1',0,0,'{}',now(),1);

        INSERT INTO public."Feedbacks" ("UserId","Email","Category","Message","CreatedAt")
        VALUES ('u1','a@example.com','bug','tohum geri bildirim',now()), (NULL,NULL,'idea','anonim',now());
        INSERT INTO public."SqlExecutionAudits" ("Id","UserId","DbType","ScriptHash","ScriptPreview","ScriptLength","ContainsDestructiveKeyword","Success","StatementsExecuted","PartialApplyPossible","CreatedAt")
        VALUES ('sa1','u1','PostgreSQL','h','select 1',8,false,true,1,false,now());
        INSERT INTO public."FeatureFlags" ("FeatureKey","DisplayName","Description","Category","IsEnabled","Mode","UpdatedAt")
        VALUES ('canvas','Canvas','d','Platform',false,'Hidden',now());
        INSERT INTO public."AiToolRoutes" ("ToolKey","DisplayName","Provider","ModelName","Temperature","UpdatedAt")
        VALUES ('ai_dba','DBA','deepseek','m',0.3,now());
        """;

    private static readonly (string Table, long Rows)[] Seeded =
    {
        ("studio.\"Branches\"", 2), ("studio.\"SchemaVersions\"", 1), ("studio.\"CrossDatabaseRelations\"", 1),
        ("hub.\"RepoStars\"", 1), ("hub.\"RepoIssues\"", 2), ("hub.\"RepoIssueComments\"", 1),
        ("gateway.\"GatewayApiKeys\"", 1), ("gateway.\"GatewayTablePermissions\"", 1), ("gateway.\"GatewayAuditEntries\"", 1),
        ("ops.\"Feedbacks\"", 2), ("ops.\"SqlExecutionAudits\"", 1), ("ops.\"FeatureFlags\"", 1), ("ops.\"AiToolRoutes\"", 1),
        ("governance.\"ChangeRequests\"", 1),
    };

    /// <summary>Bu fazda şema değiştirmeyenler: her iki hâlde aynı yerde.</summary>
    private static readonly (string Table, long Rows)[] Stationary =
    {
        ("studio.projects", 2), ("studio.project_connections", 1), ("studio.project_shares", 1),
        ("hub.repositories", 2), ("hub.project_tags", 1), ("hub.issue_labels", 2), ("hub.repo_issue_labels", 2),
        ("gateway.api_key_allowed_origins", 1), ("gateway.api_key_allowed_ips", 1), ("gateway.table_masked_columns", 1),
    };

    public static IReadOnlyList<(string Table, long Expected)> SeededCountsBeforeMove { get; } =
        Seeded.Select(s => (string.Concat("public.", s.Table.AsSpan(s.Table.IndexOf('.') + 1)), s.Rows)).Concat(Stationary).ToList();

    public static IReadOnlyList<(string Table, long Expected)> SeededCountsAfterMove { get; } =
        Seeded.Concat(Stationary).ToList();

    private static async Task<long> ScalarAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    /// <summary>Taşınmış veriyi uygulama bağlamlarıyla okur, yazar ve şemalar arası kısıtların canlı olduğunu sınar.</summary>
    public static async Task AssertSeedIntactAsync(string cs)
    {
        await using var ops = new OpsDbContext(ModuleDbContextRegistration.MigrationOptions<OpsDbContext>(cs, OpsDbContext.SchemaName));
        var flag = await ops.FeatureFlags.AsNoTracking().SingleAsync(f => f.FeatureKey == "canvas");
        Assert.False(flag.IsEnabled);
        Assert.Equal("Hidden", flag.Mode);

        // Kimlik dizisi tabloyla taşındı: yeni satır çakışmadan eklenir.
        var added = new Namines.Ops.Domain.Feedback { Message = "taşımadan sonra", UserId = "u1" };
        ops.Feedbacks.Add(added);
        await ops.SaveChangesAsync();
        Assert.True(added.Id > 2);
        ops.Feedbacks.Remove(added);
        await ops.SaveChangesAsync();

        await using var studio = new StudioDbContext(ModuleDbContextRegistration.MigrationOptions<StudioDbContext>(cs, StudioDbContext.SchemaName));
        var project = await studio.Projects.AsNoTracking().SingleAsync(p => p.Id == "p1");
        Assert.True(project.RowVersion > 0);
        Assert.Equal(1, await studio.SchemaVersions.CountAsync(v => v.BranchId == "b1"));

        await using var hub = new HubDbContext(ModuleDbContextRegistration.MigrationOptions<HubDbContext>(cs, HubDbContext.SchemaName));
        var link = await hub.RepoIssueLabels.AsNoTracking().Include(l => l.Label).SingleAsync(l => l.RepoIssueId == "i1");
        Assert.Equal(("p1", "bug"), (link.ProjectId, link.Label.NormalizedName));
        Assert.Equal(1, await hub.RepoIssues.CountAsync(i => i.ProjectId == "p1" && i.LabelLinks.Any(l => l.Label.NormalizedName == "bug")));

        await using var gateway = new GatewayDbContext(ModuleDbContextRegistration.MigrationOptions<GatewayDbContext>(cs, GatewayDbContext.SchemaName));
        var key = await gateway.GatewayApiKeys.AsNoTracking().Include(k => k.OriginRules).Include(k => k.IpRules).AsSplitQuery().SingleAsync();
        Assert.Equal(GatewayKeys.Hash(SeededKeyRaw), key.KeyHash);
        Assert.Equal("https://a.com", key.OriginRules.Single().Origin);
        Assert.Equal("eposta", (await gateway.MaskedColumns.AsNoTracking().SingleAsync()).ColumnName);

        // Tüm şemalar arası FK'ler silinmedi ve hedefleri doğru.
        foreach (var (table, constraint, action) in CrossSchemaForeignKeys)
        {
            var rows = await ScalarAsync(cs, $"""
                SELECT count(*) FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid JOIN pg_namespace n ON n.oid = t.relnamespace
                WHERE n.nspname || '.' || t.relname = '{table}' AND c.conname = '{constraint}' AND c.confdeltype = '{action}' AND c.convalidated
                """);
            Assert.True(rows == 1, $"{table}.{constraint} ({action}) yok ya da doğrulanmamış");
        }
    }

    /// <summary>Modül bağlamlarının modellemediği, DB'de kalan FK'ler. a = NO ACTION, c = cascade, n = set null, r = restrict.</summary>
    public static IReadOnlyList<(string Table, string Constraint, char Action)> CrossSchemaForeignKeys { get; } = new[]
    {
        ("ops.Feedbacks", "FK_Feedbacks_AspNetUsers_UserId", 'n'),
        ("studio.projects", "FK_projects_AspNetUsers_UserId", 'c'),
        ("studio.projects", "FK_projects_Organizations_OrganizationId", 'c'),
        ("studio.Branches", "FK_Branches_AspNetUsers_CreatedByUserId", 'n'),
        ("studio.SchemaVersions", "FK_SchemaVersions_AspNetUsers_AuthorUserId", 'n'),
        ("studio.CrossDatabaseRelations", "FK_CrossDatabaseRelations_AspNetUsers_CreatedByUserId", 'n'),
        ("hub.repositories", "FK_repositories_projects_ProjectId", 'c'),
        ("hub.issue_labels", "FK_issue_labels_projects_ProjectId", 'c'),
        ("hub.RepoIssues", "FK_RepoIssues_projects_ProjectId", 'c'),
        ("hub.RepoIssues", "FK_RepoIssues_AspNetUsers_CreatedByUserId", 'n'),
        ("hub.RepoIssueComments", "FK_RepoIssueComments_AspNetUsers_AuthorUserId", 'n'),
        ("hub.RepoStars", "FK_RepoStars_projects_ProjectId", 'c'),
        ("hub.RepoStars", "FK_RepoStars_AspNetUsers_UserId", 'c'),
        ("gateway.GatewayApiKeys", "FK_GatewayApiKeys_projects_ProjectId", 'c'),
        ("gateway.GatewayApiKeys", "FK_GatewayApiKeys_AspNetUsers_CreatedByUserId", 'n'),
        ("gateway.GatewayTablePermissions", "FK_GatewayTablePermissions_projects_ProjectId", 'c'),
        ("governance.ChangeRequests", "FK_ChangeRequests_projects_ProjectId", 'c'),
        ("governance.ChangeRequests", "FK_ChangeRequests_Branches_BranchId", 'c'),
        ("governance.ChangeRequests", "FK_ChangeRequests_SchemaVersions_HeadVersionId", 'a'),
        ("flow.automation_rules", "FK_automation_rules_projects_ProjectId", 'c'),
        ("ground.ground_databases", "FK_ground_databases_projects_ProjectId", 'r'),
        ("namines_vault.vault_backups", "FK_vault_backups_projects_ProjectId", 'r'),
        ("desk.saved_queries", "FK_saved_queries_projects_ProjectId", 'c'),
    };
}
