using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Tests.Generators;

/// <summary>
/// Eject köken dosyası (new-phase/23-GTM.md §2 Döngü 4).
///
/// Dosyanın tek değeri <b>kararlı</b> olmasında: şema değişmediği hâlde değişen
/// bir lock dosyası, her eject'i sahte bir git diff'ine çevirir ve o zaman kimse
/// gerçekten değiştiğinde fark etmez.
/// </summary>
public class EjectLockFileTests
{
    private static DatabaseSchema Schema()
    {
        var schema = new DatabaseSchema { Name = "Shop" };
        schema.Tables.Add(new SchemaTable
        {
            Id = "users", Name = "users",
            Columns =
            {
                new SchemaColumn { Name = "id", Type = "int", IsPK = true },
                new SchemaColumn { Name = "email", Type = "varchar" },
            },
        });
        schema.Tables.Add(new SchemaTable
        {
            Id = "orders", Name = "orders",
            Columns = { new SchemaColumn { Name = "id", Type = "int", IsPK = true } },
        });
        schema.Relations.Add(new SchemaRelation
        {
            SourceTableId = "orders", SourceColumnId = "user_id",
            TargetTableId = "users", TargetColumnId = "id",
        });
        return schema;
    }

    private static string Lock(DatabaseSchema schema) =>
        EjectLockFile.Generate(schema, DatabaseType.PostgreSQL, "orm.drizzle", "Drizzle ORM");

    [Fact]
    public void It_records_where_the_code_came_from()
    {
        var content = Lock(Schema());

        Assert.Contains("name      = \"Shop\"", content);
        Assert.Contains("engine    = \"PostgreSQL\"", content);
        Assert.Contains("target = \"orm.drizzle\"", content);
        Assert.Contains("tables    = 2", content);
        Assert.Contains("columns   = 3", content);
        Assert.Contains("relations = 1", content);
        Assert.Contains("https://namines.com", content);
    }

    [Fact]
    public void The_same_schema_produces_a_byte_identical_file()
    {
        Assert.Equal(Lock(Schema()), Lock(Schema()));
    }

    [Fact]
    public void Reordering_tables_does_not_change_the_fingerprint()
    {
        // Aynı veritabanının iki introspection'ı tablo sırasını farklı verebilir;
        // sıraya duyarlı bir parmak izi, değişmemiş bir şemayı "değişti" gösterirdi.
        var a = Schema();
        var b = Schema();
        b.Tables.Reverse();

        Assert.Equal(EjectLockFile.Fingerprint(a), EjectLockFile.Fingerprint(b));
    }

    [Fact]
    public void A_real_schema_change_does_change_the_fingerprint()
    {
        var before = Schema();
        var after = Schema();
        after.Tables[0].Columns.Add(new SchemaColumn { Name = "created_at", Type = "timestamp" });

        Assert.NotEqual(EjectLockFile.Fingerprint(before), EjectLockFile.Fingerprint(after));
    }

    [Fact]
    public void Making_a_column_nullable_changes_the_fingerprint()
    {
        // Sadece ad/tip'e bakan bir parmak izi, NOT NULL kaldırılmasını kaçırırdı —
        // üretilen tiplerin doğruluğunu doğrudan etkileyen bir değişiklik.
        var before = Schema();
        var after = Schema();
        after.Tables[0].Columns[1].IsNullable = true;

        Assert.NotEqual(EjectLockFile.Fingerprint(before), EjectLockFile.Fingerprint(after));
    }

    [Fact]
    public void There_is_no_timestamp_in_the_file()
    {
        // Zaman damgası, şema hiç değişmese bile her üretimde dosyayı değiştirir.
        var content = Lock(Schema());

        Assert.DoesNotContain(DateTime.UtcNow.Year.ToString(), content);
    }

    [Fact]
    public void A_name_with_quotes_stays_parseable()
    {
        var schema = Schema();
        schema.Name = "He said \"hi\" \\ done";

        Assert.Contains("name      = \"He said \\\"hi\\\" \\\\ done\"", Lock(schema));
    }
}
