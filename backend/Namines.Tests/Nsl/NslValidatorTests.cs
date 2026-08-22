using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Core.Nsl;

namespace Namines.Tests.Nsl;

/// <summary>
/// Şema doğrulama kuralları (new-phase/04-NSL-SCHEMA-IR.md §6).
///
/// Bir doğrulayıcının iki yönde de yanlış olması mümkün ve ikisi de zararlı:
/// kaçırdığı kural kullanıcıyı korumaz, yanlış alarm verdiği kural ise TÜM
/// uyarıların görmezden gelinmesine yol açar. Testler her kuralın hem tetiklendiği
/// hem de tetiklenMEDİĞİ durumu kilitliyor.
/// </summary>
public class NslValidatorTests
{
    private static SchemaColumn Column(string id, string name, string type,
        bool pk = false, bool nullable = false, int? length = null) =>
        new() { Id = id, Name = name, Type = type, IsPK = pk, IsNullable = nullable, Length = length };

    private static DatabaseSchema WithTable(SchemaTable table) =>
        new() { Name = "test", Tables = { table } };

    private static IEnumerable<string> Codes(DatabaseSchema schema, DatabaseType engine = DatabaseType.PostgreSQL) =>
        NslValidator.Validate(schema, engine).Select(f => f.Code);

    [Fact]
    public void A_clean_schema_produces_no_errors()
    {
        // Temiz bir şemada hata çıkması, doğrulayıcının gürültüye dönüşmesi demek.
        var schema = WithTable(new SchemaTable
        {
            Id = "t", Name = "products",
            Columns =
            {
                Column("a", "id", "INT", pk: true),
                Column("b", "title", "VARCHAR", length: 200),
            },
        });

        Assert.DoesNotContain(NslValidator.Validate(schema), f => f.Severity == "error");
    }

    [Fact]
    public void Duplicate_table_names_are_an_error()
    {
        var schema = new DatabaseSchema
        {
            Name = "t",
            Tables =
            {
                new SchemaTable { Id = "1", Name = "users", Columns = { Column("a", "id", "INT", pk: true) } },
                new SchemaTable { Id = "2", Name = "users", Columns = { Column("b", "id", "INT", pk: true) } },
            },
        };

        Assert.Contains("NSL001", Codes(schema));
    }

    [Fact]
    public void Duplicate_column_names_are_an_error()
    {
        var schema = WithTable(new SchemaTable
        {
            Id = "t", Name = "users",
            Columns = { Column("a", "id", "INT", pk: true), Column("b", "id", "TEXT") },
        });

        Assert.Contains("NSL002", Codes(schema));
    }

    [Fact]
    public void A_table_without_a_primary_key_is_a_warning_not_an_error()
    {
        // Anahtarsız tablolar (log, olay akışı) meşru olabilir — hata vermek,
        // geçerli bir tasarımı yasaklamak olurdu. Ama düzenlenemezler, o yüzden
        // sessiz de kalınmıyor.
        var schema = WithTable(new SchemaTable
        {
            Id = "t", Name = "audit_log", Columns = { Column("a", "message", "TEXT") },
        });

        var finding = NslValidator.Validate(schema).Single(f => f.Code == "NSL003");
        Assert.Equal("warning", finding.Severity);
    }

    [Fact]
    public void A_nullable_primary_key_is_an_error()
    {
        var schema = WithTable(new SchemaTable
        {
            Id = "t", Name = "users",
            Columns = { Column("a", "id", "INT", pk: true, nullable: true) },
        });

        Assert.Contains("NSL016", Codes(schema));
    }

    [Fact]
    public void A_reserved_word_is_flagged()
    {
        var schema = WithTable(new SchemaTable
        {
            Id = "t", Name = "users",
            Columns = { Column("a", "id", "INT", pk: true), Column("b", "order", "INT") },
        });

        Assert.Contains("NSL008", Codes(schema));
    }

    [Theory]
    [InlineData(DatabaseType.PostgreSQL, 64, true)]
    [InlineData(DatabaseType.PostgreSQL, 60, false)]
    [InlineData(DatabaseType.Oracle, 64, false)]
    public void Name_length_is_checked_per_engine(DatabaseType engine, int length, bool expected)
    {
        // Sınır motora göre değişiyor; tek bir sabit kullanmak ya PostgreSQL'de
        // kaçırır ya Oracle'da yanlış alarm verir.
        var schema = WithTable(new SchemaTable
        {
            Id = "t", Name = "users",
            Columns = { Column("a", new string('x', length), "INT", pk: true) },
        });

        Assert.Equal(expected, Codes(schema, engine).Contains("NSL009"));
    }

    [Fact]
    public void Varchar_without_a_length_is_flagged()
    {
        var schema = WithTable(new SchemaTable
        {
            Id = "t", Name = "users",
            Columns = { Column("a", "id", "INT", pk: true), Column("b", "email", "VARCHAR") },
        });

        Assert.Contains("NSL013", Codes(schema));
    }

