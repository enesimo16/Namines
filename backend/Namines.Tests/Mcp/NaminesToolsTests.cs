using System.Text.Json;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Infrastructure.Generators.PrismaGenerator;
using Namines.Mcp;

namespace Namines.Tests.Mcp;

/// <summary>
/// MCP araç yüzeyi (new-phase/33 §5 Faz 1).
///
/// Testlerin odağı iş mantığı DEĞİL — o zaten SchemaImpactAnalyzer/BranchTestRunner
/// testleriyle kapsanıyor. Buradaki risk **sınır katmanı**: LLM'den gelen JSON'un
/// doğru bağlanması. Geliştirme sırasında tam da burada sessiz bir hata bulundu:
/// camelCase girdi (ki pull_schema'nın kendi çıktısı camelCase) boş şemaya çözülüp
/// analiz "Safe, hiçbir şey değişmemiş" diyordu. Yanlış güven veren bir analiz,
/// analiz olmamasından kötüdür.
/// </summary>
public class NaminesToolsTests
{
    private sealed class StubIntrospection : IDbIntrospectionService
    {
        public Task<DatabaseSchema> IntrospectAsync(string connectionString, string dbType, CancellationToken ct = default)
            => Task.FromResult(new DatabaseSchema { Name = "stub" });
    }

    private sealed class StubTestRunner : IBranchTestRunner
    {
        public Task<TestRunResult> RunAsync(DatabaseSchema schema, Core.Enums.DatabaseType engine, CancellationToken ct = default)
            => Task.FromResult(new TestRunResult(true, true, null, null, 1));
    }

    private static NaminesTools Tools() => new(
        new StubIntrospection(),
        new StubTestRunner(),
        new DdlGeneratorFactory(),
        new PrismaGeneratorService(),
        new NaminesCloudClient(new HttpClient()));

    /// <summary>camelCase — pull_schema'nın ürettiği biçim.</summary>
    private const string CamelCaseBase = """
        {"schemaId":"s1","name":"app","tables":[{"id":"t1","name":"users","stableUuid":"u1",
        "columns":[{"id":"c1","name":"id","type":"INT","isPK":true,"isFK":false,"isNullable":false,"stableUuid":"cu1"},
                   {"id":"c2","name":"email","type":"VARCHAR","length":255,"isPK":false,"isFK":false,"isNullable":false,"stableUuid":"cu2"}]}],
        "relations":[]}
        """;

    /// <summary>Aynı şema PascalCase — eski çıktı biçimi, hâlâ kabul edilmeli.</summary>
    private const string PascalCaseBase = """
        {"SchemaId":"s1","Name":"app","Tables":[{"Id":"t1","Name":"users","StableUuid":"u1",
        "Columns":[{"Id":"c1","Name":"id","Type":"INT","IsPK":true,"IsFK":false,"IsNullable":false,"StableUuid":"cu1"},
                   {"Id":"c2","Name":"email","Type":"VARCHAR","Length":255,"IsPK":false,"IsFK":false,"IsNullable":false,"StableUuid":"cu2"}]}],
        "Relations":[]}
        """;

    /// <summary>email kolonu silinmiş hâl — veri kaybı + breaking üretmeli.</summary>
    private const string TargetWithoutEmail = """
        {"schemaId":"s1","name":"app","tables":[{"id":"t1","name":"users","stableUuid":"u1",
        "columns":[{"id":"c1","name":"id","type":"INT","isPK":true,"isFK":false,"isNullable":false,"stableUuid":"cu1"}]}],
        "relations":[]}
        """;

    [Fact]
    public void Analyze_binds_camelcase_input_and_detects_the_real_change()
    {
        var json = Tools().AnalyzeImpact(CamelCaseBase, TargetWithoutEmail, "PostgreSQL");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Bağlama sessizce boşalsaydı burası "Safe" ve 0 olurdu — asıl yakalanan hata buydu.
        Assert.NotEqual("Safe", root.GetProperty("overallRisk").GetString());
        Assert.True(root.GetProperty("affectedTables").GetArrayLength() > 0);
        Assert.True(root.GetProperty("dataLossRisks").GetArrayLength() > 0);
    }

