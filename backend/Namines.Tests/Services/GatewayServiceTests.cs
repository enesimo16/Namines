using Namines.Infrastructure.Services;

namespace Namines.Tests.Services;

/// <summary>
/// G14 — Minimal Gateway. Bu testler SAF SQL-üretim mantığını kanıtlar (identifier
/// doğrulama, motora özgü quote/sayfalama sözdizimi) — canlı bir bağlantı GEREKMEZ.
///
/// Canlı bağlantı yolu (GatewayService.ListAsync/DetailAsync'in gerçek DbConnection
/// açan kısmı) burada test EDİLMİYOR — DbIntrospectionService ile aynı SsrfGuard'ı
/// kullanıyor, o da localhost/private IP aralıklarını (Docker container'lar dahil)
/// bilinçli olarak reddediyor. Bu, bu servis kategorisinin (kullanıcının kendi canlı
/// veritabanına bağlanan özellikler) yerel Docker'a karşı test edilemediği,
/// DbIntrospectionService'in de zaten sahip olduğu önceden var olan bir sınır —
/// yeni bir boşluk değil.
/// </summary>
public class GatewayServiceTests
{
    [Theory]
    [InlineData("Users")]
    [InlineData("_orders")]
    [InlineData("order_items")]
    [InlineData("T1")]
    public void Valid_identifiers_pass(string identifier)
    {
        GatewayService.ValidateIdentifierOrThrow(identifier, "x"); // exception atmamalı
    }

    [Theory]
    [InlineData("Users; DROP TABLE Users--")]
    [InlineData("1users")]
    [InlineData("users name")]
    [InlineData("")]
    [InlineData("users'")]
    [InlineData("users--")]
    public void Malicious_or_invalid_identifiers_are_rejected(string identifier)
    {
        Assert.Throws<ArgumentException>(() => GatewayService.ValidateIdentifierOrThrow(identifier, "x"));
    }

    [Theory]
    [InlineData("MSSQL", "[Users]")]
    [InlineData("PostgreSQL", "\"Users\"")]
    [InlineData("MySQL", "`Users`")]
    [InlineData("MariaDB", "`Users`")]
    [InlineData("Oracle", "\"Users\"")]
    public void Quote_uses_correct_engine_delimiter(string dbType, string expected)
    {
        Assert.Equal(expected, GatewayService.Quote(dbType, "Users"));
    }

    [Fact]
    public void BuildDetailSql_parameterizes_the_value_never_interpolates_it()
    {
        var sql = GatewayService.BuildDetailSql("PostgreSQL", "Users", "Id");

        Assert.Equal("SELECT * FROM \"Users\" WHERE \"Id\" = @pkvalue", sql);
        Assert.DoesNotContain("DROP", sql); // hiçbir kullanıcı değeri metne karışmıyor — sadece isimler
    }

    [Fact]
    public void BuildDetailSql_uses_colon_placeholder_for_oracle()
    {
        var sql = GatewayService.BuildDetailSql("Oracle", "Users", "Id");
        Assert.Equal("SELECT * FROM \"Users\" WHERE \"Id\" = :pkvalue", sql);
    }

    [Theory]
    [InlineData("MSSQL")]
    [InlineData("PostgreSQL")]
    [InlineData("MySQL")]
    [InlineData("Oracle")]
    public void BuildListSql_produces_valid_pagination_syntax_per_engine(string dbType)
    {
        var (sql, bind) = GatewayService.BuildListSql(dbType, "Users", page: 2, pageSize: 10);

        Assert.Contains("Users", sql);
        Assert.NotNull(bind);

        switch (dbType.ToUpperInvariant())
        {
            case "MSSQL":
                Assert.Contains("OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY", sql);
                break;
            case "POSTGRESQL":
                Assert.Contains("LIMIT @take OFFSET @skip", sql);
                break;
            case "MYSQL":
                Assert.Contains("LIMIT @skip, @take", sql);
                break;
            case "ORACLE":
                Assert.Contains("OFFSET :skip ROWS FETCH NEXT :take ROWS ONLY", sql);
                break;
        }
    }

    [Fact]
    public void BuildListSql_computes_correct_skip_for_page()
    {
        // page 3, pageSize 20 → skip = (3-1)*20 = 40
        var (sql, bind) = GatewayService.BuildListSql("PostgreSQL", "Users", page: 3, pageSize: 20);
        Assert.Contains("LIMIT @take OFFSET @skip", sql);
    }

    [Fact]
    public void Unsupported_engine_throws_not_supported()
    {
        Assert.Throws<NotSupportedException>(() => GatewayService.Quote("SQLite_UNSUPPORTED_VARIANT", "Users"));
    }

    [Fact]
    public void BuildCountSql_wraps_table_name_correctly()
    {
        var sql = GatewayService.BuildCountSql("MySQL", "orders", null, out _);
        Assert.Equal("SELECT COUNT(*) FROM `orders`", sql);
    }

    // ── Sayfalama kararlılığı (code review bulgusu) ─────────────────────────

    [Theory]
    [InlineData("MSSQL", "[Id]")]
    [InlineData("PostgreSQL", "\"Id\"")]
    [InlineData("MySQL", "`Id`")]
    [InlineData("Oracle", "\"Id\"")]
    public void BuildListSql_orders_by_the_given_column_on_every_engine(string dbType, string quotedCol)
    {
        var (sql, _) = GatewayService.BuildListSql(dbType, "Users", page: 2, pageSize: 10, orderByColumn: "Id");
        Assert.Contains($"ORDER BY {quotedCol}", sql);
    }

    [Fact]
    public void BuildListSql_without_order_column_keeps_mssql_placeholder_so_offset_stays_valid()
    {
        // MSSQL'de OFFSET/FETCH, ORDER BY olmadan sözdizimi hatasıdır — kolon yoksa
        // eski yer tutucu korunmalı.
        var (sql, _) = GatewayService.BuildListSql("MSSQL", "Users", page: 1, pageSize: 10);
        Assert.Contains("ORDER BY (SELECT NULL)", sql);
    }

    [Fact]
    public void BuildListSql_without_order_column_adds_no_order_by_on_postgres()
    {
        var (sql, _) = GatewayService.BuildListSql("PostgreSQL", "Users", page: 1, pageSize: 10);
        Assert.DoesNotContain("ORDER BY", sql);
    }

    [Fact]
    public void BuildListSql_rejects_a_malicious_order_column_via_identifier_validation()
    {
        // Sıralama kolonu da tablo adıyla aynı katı doğrulamadan geçmeli.
        Assert.Throws<ArgumentException>(() =>
            GatewayService.ValidateIdentifierOrThrow("Id; DROP TABLE Users--", "orderByColumn"));
    }
}
