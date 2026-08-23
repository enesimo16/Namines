using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Core.Prompts;

namespace Namines.Tests.Analysis;

/// <summary>
/// Doğal dilden üretilen SQL'in sınıflandırılması (08 §2 <c>/query/nl</c>).
///
/// <b>Bu bir güvenlik kapısı, bir kolaylık değil.</b> Modelin "yalnızca SELECT
/// üret" talimatına uyacağına güvenmek bir güvenlik kararı olamaz; asıl kapı
/// burada. Sınıflandırma yanılırsa, "geçen ayki siparişleri göster" diyen birinin
/// isteği bir <c>DELETE</c> olarak çalışabilir.
/// </summary>
public class SqlStatementKindTests
{
    [Theory]
    [InlineData("SELECT * FROM users")]
    [InlineData("  select 1")]
    [InlineData("WITH recent AS (SELECT 1) SELECT * FROM recent")]
    [InlineData("EXPLAIN SELECT * FROM users")]
    [InlineData("SHOW TABLES")]
    public void Read_statements_are_recognised(string sql)
    {
        Assert.Equal(SqlKind.Read, SqlStatementKind.Classify(sql));
    }

    [Theory]
    [InlineData("DELETE FROM users")]
    [InlineData("update users set x = 1")]
    [InlineData("INSERT INTO users VALUES (1)")]
    [InlineData("DROP TABLE users")]
    [InlineData("TRUNCATE users")]
    [InlineData("CALL do_something()")]
    [InlineData("GRANT ALL ON users TO public")]
    public void Write_statements_are_recognised(string sql)
    {
        Assert.Equal(SqlKind.Write, SqlStatementKind.Classify(sql));
    }

    [Fact]
    public void A_cte_that_writes_is_not_mistaken_for_a_read()
    {
        // "WITH ... INSERT" gerçek bir kalıp. İlk kelimeye bakıp okuma saymak,
        // tam da bu kalıpla atlatılabilir bir kapı bırakırdı.
        Assert.Equal(SqlKind.Write,
            SqlStatementKind.Classify("WITH x AS (SELECT 1) INSERT INTO t SELECT * FROM x"));
    }

    [Theory]
    [InlineData("-- rapor\nSELECT 1")]
    [InlineData("/* rapor */ SELECT 1")]
    [InlineData("/* a */ /* b */\n  select 1")]
    public void A_leading_comment_does_not_hide_the_verb(string sql)
    {
        // Yorumla başlatarak sınıflandırmayı şaşırtmak bilinen bir numaradır.
        Assert.Equal(SqlKind.Read, SqlStatementKind.Classify(sql));
    }

    [Fact]
    public void A_comment_cannot_disguise_a_delete_as_a_read()
    {
        Assert.Equal(SqlKind.Write, SqlStatementKind.Classify("-- SELECT\nDELETE FROM users"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("LOCK TABLE users")]
    [InlineData("¯\\_(ツ)_/¯")]
    public void Anything_unrecognised_is_unknown_not_read(string? sql)
    {
        // Beyaz liste: yalnızca okuduğundan EMİN olduğumuz fiiller okuma sayılır.
        // Kara liste kullansaydık, listede olmayan tek bir fiil sessizce
        // çalıştırılırdı.
        Assert.Equal(SqlKind.Unknown, SqlStatementKind.Classify(sql));
    }

    // ── İstem ────────────────────────────────────────────────────────────────

    private static DatabaseSchema Schema()
    {
        var schema = new DatabaseSchema { Name = "shop" };
        schema.Enums.Add(new SchemaEnum { Id = "e1", Name = "order_status", Values = { "paid" } });
        schema.Tables.Add(new SchemaTable
        {
            Id = "t1", Name = "orders",
            Columns =
            {
                new SchemaColumn { Id = "c1", Name = "id", Type = "INT", IsPK = true },
                new SchemaColumn { Id = "c2", Name = "status", Type = "TEXT", EnumRef = "order_status" },
            },
        });
        return schema;
    }

    [Fact]
    public void The_prompt_carries_the_real_schema()
    {
        // Modelin var olmayan tablo adları uydurmasının önündeki tek engel, ona
        // gerçek şemayı vermek.
        var (_, user) = NlQueryPromptBuilder.Build(Schema(), DatabaseType.PostgreSQL, "how many orders?");

        Assert.Contains("orders(id INT pk, status enum(order_status))", user);
        Assert.Contains("order_status: paid", user);
        Assert.Contains("how many orders?", user);
    }

    [Fact]
    public void The_prompt_lets_the_model_refuse()
    {
        // Uyduran bir sorgu, boş dönen bir sorgudan çok daha kötüdür: kullanıcı
        // sonucun doğru olduğunu sanar.
        var (system, _) = NlQueryPromptBuilder.Build(Schema(), DatabaseType.PostgreSQL, "x");

        Assert.Contains("UNANSWERABLE", system);
    }

    [Theory]
    [InlineData("```sql\nSELECT 1\n```", "SELECT 1")]
    [InlineData("```\nSELECT 1\n```", "SELECT 1")]
    [InlineData("SELECT 1", "SELECT 1")]
    public void Markdown_fences_are_stripped(string answer, string expected)
    {
        // Modeller talimata rağmen sık sık ```sql ile sarar; çiti bırakmak,
        // motorun sorguyu ilk karakterde reddetmesi demek olurdu.
        Assert.Equal(expected, NlQueryPromptBuilder.StripFences(answer));
    }
}
