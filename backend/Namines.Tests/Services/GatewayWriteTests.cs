using System.Data.Common;
using Namines.Core.Models;
using Namines.Infrastructure.Services;

namespace Namines.Tests.Services;

/// <summary>
/// Faz B/08 — Gateway filtreleme + yazma yolu.
///
/// <see cref="GatewayServiceTests"/> ile aynı sınır: canlı bağlantı yolu burada test
/// EDİLMİYOR, saf SQL-üretim mantığı kanıtlanıyor. Bu, yazma tarafında daha da
/// önemli — üretilen metnin YANLIŞ olması burada veri kaybı demek, ve metnin
/// doğruluğu bağlantı olmadan da kanıtlanabilir.
///
/// İşlem/etkilenen-satır koruması (<c>ExecuteGuardedWriteAsync</c>) gerçek bir
/// bağlantı gerektirdiği için burada değil; onun yerine bu testler o korumanın
/// ÖN KOŞULUNU kilitliyor: koşulsuz bir UPDATE/DELETE üretmenin yolu olmaması.
/// </summary>
public class GatewayWriteTests
{
    // ── Filtreleme ───────────────────────────────────────────────────────────

    [Fact]
    public void No_filter_produces_no_where_clause()
    {
        Assert.Equal(string.Empty, GatewayService.BuildWhere("PostgreSQL", null, out _));
        Assert.Equal(string.Empty, GatewayService.BuildWhere("PostgreSQL", new List<GatewayFilter>(), out _));
    }

    [Theory]
    [InlineData(GatewayOperator.Eq, "=")]
    [InlineData(GatewayOperator.Neq, "<>")]
    [InlineData(GatewayOperator.Gt, ">")]
    [InlineData(GatewayOperator.Gte, ">=")]
    [InlineData(GatewayOperator.Lt, "<")]
    [InlineData(GatewayOperator.Lte, "<=")]
    [InlineData(GatewayOperator.Like, "LIKE")]
    public void Comparison_operators_emit_parameterised_sql(GatewayOperator op, string symbol)
    {
        var filters = new List<GatewayFilter> { new("status", op, new string?[] { "paid" }) };
        var where = GatewayService.BuildWhere("PostgreSQL", filters, out _);

        Assert.Equal($" WHERE \"status\" {symbol} @f0", where);
        // Değerin SQL metnine sızmaması sözleşmenin kendisi.
        Assert.DoesNotContain("paid", where);
    }

    [Fact]
    public void Null_checks_bind_no_parameter()
    {
        var filters = new List<GatewayFilter>
        {
            new("deleted_at", GatewayOperator.IsNull, Array.Empty<string?>()),
            new("email", GatewayOperator.IsNotNull, Array.Empty<string?>()),
        };

        var where = GatewayService.BuildWhere("PostgreSQL", filters, out _);
        Assert.Equal(" WHERE \"deleted_at\" IS NULL AND \"email\" IS NOT NULL", where);
    }

    [Fact]
    public void In_expands_to_one_parameter_per_value()
    {
        var filters = new List<GatewayFilter>
        {
            new("status", GatewayOperator.In, new string?[] { "paid", "shipped", "refunded" }),
        };

        var where = GatewayService.BuildWhere("MySQL", filters, out _);
        Assert.Equal(" WHERE `status` IN (@f0, @f1, @f2)", where);
    }

    [Fact]
    public void Empty_in_list_is_rejected_instead_of_producing_invalid_sql()
    {
        // Boş IN listesi motorlarda sözdizimi hatasıdır; ham SQL hatası yerine
        // anlaşılır bir doğrulama hatası verilmeli.
        var filters = new List<GatewayFilter> { new("status", GatewayOperator.In, Array.Empty<string?>()) };
        Assert.Throws<ArgumentException>(() => GatewayService.BuildWhere("PostgreSQL", filters, out _));
    }

    [Fact]
    public void Filter_column_names_go_through_identifier_validation()
    {
        // Filtre kolonu, kullanıcı girdisinin SQL metnine EN YAKIN olduğu yer.
        var filters = new List<GatewayFilter>
        {
            new("id; DROP TABLE users--", GatewayOperator.Eq, new string?[] { "1" }),
        };
        Assert.Throws<ArgumentException>(() => GatewayService.BuildWhere("PostgreSQL", filters, out _));
    }

    [Fact]
    public void Oracle_filters_use_colon_placeholders()
    {
        var filters = new List<GatewayFilter> { new("id", GatewayOperator.Eq, new string?[] { "1" }) };
        var where = GatewayService.BuildWhere("Oracle", filters, out _);
        Assert.Equal(" WHERE \"id\" = :f0", where);
    }

