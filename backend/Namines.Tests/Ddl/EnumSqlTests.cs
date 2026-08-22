using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;

namespace Namines.Tests.Ddl;

/// <summary>
/// Enum tipleri (04 §3 <c>enums</c>).
///
/// <b>Asıl sözleşme: kısıt hiçbir motorda KAYBOLMAZ.</b> Yalnızca iki motorun
/// gerçek bir enum tipi var; diğerlerinde sessizce <c>varchar</c>'a düşmek,
/// kullanıcının koruma sandığı şeyi yok etmek olurdu — kolon her değeri kabul
/// eder ve yanlış veri, bir kez yazıldıktan sonra temizlenmesi gereken bir borç
/// hâline gelir.
/// </summary>
public class EnumSqlTests
{
    private static DatabaseSchema Schema()
    {
        var schema = new DatabaseSchema { Name = "shop" };
        schema.Enums.Add(new SchemaEnum
        {
            Id = "e1", Name = "order_status",
            Values = { "pending", "paid", "shipped" },
        });
        schema.Tables.Add(new SchemaTable
        {
            Id = "t1", Name = "orders",
            Columns =
            {
                new SchemaColumn { Id = "c1", Name = "id", Type = "INT", IsPK = true },
                new SchemaColumn { Id = "c2", Name = "status", Type = "VARCHAR", EnumRef = "order_status" },
            },
        });
        return schema;
    }

    private static string Ddl(DatabaseType engine, DatabaseSchema? schema = null) =>
        new DdlGeneratorFactory().GetGenerator(engine).Generate(schema ?? Schema());

    // ── Kendi enum tipi olan motorlar ────────────────────────────────────────

    [Fact]
    public void Postgres_creates_the_type_before_the_table_that_uses_it()
    {
        // Sıra zorunlu: PostgreSQL'de bir tablo, henüz var olmayan bir tipe
        // başvuramaz. Yanlış sırada üretilen DDL hiç çalışmaz.
        var ddl = Ddl(DatabaseType.PostgreSQL);

        var typeAt = ddl.IndexOf("CREATE TYPE", StringComparison.Ordinal);
        var tableAt = ddl.IndexOf("CREATE TABLE", StringComparison.Ordinal);

        Assert.True(typeAt >= 0, "No CREATE TYPE was produced.");
        Assert.True(typeAt < tableAt, "The type was declared after the table that uses it.");
        Assert.Contains("AS ENUM ('pending', 'paid', 'shipped')", ddl);
        Assert.Contains("\"status\" \"order_status\"", ddl);
    }

    [Theory]
    [InlineData(DatabaseType.MySQL)]
    [InlineData(DatabaseType.MariaDB)]
    public void Mysql_family_writes_the_enum_on_the_column(DatabaseType engine)
    {
        var ddl = Ddl(engine);

        Assert.Contains("ENUM('pending', 'paid', 'shipped')", ddl);
        // Kendi tipi kısıtladığı için ikinci bir CHECK gürültü olurdu.
        Assert.DoesNotContain("CK_orders_status", ddl);
    }

    // ── Karşılığı olmayan motorlar ───────────────────────────────────────────

    [Theory]
    [InlineData(DatabaseType.MSSQL, "NVARCHAR(7)")]
    [InlineData(DatabaseType.Oracle, "VARCHAR2(7)")]
    [InlineData(DatabaseType.SQLite, "TEXT")]
    public void An_engine_without_enums_still_enforces_the_values(DatabaseType engine, string expectedType)
    {
        // Kısıt DÜŞÜRÜLMÜYOR, CHECK'e çevriliyor — ReferentialActionSql'deki
        // ilkeyle aynı: desteklenmeyen bir istek en KISITLAYICI karşılığa düşer.
        var ddl = Ddl(engine);

        Assert.Contains(expectedType, ddl);
        Assert.Contains("CHECK", ddl);
        Assert.Contains("'pending'", ddl);
        Assert.Contains("'shipped'", ddl);
    }

    [Fact]
    public void The_text_length_fits_the_longest_value()
    {
        // Sabit bir 255 seçmek, kısa bir enum için gereksiz yer ayırmak;
        // sabit bir 10 seçmek ise uzun bir değeri sessizce kesmek olurdu.
        var schema = Schema();
        schema.Enums[0].Values.Add("partially_refunded");

        Assert.Contains("NVARCHAR(18)", Ddl(DatabaseType.MSSQL, schema));
    }

    // ── Hatalı kullanım ──────────────────────────────────────────────────────

    [Fact]
    public void A_column_pointing_at_an_undefined_enum_is_refused()
    {
        // Sessizce metne düşmek, kullanıcının yazdığı kısıtın hiç uygulanmaması
        // ve bunu ancak yanlış veri girildiğinde fark etmesi demek olurdu.
        var schema = Schema();
        schema.Tables[0].Columns[1].EnumRef = "typo_status";

        var error = Assert.Throws<NotSupportedException>(() => Ddl(DatabaseType.PostgreSQL, schema));

        Assert.Contains("typo_status", error.Message);
    }

    [Fact]
    public void An_empty_enum_is_refused()
    {
        // Hiçbir değeri olmayan bir enum, hiçbir şeyi tutamayan bir kolon demek.
        var schema = Schema();
        schema.Enums[0].Values.Clear();

        Assert.Throws<NotSupportedException>(() => Ddl(DatabaseType.SQLite, schema));
    }

    [Fact]
    public void A_value_with_an_apostrophe_does_not_break_the_ddl()
    {
        // Kaçırılmazsa DDL ayrıştırılamaz hâle gelir.
        var schema = Schema();
        schema.Enums[0].Values.Add("can't_ship");

        Assert.Contains("'can''t_ship'", Ddl(DatabaseType.PostgreSQL, schema));
    }

    [Fact]
    public void A_schema_without_enums_is_untouched()
    {
        // Geriye dönük uyum: bugüne kadar üretilmiş her şema bunu bekliyor.
        var schema = Schema();
        schema.Enums.Clear();
        schema.Tables[0].Columns[1].EnumRef = null;

        var ddl = Ddl(DatabaseType.PostgreSQL, schema);

        Assert.DoesNotContain("CREATE TYPE", ddl);
        Assert.StartsWith("CREATE TABLE", ddl.TrimStart('﻿'));
    }
}
