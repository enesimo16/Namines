using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Tests.Analysis;

/// <summary>
/// second-phase/07-MOTOR-DONUSUMU.md — her koşul, gerçek DDL üreticilerinin
/// (bkz. ColumnFeatureSqlTests) fırlattığı <c>NotSupportedException</c>
/// noktalarıyla bire bir örtüşmeli. Burada iddia edilen, üreticinin gerçekten
/// hata vereceği koşul.
/// </summary>
public class EngineConversionAnalyzerTests
{
    private static DatabaseSchema Base() => new()
    {
        Name = "shop",
        Tables =
        {
            new SchemaTable
            {
                Id = "t1", Name = "users",
                Columns =
                {
                    new SchemaColumn { Id = "c1", Name = "id", Type = "INT", IsPK = true },
                },
            },
        },
    };

    [Fact]
    public void Array_column_is_flagged_for_every_non_postgres_target()
    {
        var schema = Base();
        schema.Tables[0].Columns.Add(new SchemaColumn { Id = "c2", Name = "tags", Type = "TEXT", IsArray = true });

        var report = EngineConversionAnalyzer.Analyze(schema, DatabaseType.PostgreSQL, DatabaseType.MySQL);

        var finding = Assert.Single(report.Findings);
        Assert.Equal(ConversionCategory.Array, finding.Category);
        Assert.Contains(finding.Options, o => o.Key == "child_table");
        Assert.Contains(finding.Options, o => o.Key == "json_text");
        Assert.Contains(finding.Options, o => o.Key == "manual");
    }

    [Fact]
    public void Array_column_targeting_postgres_is_not_flagged()
    {
        var schema = Base();
        schema.Tables[0].Columns.Add(new SchemaColumn { Id = "c2", Name = "tags", Type = "TEXT", IsArray = true });

        var report = EngineConversionAnalyzer.Analyze(schema, DatabaseType.MySQL, DatabaseType.PostgreSQL);

        Assert.Empty(report.Findings);
    }

    [Fact]
    public void Collation_targeting_oracle_is_always_flagged()
    {
        var schema = Base();
        schema.Tables[0].Columns[0].Collation = "en_US";

        var report = EngineConversionAnalyzer.Analyze(schema, DatabaseType.PostgreSQL, DatabaseType.Oracle);

        var finding = Assert.Single(report.Findings);
        Assert.Equal(ConversionCategory.Collation, finding.Category);
        Assert.DoesNotContain(finding.Options, o => o.Key == "map"); // Oracle'a eşleme yok, üretici hiç yazmıyor
    }

    [Fact]
    public void Collation_with_dashes_is_flagged_for_engines_expecting_a_bare_identifier()
    {
        var schema = Base();
        schema.Tables[0].Columns[0].Collation = "tr-TR-x-icu";

        var report = EngineConversionAnalyzer.Analyze(schema, DatabaseType.PostgreSQL, DatabaseType.MSSQL);

        var finding = Assert.Single(report.Findings);
        Assert.Contains(finding.Options, o => o.Key == "map");
    }

    [Fact]
    public void Bare_identifier_collation_is_not_flagged_for_mysql_family()
    {
        var schema = Base();
        schema.Tables[0].Columns[0].Collation = "utf8mb4_turkish_ci";

        var report = EngineConversionAnalyzer.Analyze(schema, DatabaseType.MySQL, DatabaseType.MariaDB);

        Assert.Empty(report.Findings);
    }

    [Theory]
    [InlineData("PostgreSQL")]
    [InlineData("SQLite")]
    public void Collation_targeting_postgres_or_sqlite_is_never_flagged(string targetName)
    {
        var schema = Base();
        schema.Tables[0].Columns[0].Collation = "tr-TR-x-icu";
        var target = System.Enum.Parse<DatabaseType>(targetName);

        var report = EngineConversionAnalyzer.Analyze(schema, DatabaseType.MSSQL, target);

        Assert.Empty(report.Findings);
    }

    [Fact]
    public void Generated_primary_key_targeting_sqlite_is_flagged()
    {
        var schema = Base();
        schema.Tables[0].Columns[0].Generated = "id + 1"; // aynı kolon zaten PK

        var report = EngineConversionAnalyzer.Analyze(schema, DatabaseType.PostgreSQL, DatabaseType.SQLite);

        var finding = Assert.Single(report.Findings);
        Assert.Equal(ConversionCategory.GeneratedPrimaryKey, finding.Category);
    }

    [Fact]
    public void Generated_non_primary_key_column_is_never_flagged()
    {
        var schema = Base();
        schema.Tables[0].Columns.Add(new SchemaColumn { Id = "c2", Name = "total", Type = "INT", Generated = "qty * price" });

        var report = EngineConversionAnalyzer.Analyze(schema, DatabaseType.PostgreSQL, DatabaseType.SQLite);

        Assert.Empty(report.Findings);
    }

    [Fact]
    public void Enum_columns_are_never_flagged_because_the_generator_already_downgrades_them_losslessly()
    {
        var schema = Base();
        schema.Enums.Add(new SchemaEnum { Id = "e1", Name = "status", Values = { "active", "inactive" } });
        schema.Tables[0].Columns.Add(new SchemaColumn { Id = "c2", Name = "status", EnumRef = "status" });

        var report = EngineConversionAnalyzer.Analyze(schema, DatabaseType.PostgreSQL, DatabaseType.Oracle);

        Assert.Empty(report.Findings);
    }

    [Fact]
    public void Clean_schema_produces_no_findings()
    {
        var report = EngineConversionAnalyzer.Analyze(Base(), DatabaseType.PostgreSQL, DatabaseType.MySQL);

        Assert.False(report.HasFindings);
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void Finding_ids_are_stable_and_scoped_to_table_and_column()
    {
        var schema = Base();
        schema.Tables[0].Columns.Add(new SchemaColumn { Id = "c2", Name = "tags", Type = "TEXT", IsArray = true });

        var report1 = EngineConversionAnalyzer.Analyze(schema, DatabaseType.PostgreSQL, DatabaseType.MySQL);
        var report2 = EngineConversionAnalyzer.Analyze(schema, DatabaseType.PostgreSQL, DatabaseType.MySQL);

        Assert.Equal(report1.Findings[0].Id, report2.Findings[0].Id);
        Assert.Contains("t1", report1.Findings[0].Id);
        Assert.Contains("c2", report1.Findings[0].Id);
    }
}
