using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Interfaces;
using Namines.Core.Security;
using Namines.Flow.Services;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Infrastructure.Generators.Eject;
using Namines.Infrastructure.Generators.PrismaGenerator;
using Namines.Infrastructure.Services;
using Namines.Mcp;
using Xunit;

namespace Namines.Tests.Security;

/// <summary>
/// MCP sertleştirmesi: namines_desk_execute_query artık SALT OKUNUR ve korumalı;
/// McpConnectionGuard yinelenen/çoklu host anahtarlarıyla atlatılamıyor.
/// </summary>
public class McpHardeningTests
{
    // ── McpConnectionGuard ────────────────────────────────────────────────────

    private static McpConnectionGuard Guard(string? allowedHosts = null, bool? allowPublic = null)
    {
        var settings = new Dictionary<string, string?>();
        if (allowedHosts is not null) settings["Security:McpAllowedDbHosts"] = allowedHosts;
        if (allowPublic is not null) settings["Security:McpAllowPublicDbHosts"] = allowPublic.Value ? "true" : "false";

        return new McpConnectionGuard(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            NullLogger<McpConnectionGuard>.Instance);
    }

    [Theory]
    // Yinelenen anahtar: sürücü SON değeri kullanır.
    [InlineData("PostgreSQL", "Host=localhost;Port=5432;Host=8.8.8.8;Username=u;Password=p")]
    [InlineData("PostgreSQL", "Host=8.8.8.8;Host=localhost;Username=u")]
    // Eşanlamlı anahtarlar farklı değerlerle.
    [InlineData("PostgreSQL", "Host=localhost;Server=8.8.8.8;Username=u")]
    [InlineData("MySQL", "Server=localhost;Addr=8.8.8.8;Uid=u")]
    [InlineData("MSSQL", "Server=localhost;Address=8.8.8.8;User Id=u")]
    // Çoklu-host listesi.
    [InlineData("PostgreSQL", "Host=localhost,8.8.8.8;Username=u")]
    [InlineData("PostgreSQL", "Host=localhost:5432,8.8.8.8:5432;Username=u")]
    [InlineData("MySQL", "Server=localhost,8.8.8.8;Uid=u")]
    [InlineData("MariaDB", "Server=127.0.0.1,203.0.113.5;Uid=u")]
    [InlineData("Oracle", "Data Source=localhost:1521,8.8.8.8:1521/XE;User Id=u")]
    [InlineData("Oracle", "Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=8.8.8.8)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=xe)));User Id=u")]
    // Parolanın içindeki tırnak, sonraki gerçek Host anahtarını gizleyemez.
    [InlineData("PostgreSQL", "Host=localhost;Password=pa'ss;Host=8.8.8.8;Username=u")]
    // SQLite UNC yolu bir ağ hedefidir.
    [InlineData("SQLite", @"Data Source=\\8.8.8.8\share\app.db")]
    public void Birden_cok_ya_da_catisan_host_REDDEDILIYOR(string engine, string cs)
    {
        Assert.Throws<InvalidOperationException>(() => Guard().EnsureAllowed(cs, engine));
    }

    [Theory]
    [InlineData("PostgreSQL", "Host=localhost;Port=5432;Database=app;Username=u;Password=p")]
    [InlineData("PostgreSQL", "Server=127.0.0.1;Database=app")]
    [InlineData("PostgreSQL", "Host=/var/run/postgresql;Database=app")]
    [InlineData("PostgreSQL", "Host=localhost,127.0.0.1;Database=app")]
    [InlineData("MySQL", "Server=localhost;Port=3306;Database=app;Uid=u;Pwd=p")]
    [InlineData("MSSQL", @"Server=localhost\SQLEXPRESS;Database=app;Trusted_Connection=True")]
    [InlineData("MSSQL", @"Server=(localdb)\MSSQLLocalDB;Database=app")]
    [InlineData("MSSQL", @"Server=.\SQLEXPRESS;Database=app")]
    [InlineData("MSSQL", "Server=tcp:localhost,1433;Database=app")]
    [InlineData("MSSQL", "Server=(local);Database=app")]
    [InlineData("Oracle", "Data Source=localhost:1521/XEPDB1;User Id=u;Password=p")]
    [InlineData("SQLite", "Data Source=app.db")]
    [InlineData("SQLite", @"Data Source=C:\data\app.db")]
    [InlineData("SQLite", "Data Source=:memory:")]
    public void Yerel_hedefler_MSSQL_ornekleri_ve_SQLite_dosyalari_GECIYOR(string engine, string cs)
    {
        Guard().EnsureAllowed(cs, engine);
    }