    [Fact]
    public void Count_applies_the_same_filters_as_the_list()
    {
        // Aksi hâlde sayfalama çubuğu, gösterilen filtrelenmiş listeyle çelişen
        // bir toplam gösterir — kullanıcı için sessiz ve kafa karıştırıcı bir yanlış.
        var filters = new List<GatewayFilter> { new("status", GatewayOperator.Eq, new string?[] { "paid" }) };
        var sql = GatewayService.BuildCountSql("PostgreSQL", "orders", filters, out _);

        Assert.Equal("SELECT COUNT(*) FROM \"orders\" WHERE \"status\" = @f0", sql);
    }

    [Fact]
    public void List_sql_carries_filters_and_sort_direction()
    {
        var filters = new List<GatewayFilter> { new("status", GatewayOperator.Eq, new string?[] { "paid" }) };
        var (sql, _) = GatewayService.BuildListSql(
            "PostgreSQL", "orders", page: 2, pageSize: 25, orderByColumn: "id",
            sortDirection: GatewaySortDirection.Desc, filters: filters);

        Assert.Equal(
            "SELECT * FROM \"orders\" WHERE \"status\" = @f0 ORDER BY \"id\" DESC LIMIT @take OFFSET @skip",
            sql);
    }

    [Fact]
    public void Sort_direction_comes_from_an_enum_so_it_cannot_carry_injected_text()
    {
        var (asc, _) = GatewayService.BuildListSql("PostgreSQL", "t", 1, 10, "id", GatewaySortDirection.Asc);
        var (desc, _) = GatewayService.BuildListSql("PostgreSQL", "t", 1, 10, "id", GatewaySortDirection.Desc);

        Assert.Contains("ORDER BY \"id\" ASC", asc);
        Assert.Contains("ORDER BY \"id\" DESC", desc);
    }

    // ── Yazma SQL'i ──────────────────────────────────────────────────────────

    [Fact]
    public void Insert_quotes_columns_and_parameterises_every_value()
    {
        var sql = GatewayService.BuildInsertSql("MySQL", "orders", new[] { "status", "total" });
        Assert.Equal("INSERT INTO `orders` (`status`, `total`) VALUES (@v_status, @v_total)", sql);
    }

    [Fact]
    public void Postgres_insert_returns_the_created_row()
    {
        var sql = GatewayService.BuildInsertSql("PostgreSQL", "orders", new[] { "status" });
        Assert.EndsWith("RETURNING *", sql);
    }

