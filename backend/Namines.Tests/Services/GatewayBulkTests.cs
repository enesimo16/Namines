using Namines.Infrastructure.Services;

namespace Namines.Tests.Services;

/// <summary>
/// Gateway'in toplu yazma, fonksiyon çağrısı ve ham sorgu uçları (08 §2).
///
/// <see cref="GatewayWriteTests"/> ile aynı sınır: burada saf metin/doğrulama
/// mantığı kanıtlanıyor. Bu üç uçta metnin yanlış olması ya sessizce yanlış veri
/// yazmak ya da izin modelini delmek demek — ikisi de bağlantı olmadan
/// kanıtlanabilir.
/// </summary>
public class GatewayBulkTests
{
    // ── Ham SQL: tek ifade zorunluluğu ───────────────────────────────────────

    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("SELECT 1;")]
    [InlineData("SELECT 1;   ")]
    [InlineData("UPDATE users SET note = 'a;b' WHERE id = 1")]
    [InlineData("SELECT \"we;ird\" FROM t")]
    public void A_single_statement_is_accepted(string sql)
    {
        // Dize ve tanımlayıcı içindeki noktalı virgül ifade sonu DEĞİLDİR; onu
        // reddetmek, tamamen geçerli sorguları engellerdi.
        GatewayService.EnsureSingleStatement(sql);
    }

    [Theory]
    [InlineData("SELECT 1; DROP TABLE users")]
    [InlineData("SELECT 1;SELECT 2")]
    [InlineData("UPDATE t SET a = 1; DELETE FROM t")]
    public void Chained_statements_are_rejected(string sql)
    {
        // İncelenen sorgunun yanına ikinci bir sorgu iliştirmenin klasik yolu.
        var error = Assert.Throws<ArgumentException>(() => GatewayService.EnsureSingleStatement(sql));

        Assert.Contains("one statement", error.Message);
    }

    // ── RPC: motor sözdizimi ─────────────────────────────────────────────────

    [Theory]
    [InlineData("PostgreSQL", "SELECT * FROM \"total_for\"(@a0, @a1)")]
    [InlineData("MySQL", "CALL `total_for`(@a0, @a1)")]
    [InlineData("MariaDB", "CALL `total_for`(@a0, @a1)")]
    [InlineData("MSSQL", "EXEC [total_for] @a0, @a1")]
    public void Each_engine_gets_its_own_call_syntax(string dbType, string expected)
    {
        Assert.Equal(expected, GatewayService.BuildRpcSql(dbType, "total_for", 2));
    }

    [Fact]
    public void An_unsupported_engine_is_refused_rather_than_guessed()
    {
        // Yanlış sözdizimi üretip veritabanının hata vermesini beklemek, çağırana
        // "Namines bozuk" dedirtir; hangi motorların desteklendiği söyleniyor.
        var error = Assert.Throws<NotSupportedException>(
            () => GatewayService.BuildRpcSql("SQLite", "total_for", 0));

        Assert.Contains("PostgreSQL", error.Message);
    }

    [Fact]
    public void A_function_name_is_never_concatenated_unquoted()
    {
        // Ad tanımlayıcı olarak doğrulanıyor ve alıntılanıyor; aksi hâlde ad
        // alanı doğrudan bir SQL enjeksiyon yüzeyi olurdu.
        Assert.Throws<ArgumentException>(
            () => GatewayService.ValidateIdentifierOrThrow("drop; --", "function"));
    }

    [Fact]
    public void A_call_without_arguments_has_no_dangling_separator()
    {
        Assert.Equal("SELECT * FROM \"ping\"()", GatewayService.BuildRpcSql("PostgreSQL", "ping", 0));
        Assert.Equal("EXEC [ping]", GatewayService.BuildRpcSql("MSSQL", "ping", 0));
    }

    // ── Import: geri okuma yapılmıyor ────────────────────────────────────────

    [Fact]
    public void Bulk_insert_does_not_ask_for_the_row_back()
    {
        // 10.000 satırın her biri için eklenen satırı geri okumak, hiç
        // kullanılmayacak veriyi ağdan geçirmek olurdu.
        var columns = new[] { "email", "note" };

        var single = GatewayService.BuildInsertSql("PostgreSQL", "users", columns);
        var bulk = GatewayService.BuildInsertSql("PostgreSQL", "users", columns, includeReturning: false);

        Assert.Contains("RETURNING *", single);
        Assert.DoesNotContain("RETURNING", bulk);
    }
}
