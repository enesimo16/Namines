using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Persistence;
using Namines.Infrastructure.Data;
using Npgsql;
using Xunit;

namespace Namines.Tests.TestSupport;

/// <summary>Migration testlerinin ortak el kitabı: taşınan tablolar, tohum, beklenen sayımlar.</summary>
public static partial class TestModules
{
    /// <summary>D-1'den önceki son Auth migration'ı: canlı sistemin bugünkü seviyesi.</summary>
    public const string Level14_2C = "20261105114438_Phase14_2C_NormalizeCollections";

    public static IReadOnlyList<(string Schema, string Table)> MovedTables { get; } = new[]
    {
        ("ops", "Feedbacks"),
        ("ops", "SqlExecutionAudits"),
        ("ops", "FeatureFlags"),
        ("ops", "AiToolRoutes"),
    };

    public static IReadOnlyList<Func<string, DbContext>> OwnerContextFactories =>
        Descriptors.Where(d => d.Ownership == ModuleMigrationOwnership.OwnsChain).Select(d => d.CreateContext).ToList();

    /// <summary>
    /// Auth dışındakiler: Auth zincirinin Vault/Ground/Flow/Desk tablolarındaki eski PK adları (tablo adı
    /// değiştirilirken PK'lar yeniden adlandırılmamış) bu fazın kapsamı dışında, ayrı backlog maddesi.
    /// </summary>
    public static IReadOnlyList<Func<string, DbContext>> ModuleOwnerContextFactories =>
        Descriptors.Where(d => d.Ownership == ModuleMigrationOwnership.OwnsChain && d.Module != "Auth").Select(d => d.CreateContext).ToList();

    public static IReadOnlyList<Func<string, DbContext>> AllContextFactories => OwnerContextFactories;

    public static Task MigrateAllAsync(string connectionString, bool upToAuthOnly) =>
        ModuleMigrationOrchestrator.MigrateAllAsync(
            upToAuthOnly ? Descriptors.Where(d => d.Module == "Auth") : Descriptors,
            connectionString, TimeSpan.FromMinutes(5));

    /// <summary>14.2C seviyesinde, tabloların o günkü yerinde gerçekçi tohum.</summary>
    public static async Task SeedLiveAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(SeedSql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private const string SeedSql = """
        INSERT INTO "AspNetUsers" ("Id","Type","CreatedAt","IsDev","EmailConfirmed","PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount","UserName","NormalizedUserName")
        VALUES ('u1',1,now(),false,true,false,false,true,0,'una','UNA');
        INSERT INTO public."Feedbacks" ("UserId","Email","Category","Message","CreatedAt")
        VALUES ('u1','a@example.com','bug','tohum geri bildirim',now()), (NULL,NULL,'idea','anonim',now());
        INSERT INTO public."SqlExecutionAudits" ("Id","UserId","DbType","ScriptHash","ScriptPreview","ScriptLength","ContainsDestructiveKeyword","Success","StatementsExecuted","PartialApplyPossible","CreatedAt")
        VALUES ('sa1','u1','PostgreSQL','h','select 1',8,false,true,1,false,now());
        INSERT INTO public."FeatureFlags" ("FeatureKey","DisplayName","Description","Category","IsEnabled","Mode","UpdatedAt")
        VALUES ('canvas','Canvas','d','Platform',false,'Hidden',now());
        INSERT INTO public."AiToolRoutes" ("ToolKey","DisplayName","Provider","ModelName","Temperature","UpdatedAt")
        VALUES ('ai_dba','DBA','deepseek','m',0.3,now());
        """;

    public static IReadOnlyList<(string Table, long Expected)> SeededCountsBeforeMove { get; } = new (string, long)[]
    {
        ("public.\"Feedbacks\"", 2), ("public.\"SqlExecutionAudits\"", 1), ("public.\"FeatureFlags\"", 1), ("public.\"AiToolRoutes\"", 1),
    };

    public static IReadOnlyList<(string Table, long Expected)> SeededCountsAfterMove { get; } = new (string, long)[]
    {
        ("ops.\"Feedbacks\"", 2), ("ops.\"SqlExecutionAudits\"", 1), ("ops.\"FeatureFlags\"", 1), ("ops.\"AiToolRoutes\"", 1),
    };

    /// <summary>Taşınmış veriyi uygulama bağlamıyla okur ve kısıtların canlı olduğunu sınar.</summary>
    public static async Task AssertSeedIntactAsync(string cs)
    {
        await using var ops = new OpsDbContext(ModuleDbContextRegistration.MigrationOptions<OpsDbContext>(cs, OpsDbContext.SchemaName));
        var flag = await ops.FeatureFlags.AsNoTracking().SingleAsync(f => f.FeatureKey == "canvas");
        Assert.False(flag.IsEnabled);
        Assert.Equal("Hidden", flag.Mode);
        Assert.Equal(2, await ops.Feedbacks.CountAsync());

        // Kimlik dizisi tabloyla taşındı: yeni satır çakışmadan eklenir.
        ops.Feedbacks.Add(new Namines.Ops.Domain.Feedback { Message = "taşımadan sonra", UserId = "u1" });
        await ops.SaveChangesAsync();
        var added = await ops.Feedbacks.AsNoTracking().OrderByDescending(f => f.Id).FirstAsync();
        Assert.True(added.Id > 2);
        ops.Feedbacks.Remove(await ops.Feedbacks.SingleAsync(f => f.Id == added.Id));
        await ops.SaveChangesAsync();

        // Şemalar arası FK ayakta: kullanıcı silinince SET NULL.
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_constraint c JOIN pg_class t ON t.oid = c.conrelid JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE n.nspname = 'ops' AND t.relname = 'Feedbacks' AND c.conname = 'FK_Feedbacks_AspNetUsers_UserId' AND c.confdeltype = 'n'
            """, conn);
        Assert.Equal(1L, await cmd.ExecuteScalarAsync());
    }
}
