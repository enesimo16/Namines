using System.Text.Json;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Infrastructure.Generators.PrismaGenerator;
using Namines.Mcp;

namespace Namines.Tests.Mcp;

/// <summary>
/// CLI duman testinde bulunan hatanın regresyon kilidi.
///
/// <b>Hata:</b> <c>StableUuid</c> hem model varsayılanında hem introspection'da
/// <c>Guid.NewGuid()</c> idi. <see cref="SchemaImpactAnalyzer"/> tabloları bu alanla
/// eşleştirip eşleşmeyeni "kaldırıldı + eklendi" saydığı için, AYNI şemayı iki kez
/// çözümlemek "her tablo silinecek, veri kaybı, Breaking" veriyordu. Bu, MCP/CLI'ın
/// birincil akışının (pull → öner → analiz) tam ortasıydı.
///
/// Yanlış ALARM, camelCase hatasındaki yanlış GÜVEN'in aynasıdır: ikisi de aracın
/// bulgularını değersizleştirir. Her tabloda "veri kaybı" gören kullanıcı, gerçek
/// veri kaybını gördüğünde de inanmaz.
/// </summary>
public class SchemaIdentityTests
{
    private sealed class StubIntrospection : IDbIntrospectionService
    {
        public Task<DatabaseSchema> IntrospectAsync(string connectionString, string dbType, CancellationToken ct = default)
            => Task.FromResult(new DatabaseSchema { Name = "stub" });
    }

    private sealed class StubTestRunner : IBranchTestRunner
    {
        public Task<TestRunResult> RunAsync(DatabaseSchema schema, DatabaseType engine, CancellationToken ct = default)
            => Task.FromResult(new TestRunResult(true, true, null, null, 1));
    }

    private static NaminesTools Tools() => new(
        new StubIntrospection(), new StubTestRunner(),
        new DdlGeneratorFactory(), new PrismaGeneratorService(),
        new NaminesCloudClient(new HttpClient()));

    /// <summary>StableUuid taşımayan şema — bir agent'ın yazacağı tipik JSON.</summary>
    private const string WithoutUuids = """
        {"name":"app","tables":[{"name":"users","columns":[
          {"name":"id","type":"INT","isPK":true,"isNullable":false},
          {"name":"email","type":"VARCHAR","length":255,"isNullable":false}]}],"relations":[]}
        """;

    [Fact]
    public void Identical_schemas_without_uuids_report_no_change()
    {
        var json = Tools().AnalyzeImpact(WithoutUuids, WithoutUuids, "PostgreSQL");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Safe", root.GetProperty("overallRisk").GetString());
        Assert.Empty(root.GetProperty("affectedTables").EnumerateArray());
        Assert.Empty(root.GetProperty("dataLossRisks").EnumerateArray());
    }

    [Fact]
    public void A_real_column_drop_is_still_detected()
    {
        // Yukarıdaki düzeltme "her şeyi eşleştir" hâline gelseydi bu test de geçerdi
        // ama araç işe yaramaz olurdu. Gerçek kayıp hâlâ görülmeli.
        const string withoutEmail = """
            {"name":"app","tables":[{"name":"users","columns":[
              {"name":"id","type":"INT","isPK":true,"isNullable":false}]}],"relations":[]}
            """;

        var json = Tools().AnalyzeImpact(WithoutUuids, withoutEmail, "PostgreSQL");
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("Breaking", doc.RootElement.GetProperty("overallRisk").GetString());
        Assert.Contains("email", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explicit_uuids_are_honoured_so_renames_still_read_as_renames()
    {
        // Rename = AYNI uuid, farklı ad. Türetme bunu ezseydi rename tekrar
        // "sil + ekle" olurdu ve veri kaybı uyarısı sahte olurdu.
        const string before = """
            {"name":"app","tables":[{"name":"users","stableUuid":"t-1","columns":[
              {"name":"id","type":"INT","isPK":true,"stableUuid":"c-1"}]}],"relations":[]}
            """;
        const string after = """
            {"name":"app","tables":[{"name":"members","stableUuid":"t-1","columns":[
              {"name":"id","type":"INT","isPK":true,"stableUuid":"c-1"}]}],"relations":[]}
            """;

        var json = Tools().AnalyzeImpact(before, after, "PostgreSQL");

        Assert.Contains("RenamedFrom", json);
        // Rename veri kaybı DEĞİLDİR; öyle raporlanırsa kullanıcı gereksiz yere durur.
        using var doc = JsonDocument.Parse(json);
        Assert.Empty(doc.RootElement.GetProperty("dataLossRisks").EnumerateArray());
    }

    [Fact]
    public void Table_identity_is_case_insensitive_like_the_engines()
    {
        Assert.Equal(SchemaIdentity.ForTable("Users"), SchemaIdentity.ForTable("users"));
        Assert.Equal(SchemaIdentity.ForColumn("Users", "Email"), SchemaIdentity.ForColumn("users", "email"));
        Assert.NotEqual(SchemaIdentity.ForTable("users"), SchemaIdentity.ForTable("orders"));
    }

    [Fact]
    public void Derived_identities_are_recognisable_as_derived()
    {
        // Ayıklarken "bu kimlik nereden geldi" sorusunu cevaplayabilmek için.
        Assert.True(SchemaIdentity.IsDerived(SchemaIdentity.ForTable("users")));
        Assert.False(SchemaIdentity.IsDerived(Guid.NewGuid().ToString()));
    }
}
