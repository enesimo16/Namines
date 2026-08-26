using System.Collections.Generic;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Infrastructure.Services;

namespace Namines.Tests.Services;

/// <summary>
/// second-phase/07-MOTOR-DONUSUMU.md — dönüştürülmüş şemanın gerçekten hedef
/// motorda DERLENDİĞİNİ doğruluyor, yalnızca alan değerlerini değil. Bir
/// çözümün "doğru" sayılması için üretilen DDL'in <see cref="IDdlGeneratorFactory"/>
/// üzerinden hatasız çıkması gerekiyor.
/// </summary>
public class SchemaConverterTests
{
    private static readonly IDdlGeneratorFactory Ddl = new DdlGeneratorFactory();

    private static DatabaseSchema ArraySchema()
    {
        var schema = new DatabaseSchema
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
                        new SchemaColumn { Id = "c2", Name = "tags", Type = "TEXT", IsArray = true, IsNullable = true },
                    },
                },
            },
        };
        return schema;
    }

    [Fact]
    public void Unresolved_finding_leaves_schema_unchanged_and_target_ddl_still_fails()
    {
        var schema = ArraySchema();
        var report = EngineConversionAnalyzer.Analyze(schema, DatabaseType.PostgreSQL, DatabaseType.MySQL);

        var converted = SchemaConverter.Apply(schema, DatabaseType.MySQL, report.Findings, new Dictionary<string, string>());

        Assert.True(converted.Tables[0].Columns[1].IsArray);
        Assert.Throws<System.NotSupportedException>(() => Ddl.GetGenerator(DatabaseType.MySQL).Generate(converted));
    }

    [Fact]
    public void Json_text_resolution_produces_ddl_that_compiles_on_the_target()
    {
        var schema = ArraySchema();
        var report = EngineConversionAnalyzer.Analyze(schema, DatabaseType.PostgreSQL, DatabaseType.MySQL);
        var resolutions = new Dictionary<string, string> { [report.Findings[0].Id] = "json_text" };

        var converted = SchemaConverter.Apply(schema, DatabaseType.MySQL, report.Findings, resolutions);

        Assert.False(converted.Tables[0].Columns[1].IsArray);
        var ddl = Ddl.GetGenerator(DatabaseType.MySQL).Generate(converted);
        Assert.Contains("tags", ddl);
    }

    [Fact]
    public void Child_table_resolution_moves_the_column_into_a_new_related_table()
    {
        var schema = ArraySchema();
        var report = EngineConversionAnalyzer.Analyze(schema, DatabaseType.PostgreSQL, DatabaseType.MySQL);
        var resolutions = new Dictionary<string, string> { [report.Findings[0].Id] = "child_table" };

        var converted = SchemaConverter.Apply(schema, DatabaseType.MySQL, report.Findings, resolutions);

        Assert.Single(converted.Tables[0].Columns); // "tags" taşındı
        Assert.Equal(2, converted.Tables.Count);
        var child = converted.Tables[1];
        Assert.Contains(child.Columns, c => c.IsFK);
        // Source = FK kolonunu taşıyan taraf (alt tablo), Target = referans verilen ebeveyn.
        Assert.Contains(converted.Relations, r => r.SourceTableId == child.Id && r.TargetTableId == "t1");

        var ddl = Ddl.GetGenerator(DatabaseType.MySQL).Generate(converted);
        Assert.Contains(child.Name, ddl);
    }

    [Fact]
    public void Original_schema_passed_in_is_never_mutated()
    {
        var schema = ArraySchema();
        var report = EngineConversionAnalyzer.Analyze(schema, DatabaseType.PostgreSQL, DatabaseType.MySQL);
        var resolutions = new Dictionary<string, string> { [report.Findings[0].Id] = "json_text" };

        SchemaConverter.Apply(schema, DatabaseType.MySQL, report.Findings, resolutions);

        Assert.True(schema.Tables[0].Columns[1].IsArray); // kaynak şema hâlâ dizi
    }

    [Fact]
    public void Manual_resolution_behaves_like_unresolved()
    {
        var schema = ArraySchema();
        var report = EngineConversionAnalyzer.Analyze(schema, DatabaseType.PostgreSQL, DatabaseType.MySQL);
        var resolutions = new Dictionary<string, string> { [report.Findings[0].Id] = "manual" };

        var converted = SchemaConverter.Apply(schema, DatabaseType.MySQL, report.Findings, resolutions);

        Assert.True(converted.Tables[0].Columns[1].IsArray);
    }

    [Fact]
    public void Collation_drop_resolution_removes_the_collation_and_ddl_compiles()
    {
        var schema = new DatabaseSchema
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
                        new SchemaColumn { Id = "c2", Name = "name", Type = "VARCHAR", Length = 100, Collation = "en_US" },
                    },
                },
            },
        };
        var report = EngineConversionAnalyzer.Analyze(schema, DatabaseType.PostgreSQL, DatabaseType.Oracle);
        var resolutions = new Dictionary<string, string> { [report.Findings[0].Id] = "drop" };

        var converted = SchemaConverter.Apply(schema, DatabaseType.Oracle, report.Findings, resolutions);

        Assert.Null(converted.Tables[0].Columns[1].Collation);
        var ddl = Ddl.GetGenerator(DatabaseType.Oracle).Generate(converted);
        Assert.Contains("name", ddl.ToLowerInvariant());
    }
}