    [Fact]
    public void Ayni_host_iki_kez_yazilirsa_celiski_degil()
    {
        Guard().EnsureAllowed("Host=localhost;Server=localhost;Database=app", "PostgreSQL");
    }

    [Fact]
    public void Allowlist_cok_hostlu_dizede_HER_hostu_kontrol_ediyor()
    {
        var guard = Guard(allowedHosts: "localhost");
        guard.EnsureAllowed("Host=localhost;Database=app", "PostgreSQL");

        var error = Assert.Throws<InvalidOperationException>(
            () => guard.EnsureAllowed("Host=localhost,db.saldirgan.example;Database=app", "PostgreSQL"));
        Assert.Contains("McpAllowedDbHosts", error.Message);
    }

    [Fact]
    public void Bilinmeyen_motor_ve_bozuk_dize_REDDEDILIYOR()
    {
        Assert.Throws<InvalidOperationException>(() => Guard().EnsureAllowed("Host=localhost", "Db2"));
        Assert.Throws<InvalidOperationException>(() => Guard().EnsureAllowed("Host=localhost;Port=abc", "PostgreSQL"));
    }

    [Fact]
    public void Genel_host_izni_aciksa_cok_hostlu_dize_gecer()
    {
        Guard(allowPublic: true).EnsureAllowed("Host=localhost,8.8.8.8", "PostgreSQL");
    }

    // ── namines_desk_execute_query ────────────────────────────────────────────

    private sealed class DenyAllPolicy : IDbHostAccessPolicy
    {
        public bool IsHostAllowed(string? host, out string denyReason)
        {
            denyReason = "denied by test policy";
            return false;
        }
    }

    private static NaminesTools ToolsWithGateway()
    {
        var ddlFactory = new DdlGeneratorFactory();
        return new NaminesTools(
            introspection: null!,
            testRunner: null!,
            ddlFactory: ddlFactory,
            prisma: new PrismaGeneratorService(),
            ejectRegistry: new EjectGeneratorRegistry(ddlFactory),
            gateway: new GatewayService(new DenyAllPolicy()),
            executor: null!,
            flowMatcher: new FlowRuleMatcher(),
            cloud: new NaminesCloudClient(new HttpClient()),
            connectionGuard: Guard());
    }

    private const string LocalPg = "Host=localhost;Port=1;Database=app;Username=u;Password=p";

    [Theory]
    [InlineData("INSERT INTO users (name) VALUES ('x')")]
    [InlineData("UPDATE users SET name = 'x'")]
    [InlineData("DELETE FROM users")]
    [InlineData("DROP TABLE users")]
    [InlineData("TRUNCATE TABLE users")]
    [InlineData("CREATE TABLE t (id int)")]
    [InlineData("ALTER TABLE users ADD x int")]
    [InlineData("SELECT 1; DROP TABLE users")]
    [InlineData("SELECT 1 -- '\n; DROP TABLE users")]
    [InlineData("SELECT * INTO backup FROM users")]
    [InlineData("WITH d AS (DELETE FROM users RETURNING *) SELECT * FROM d")]
    [InlineData("EXEC xp_cmdshell 'dir'")]
    [InlineData("")]
    public async Task Desk_execute_query_yazan_ya_da_zincirli_SQLi_baglanti_acmadan_REDDEDIYOR(string sql)
    {
        var tools = ToolsWithGateway();

        // ArgumentException = gateway'in salt-okunur kapısı, bağlantı denemesinden ÖNCE.
        await Assert.ThrowsAsync<ArgumentException>(
            () => tools.DeskExecuteQueryAsync(LocalPg, "PostgreSQL", sql));
    }

