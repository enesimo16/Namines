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
}
