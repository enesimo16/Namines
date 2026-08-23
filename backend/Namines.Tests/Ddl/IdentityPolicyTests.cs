using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;

namespace Namines.Tests.Ddl;

/// <summary>
/// Otomatik artan anahtar kararı (04 §3 <c>identity</c>).
///
/// <b>Bu kural bugüne kadar altı DDL üreticisinde ayrı ayrı yazılıydı</b> ve
/// kullanıcının onu bozma yolu yoktu: tek kolonlu tamsayı birincil anahtar her
/// zaman otomatik artan sayılıyordu. Dışarıdan atanan bir kimlik (başka bir
/// sistemden gelen sipariş numarası gibi) için bu, veritabanının değeri ezmesi
/// demek — sessiz veri kaybı.
/// </summary>
public class IdentityPolicyTests
{
    private static SchemaColumn Column(string name, string type, bool isPk = false, bool? identity = null) =>
        new() { Id = name, Name = name, Type = type, IsPK = isPk, Identity = identity };

    // ── Karar ────────────────────────────────────────────────────────────────

    [Fact]
    public void An_unspecified_single_integer_key_keeps_the_old_inference()
    {
        // Geriye dönük uyum: bugüne kadar üretilmiş her şema bunu bekliyor.
        Assert.True(IdentityPolicy.IsGenerated(Column("id", "INT", isPk: true), 1));
    }

    [Fact]
    public void A_key_the_user_assigns_is_not_generated()
    {
        // Asıl eklenen yetenek: "hayır" diyebilmek.
        Assert.False(IdentityPolicy.IsGenerated(Column("order_no", "INT", isPk: true, identity: false), 1));
    }

    [Fact]
    public void A_non_key_column_can_be_generated_when_asked()
    {
        Assert.True(IdentityPolicy.IsGenerated(Column("seq", "BIGINT", identity: true), 1));
    }

    [Fact]
    public void A_composite_key_is_never_generated_by_inference()
    {
        // Motorların çoğu tabloda tek otomatik kolona izin verir (SQL Server
        // Msg 2744, Oracle ORA-30673); ikisine birden vermek DDL'i çalıştırılamaz
        // hâle getirir.
        Assert.False(IdentityPolicy.IsGenerated(Column("order_id", "INT", isPk: true), 2));
        Assert.False(IdentityPolicy.IsGenerated(Column("product_id", "INT", isPk: true), 2));
    }

    [Theory]
    [InlineData("UUID")]
    [InlineData("VARCHAR")]
    [InlineData("TEXT")]
    public void A_non_integer_key_is_not_generated(string type)
    {
        // Bir uuid ya da metin anahtarı "artırmanın" karşılığı yok.
        Assert.False(IdentityPolicy.IsGenerated(Column("id", type, isPk: true), 1));
    }

    // ── Motorlara yansıması ──────────────────────────────────────────────────

    private static DatabaseSchema Schema(bool? identity)
    {
        var schema = new DatabaseSchema { Name = "shop" };
        schema.Tables.Add(new SchemaTable
        {
            Id = "t1", Name = "orders",
            Columns = { Column("id", "INT", isPk: true, identity: identity) },
        });
        return schema;
    }

    private static string Ddl(DatabaseType engine, bool? identity) =>
        new DdlGeneratorFactory().GetGenerator(engine).Generate(Schema(identity));

    [Theory]
    [InlineData(DatabaseType.PostgreSQL, "SERIAL")]
    [InlineData(DatabaseType.MSSQL, "IDENTITY(1,1)")]
    [InlineData(DatabaseType.MySQL, "AUTO_INCREMENT")]
    [InlineData(DatabaseType.MariaDB, "AUTO_INCREMENT")]
    [InlineData(DatabaseType.Oracle, "GENERATED ALWAYS AS IDENTITY")]
    [InlineData(DatabaseType.SQLite, "AUTOINCREMENT")]
    public void Saying_no_removes_the_auto_increment_on_every_engine(DatabaseType engine, string marker)
    {
        // Tek bir motorda kuralın uygulanmaması, aynı şemanın iki motorda farklı
        // davranması demek — ve bu farkı ancak veriler bozulunca fark edersin.
        Assert.Contains(marker, Ddl(engine, identity: null));
        Assert.DoesNotContain(marker, Ddl(engine, identity: false));
    }

    // ── Hesaplanan kolon ile çakışma ─────────────────────────────────────────

    [Fact]
    public void A_generated_column_is_never_also_auto_increment()
    {
        // İkisi de "bu değeri kim koyuyor" sorusuna cevap veriyor ve iki cevap
        // birden olamaz. PostgreSQL bunu `SERIAL GENERATED ALWAYS AS (...)` ile
        // reddediyordu; SQLite ise sessizce ifadeyi düşürüp kolonu boş bırakıyordu.
        var column = Column("id", "INT", isPk: true);
        column.Generated = "a + b";

        Assert.False(IdentityPolicy.IsGenerated(column, 1));
    }

    [Theory]
    [InlineData(DatabaseType.PostgreSQL, "SERIAL")]
    [InlineData(DatabaseType.MSSQL, "IDENTITY(1,1)")]
    [InlineData(DatabaseType.MySQL, "AUTO_INCREMENT")]
    [InlineData(DatabaseType.MariaDB, "AUTO_INCREMENT")]
    [InlineData(DatabaseType.Oracle, "GENERATED ALWAYS AS IDENTITY")]
    public void No_engine_pairs_an_expression_with_auto_increment(DatabaseType engine, string marker)
    {
        var schema = Schema(identity: null);
        schema.Tables[0].Columns[0].Generated = "a + b";

        var ddl = Ddl(engine, identity: null);
        var generatedDdl = new DdlGeneratorFactory().GetGenerator(engine).Generate(schema);

        Assert.Contains(marker, ddl);
        Assert.DoesNotContain(marker, generatedDdl);
        // İfade DÜŞMEMELİ de: SQLite'ta olan buydu ve hata ancak veriler
        // yazıldıktan sonra görünüyordu.
        Assert.Contains("a + b", generatedDdl);
    }

    [Fact]
    public void Sqlite_refuses_the_combination_outright()
    {
        // SQLite listede DEĞİL çünkü hesaplanan bir kolonun birincil anahtar
        // olmasına hiç izin vermiyor ("generated columns cannot be part of the
        // PRIMARY KEY" — gerçek motorda ölçüldü). Diğer beş motorda çıktı
        // üretiliyor, burada reddediliyor; ikisi de doğru davranış.
        var schema = Schema(identity: null);
        schema.Tables[0].Columns[0].Generated = "a + b";

        Assert.Throws<NotSupportedException>(
            () => new DdlGeneratorFactory().GetGenerator(DatabaseType.SQLite).Generate(schema));
    }
}
