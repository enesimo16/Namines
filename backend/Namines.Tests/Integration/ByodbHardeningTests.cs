using Namines.Core.Security;
using Namines.Infrastructure.Services;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Namines.Tests.Integration;

/// <summary>
/// BYODB sertleştirme (new-phase/06-DATA-PLANE.md §5) — GERÇEK PostgreSQL'e karşı.
///
/// Salt-okunur oturumun anlamı, "yazma uçlarını çağırmıyoruz" değil: okuma yolundaki
/// bir SQL üretim hatası bile veri YAZAMAMALI. Bunu ancak gerçek bir motora yazmayı
/// deneyip reddedildiğini görerek kanıtlayabiliriz; metin testi bu güvenceyi veremez.
/// </summary>
[Collection("Docker")]
public class ByodbHardeningTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private string ConnectionString => _container.GetConnectionString();

    private async Task SeedAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DROP TABLE IF EXISTS t; CREATE TABLE t (id INT); INSERT INTO t VALUES (1)";
        await cmd.ExecuteNonQueryAsync();
    }

    [RequiresDockerFact]
    public async Task A_read_only_session_actually_refuses_writes()
    {
        await SeedAsync();

        await using var conn = await UserDbConnection.OpenAsync(
            ConnectionString, "PostgreSQL", readOnly: true, CancellationToken.None);

        await using var read = conn.CreateCommand();
        read.CommandText = "SELECT COUNT(*) FROM t";
        Assert.Equal(1, Convert.ToInt64(await read.ExecuteScalarAsync()));

        await using var write = conn.CreateCommand();
        write.CommandText = "INSERT INTO t VALUES (2)";

        // Motorun kendisi reddetmeli — koruma bizim kodumuzun disiplinine değil,
        // veritabanının uyguladığı bir kurala dayanıyor.
        var ex = await Assert.ThrowsAsync<PostgresException>(() => write.ExecuteNonQueryAsync());
        Assert.Contains("read-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [RequiresDockerFact]
    public async Task A_writable_session_is_still_writable()
    {
        // Koruma "her şeyi kilitle"ye dönüşseydi bu test kırılırdı; yazma uçlarının
        // çalışmaya devam etmesi sözleşmenin diğer yarısı.
        await SeedAsync();

        await using var conn = await UserDbConnection.OpenAsync(
            ConnectionString, "PostgreSQL", readOnly: false, CancellationToken.None);

        await using var write = conn.CreateCommand();
        write.CommandText = "INSERT INTO t VALUES (2)";
        Assert.Equal(1, await write.ExecuteNonQueryAsync());
    }

    [RequiresDockerFact]
    public async Task Privileges_report_flags_a_superuser_connection()
    {
        // Test container'ı `postgres` süper kullanıcısıyla bağlanıyor — insanların
        // gerçek hayatta da alışkanlıkla yaptığı şey. Rapor bunu göstermeli.
        var report = await new DbPrivilegeInspector(new AllowAllHosts())
            .InspectAsync(ConnectionString, "PostgreSQL");

        Assert.True(report.IsSuperuser);
        Assert.True(report.CanDropObjects);
        Assert.Equal("postgres", report.Username);
        Assert.Contains(report.Findings, f => f.Severity == "high");
        Assert.NotNull(report.Recommendation);
    }

    [RequiresDockerFact]
    public async Task Privileges_report_recognises_a_read_only_role()
    {
        await SeedAsync();
        await using (var conn = new NpgsqlConnection(ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                DROP ROLE IF EXISTS reader;
                CREATE ROLE reader LOGIN PASSWORD 'reader_pw';
                GRANT CONNECT ON DATABASE postgres TO reader;
                GRANT USAGE ON SCHEMA public TO reader;
                GRANT SELECT ON ALL TABLES IN SCHEMA public TO reader;
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var readerCs = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Username = "reader",
            Password = "reader_pw",
        }.ConnectionString;

        var report = await new DbPrivilegeInspector(new AllowAllHosts())
            .InspectAsync(readerCs, "PostgreSQL");

        Assert.False(report.IsSuperuser);
        Assert.False(report.CanDropObjects);
        Assert.False(report.CanWrite);
        // Salt-okunur bir bağlantıya "daha dar bir rol kullanın" demek uyarıyı
        // gürültüye çevirirdi; öneri yalnızca yapılacak bir şey varken verilir.
        Assert.Null(report.Recommendation);
    }

    [Fact]
    public void Tls_is_required_for_public_hosts_and_not_for_loopback()
    {
        // Kamuya açık bir veritabanına düz metin bağlanmak kimlik bilgisini ağa yayar.
        // Yerel bir container'da TLS beklemek ise özelliği geliştirme makinesinde
        // kullanılamaz kılardı.
        Assert.True(UserDbConnection.ShouldRequireTls("db.example.com"));
        // Çözülemeyen bir ad da TLS ister: bilinmeyeni "özel" saymak, varsayılanı
        // güvensiz tarafa düşürürdü (ilk yazımdaki kusur tam olarak buydu).
        Assert.True(UserDbConnection.ShouldRequireTls("this-host-does-not-resolve.invalid"));
        Assert.False(UserDbConnection.ShouldRequireTls("localhost"));
        Assert.False(UserDbConnection.ShouldRequireTls("127.0.0.1"));
        Assert.False(UserDbConnection.ShouldRequireTls("10.0.0.5"));
    }

    [Fact]
    public void Read_only_sessions_are_only_claimed_where_the_engine_supports_them()
    {
        // SQL Server ve Oracle'da oturum seviyesinde karşılığı yok. "Uygulandı" gibi
        // göstermek, olmayan bir güvenceye güvenmek olurdu.
        Assert.True(UserDbConnection.AppliesReadOnlySession("PostgreSQL"));
        Assert.True(UserDbConnection.AppliesReadOnlySession("MySQL"));
        Assert.False(UserDbConnection.AppliesReadOnlySession("MSSQL"));
        Assert.False(UserDbConnection.AppliesReadOnlySession("Oracle"));
    }

    private sealed class AllowAllHosts : IDbHostAccessPolicy
    {
        public bool IsHostAllowed(string? host, out string denyReason)
        {
            denyReason = string.Empty;
            return true;
        }
    }
}
