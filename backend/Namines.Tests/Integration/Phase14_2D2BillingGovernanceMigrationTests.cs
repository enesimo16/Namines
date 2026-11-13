using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Namines.BuildingBlocks.Persistence;
using Namines.Identity.Infrastructure.Persistence;
using Namines.Tests.TestSupport;
using Npgsql;
using Xunit;

namespace Namines.Tests.Integration;

/// <summary>
/// Billing_01 ve Governance_01 migration'ları gerçek PostgreSQL'de: boş veritabanı, D-1 seviyesinden (Stripe alanları
/// kullanıcı satırında, dolu/boş karışık) yükseltme ve alan düzeyinde eşitlik, oturum alanlarına dokunulmaması, Down ile
/// veri geri dönüşü ve yeniden ileri, aynı Stripe müşterisinin iki kullanıcıda bulunması hâlinde açık hatayla durma.
/// </summary>
[Collection("Docker")]
public sealed class Phase14_2D2BillingGovernanceMigrationTests : IClassFixture<Phase14_2BPostgresFixture>
{
    /// <summary>D-2'den önceki son Auth migration'ı: canlı sistemin D-1 sonrası seviyesi.</summary>
    private const string LevelD1 = "20261105163819_Phase14_2D1_DropModuleOrganizationCopies";

    private static readonly string[] StripeColumns =
        { "StripeCustomerId", "StripeSubscriptionId", "SubscriptionStatus", "CurrentPeriodEnd", "PlanCode" };

    private static readonly string[] BillingTables =
        { "UsageEvents", "UserAIQuotas", "UserBillingSettings", "GlobalAiUsages", "OrgAiUsages", "StripeWebhookEvents", "PendingSubscriptionCancellations" };

    private static readonly string[] GovernanceTables =
        { "ChangeRequests", "ChangeRequestApprovals", "ChangeRequestAuditLogs", "UserAIPolicies" };

    private readonly Phase14_2BPostgresFixture _pg;

    public Phase14_2D2BillingGovernanceMigrationTests(Phase14_2BPostgresFixture pg) => _pg = pg;

    private static AuthDbContext Open(string cs) =>
        new(new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(cs).Options);

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

    private static async Task<string> Md5Async(string cs, string table)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT coalesce(md5(string_agg(t::text, '|' ORDER BY t::text)), 'empty') FROM {table} t", conn);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private static Task<long> TableExistsAsync(string cs, string schema, string table) =>
        CountAsync(cs, $"SELECT count(*) FROM information_schema.tables WHERE table_schema = '{schema}' AND table_name = '{table}'");

    // ── D-1 seviyesi ─────────────────────────────────────────────────────────

    /// <summary>Auth zinciri D-1'de durur, D-1 modül zincirleri uygulanmıştır; Billing/Governance tabloları hâlâ public'te.</summary>
    private async Task<string> D1DatabaseAsync(string name)
    {
        PostgresTestPools.Reset();
        var cs = await _pg.CreateDatabaseAsync(name);
        await using (var ctx = Open(cs))
            await ctx.GetService<IMigrator>().MigrateAsync(LevelD1);
        var d1Modules = TestModules.Descriptors.Where(d => d.Order is 20 or 90 or 100 or 110);
        await ModuleMigrationOrchestrator.MigrateAllAsync(d1Modules, cs, TimeSpan.FromMinutes(5));
        return cs;
    }

