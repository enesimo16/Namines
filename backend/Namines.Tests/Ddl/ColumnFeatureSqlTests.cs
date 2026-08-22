using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;

namespace Namines.Tests.Ddl;

/// <summary>
/// Kolon üzerindeki motora bağlı özellikler: hesaplanan kolon, collation, dizi
/// (04 §3).
///
/// <b>Ortak sözleşme: desteklenmeyen bir istek sessizce ATILMAZ.</b> Bir diziyi
/// skalere düşürmek DDL'i çalıştırır ama kolonun anlamını değiştirir — hata
/// çalışma zamanına ertelenir ve orada bulunması çok daha pahalıdır.
/// </summary>
public class ColumnFeatureSqlTests
{
    private static DatabaseSchema Schema(Action<SchemaColumn> configure)
    {
        var column = new SchemaColumn { Id = "c2", Name = "total", Type = "DECIMAL" };
        configure(column);

        return new DatabaseSchema
        {
            Name = "shop",
            Tables =
            {
                new SchemaTable
                {
                    Id = "t1", Name = "orders",
                    Columns = { new SchemaColumn { Id = "c1", Name = "id", Type = "INT", IsPK = true }, column },
                },
            },
        };
    }

    private static string Ddl(DatabaseType engine, DatabaseSchema schema) =>
        new DdlGeneratorFactory().GetGenerator(engine).Generate(schema);

    // ── Hesaplanan kolon ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(DatabaseType.PostgreSQL, "GENERATED ALWAYS AS (quantity * unit_price) STORED")]
    [InlineData(DatabaseType.MySQL, "GENERATED ALWAYS AS (quantity * unit_price) STORED")]
    [InlineData(DatabaseType.MariaDB, "GENERATED ALWAYS AS (quantity * unit_price) STORED")]
    [InlineData(DatabaseType.MSSQL, "AS (quantity * unit_price) PERSISTED")]
    [InlineData(DatabaseType.SQLite, "GENERATED ALWAYS AS (quantity * unit_price) STORED")]
    [InlineData(DatabaseType.Oracle, "GENERATED ALWAYS AS (quantity * unit_price) VIRTUAL")]
    public void A_generated_column_uses_the_engines_own_syntax(DatabaseType engine, string expected)
    {
        Assert.Contains(expected, Ddl(engine, Schema(c => c.Generated = "quantity * unit_price")));
    }

    [Theory]
    [InlineData(DatabaseType.PostgreSQL)]
    [InlineData(DatabaseType.MSSQL)]
    [InlineData(DatabaseType.SQLite)]
    public void A_generated_column_carries_no_type_default_or_nullability(DatabaseType engine)
    {
        // Motorların çoğunda hesaplanan kolonun tipi ifadeden çıkarılır ve
        // ayrıca tip/DEFAULT yazmak sözdizimi hatasıdır — üretilen DDL hiç
        // çalışmazdı.
        var schema = Schema(c =>
        {
            c.Generated = "quantity * unit_price";
            c.DefaultValue = "0";
            c.IsNullable = false;
        });

        var line = Ddl(engine, schema)
            .Split('\n')
            .First(l => l.Contains("total"));

        Assert.DoesNotContain("DEFAULT", line);
        Assert.DoesNotContain("NOT NULL", line);
        Assert.DoesNotContain("DECIMAL", line, StringComparison.OrdinalIgnoreCase);
    }

    // ── Dizi ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Postgres_writes_an_array_type()
    {
        var schema = Schema(c => { c.Name = "tags"; c.Type = "TEXT"; c.IsArray = true; });

        Assert.Contains("\"tags\" text[]", Ddl(DatabaseType.PostgreSQL, schema), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DatabaseType.MSSQL)]
    [InlineData(DatabaseType.MySQL)]
    [InlineData(DatabaseType.MariaDB)]
    [InlineData(DatabaseType.Oracle)]
    [InlineData(DatabaseType.SQLite)]
    public void An_array_on_an_engine_without_arrays_is_refused(DatabaseType engine)
    {
        // Skalere düşmek DDL'i çalıştırır ama uygulama listeye yazmaya çalışır
        // ve hata çalışma zamanına ertelenir.
        var schema = Schema(c => { c.Name = "tags"; c.Type = "TEXT"; c.IsArray = true; });

        var error = Assert.Throws<NotSupportedException>(() => Ddl(engine, schema));
        Assert.Contains("array", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Collation ────────────────────────────────────────────────────────────

    [Fact]
    public void Postgres_quotes_the_collation_name()
    {
        // Tırnaksız yazmak, tire içeren her collation adında sözdizimi hatası verir.
        var schema = Schema(c => { c.Type = "VARCHAR"; c.Collation = "tr-TR-x-icu"; });

        Assert.Contains("COLLATE \"tr-TR-x-icu\"", Ddl(DatabaseType.PostgreSQL, schema));
    }

    [Fact]
    public void Sqlite_also_needs_the_collation_quoted()
    {
        // BULUNMA YERİ: gerçek SQLite, "COLLATE tr-TR-x-icu" için
        // 'near "-": syntax error' verdi. Birim testi tırnaksızı doğruluyordu.
        var schema = Schema(c => { c.Type = "VARCHAR"; c.Collation = "tr-TR-x-icu"; });

        Assert.Contains("COLLATE \"tr-TR-x-icu\"", Ddl(DatabaseType.SQLite, schema));
    }

    [Theory]
    [InlineData(DatabaseType.MSSQL)]
    [InlineData(DatabaseType.MySQL)]
    [InlineData(DatabaseType.MariaDB)]
    public void Engines_expecting_a_bare_identifier_get_one(DatabaseType engine)
    {
        var schema = Schema(c => { c.Type = "VARCHAR"; c.Collation = "Turkish_CI_AS"; });

        Assert.Contains("COLLATE Turkish_CI_AS", Ddl(engine, schema));
    }

    [Theory]
    [InlineData(DatabaseType.MSSQL)]
    [InlineData(DatabaseType.MySQL)]
    public void A_collation_name_those_engines_would_reject_is_caught_here(DatabaseType engine)
    {
        // Bozuk SQL üretip veritabanının anlaşılmaz bir hatayla düşmesini
        // beklemek yerine, sorun burada söyleniyor.
        var schema = Schema(c => { c.Type = "VARCHAR"; c.Collation = "tr-TR-x-icu"; });

        Assert.Throws<NotSupportedException>(() => Ddl(engine, schema));
    }

    [Fact]
    public void Oracle_says_it_does_not_emit_collations()
    {
        // Üretip veritabanının reddetmesini beklemek, çağırana "Namines bozuk"
        // dedirtir; desteklenmediği söyleniyor.
        var schema = Schema(c => { c.Type = "VARCHAR"; c.Collation = "BINARY_CI"; });

        Assert.Throws<NotSupportedException>(() => Ddl(DatabaseType.Oracle, schema));
    }

    [Fact]
    public void A_schema_using_none_of_these_is_untouched()
    {
        // Geriye dönük uyum: alanların hiçbiri dolu değilse çıktı eskisiyle aynı.
        var ddl = Ddl(DatabaseType.PostgreSQL, Schema(_ => { }));

        Assert.DoesNotContain("GENERATED ALWAYS", ddl);
        Assert.DoesNotContain("COLLATE", ddl);
        Assert.DoesNotContain("[]", ddl);
    }
}