    [Fact]
    public void Sqlserver_insert_does_not_use_output_inserted()
    {
        // OUTPUT INSERTED.*, hedef tabloda trigger varsa Msg 334 ile PATLAR — yazma
        // tamamen çalışmaz hâle gelirdi. Satırı geri okuyamamak, yazmayı kırmaktan iyi.
        var sql = GatewayService.BuildInsertSql("MSSQL", "orders", new[] { "status" });
        Assert.DoesNotContain("OUTPUT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RETURNING", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Update_always_carries_a_key_predicate()
    {
        // Koşulsuz bir UPDATE tüm tabloyu ezer. Üretilen metinde WHERE'in her zaman
        // bulunması, bunun "unutulabilir bir seçenek" olmadığının kanıtı.
        var sql = GatewayService.BuildUpdateSql("PostgreSQL", "orders", "id", new[] { "status" });
        Assert.Equal("UPDATE \"orders\" SET \"status\" = @v_status WHERE \"id\" = @pkvalue", sql);
    }

    [Fact]
    public void Delete_always_carries_a_key_predicate()
    {
        var sql = GatewayService.BuildDeleteSql("MSSQL", "orders", "id");
        Assert.Equal("DELETE FROM [orders] WHERE [id] = @pkvalue", sql);
    }

    [Theory]
    [InlineData("PostgreSQL")]
    [InlineData("MySQL")]
    [InlineData("MSSQL")]
    [InlineData("Oracle")]
    [InlineData("SQLite")]
    public void No_engine_can_produce_an_unfiltered_update_or_delete(string engine)
    {
        Assert.Contains("WHERE", GatewayService.BuildUpdateSql(engine, "t", "id", new[] { "c" }));
        Assert.Contains("WHERE", GatewayService.BuildDeleteSql(engine, "t", "id"));
    }

    [Fact]
    public void Oracle_writes_use_colon_placeholders()
    {
        Assert.Contains(":pkvalue", GatewayService.BuildDeleteSql("Oracle", "orders", "id"));
        Assert.Contains(":v_status", GatewayService.BuildInsertSql("Oracle", "orders", new[] { "status" }));
    }

    [Fact]
    public async Task Malicious_column_names_never_reach_the_write_sql()
    {
        var service = new GatewayService(new AllowEverythingHostPolicy());
        var values = new Dictionary<string, string?> { ["id; DROP TABLE users--"] = "1" };

        // Bağlantı açılmadan ÖNCE patlamalı: doğrulama ilk adım.
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync("Host=example.com;Database=x", "PostgreSQL", "orders", values));
    }

    private sealed class AllowEverythingHostPolicy : Core.Security.IDbHostAccessPolicy
    {
        public bool IsHostAllowed(string? host, out string denyReason)
        {
            denyReason = string.Empty;
            return true;
        }
    }

    // ── Sorgu dili: projeksiyon ve OR grupları (08 §2.1) ─────────────────────

    [Fact]
    public void No_select_list_returns_every_column()
    {
        Assert.Equal("*", GatewayService.BuildProjection("PostgreSQL", null));
        Assert.Equal("*", GatewayService.BuildProjection("PostgreSQL", Array.Empty<string>()));
    }

    [Fact]
    public void Select_quotes_each_column()
    {
        var projection = GatewayService.BuildProjection("MySQL", new[] { "id", "total" });
        Assert.Equal("`id`, `total`", projection);
    }

    [Fact]
    public void Select_columns_go_through_identifier_validation()
    {
        Assert.Throws<ArgumentException>(() =>
            GatewayService.BuildProjection("PostgreSQL", new[] { "id, (SELECT password FROM users)" }));
    }

    [Fact]
    public void An_or_group_is_parenthesised()
    {
        // Parantezsiz yazılırsa AND'in önceliği yüzünden anlam SESSİZCE değişir ve
        // filtre beklenenden fazla satır döndürür — sessiz yanlış sonuç.
        var filters = new List<GatewayFilter> { new("tenant_id", GatewayOperator.Eq, new string?[] { "7" }) };
        var groups = new List<GatewayFilterGroup>
        {
            new(new List<GatewayFilter>
            {
                new("status", GatewayOperator.Eq, new string?[] { "paid" }),
                new("total", GatewayOperator.Gte, new string?[] { "1000" }),
            }),
        };

        var where = GatewayService.BuildWhere("PostgreSQL", filters, groups, out _);

        Assert.Equal(
            " WHERE \"tenant_id\" = @f0 AND (\"status\" = @f1 OR \"total\" >= @f2)",
            where);
    }

    [Fact]
    public void Or_group_values_are_still_parameterised()
    {
        var groups = new List<GatewayFilterGroup>
        {
            new(new List<GatewayFilter> { new("status", GatewayOperator.Eq, new string?[] { "paid'; DROP TABLE t--" }) }),
        };

        var where = GatewayService.BuildWhere("PostgreSQL", null, groups, out _);
        Assert.DoesNotContain("DROP", where);
    }

    [Fact]
    public void An_empty_or_group_is_ignored_rather_than_producing_broken_sql()
    {
        var groups = new List<GatewayFilterGroup> { new(Array.Empty<GatewayFilter>()) };
        Assert.Equal(string.Empty, GatewayService.BuildWhere("PostgreSQL", null, groups, out _));
    }

    [Fact]
    public void Count_applies_or_groups_too()
    {
        // Liste ile COUNT ayrışırsa sayfalama çubuğu listeyle çelişir.
        var groups = new List<GatewayFilterGroup>
        {
            new(new List<GatewayFilter>
            {
                new("status", GatewayOperator.Eq, new string?[] { "paid" }),
                new("status", GatewayOperator.Eq, new string?[] { "shipped" }),
            }),
        };

        var sql = GatewayService.BuildCountSql("PostgreSQL", "orders", null, groups, out _);
        Assert.Contains("OR", sql);
        Assert.StartsWith("SELECT COUNT(*)", sql);
    }

    [Fact]
    public void List_sql_honours_the_select_list()
    {
        var (sql, _) = GatewayService.BuildListSql(
            "PostgreSQL", "orders", 1, 25, "id", GatewaySortDirection.Asc,
            filters: null, orGroups: null, selectColumns: new[] { "id", "status" });

        Assert.StartsWith("SELECT \"id\", \"status\" FROM", sql);
    }
}