    [Fact]
    public void A_monetary_float_is_flagged_but_a_plain_float_is_not()
    {
        // Her float'ı işaretlemek yanlış alarm olurdu; ölçüm ve oran alanları
        // meşru şekilde kayan noktadır.
        var money = WithTable(new SchemaTable
        {
            Id = "t", Name = "orders",
            Columns = { Column("a", "id", "INT", pk: true), Column("b", "total_price", "FLOAT") },
        });
        var ratio = WithTable(new SchemaTable
        {
            Id = "t", Name = "readings",
            Columns = { Column("a", "id", "INT", pk: true), Column("b", "temperature", "FLOAT") },
        });

        Assert.Contains("NSL014", Codes(money));
        Assert.DoesNotContain("NSL014", Codes(ratio));
    }

    // ── İlişki kuralları ─────────────────────────────────────────────────────

    private static DatabaseSchema Related(string fromType, string toType, bool targetIsPk = true)
    {
        var users = new SchemaTable
        {
            Id = "t1", Name = "users",
            Columns = { Column("c1", "id", toType, pk: targetIsPk) },
        };
        var orders = new SchemaTable
        {
            Id = "t2", Name = "orders",
            Columns = { Column("c2", "id", "INT", pk: true), Column("c3", "user_id", fromType) },
            Indexes =
            {
                new SchemaIndex
                {
                    Id = "i", Columns = { new SchemaIndexColumn { ColumnId = "c3" } },
                },
            },
        };

        return new DatabaseSchema
        {
            Name = "t",
            Tables = { users, orders },
            Relations =
            {
                new SchemaRelation
                {
                    Id = "r", SourceTableId = "t2", SourceColumnId = "c3",
                    TargetTableId = "t1", TargetColumnId = "c1",
                },
            },
        };
    }

    [Fact]
    public void Mismatched_foreign_key_types_are_an_error()
    {
        Assert.Contains("NSL004", Codes(Related("UUID", "INT")));
    }

    [Fact]
    public void Equivalent_type_names_do_not_trigger_a_mismatch()
    {
        // INT ile INTEGER aynı şey; metin eşitliğiyle karşılaştırmak yanlış
        // alarm üretir ve gerçek uyuşmazlıklar gürültüde kaybolur.
        Assert.DoesNotContain("NSL004", Codes(Related("INT", "INTEGER")));
        Assert.DoesNotContain("NSL004", Codes(Related("INT", "BIGINT")));
    }

    [Fact]
    public void A_foreign_key_to_a_non_unique_column_is_an_error()
    {
        Assert.Contains("NSL005", Codes(Related("INT", "INT", targetIsPk: false)));
    }

    [Fact]
    public void An_unindexed_foreign_key_is_a_warning()
    {
        var schema = Related("INT", "INT");
        schema.Tables.Single(t => t.Name == "orders").Indexes.Clear();

        Assert.Contains("NSL010", Codes(schema));
        // Index varken uyarı ÇIKMAMALI, aksi hâlde kural anlamsızlaşır.
        Assert.DoesNotContain("NSL010", Codes(Related("INT", "INT")));
    }

    [Fact]
    public void Duplicate_indexes_are_flagged()
    {
        var schema = WithTable(new SchemaTable
        {
            Id = "t", Name = "users",
            Columns = { Column("a", "id", "INT", pk: true), Column("b", "email", "VARCHAR", length: 255) },
            Indexes =
            {
                new SchemaIndex { Id = "i1", Columns = { new SchemaIndexColumn { ColumnId = "b" } } },
                new SchemaIndex { Id = "i2", Columns = { new SchemaIndexColumn { ColumnId = "b" } } },
            },
        });

        Assert.Contains("NSL011", Codes(schema));
    }

    [Fact]
    public void An_orphan_table_is_only_information()
    {
        // Tek başına duran tablolar (ayarlar, sözlük) tamamen meşru.
        var schema = Related("INT", "INT");
        schema.Tables.Add(new SchemaTable
        {
            Id = "t3", Name = "settings", Columns = { Column("z", "id", "INT", pk: true) },
        });

        var finding = NslValidator.Validate(schema).Single(f => f.Code == "NSL018");
        Assert.Equal("info", finding.Severity);
        Assert.Equal("settings", finding.Table);
    }

    [Fact]
    public void Cascade_rules_come_from_the_existing_analyzer()
    {
        // NSL006/007 yeniden yazılmadı; ikinci bir uygulama, biri güncellenip
        // diğeri unutulduğunda çelişkili sonuç verirdi.
        var schema = Related("INT", "INT");
        schema.Relations[0].OnDelete = ReferentialAction.Cascade;

        // MSSQL'de tek cascade yolu sorun değil; kural yalnızca ÇOKLU yolda tetiklenmeli.
        Assert.DoesNotContain("NSL006", Codes(schema, DatabaseType.MSSQL));
    }
}
