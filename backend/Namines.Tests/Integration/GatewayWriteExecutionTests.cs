using Namines.Core.Models;
using Namines.Core.Security;
using Namines.Infrastructure.Services;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Namines.Tests.Integration;

/// <summary>
/// Gateway yazma yolu — GERÇEK PostgreSQL'e karşı (Faz B/08).
///
/// <see cref="Namines.Tests.Services.GatewayWriteTests"/> üretilen SQL METNİNİ
/// doğruluyor. Buradaki testler asıl güvenlik mekanizmasını doğruluyor ve o
/// mekanizma metinde GÖRÜNMÜYOR: her yazma bir işlem içinde çalışır, etkilenen
/// satır sayısı doğrulanır, 1'den fazlaysa GERİ ALINIR.
///
/// Bu ayrımın bedeli somut: "birincil anahtar" diye verilen kolon aslında benzersiz
/// değilse, tek bir istek sessizce onlarca satırı değiştirir. Metin testi bunu
/// göremez — üretilen UPDATE mükemmel görünür. Yalnızca gerçek bir motorda,
/// gerçekten yinelenen satırlarla çalıştırınca ortaya çıkar.
///
/// Docker gerekir; yoksa atlanır (bkz. RequiresDockerFact).
/// </summary>
[Collection("Docker")]
public class GatewayWriteExecutionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private string ConnectionString => _container.GetConnectionString();

    // SsrfGuard container'ın özel IP'sini reddeder; bu test o politikayı DEĞİL,
    // yazma mantığını sınıyor. Ortam kararını burada açıkça veriyoruz.
    private static GatewayService Service() => new(new AllowAllHosts());

    private sealed class AllowAllHosts : IDbHostAccessPolicy
    {
        public bool IsHostAllowed(string? host, out string denyReason)
        {
            denyReason = string.Empty;
            return true;
        }
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task SeedOrdersAsync()
    {
        await ExecuteAsync("DROP TABLE IF EXISTS orders");
        await ExecuteAsync(@"CREATE TABLE orders (
            id SERIAL PRIMARY KEY,
            status VARCHAR(32) NOT NULL,
            note VARCHAR(64) NULL)");
        await ExecuteAsync(
            "INSERT INTO orders (status, note) VALUES ('paid','a'),('shipped','b'),('paid','c')");
    }

    // ── Temel CRUD ───────────────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task Create_inserts_the_row_and_returns_it()
    {
        await SeedOrdersAsync();

        var result = await Service().CreateAsync(
            ConnectionString, "PostgreSQL", "orders",
            new Dictionary<string, string?> { ["status"] = "pending", ["note"] = "created" });

        Assert.Equal(1, result.AffectedRows);
        // PostgreSQL RETURNING destekliyor — satır geri gelmeli.
        Assert.NotNull(result.Row);
        Assert.Equal("pending", result.Row!.Values["status"]?.ToString());
        Assert.Equal(4, await ScalarAsync("SELECT COUNT(*) FROM orders"));
    }

    [RequiresDockerFact]
    public async Task Create_can_write_a_real_null()
    {
        // AddParameter null'ı DBNull'a çevirmezse sürücü parametreyi düşürür ya da
        // patlar; ikisi de "bu kolona NULL yaz" niyetini sessizce başka şeye çevirir.
        await SeedOrdersAsync();

        var result = await Service().CreateAsync(
            ConnectionString, "PostgreSQL", "orders",
            new Dictionary<string, string?> { ["status"] = "pending", ["note"] = null });

        Assert.Equal(1, result.AffectedRows);
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM orders WHERE note IS NULL"));
    }

    [RequiresDockerFact]
    public async Task Update_changes_only_the_targeted_row()
    {
        await SeedOrdersAsync();

        var result = await Service().UpdateAsync(
            ConnectionString, "PostgreSQL", "orders", "id", "1",
            new Dictionary<string, string?> { ["status"] = "refunded" });

        Assert.Equal(1, result.AffectedRows);
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM orders WHERE status = 'refunded'"));
        Assert.Equal(3, await ScalarAsync("SELECT COUNT(*) FROM orders"));
    }

    [RequiresDockerFact]
    public async Task Delete_removes_only_the_targeted_row()
    {
        await SeedOrdersAsync();

        var result = await Service().DeleteAsync(ConnectionString, "PostgreSQL", "orders", "id", "2");

        Assert.Equal(1, result.AffectedRows);
        Assert.Equal(2, await ScalarAsync("SELECT COUNT(*) FROM orders"));
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM orders WHERE id = 2"));
    }

    [RequiresDockerFact]
    public async Task Missing_row_affects_nothing_and_is_not_an_error()
    {
        await SeedOrdersAsync();

        var update = await Service().UpdateAsync(
            ConnectionString, "PostgreSQL", "orders", "id", "9999",
            new Dictionary<string, string?> { ["status"] = "x" });
        var delete = await Service().DeleteAsync(ConnectionString, "PostgreSQL", "orders", "id", "9999");

        // 0 satır bir hata değil, "kayıt yok" — çağıran bunu 404'e çevirir.
        Assert.Equal(0, update.AffectedRows);
        Assert.Equal(0, delete.AffectedRows);
        Assert.Equal(3, await ScalarAsync("SELECT COUNT(*) FROM orders"));
    }

    // ── ASIL KORUMA: benzersiz olmayan anahtar ───────────────────────────────

    private async Task SeedDuplicateKeysAsync()
    {
        // "id" birincil anahtar DEĞİL ve yinelenen değerler taşıyor. Çağıran onu
        // anahtar sanıyor — gerçek hayatta bu, yanlış kolon adı ya da eski bir
        // şema varsayımıyla kolayca olur.
        await ExecuteAsync("DROP TABLE IF EXISTS legacy_rows");
        await ExecuteAsync("CREATE TABLE legacy_rows (id INT NOT NULL, status VARCHAR(32) NOT NULL)");
        await ExecuteAsync("INSERT INTO legacy_rows (id, status) VALUES (7,'a'),(7,'b'),(7,'c')");
    }

    [RequiresDockerFact]
    public async Task Update_that_would_hit_many_rows_is_rolled_back()
    {
        await SeedDuplicateKeysAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().UpdateAsync(
                ConnectionString, "PostgreSQL", "legacy_rows", "id", "7",
                new Dictionary<string, string?> { ["status"] = "OVERWRITTEN" }));

        Assert.Contains("Refusing to modify", ex.Message);

        // Kanıt: HİÇBİR satır değişmemiş olmalı. Koruma yalnızca istisna fırlatıp
        // işlemi commit etseydi, 3 satır sessizce ezilmiş olurdu.
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM legacy_rows WHERE status = 'OVERWRITTEN'"));
        Assert.Equal(3, await ScalarAsync("SELECT COUNT(*) FROM legacy_rows"));
    }

    [RequiresDockerFact]
    public async Task Delete_that_would_hit_many_rows_is_rolled_back()
    {
        await SeedDuplicateKeysAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().DeleteAsync(ConnectionString, "PostgreSQL", "legacy_rows", "id", "7"));

        // Üç satır da yerinde durmalı. Bu testin varlık sebebi tam olarak budur:
        // korumasız hâlde tek bir istek üçünü birden silerdi.
        Assert.Equal(3, await ScalarAsync("SELECT COUNT(*) FROM legacy_rows"));
    }

    // ── Filtreleme (gerçek motorda) ──────────────────────────────────────────

    [RequiresDockerFact]
    public async Task Filters_narrow_both_the_rows_and_the_total_count()
    {
        await SeedOrdersAsync();

        var filters = new List<GatewayFilter>
        {
            new("status", GatewayOperator.Eq, new string?[] { "paid" }),
        };

        var result = await Service().ListAsync(
            ConnectionString, "PostgreSQL", "orders", page: 1, pageSize: 25,
            orderByColumn: "id", includeTotalCount: true,
            sortDirection: GatewaySortDirection.Asc, filters: filters);

        Assert.Equal(2, result.Rows.Count);
        // COUNT filtrelenmezse sayfalama çubuğu 3 gösterip listeyle çelişirdi.
        Assert.Equal(2, result.TotalCount);
    }

    [RequiresDockerFact]
    public async Task In_and_null_filters_work_against_a_real_engine()
    {
        await SeedOrdersAsync();
        await ExecuteAsync("INSERT INTO orders (status, note) VALUES ('cancelled', NULL)");

        var inFilter = new List<GatewayFilter>
        {
            new("status", GatewayOperator.In, new string?[] { "paid", "cancelled" }),
        };
        var inResult = await Service().ListAsync(
            ConnectionString, "PostgreSQL", "orders", 1, 25, "id", true,
            GatewaySortDirection.Asc, inFilter);
        Assert.Equal(3, inResult.Rows.Count);

        var nullFilter = new List<GatewayFilter>
        {
            new("note", GatewayOperator.IsNull, Array.Empty<string?>()),
        };
        var nullResult = await Service().ListAsync(
            ConnectionString, "PostgreSQL", "orders", 1, 25, "id", true,
            GatewaySortDirection.Asc, nullFilter);
        Assert.Single(nullResult.Rows);
    }

    [RequiresDockerFact]
    public async Task Descending_sort_reverses_the_order()
    {
        await SeedOrdersAsync();

        var result = await Service().ListAsync(
            ConnectionString, "PostgreSQL", "orders", 1, 25, "id", false,
            GatewaySortDirection.Desc);

        var ids = result.Rows.Select(r => Convert.ToInt32(r.Values["id"])).ToList();
        Assert.Equal(new[] { 3, 2, 1 }, ids);
    }

    [RequiresDockerFact]
    public async Task A_filter_value_containing_sql_is_treated_as_data_not_code()
    {
        await SeedOrdersAsync();

        var filters = new List<GatewayFilter>
        {
            new("status", GatewayOperator.Eq, new string?[] { "paid'; DROP TABLE orders--" }),
        };

        var result = await Service().ListAsync(
            ConnectionString, "PostgreSQL", "orders", 1, 25, "id", true,
            GatewaySortDirection.Asc, filters);

        // Eşleşme yok (böyle bir status yok) ve tablo hâlâ duruyor.
        Assert.Empty(result.Rows);
        Assert.Equal(3, await ScalarAsync("SELECT COUNT(*) FROM orders"));
    }

    [RequiresDockerFact]
    public async Task Detail_works_on_an_integer_primary_key()
    {
        // ÖNCEDEN VAR OLAN HATANIN regresyon kilidi. Gateway'in değerleri HTTP'den
        // string gelir; Npgsql bunları `text` bildirince Postgres "operator does not
        // exist: integer = text" ile REDDEDİYORDU — yani tamsayı PK'lı bir tabloda
        // detay ucu hiç çalışmıyordu. Metin testleri bunu göremezdi, üretilen SQL
        // kusursuz görünüyor.
        await SeedOrdersAsync();

        var row = await Service().DetailAsync(ConnectionString, "PostgreSQL", "orders", "id", "2");

        Assert.NotNull(row);
        Assert.Equal("shipped", row!.Values["status"]?.ToString());
    }

    [RequiresDockerFact]
    public async Task Or_groups_narrow_correctly_against_a_real_engine()
    {
        await SeedOrdersAsync();
        await ExecuteAsync("INSERT INTO orders (status, note) VALUES ('cancelled','d')");

        // (status = paid OR status = cancelled) — 2 paid + 1 cancelled = 3
        var groups = new List<GatewayFilterGroup>
        {
            new(new List<GatewayFilter>
            {
                new("status", GatewayOperator.Eq, new string?[] { "paid" }),
                new("status", GatewayOperator.Eq, new string?[] { "cancelled" }),
            }),
        };

        var result = await Service().ListAsync(
            ConnectionString, "PostgreSQL", "orders", 1, 25, "id", true,
            GatewaySortDirection.Asc, filters: null, orGroups: groups);

        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(3, result.TotalCount);
    }

    [RequiresDockerFact]
    public async Task An_and_filter_combined_with_an_or_group_keeps_its_meaning()
    {
        // Parantezleme hatası tam burada görünür: parantezsiz yazılsaydı
        // "note='a' AND status='paid' OR status='shipped'" olur ve shipped satırı
        // note filtresini atlayarak sonuca girerdi.
        await SeedOrdersAsync();

        var filters = new List<GatewayFilter> { new("note", GatewayOperator.Eq, new string?[] { "a" }) };
        var groups = new List<GatewayFilterGroup>
        {
            new(new List<GatewayFilter>
            {
                new("status", GatewayOperator.Eq, new string?[] { "paid" }),
                new("status", GatewayOperator.Eq, new string?[] { "shipped" }),
            }),
        };

        var result = await Service().ListAsync(
            ConnectionString, "PostgreSQL", "orders", 1, 25, "id", true,
            GatewaySortDirection.Asc, filters, groups);

        Assert.Single(result.Rows);
        Assert.Equal("a", result.Rows[0].Values["note"]?.ToString());
    }

    [RequiresDockerFact]
    public async Task Select_returns_only_the_requested_columns()
    {
        await SeedOrdersAsync();

        var result = await Service().ListAsync(
            ConnectionString, "PostgreSQL", "orders", 1, 25, "id", false,
            GatewaySortDirection.Asc, filters: null, orGroups: null,
            selectColumns: new[] { "id", "status" });

        Assert.NotEmpty(result.Rows);
        // "note" hiç okunmamalı: istemcinin istemediği bir kolonu döndürmek onu
        // istemeden loglara/önbelleğe taşıyabilir.
        Assert.False(result.Rows[0].Values.ContainsKey("note"));
        Assert.True(result.Rows[0].Values.ContainsKey("status"));
    }
}