    /// <summary>Karışık gerçekçi tohum: Stripe alanları dolu, kısmen dolu, boş ve boş dizge olan kullanıcılar + her modül tablosu dolu.</summary>
    private static Task SeedD1Async(string cs, string duplicateCustomerFor = "")
    {
        const string userColumns = "\"Type\",\"CreatedAt\",\"IsDev\",\"EmailConfirmed\",\"PhoneNumberConfirmed\",\"TwoFactorEnabled\",\"LockoutEnabled\",\"AccessFailedCount\"";
        const string common = "1,now(),false,true,false,false,true,0";
        return ExecAsync(cs, """
            INSERT INTO "AspNetUsers" ("Id",@@UC@@,"UserName","NormalizedUserName","PasswordHash","SecurityStamp","ConcurrencyStamp","StripeCustomerId","StripeSubscriptionId","SubscriptionStatus","PlanCode","CurrentPeriodEnd") VALUES
              ('u1',@@COMMON@@,'una','UNA','hash-u1','ss-1','cs-1','cus_1','sub_1','active','pro','2026-12-01T10:30:00Z'),
              ('u2',@@COMMON@@,'ece','ECE','hash-u2','ss-2','cs-2','cus_2',NULL,'canceled',NULL,NULL),
              ('u3',@@COMMON@@,'mert','MERT','hash-u3','ss-3','cs-3',NULL,NULL,NULL,NULL,NULL),
              ('u4',@@COMMON@@,'ada','ADA','hash-u4','ss-4','cs-4','',NULL,NULL,NULL,NULL),
              ('u5',@@COMMON@@,'can','CAN','hash-u5','ss-5','cs-5',NULL,'sub_5','past_due','team','2027-01-01T00:00:00Z'),
              ('u6',@@COMMON@@,'efe','EFE','hash-u6','ss-6','cs-6',NULL,NULL,NULL,'pro',NULL);
            @@DUP@@

            INSERT INTO "Organizations" ("Id","Name","IsPersonal","CreatedAt","CreatedByUserId") VALUES ('o1','Org',false,now(),'u1');
            INSERT INTO "OrganizationMembers" ("OrganizationId","UserId","Role","JoinedAt") VALUES ('o1','u1',3,now()), ('o1','u2',2,now());
            INSERT INTO studio.projects ("Id","Name","DbType","SchemaJson","NodePositionsJson","UserId","OrganizationId","CreatedAt","UpdatedAt","AutoApproveSafeChanges")
              VALUES ('p1','P1','PostgreSQL','{"tables":[]}','{}','u1','o1',now(),now(),false);
            INSERT INTO studio."Branches" ("Id","ProjectId","Name","IsDefault","CreatedByUserId","CreatedAt") VALUES ('b1','p1','main',true,'u1',now());
            INSERT INTO studio."SchemaVersions" ("Id","ProjectId","BranchId","Version","Checksum","SchemaJson","TableCount","AuthorUserId","CreatedAt")
              VALUES ('v1','p1','b1',1,'c','{}',0,'u2',now());

            INSERT INTO "UsageEvents" ("UserId","Resource","Quantity","BillingPeriod","OccurredAt","Context") VALUES
              ('u1',0,3.5,'2026-11-01T00:00:00Z',now(),'ctx'), ('u2',1,100,'2026-11-01T00:00:00Z',now(),NULL), (NULL,0,1,'2026-10-01T00:00:00Z',now(),NULL);
            INSERT INTO "UserAIQuotas" ("UserId","DailyLimit","DailyUsageCount","LastResetDate") VALUES ('u1',20000,1500,now()), ('u2',100,0,now());
            INSERT INTO "UserBillingSettings" ("UserId","OverageEnabled","MonthlyCapUsd","UpdatedAt") VALUES ('u1',true,25.50,now());
            INSERT INTO "GlobalAiUsages" ("Date","TokensUsed") VALUES ('2026-11-06T00:00:00Z',123456), ('2026-11-07T00:00:00Z',789);
            INSERT INTO "OrgAiUsages" ("OrganizationId","Date","TokensUsed") VALUES ('o1','2026-11-07T00:00:00Z',4242);
            INSERT INTO "StripeWebhookEvents" ("Id","EventType","EventCreatedAt","StripeCustomerId","ProcessedAt","LatestProcessedForCustomer") VALUES
              ('evt_1','checkout.session.completed',now(),'cus_1',now(),true), ('evt_2','customer.subscription.deleted',now(),'cus_2',now(),false);
            INSERT INTO "PendingSubscriptionCancellations" ("Id","SubscriptionId","CreatedAt","Attempts","LastAttemptAt","NextAttemptAt","CompletedAt") VALUES
              ('pc1','sub_old',now(),2,now(),now(),NULL), ('pc2','sub_done',now(),1,now(),now(),now());

            INSERT INTO "ChangeRequests" ("Id","ProjectId","BranchId","HeadVersionId","Title","Status","RiskLevel","ImpactReportJson","CreatedByUserId","CreatedAt","ResolvedAt","VotingMembersAtCreation") VALUES
              ('cr0','p1','b1','v1','pending',0,1,'{}','u1',now(),NULL,2),
              ('cr1','p1','b1','v1','approved',1,2,'{}','u1',now(),now(),2),
              ('cr2','p1','b1','v1','rejected',2,3,'{}','u2',now(),now(),2);
            INSERT INTO "ChangeRequestApprovals" ("Id","ChangeRequestId","UserId","Decision","Comment","CreatedAt") VALUES
              ('ap1','cr1','u2',0,'lgtm',now()), ('ap2','cr2','u1',1,'no',now());
            INSERT INTO "ChangeRequestAuditLogs" ("Id","ChangeRequestId","ProjectId","ChangeRequestTitle","Action","ActorUserId","Details","CreatedAt") VALUES
              ('al0','cr0','p1','pending',0,'u1',NULL,now()), ('al1','cr1','p1','approved',2,'u2','lgtm',now()), ('al2','cr1','p1','approved',1,NULL,'auto',now());
            INSERT INTO "UserAIPolicies" ("UserId","SmartSeed","Documentation","Scaffolding","SchemaGeneration","SchemaRevision","DbaAnalysis","Migration","Voice","AdvancedJson","UpdatedAt")
              VALUES ('u1',4,1,1,2,1,1,1,1,'{"maxTokens":"8000"}',now());
            """
            .Replace("@@UC@@", userColumns).Replace("@@COMMON@@", common)
            .Replace("@@DUP@@", duplicateCustomerFor.Length == 0 ? "" : $"UPDATE \"AspNetUsers\" SET \"StripeCustomerId\" = 'cus_1' WHERE \"Id\" = '{duplicateCustomerFor}';"));
    }

