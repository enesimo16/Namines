using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Tests.Fixtures;

namespace Namines.Tests.Analysis;

/// <summary>
/// <see cref="FkCascadeAnalyzer"/> — çalıştırılamayan veya veri kaybettiren DDL'in
/// kullanıcıya ulaşmadan yakalanması.
///
/// Bu analizör olmadan varsayılanı NO ACTION yapmak yetmez: kullanıcı elle CASCADE
/// seçtiğinde aynı hataya geri düşülür. Analizör, hatanın TEKRAR OLUŞMASINI engeller.
/// </summary>
public class FkCascadeAnalyzerTests
{
    // ── Varsayılan şemalar temiz olmalı ──────────────────────────────────────

    [Theory]
    [InlineData("01-minimal")]
    [InlineData("02-ecommerce")]
    [InlineData("03-composite-key")]
    [InlineData("04-self-referencing")]
    [InlineData("05-multi-cascade-path")]
    public void Default_schemas_have_no_issues(string fixtureName)
    {
        var schema = SchemaFixtures.ByName(fixtureName);

        var issues = FkCascadeAnalyzer.Analyze(schema, DatabaseType.MSSQL);

        Assert.Empty(issues);
    }

    // ── Çoklu cascade yolu yakalanmalı ───────────────────────────────────────

    [Fact]
    public void Detects_multiple_cascade_paths()
    {
        var schema = SchemaFixtures.MultiCascadePath();
        foreach (var rel in schema.Relations)
            rel.OnDelete = ReferentialAction.Cascade;

        var issues = FkCascadeAnalyzer.Analyze(schema, DatabaseType.MSSQL);

        var multi = issues.Where(i => i.Kind == CascadeIssueKind.MultipleCascadePaths).ToList();
        Assert.NotEmpty(multi);
        Assert.Contains(multi, i => i.FromTable == "Orders" && i.ToTable == "Users");
        Assert.Contains("Msg 1785", multi.First(i => i.FromTable == "Orders").Message);
    }

    [Fact]
    public void Blocking_issues_reported_for_mssql()
    {
        var schema = SchemaFixtures.MultiCascadePath();
        foreach (var rel in schema.Relations)
            rel.OnDelete = ReferentialAction.Cascade;

        Assert.True(FkCascadeAnalyzer.HasBlockingIssues(schema, DatabaseType.MSSQL));
    }

    // ── Döngü yakalanmalı ────────────────────────────────────────────────────

    [Fact]
    public void Detects_self_referencing_cascade_cycle()
    {
        var schema = SchemaFixtures.SelfReferencing();
        schema.Relations[0].OnDelete = ReferentialAction.Cascade;

        var issues = FkCascadeAnalyzer.Analyze(schema, DatabaseType.MSSQL);

        Assert.Contains(issues, i => i.Kind == CascadeIssueKind.CascadeCycle && i.FromTable == "Categories");
    }

    // ── SET NULL / SET DEFAULT tutarlılığı ───────────────────────────────────

    [Fact]
    public void Detects_set_null_on_not_null_column()
    {
        var schema = SchemaFixtures.ECommerce();
        // Orders.UserId NOT NULL — SET NULL burada çalışma zamanında ihlal üretir.
        schema.Relations[0].OnDelete = ReferentialAction.SetNull;

        var issues = FkCascadeAnalyzer.Analyze(schema, DatabaseType.PostgreSQL);

        Assert.Contains(issues, i => i.Kind == CascadeIssueKind.SetNullOnNotNullColumn);
    }

    [Fact]
    public void Set_null_on_nullable_column_is_fine()
    {
        var schema = SchemaFixtures.SelfReferencing();
        // Categories.ParentId nullable — SET NULL burada geçerli.
        schema.Relations[0].OnDelete = ReferentialAction.SetNull;

        var issues = FkCascadeAnalyzer.Analyze(schema, DatabaseType.PostgreSQL);

        Assert.DoesNotContain(issues, i => i.Kind == CascadeIssueKind.SetNullOnNotNullColumn);
    }

    [Fact]
    public void Detects_set_default_without_default_value()
    {
        var schema = SchemaFixtures.ECommerce();
        schema.Relations[0].OnDelete = ReferentialAction.SetDefault;

        var issues = FkCascadeAnalyzer.Analyze(schema, DatabaseType.PostgreSQL);

        Assert.Contains(issues, i => i.Kind == CascadeIssueKind.SetDefaultWithoutDefaultValue);
    }

    // ── Tek cascade yolu sorun değil ─────────────────────────────────────────

    [Fact]
    public void Single_cascade_path_is_allowed()
    {
        var schema = SchemaFixtures.ECommerce();
        // OrderItems -> Orders tek yol; sorun değil.
        schema.Relations.Single(r => r.Id == "r2").OnDelete = ReferentialAction.Cascade;

        var issues = FkCascadeAnalyzer.Analyze(schema, DatabaseType.MSSQL);

        Assert.DoesNotContain(issues, i => i.Kind == CascadeIssueKind.MultipleCascadePaths);
        Assert.False(FkCascadeAnalyzer.HasBlockingIssues(schema, DatabaseType.MSSQL));
    }

    // ── Motor farkı mesaja yansımalı ─────────────────────────────────────────

    [Fact]
    public void Message_differs_between_mssql_and_postgres()
    {
        var schema = SchemaFixtures.MultiCascadePath();
        foreach (var rel in schema.Relations)
            rel.OnDelete = ReferentialAction.Cascade;

        var mssql = FkCascadeAnalyzer.Analyze(schema, DatabaseType.MSSQL)
            .First(i => i.Kind == CascadeIssueKind.MultipleCascadePaths);
        var postgres = FkCascadeAnalyzer.Analyze(schema, DatabaseType.PostgreSQL)
            .First(i => i.Kind == CascadeIssueKind.MultipleCascadePaths);

        Assert.Contains("Msg 1785", mssql.Message);
        Assert.DoesNotContain("Msg 1785", postgres.Message);
    }
}
