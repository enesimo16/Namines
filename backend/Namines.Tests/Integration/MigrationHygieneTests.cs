using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Namines.Infrastructure.Data;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Namines.Tests.Integration;

/// <summary>
/// DB-30/31: migration zinciri GERÇEK PostgreSQL'e karşı — boş veritabanı, Down yolu ve
/// "eski veriyle" yükseltme. İlk üçü, 20261029153509_AddHubSlugUniqueIndex sonrasındaki
/// (DB sağlamlık denetimi) migration'larını sınar: FK'lar, silme davranışı, CHECK'ler,
/// benzersizlik, ShareTokenHash.
///
/// Docker yoksa atlanır (CI'da Docker vardır, atlanan test orada uyarıdır).
/// </summary>
[Collection("Docker")]
public class MigrationHygieneTests : IAsyncLifetime
{
    /// <summary>Denetim migration'larından ÖNCEKİ son migration: "eski" şema.</summary>
    private const string BeforeAudit = "20261029153509_AddHubSlugUniqueIndex";

    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        if (!DockerAvailable.Value) return;
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (!DockerAvailable.Value) return;
        await _container.DisposeAsync();
    }

    private AuthDbContext NewContext(string? connectionString = null) =>
        new(new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(connectionString ?? _container.GetConnectionString()).Options);

    /// <summary>Her test kendi veritabanında çalışır (aynı container, ayrı DB).</summary>
    private async Task<string> CreateDatabaseAsync(string name)
    {
        await using var admin = new NpgsqlConnection(_container.GetConnectionString());
        await admin.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
        await cmd.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }

    /// <summary>
    /// Ham komut: ExecuteSqlRaw KULLANILMIYOR çünkü string.Format uyguluyor ve '{}' gibi JSON
    /// sabitlerinde FormatException atıyor.
    /// </summary>
    private static async Task ExecAsync(AuthDbContext ctx, string sql)
    {
        var conn = ctx.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(AuthDbContext ctx, string sql)
    {
        var conn = ctx.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static string Sha256Hex(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    // ── 1) Boş veritabanı → son migration ───────────────────────────────────

    [RequiresDockerFact]
    public async Task Bos_veritabani_son_migration_a_kadar_yukseliyor()
    {
        var cs = await CreateDatabaseAsync("empty_to_latest");
        await using var ctx = NewContext(cs);

        await ctx.Database.MigrateAsync();

        Assert.Empty(await ctx.Database.GetPendingMigrationsAsync());
        Assert.Contains(BeforeAudit, await ctx.Database.GetAppliedMigrationsAsync());
    }

    // ── 2) Down yolu: denetim migration'ları geri alınıp tekrar uygulanıyor ──

    [RequiresDockerFact]
    public async Task Denetim_migrationlari_geri_alinip_tekrar_uygulanabiliyor()
    {
        var cs = await CreateDatabaseAsync("down_and_up");
        await using var ctx = NewContext(cs);
        await ctx.Database.MigrateAsync();

        var all = (await ctx.Database.GetAppliedMigrationsAsync()).ToList();
        var newOnes = all.SkipWhile(m => m != BeforeAudit).Skip(1).ToList();
        Assert.NotEmpty(newOnes);

        var migrator = ctx.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeAudit);

        var afterDown = (await ctx.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.Equal(BeforeAudit, afterDown.Last());
        Assert.Equal(newOnes.Count, all.Count - afterDown.Count);

        // Geri alınmış şemada yeni nesneler gerçekten yok (Down'lar bir şey yapıyor).
        Assert.Equal(0, await CountAsync(ctx,
            "SELECT count(*) FROM pg_constraint WHERE conname LIKE 'CK\\_%' ESCAPE '\\'"));
        Assert.Equal(0, await CountAsync(ctx,
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'CloudProjects' AND column_name = 'ShareTokenHash'"));

        await ctx.Database.MigrateAsync();
        Assert.Empty(await ctx.Database.GetPendingMigrationsAsync());
    }

    // ── 3) Yetim / uç veriyle eski şemadan yükseltme ─────────────────────────

    [RequiresDockerFact]
    public async Task Yetim_satirli_eski_veri_son_migration_a_yukseltilebiliyor()
    {
        var cs = await CreateDatabaseAsync("legacy_with_orphans");
        await using (var old = NewContext(cs))
        {
            await old.GetService<IMigrator>().MigrateAsync(BeforeAudit);

            const string T = "now()";
            // Kullanıcılar (U1 sahip, ikisi de benzersiz e-postalı).
            await ExecAsync(old, $@"
INSERT INTO ""AspNetUsers"" (""Id"",""Type"",""CreatedAt"",""IsDev"",""EmailConfirmed"",""PhoneNumberConfirmed"",""TwoFactorEnabled"",""LockoutEnabled"",""AccessFailedCount"",""NormalizedEmail"",""Email"")
VALUES ('u1',1,{T},false,true,false,false,true,0,'U1@EXAMPLE.COM','u1@example.com'),
       ('u2',1,{T},false,true,false,false,true,0,'U2@EXAMPLE.COM','u2@example.com');");

            // u1'in İKİ kişisel org'u (eski yarış hatasının ürünü) + bir normal org.
            await ExecAsync(old, $@"
INSERT INTO ""Organizations"" (""Id"",""Name"",""IsPersonal"",""CreatedByUserId"",""CreatedAt"")
VALUES ('org-a','A',true,'u1',{T}), ('org-b','B',true,'u1',{T}), ('org-team','T',false,'u1',{T});");

            // Proje + paylaşım token'ı + sürüm/CR/denetim zinciri.
            await ExecAsync(old, $@"
INSERT INTO ""CloudProjects"" (""Id"",""Name"",""DbType"",""SchemaJson"",""NodePositionsJson"",""UserId"",""OrganizationId"",""CreatedAt"",""UpdatedAt"",
        ""AutoApproveSafeChanges"",""AllowDeskSql"",""Visibility"",""Category"",""StarCount"",""ForkCount"",""ShareToken"",""ForkedFromProjectId"")
VALUES ('p1','P1','PostgreSQL','{{}}','{{}}','u1','org-team',{T},{T},false,false,0,'General',0,0,'share-token-1','missing-project'),
       ('p2','P2','PostgreSQL','{{}}','{{}}','u1','org-team',{T},{T},false,false,99,'General',0,0,NULL,NULL);
INSERT INTO ""Branches"" (""Id"",""ProjectId"",""Name"",""IsDefault"",""CreatedByUserId"",""CreatedAt"") VALUES ('br1','p1','main',true,'u1',{T});
INSERT INTO ""SchemaVersions"" (""Id"",""ProjectId"",""BranchId"",""Version"",""Checksum"",""SchemaJson"",""TableCount"",""CreatedAt"") VALUES ('sv1','p1','br1',1,'c','{{}}',0,{T});
INSERT INTO ""ChangeRequests"" (""Id"",""ProjectId"",""BranchId"",""HeadVersionId"",""Title"",""Status"",""RiskLevel"",""ImpactReportJson"",""CreatedByUserId"",""CreatedAt"",""VotingMembersAtCreation"")
VALUES ('cr1','p1','br1','sv1','My CR',0,0,'{{}}','u1',{T},1);
INSERT INTO ""ChangeRequestAuditLogs"" (""Id"",""ChangeRequestId"",""Action"",""ActorUserId"",""CreatedAt"") VALUES ('al1','cr1',0,'u1',{T});");

            // Yetimler: var olmayan proje/kullanıcı/yedek/org referansları.
            await ExecAsync(old, $@"
INSERT INTO namines_vault.vault_backups (""Id"",""ProjectId"",""CreatedByUserId"",""DatabaseName"",""Engine"",""StorageKey"",""StoreDescription"",""SizeBytes"",""Status"",""Kind"",""CreatedAt"")
VALUES ('vb-orphan','gone-project','u1','db','postgres','blob/orphan','s3',10,1,0,{T}),
       ('vb-ok','p1','ghost-user','db','postgres','blob/ok','s3',10,1,0,{T});
INSERT INTO namines_vault.vault_restores (""Id"",""ProjectId"",""BackupId"",""PreRestoreBackupId"",""PerformedByUserId"",""Status"",""StartedAt"")
VALUES ('vr1','p1','vb-orphan','vb-ok','u1',1,{T}), ('vr-orphan','gone-project','vb-ok','vb-ok','u1',1,{T});
INSERT INTO ground.ground_databases (""Id"",""ProjectId"",""Provider"",""ProviderProjectId"",""Status"",""CreatedByUserId"",""CreatedAt"")
VALUES ('gd-orphan','gone-project','neon','neon-123',1,'u1',{T});
INSERT INTO desk.sql_query_history (""Id"",""ProjectId"",""UserId"",""Sql"",""Truncated"",""RowCount"",""Succeeded"",""DurationMs"",""CreatedAt"")
VALUES ('h1','p1','u1','SELECT 1',false,1,true,1,{T}), ('h-orphan','gone-project','u1','SELECT 2',false,1,true,1,{T});
INSERT INTO desk.saved_queries (""Id"",""ProjectId"",""UserId"",""Name"",""Sql"",""CreatedAt"",""UpdatedAt"")
VALUES ('q-orphan','p1','ghost-user','n','SELECT 1',{T},{T});
INSERT INTO ""Feedbacks"" (""UserId"",""Category"",""Message"",""CreatedAt"") VALUES ('ghost-user','bug','hello',{T});");
        }

        await using var ctx = NewContext(cs);
        await ctx.Database.MigrateAsync();   // patlamamalı
        Assert.Empty(await ctx.Database.GetPendingMigrationsAsync());

        // Yedek ve ground kayıtları SİLİNMEDİ: karantinaya taşındı (blob / sağlayıcı izi kaybolmadı).
        Assert.Equal(2, await CountAsync(ctx, "SELECT count(*) FROM migration_quarantine.orphaned_rows"));
        Assert.Equal(1, await CountAsync(ctx,
            "SELECT count(*) FROM migration_quarantine.orphaned_rows WHERE row_data->>'StorageKey' = 'blob/orphan'"));
        Assert.Equal(1, await CountAsync(ctx,
            "SELECT count(*) FROM migration_quarantine.orphaned_rows WHERE row_data->>'ProviderProjectId' = 'neon-123'"));
        Assert.Equal(1, await CountAsync(ctx, "SELECT count(*) FROM namines_vault.vault_backups"));
        Assert.Equal(1, await CountAsync(ctx,
            "SELECT count(*) FROM namines_vault.vault_backups WHERE \"CreatedByUserId\" IS NULL"));

        // Geri yükleme geçmişi: yetim proje satırı gitti, kırık yedek işaretçisi NULL'landı.
        Assert.Equal(1, await CountAsync(ctx, "SELECT count(*) FROM namines_vault.vault_restores"));
        Assert.Equal(1, await CountAsync(ctx,
            "SELECT count(*) FROM namines_vault.vault_restores WHERE \"BackupId\" IS NULL AND \"PreRestoreBackupId\" = 'vb-ok'"));

        // Desk: yetim geçmiş/kayıtlı sorgu silindi, sağlam olan kaldı.
        Assert.Equal(1, await CountAsync(ctx, "SELECT count(*) FROM desk.sql_query_history"));
        Assert.Equal(0, await CountAsync(ctx, "SELECT count(*) FROM desk.saved_queries"));

        // Diğer atıf kolonları: fork işaretçisi ve feedback kullanıcısı NULL.
        Assert.Equal(1, await CountAsync(ctx,
            "SELECT count(*) FROM \"CloudProjects\" WHERE \"Id\" = 'p1' AND \"ForkedFromProjectId\" IS NULL"));
        Assert.Equal(1, await CountAsync(ctx, "SELECT count(*) FROM \"Feedbacks\" WHERE \"UserId\" IS NULL"));

        // Kişisel org tekilliği: fazlalık normal org'a çevrildi, hiçbiri silinmedi.
        Assert.Equal(1, await CountAsync(ctx,
            "SELECT count(*) FROM \"Organizations\" WHERE \"CreatedByUserId\" = 'u1' AND \"IsPersonal\""));
        Assert.Equal(3, await CountAsync(ctx, "SELECT count(*) FROM \"Organizations\""));

        // Denetim izi anlık kopyaları dolduruldu; paylaşım özeti C# ile aynı.
        Assert.Equal(1, await CountAsync(ctx,
            "SELECT count(*) FROM \"ChangeRequestAuditLogs\" WHERE \"ProjectId\" = 'p1' AND \"ChangeRequestTitle\" = 'My CR'"));
        var hash = Sha256Hex("share-token-1");
        Assert.Equal(1, await CountAsync(ctx,
            $"SELECT count(*) FROM \"CloudProjects\" WHERE \"Id\" = 'p1' AND \"ShareTokenHash\" = '{hash}'"));

        // CHECK'ler: temiz olanlar doğrulandı; p2'nin Visibility=99 satırı olan NOT VALID kaldı
        // (ama yeni yazımlar için zorunlu).
        Assert.Equal(1, await CountAsync(ctx,
            "SELECT count(*) FROM pg_constraint WHERE conname = 'CK_CloudProjects_Visibility' AND NOT convalidated"));
        Assert.Equal(1, await CountAsync(ctx,
            "SELECT count(*) FROM pg_constraint WHERE conname = 'CK_ChangeRequests_Status' AND convalidated"));
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(ctx,
            "UPDATE \"CloudProjects\" SET \"Visibility\" = 98 WHERE \"Id\" = 'p1'"));
        Assert.Contains("CK_CloudProjects_Visibility", ex.ToString());
    }

    // ── 4) Yinelenen e-posta: migration net hatayla durur, çözülünce geçer ───

    [RequiresDockerFact]
    public async Task Yinelenen_eposta_migrationi_net_hatayla_durduruyor()
    {
        var cs = await CreateDatabaseAsync("duplicate_email");
        await using (var old = NewContext(cs))
        {
            await old.GetService<IMigrator>().MigrateAsync(BeforeAudit);
            await ExecAsync(old, @"
INSERT INTO ""AspNetUsers"" (""Id"",""Type"",""CreatedAt"",""IsDev"",""EmailConfirmed"",""PhoneNumberConfirmed"",""TwoFactorEnabled"",""LockoutEnabled"",""AccessFailedCount"",""NormalizedEmail"")
VALUES ('d1',1,now(),false,true,false,false,true,0,'DUP@EXAMPLE.COM'),
       ('d2',1,now(),false,true,false,false,true,0,'DUP@EXAMPLE.COM');");
        }

        await using var ctx = NewContext(cs);
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.MigrateAsync());
        Assert.Contains("never auto-merged", ex.ToString());

        // Elle çözüm (belgelenen yol): e-postayı değiştir, yeniden çalıştır.
        await ExecAsync(ctx, "UPDATE \"AspNetUsers\" SET \"NormalizedEmail\" = 'D2@EXAMPLE.COM' WHERE \"Id\" = 'd2'");
        await ctx.Database.MigrateAsync();
        Assert.Empty(await ctx.Database.GetPendingMigrationsAsync());
    }

    // ── 5) Silme davranışı gerçek PostgreSQL'de (DB-09/10/11) ────────────────

    [RequiresDockerFact]
    public async Task Silme_davranisi_son_sema_ile_beklenen_gibi()
    {
        var cs = await CreateDatabaseAsync("delete_semantics");
        await using var ctx = NewContext(cs);
        await ctx.Database.MigrateAsync();

        await ExecAsync(ctx, @"
INSERT INTO ""AspNetUsers"" (""Id"",""Type"",""CreatedAt"",""IsDev"",""EmailConfirmed"",""PhoneNumberConfirmed"",""TwoFactorEnabled"",""LockoutEnabled"",""AccessFailedCount"")
VALUES ('u1',1,now(),false,true,false,false,true,0), ('u2',1,now(),false,true,false,false,true,0);
INSERT INTO ""CloudProjects"" (""Id"",""Name"",""DbType"",""SchemaJson"",""NodePositionsJson"",""UserId"",""CreatedAt"",""UpdatedAt"",""AutoApproveSafeChanges"",""AllowDeskSql"",""Visibility"",""Category"",""StarCount"",""ForkCount"")
VALUES ('p1','P1','PostgreSQL','{}','{}','u1',now(),now(),false,false,0,'General',0,0),
       ('p2','P2','PostgreSQL','{}','{}','u1',now(),now(),false,false,0,'General',0,0);
INSERT INTO ""Branches"" (""Id"",""ProjectId"",""Name"",""IsDefault"",""CreatedByUserId"",""CreatedAt"") VALUES ('br1','p1','main',true,'u2',now());
INSERT INTO ""SchemaVersions"" (""Id"",""ProjectId"",""BranchId"",""Version"",""Checksum"",""SchemaJson"",""TableCount"",""CreatedAt"") VALUES ('sv1','p1','br1',1,'c','{}',0,now());
INSERT INTO ""ChangeRequests"" (""Id"",""ProjectId"",""BranchId"",""HeadVersionId"",""BaseVersionId"",""Title"",""Status"",""RiskLevel"",""ImpactReportJson"",""CreatedByUserId"",""CreatedAt"",""VotingMembersAtCreation"")
VALUES ('cr1','p1','br1','sv1','sv1','CR',0,0,'{}','u2',now(),1);
INSERT INTO ""ChangeRequestAuditLogs"" (""Id"",""ChangeRequestId"",""ProjectId"",""ChangeRequestTitle"",""Action"",""ActorUserId"",""CreatedAt"") VALUES ('al1','cr1','p1','CR',0,'u2',now());
INSERT INTO ""UsageEvents"" (""UserId"",""Resource"",""Quantity"",""BillingPeriod"",""OccurredAt"") VALUES ('u2',0,1,now(),now());
INSERT INTO namines_vault.vault_backups (""Id"",""ProjectId"",""CreatedByUserId"",""DatabaseName"",""Engine"",""StorageKey"",""StoreDescription"",""SizeBytes"",""Status"",""Kind"",""CreatedAt"")
VALUES ('vb1','p2','u1','db','postgres','blob/1','s3',10,1,0,now());");

        // Yedeği olan proje SESSİZCE silinemez (RESTRICT).
        var restricted = await Assert.ThrowsAnyAsync<Exception>(() =>
            ExecAsync(ctx, "DELETE FROM \"CloudProjects\" WHERE \"Id\" = 'p2'"));
        Assert.Contains("23503", restricted.ToString());

        // Sürümlü + CR'li proje tek DELETE ile silinebilir (DB-11: RESTRICT/CASCADE yarışı yok)
        // ve denetim izi YAŞAR (DB-10).
        await ExecAsync(ctx, "DELETE FROM \"CloudProjects\" WHERE \"Id\" = 'p1'");
        Assert.Equal(0, await CountAsync(ctx, "SELECT count(*) FROM \"ChangeRequests\""));
        Assert.Equal(1, await CountAsync(ctx,
            "SELECT count(*) FROM \"ChangeRequestAuditLogs\" WHERE \"ProjectId\" = 'p1' AND \"ChangeRequestTitle\" = 'CR'"));

        // Kullanıcı silme: atıf kolonları NULL'lanır, faturalama kaydı kalır.
        await ExecAsync(ctx, "DELETE FROM \"AspNetUsers\" WHERE \"Id\" = 'u2'");
        Assert.Equal(1, await CountAsync(ctx,
            "SELECT count(*) FROM \"UsageEvents\" WHERE \"UserId\" IS NULL"));
        Assert.Equal(1, await CountAsync(ctx,
            "SELECT count(*) FROM \"ChangeRequestAuditLogs\" WHERE \"ActorUserId\" IS NULL"));
    }
}