    [Fact]
    public void Analyze_also_accepts_pascalcase_input()
    {
        var json = Tools().AnalyzeImpact(PascalCaseBase, TargetWithoutEmail, "PostgreSQL");
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("dataLossRisks").GetArrayLength() > 0);
    }

    [Fact]
    public void Output_is_camelcase_so_it_can_be_fed_back_into_the_next_tool()
    {
        var json = Tools().AnalyzeImpact("{}", CamelCaseBase, "PostgreSQL");
        Assert.Contains("\"overallRisk\"", json);
        Assert.DoesNotContain("\"OverallRisk\"", json);
    }

    [Fact]
    public void Enum_values_stay_pascalcase_matching_the_hosted_api()
    {
        var json = Tools().AnalyzeImpact("{}", CamelCaseBase, "PostgreSQL");
        Assert.Matches("\"overallRisk\":\\s*\"(Safe|Risky|Destructive|Breaking)\"", json);
    }

    [Fact]
    public void Empty_base_schema_is_legitimate_not_an_error()
    {
        // Boş bir veritabanına karşı karşılaştırma geçerli bir kullanım.
        var json = Tools().AnalyzeImpact("{}", CamelCaseBase, "PostgreSQL");
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("affectedTables").GetArrayLength() > 0);
    }

    [Fact]
    public void Wrong_shape_with_tables_is_rejected_instead_of_silently_analyzed()
    {
        // Bağlama tutmadığında istisna fırlamaz, boş/adsız şema üretir. Sessizce
        // "Safe" demek kullanıcıya YANLIŞ GÜVEN verir — açıkça reddedilmeli.
        var ex = Assert.Throws<ArgumentException>(() =>
            Tools().AnalyzeImpact("{}", """{"tables":[{"wrongField":1}]}""", "PostgreSQL"));
        Assert.Contains("does not match", ex.Message);
    }

    [Fact]
    public void Table_list_that_fails_to_bind_at_all_is_rejected()
    {
        // Bu şekil JSON çözümlemesinin kendisinde patlar (string → SchemaTable olmaz),
        // yukarıdaki testtekinden farklı bir mesajla reddedilir. Sözleşme belirli bir
        // metin değil: sessizce kabul edilmemesi ve nedenin söylenmesi.
        var ex = Assert.Throws<ArgumentException>(() =>
            Tools().AnalyzeImpact("{}", """{"tables":["not-an-object-shape"]}""", "PostgreSQL"));
        Assert.Contains("targetSchemaJson", ex.Message);
        Assert.Contains("Namines schema", ex.Message);
    }

    [Fact]
    public void Unknown_engine_is_rejected_with_the_valid_list()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Tools().AnalyzeImpact("{}", CamelCaseBase, "CockroachDB"));
        Assert.Contains("PostgreSQL", ex.Message);
    }

    [Fact]
    public void Empty_input_string_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Tools().AnalyzeImpact("", CamelCaseBase, "PostgreSQL"));
        Assert.Throws<ArgumentException>(() => Tools().AnalyzeImpact("{}", "   ", "PostgreSQL"));
    }

    [Fact]
    public async Task Prove_migration_serializes_the_runner_result_in_camelcase()
    {
        var json = await Tools().ProveMigrationAsync(CamelCaseBase, "SQLite");
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("supported").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Pull_schema_returns_camelcase_json()
    {
        var json = await Tools().PullSchemaAsync("Host=x;Database=y", "PostgreSQL");
        Assert.Contains("\"name\"", json);
        Assert.DoesNotContain("\"Name\"", json);
    }

    // ── Faz 2 araçları ───────────────────────────────────────────────────────

    [Fact]
    public void Generate_ddl_produces_engine_specific_sql()
    {
        var pg = Tools().GenerateDdl(CamelCaseBase, "PostgreSQL");
        var mssql = Tools().GenerateDdl(CamelCaseBase, "MSSQL");

        Assert.Contains("users", pg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("users", mssql, StringComparison.OrdinalIgnoreCase);
        // Aracın tek satırlık vaadi bu: motor SEÇİMİ çıktıyı değiştirir. İki motor
        // aynı metni verseydi jeneratör seçimi hiç uygulanmıyor demektir.
        Assert.NotEqual(pg, mssql);
    }

    [Fact]
    public void Generate_ddl_rejects_a_malformed_schema_instead_of_emitting_empty_sql()
    {
        // Boş DDL "yapacak bir şey yok" gibi okunur; bozuk girdi sessizce oraya düşmemeli.
        Assert.Throws<ArgumentException>(() =>
            Tools().GenerateDdl("""{"tables":[{"wrongField":1}]}""", "PostgreSQL"));
    }

    [Fact]
    public async Task Open_change_request_without_a_token_says_what_is_missing()
    {
        // Yapılandırma hatası ağ hatası gibi görünmemeli — kullanıcı yanlış yere bakar.
        var previous = Environment.GetEnvironmentVariable("NAMINES_API_TOKEN");
        Environment.SetEnvironmentVariable("NAMINES_API_TOKEN", null);
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Tools().OpenChangeRequestAsync("proj-1", CamelCaseBase));
            Assert.Contains("NAMINES_API_TOKEN", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NAMINES_API_TOKEN", previous);
        }
    }

    [Fact]
    public async Task Open_change_request_validates_locally_before_hitting_the_network()
    {
        // Bozuk şema için sunucuya gidip 400 almak, aynı hatayı bir ağ turu geç
        // göstermek olurdu. Token yokluğundan ÖNCE şema hatası verilmeli.
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Tools().OpenChangeRequestAsync("proj-1", """{"tables":[{"wrongField":1}]}"""));
    }
}