    [Fact]
    public async Task Desk_execute_query_SQLite_desteklenmiyor()
    {
        var tools = ToolsWithGateway();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => tools.DeskExecuteQueryAsync("Data Source=app.db", "SQLite", "SELECT 1"));
    }

    [Fact]
    public async Task Desk_execute_query_gecerli_SELECT_kapidan_gecip_host_politikasina_kadar_ilerliyor()
    {
        var tools = ToolsWithGateway();

        // DenyAllPolicy bağlantıyı açmadan reddeder: SELECT'in salt-okunur kapıyı
        // GEÇTİĞİNİ (ArgumentException DEĞİL) ve yolun gateway bağlantı katmanına
        // gittiğini kanıtlar — araç artık kendi bağlantısını açmıyor.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tools.DeskExecuteQueryAsync(LocalPg, "PostgreSQL", "SELECT 1"));
        Assert.Contains("denied by test policy", error.Message);
    }

    [Fact]
    public async Task Desk_execute_query_baglanti_hedefi_kapisindan_gecmek_zorunda()
    {
        var tools = ToolsWithGateway();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tools.DeskExecuteQueryAsync(
                "Host=localhost;Host=8.8.8.8;Database=app", "PostgreSQL", "SELECT 1"));
    }

    [Fact]
    public void Desk_execute_query_aciklamasi_yazma_vaat_etmiyor()
    {
        var method = typeof(NaminesTools).GetMethod(nameof(NaminesTools.DeskExecuteQueryAsync))!;
        var description = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;

        Assert.Contains("READ-ONLY", description);
        Assert.DoesNotContain("INSERT/UPDATE/DELETE/DDL, returns rows affected", description);
        Assert.DoesNotContain("mutation", description, StringComparison.OrdinalIgnoreCase);
    }

    // ── Araç açıklamaları ─────────────────────────────────────────────────────

    [Fact]
    public void Generate_artifact_aciklamasi_kayitli_hedeflerin_hepsini_listeliyor_ve_olmayanlari_degil()
    {
        var registry = new EjectGeneratorRegistry(new DdlGeneratorFactory());
        var method = typeof(NaminesTools).GetMethod(nameof(NaminesTools.GenerateArtifact))!;
        var description = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;

        foreach (var generator in registry.All)
            Assert.Contains($"'{generator.Target}'", description);

        Assert.DoesNotContain("golang-migrate", description);
    }

    // ── Flow: gerçek diff (ilişkiler dahil) ───────────────────────────────────

    private const string BaseSchema = """
    {"name":"app","tables":[
      {"id":"t1","name":"users","stableUuid":"u1","columns":[{"id":"c1","name":"id","type":"uuid","isPK":true}]},
      {"id":"t2","name":"orders","stableUuid":"u2","columns":[{"id":"c3","name":"id","type":"uuid","isPK":true},{"id":"c4","name":"user_id","type":"uuid"}]}],
     "relations":[]}
    """;

    private const string TargetWithRelation = """
    {"name":"app","tables":[
      {"id":"t1","name":"users","stableUuid":"u1","columns":[{"id":"c1","name":"id","type":"uuid","isPK":true}]},
      {"id":"t2","name":"orders","stableUuid":"u2","columns":[{"id":"c3","name":"id","type":"uuid","isPK":true},{"id":"c4","name":"user_id","type":"uuid","isFK":true}]}],
     "relations":[{"id":"r1","sourceTableId":"t2","sourceColumnId":"c4","targetTableId":"t1","targetColumnId":"c1"}]}
    """;

    [Fact]
    public void Flow_match_rules_diff_eklenen_ve_kaldirilan_iliskileri_raporluyor()
    {
        var tools = ToolsWithGateway();

        using var added = JsonDocument.Parse(tools.FlowMatchRules(BaseSchema, TargetWithRelation));
        Assert.Equal(1, added.RootElement.GetProperty("diffSummary").GetProperty("relationsAdded").GetInt32());

        using var removed = JsonDocument.Parse(tools.FlowMatchRules(TargetWithRelation, BaseSchema));
        Assert.Equal(1, removed.RootElement.GetProperty("diffSummary").GetProperty("relationsRemoved").GetInt32());
    }
}