    private sealed record UserRow(string Id, string? Customer, string? Subscription, string? Status, string? Plan, DateTime? PeriodEnd);

    private static async Task<List<UserRow>> ReadStripeColumnsAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT \"Id\",\"StripeCustomerId\",\"StripeSubscriptionId\",\"SubscriptionStatus\",\"PlanCode\",\"CurrentPeriodEnd\" FROM \"AspNetUsers\" ORDER BY \"Id\"", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<UserRow>();
        while (await reader.ReadAsync())
            rows.Add(new UserRow(reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetDateTime(5)));
        return rows;
    }

    /// <summary>Kimlik doğrulama durumu: parola özeti, güvenlik damgası, eşzamanlılık damgası. Göç bunlara dokunmamalı.</summary>
    private static async Task<string> SessionFingerprintAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT coalesce(string_agg(\"Id\" || ':' || \"PasswordHash\" || ':' || \"SecurityStamp\" || ':' || \"ConcurrencyStamp\", '|' ORDER BY \"Id\"), '') FROM \"AspNetUsers\"", conn);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private sealed record Snapshot(
        List<UserRow> Users, string Session, Dictionary<string, string> PublicDigests, Dictionary<string, long> Counts);

    private static async Task<Snapshot> TakeSnapshotAsync(string cs)
    {
        var digests = new Dictionary<string, string>();
        var counts = new Dictionary<string, long>();
        foreach (var table in BillingTables.Concat(GovernanceTables))
        {
            digests[table] = await Md5Async(cs, $"public.\"{table}\"");
            counts[table] = await CountAsync(cs, $"SELECT count(*) FROM public.\"{table}\"");
        }
        return new Snapshot(await ReadStripeColumnsAsync(cs), await SessionFingerprintAsync(cs), digests, counts);
    }

    private static async Task AssertUpgradedAsync(string cs, Snapshot before)
    {
        // Kolonlar fiziksel olarak düştü (yalnızca NULL'lanmadı).
        foreach (var column in StripeColumns)
            Assert.Equal(0, await CountAsync(cs, $"SELECT count(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'AspNetUsers' AND column_name = '{column}'"));

        // Alan düzeyinde eşitlik: müşteri eşlemesi yalnızca boş olmayan kimlikler için.
        var expectedCustomers = before.Users.Where(u => !string.IsNullOrEmpty(u.Customer)).Select(u => (u.Id, u.Customer)).OrderBy(x => x.Id).ToList();
        var actualCustomers = new List<(string, string?)>();
        await using (var conn = new NpgsqlConnection(cs))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT \"UserId\",\"StripeCustomerId\" FROM billing.customers ORDER BY \"UserId\"", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) actualCustomers.Add((reader.GetString(0), reader.GetString(1)));
        }
        Assert.Equal(expectedCustomers, actualCustomers);

        var expectedSubscriptions = before.Users
            .Where(u => u.Subscription != null || u.Status != null || u.Plan != null || u.PeriodEnd != null)
            .Select(u => (u.Id, u.Subscription, u.Status, u.Plan, u.PeriodEnd)).OrderBy(x => x.Id).ToList();
        var actualSubscriptions = new List<(string, string?, string?, string?, DateTime?)>();
        await using (var conn = new NpgsqlConnection(cs))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT \"UserId\",\"StripeSubscriptionId\",\"Status\",\"PlanCode\",\"CurrentPeriodEnd\" FROM billing.subscriptions ORDER BY \"UserId\"", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                actualSubscriptions.Add((reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetDateTime(4)));
        }
        Assert.Equal(expectedSubscriptions, actualSubscriptions);

        // Oturumlar etkilenmedi: parola özeti, SecurityStamp ve ConcurrencyStamp birebir aynı (kullanıcı satırlarına UPDATE yok).
        Assert.Equal(before.Session, await SessionFingerprintAsync(cs));

        // Yedi faturalama ve dört yönetişim tablosu yerinde taşındı: satır sayısı ve içerik özeti aynı.
        foreach (var table in BillingTables)
            Assert.Equal((before.Counts[table], before.PublicDigests[table]), (await CountAsync(cs, $"SELECT count(*) FROM billing.\"{table}\""), await Md5Async(cs, $"billing.\"{table}\"")));
        foreach (var table in GovernanceTables)
            Assert.Equal((before.Counts[table], before.PublicDigests[table]), (await CountAsync(cs, $"SELECT count(*) FROM governance.\"{table}\""), await Md5Async(cs, $"governance.\"{table}\"")));
        foreach (var table in BillingTables.Concat(GovernanceTables))
            Assert.Equal(0, await TableExistsAsync(cs, "public", table));

        // Denetim izi tetikleyicisi canlı.
        Assert.Equal(2, await CountAsync(cs, "SELECT count(*) FROM pg_trigger WHERE tgrelid = 'governance.\"ChangeRequestAuditLogs\"'::regclass AND NOT tgisinternal"));
    }

    // ── Testler ──────────────────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task Fresh_database_gets_the_module_tables_without_stripe_columns_and_with_the_audit_guard()
    {
        PostgresTestPools.Reset();
        var cs = await _pg.CreateDatabaseAsync("m_fresh");
        await using (var db = Open(cs)) await db.MigrateAllModulesAsync();

        foreach (var column in StripeColumns)
            Assert.Equal(0, await CountAsync(cs, $"SELECT count(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'AspNetUsers' AND column_name = '{column}'"));
        foreach (var table in BillingTables.Concat(new[] { "customers", "subscriptions" }))
            Assert.Equal(1, await TableExistsAsync(cs, "billing", table));
        foreach (var table in GovernanceTables)
            Assert.Equal(1, await TableExistsAsync(cs, "governance", table));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM pg_indexes WHERE schemaname = 'billing' AND tablename = 'customers' AND indexdef ILIKE '%UNIQUE%StripeCustomerId%'"));
        Assert.Equal(2, await CountAsync(cs, "SELECT count(*) FROM pg_trigger WHERE tgrelid = 'governance.\"ChangeRequestAuditLogs\"'::regclass AND NOT tgisinternal"));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM billing.\"__EFMigrationsHistory\""));
        // Governance_01 (tablolar + koruma) ve Governance_02 (sahibe ait saklama tabanı).
        Assert.Equal(2, await CountAsync(cs, "SELECT count(*) FROM governance.\"__EFMigrationsHistory\""));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'governance' AND p.proname = 'audit_retention_floor_days'"));

        // Kullanıcı silinince müşteri ve abonelik eşlemesi gider (FK cascade); outbox satırı hesaptan uzun yaşar (FK yok).
        await ExecAsync(cs, """
            INSERT INTO "AspNetUsers" ("Id","Type","CreatedAt","IsDev","EmailConfirmed","PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount","UserName","NormalizedUserName")
              VALUES ('x',1,now(),false,true,false,false,true,0,'x','X');
            INSERT INTO billing.customers ("UserId","StripeCustomerId","CreatedAt") VALUES ('x','cus_x',now());
            INSERT INTO billing.subscriptions ("UserId","Status","UpdatedAt") VALUES ('x','active',now());
            INSERT INTO billing."PendingSubscriptionCancellations" ("Id","SubscriptionId","CreatedAt","Attempts","NextAttemptAt") VALUES ('p','sub_x',now(),0,now());
            DELETE FROM "AspNetUsers" WHERE "Id" = 'x';
            """);
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM billing.customers"));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM billing.subscriptions"));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM billing.\"PendingSubscriptionCancellations\""));
    }

    [RequiresDockerFact]
    public async Task Upgrade_from_the_d1_level_moves_stripe_fields_losslessly_and_leaves_sessions_untouched()
    {
        var cs = await D1DatabaseAsync("m_upgrade");
        await SeedD1Async(cs);
        var before = await TakeSnapshotAsync(cs);
        Assert.Equal(6, before.Users.Count);
        Assert.All(BillingTables.Concat(GovernanceTables), t => Assert.True(before.Counts[t] > 0, t + " tohumu boş"));

        await TestModules.MigrateAllAsync(cs);

        await AssertUpgradedAsync(cs, before);

        // Taşınan veri uygulama katmanından okunur: plan çözümü, yeni tablolardan.
        await using var db = Open(cs);
        var plan = await TestModules.For(db).BillingAccounts.GetPlanSnapshotAsync("u1");
        Assert.Equal(("active", "pro", false), (plan!.SubscriptionStatus, plan.PlanCode, plan.IsDev));
        var views = await TestModules.For(db).BillingAccounts.GetSubscriptionsAsync(new[] { "u1", "u2", "u3", "u4" });
        Assert.True(views["u1"].HasStripeCustomer);
        Assert.True(views["u2"].HasStripeCustomer);
        Assert.False(views["u3"].HasStripeCustomer);
        Assert.False(views["u4"].HasStripeCustomer); // boş dizge "müşteri yok" sayıldı
    }

    [RequiresDockerFact]
    public async Task Down_restores_the_columns_and_tables_and_the_chain_can_go_up_again()
    {
        var cs = await D1DatabaseAsync("m_down");
        await SeedD1Async(cs);
        var before = await TakeSnapshotAsync(cs);
        await TestModules.MigrateAllAsync(cs);
        await AssertUpgradedAsync(cs, before);

        // Belgelenmiş sıra: önce modül zincirleri ters sırada (Governance, Billing), Auth'a dokunulmaz.
        foreach (var module in TestModules.Descriptors.Where(d => d.Order is 30 or 40).OrderByDescending(d => d.Order))
        {
            await using var ctx = module.CreateContext(cs);
            await ctx.GetService<IMigrator>().MigrateAsync("0");
        }

        // Alanlar kullanıcı satırına geri döndü: aynı değerler (tek istisna: boş dizge müşteri kimliği NULL döner, belgelenmiş).
        var restored = await ReadStripeColumnsAsync(cs);
        var expected = before.Users.Select(u => u with { Customer = string.IsNullOrEmpty(u.Customer) ? null : u.Customer }).ToList();
        Assert.Equal(expected, restored);
        Assert.Equal(before.Session, await SessionFingerprintAsync(cs));
        foreach (var table in BillingTables.Concat(GovernanceTables))
        {
            Assert.Equal(1, await TableExistsAsync(cs, "public", table));
            Assert.Equal(before.Counts[table], await CountAsync(cs, $"SELECT count(*) FROM public.\"{table}\""));
            Assert.Equal(before.PublicDigests[table], await Md5Async(cs, $"public.\"{table}\""));
        }
        Assert.Equal(0, await TableExistsAsync(cs, "billing", "customers"));
        Assert.Equal(0, await TableExistsAsync(cs, "billing", "subscriptions"));
        // Tetikleyici ve işlevi gitti: Down'dan sonra denetim satırı güncellenebilir (eski davranış).
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM pg_proc WHERE proname = 'change_request_audit_guard'"));

        // Tekrar ileri: aynı komut, aynı sonuç.
        var afterDown = await TakeSnapshotAsync(cs);
        await TestModules.MigrateAllAsync(cs);
        await AssertUpgradedAsync(cs, afterDown);
    }

    [RequiresDockerFact]
    public async Task The_same_stripe_customer_on_two_users_stops_the_migration_with_a_clear_error_and_changes_nothing()
    {
        var cs = await D1DatabaseAsync("m_duplicate");
        await SeedD1Async(cs, duplicateCustomerFor: "u2");
        var before = await TakeSnapshotAsync(cs);

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => TestModules.MigrateAllAsync(cs));

        Assert.Contains("Billing_01: the same StripeCustomerId belongs to more than one user (cus_1)", failure.ToString());
        // Hiçbir şey yarım kalmadı: kolonlar yerinde, tablolar public'te, yeni tablolar yok, veri aynı.
        foreach (var column in StripeColumns)
            Assert.Equal(1, await CountAsync(cs, $"SELECT count(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'AspNetUsers' AND column_name = '{column}'"));
        foreach (var table in BillingTables)
            Assert.Equal(1, await TableExistsAsync(cs, "public", table));
        Assert.Equal(0, await TableExistsAsync(cs, "billing", "customers"));
        Assert.Equal(before.Users, await ReadStripeColumnsAsync(cs));
        Assert.Equal(before.Session, await SessionFingerprintAsync(cs));

        // Çakışma elle çözülür; yarıda kalan çalıştırma kaldığı yerden güvenle yeniden çalışır.
        await ExecAsync(cs, "UPDATE \"AspNetUsers\" SET \"StripeCustomerId\" = 'cus_2b' WHERE \"Id\" = 'u2'");
        var resolved = await TakeSnapshotAsync(cs);
        await TestModules.MigrateAllAsync(cs);
        await AssertUpgradedAsync(cs, resolved);
    }

    private const string UserInsert = """INSERT INTO "AspNetUsers" ("Id","Type","CreatedAt","IsDev","EmailConfirmed","PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount","UserName","NormalizedUserName","StripeCustomerId","StripeSubscriptionId") VALUES""";

    [RequiresDockerFact]
    public async Task Padded_and_blank_stripe_ids_are_trimmed_into_the_new_tables_and_come_back_trimmed_on_down()
    {
        // m-3: boşluklu kimlik webhook eşleşmesini sonsuza dek kaçırırdı; kırpılarak taşınır. Boş dizge "yok" sayılır.
        var cs = await D1DatabaseAsync("m_trim");
        await ExecAsync(cs, UserInsert + """
             ('p1',0,now(),false,true,false,false,true,0,'p1','P1',' cus_pad ',' sub_pad '),
             ('p2',0,now(),false,true,false,false,true,0,'p2','P2','','');
            """);
        await TestModules.MigrateAllAsync(cs);

        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM billing.customers WHERE \"UserId\"='p1' AND \"StripeCustomerId\"='cus_pad'"));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM billing.subscriptions WHERE \"UserId\"='p1' AND \"StripeSubscriptionId\"='sub_pad'"));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM billing.customers WHERE \"UserId\"='p2'"));
        // Yalnızca boş kimlikli kullanıcı için abonelik satırı da açılmaz (içi boş satır olurdu).
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM billing.subscriptions WHERE \"UserId\"='p2'"));

        foreach (var module in TestModules.Descriptors.Where(d => d.Order is 30 or 40).OrderByDescending(d => d.Order))
        {
            await using var ctx = module.CreateContext(cs);
            await ctx.GetService<IMigrator>().MigrateAsync("0");
        }
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"AspNetUsers\" WHERE \"Id\"='p1' AND \"StripeCustomerId\"='cus_pad' AND \"StripeSubscriptionId\"='sub_pad'"));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"AspNetUsers\" WHERE \"Id\"='p2' AND \"StripeCustomerId\" IS NULL AND \"StripeSubscriptionId\" IS NULL"));
    }

    [RequiresDockerFact]
    public async Task The_same_stripe_subscription_on_two_users_stops_the_migration_and_changes_nothing()
    {
        var cs = await D1DatabaseAsync("m_dupsub");
        await ExecAsync(cs, UserInsert + """
             ('d1',0,now(),false,true,false,false,true,0,'d1','D1','cus_d1','sub_same'),
             ('d2',0,now(),false,true,false,false,true,0,'d2','D2','cus_d2',' sub_same ');
            """);
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => TestModules.MigrateAllAsync(cs));
        Assert.Contains("Billing_01: the same StripeSubscriptionId belongs to more than one user (sub_same)", failure.ToString());
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM information_schema.columns WHERE table_schema='public' AND table_name='AspNetUsers' AND column_name='StripeSubscriptionId'"));
        Assert.Equal(0, await TableExistsAsync(cs, "billing", "subscriptions"));

        await ExecAsync(cs, "UPDATE \"AspNetUsers\" SET \"StripeSubscriptionId\" = 'sub_other' WHERE \"Id\" = 'd2'");
        await TestModules.MigrateAllAsync(cs);
        Assert.Equal(2, await CountAsync(cs, "SELECT count(*) FROM billing.subscriptions WHERE \"UserId\" IN ('d1','d2')"));
        // Uygulama yolu da aynı kuralı korur: ikinci kullanıcıya aynı abonelik kimliği yazılamaz.
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(cs, "UPDATE billing.subscriptions SET \"StripeSubscriptionId\"='sub_same' WHERE \"UserId\"='d2'"));
        Assert.Contains("IX_subscriptions_StripeSubscriptionId", ex.ToString());
    }
}
