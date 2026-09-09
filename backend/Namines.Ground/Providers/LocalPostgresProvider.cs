using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Namines.Ground.Abstractions;
using Npgsql;

namespace Namines.Ground.Providers;

/// <summary>
/// Operatörün <b>zaten işlettiği</b> bir PostgreSQL sunucusunda proje başına
/// veritabanı ve ayrı kullanıcı açar.
///
/// <b>Bu, "kendi cluster'ımızı işletmek" DEĞİL</b> (<c>00-GENEL-BAKIS.md</c>
/// §2'nin reddettiği şey). Aradaki fark kritik: §2, *müşteriler için* 7/24
/// nöbetli bir veritabanı filosu işletmeyi reddediyor. Burada sunucu
/// operatörün kendisinin, sorumluluk da onun — Namines kimseye SLA vermiyor
/// ve <see cref="Capabilities"/> bunu arayüze açıkça yazıyor.
///
/// <b>Neden v1'de var:</b> Ground'un uçtan uca kanıtlanabilmesi için
/// (<c>02-V1-KARARLARI.md</c> §1). Neon anahtarı olmadan hiçbir kabul kriteri
/// karşılanamazdı; bu sağlayıcı gerçek bir veritabanı açıyor ve bağımsız bir
/// <c>psql</c> oturumuyla doğrulanabiliyor.
/// </summary>
public sealed class LocalPostgresProvider : IDatabaseProvider
{
    /// <summary>
    /// Üretilen parolanın bayt uzunluğu. 32 bayt = 256 bit; base64'e
    /// çevrildiğinde ~43 karakter.
    /// </summary>
    private const int PasswordBytes = 32;

    /// <summary>
    /// PostgreSQL tanımlayıcı sınırı 63 bayt. Ad + önek bunu aşarsa sunucu
    /// SESSİZCE kırpar — iki farklı proje aynı veritabanına düşebilirdi.
    /// </summary>
    private const int MaxIdentifierLength = 63;

    private readonly IConfiguration _configuration;
    private readonly ILogger<LocalPostgresProvider> _logger;

    public LocalPostgresProvider(IConfiguration configuration, ILogger<LocalPostgresProvider> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public string Name => "LocalPostgres";

    public ProviderCapabilities Capabilities => new(
        SupportsBranching: false,
        SupportsRegionChoice: false,
        IsLiveVerified: true,
        ResponsibilityNote:
            "Veritabanı sizin kendi PostgreSQL sunucunuzda açılır. Yedekleme, " +
            "erişilebilirlik ve kapasite sizin sorumluluğunuzdadır — Namines bu " +
            "sunucu için hizmet seviyesi taahhüdü vermez.");

    /// <summary>
    /// Yönetici bağlantısı — <c>CREATE DATABASE</c>/<c>CREATE ROLE</c> yetkisi olan.
    ///
    /// Ayrı bir anahtar (<c>Ground:LocalPostgres:AdminConnectionString</c>):
    /// control DB'nin bağlantısını doğrudan kullanmak, kiracı veritabanlarını
    /// Namines'in kendi verisiyle aynı yetki alanına sokardı ve operatörün
    /// ikisini ayırma imkânını elinden alırdı.
    /// </summary>
    private string? AdminConnectionString =>
        _configuration["Ground:LocalPostgres:AdminConnectionString"];

    /// <summary>
    /// Kullanıcıya verilecek bağlantıdaki host/port.
    ///
    /// Yönetici bağlantısındakinden FARKLI olabilir: sunucu bize
    /// <c>localhost</c> ya da bir konteyner adıyla görünürken kullanıcının
    /// uygulamasına genel bir adla görünmesi gerekir. Tanımsızsa yönetici
    /// bağlantısındaki adres kullanılır.
    /// </summary>
    private string? PublicHost => _configuration["Ground:LocalPostgres:PublicHost"];

    public Task<string?> ProbeAsync(CancellationToken ct) =>
        string.IsNullOrWhiteSpace(AdminConnectionString)
            ? Task.FromResult<string?>(
                "Ground:LocalPostgres:AdminConnectionString tanımlı değil, bu yüzden " +
                "yönetilen veritabanı açılamaz. CREATE DATABASE ve CREATE ROLE yetkisi " +
                "olan bir PostgreSQL bağlantısı verin.")
            : PingAsync(ct);

    private async Task<string?> PingAsync(CancellationToken ct)
    {
        try
        {
            await using var connection = new NpgsqlConnection(AdminConnectionString);
            await connection.OpenAsync(ct);
            return null;
        }
        catch (Exception ex)
        {
            return $"Yönetilen veritabanı sunucusuna bağlanılamadı: {Shorten(ex.Message)}";
        }
    }

    public async Task<ProvisionedDatabase> CreateAsync(ProvisionSpec spec, CancellationToken ct)
    {
        var admin = AdminConnectionString
            ?? throw new InvalidOperationException(
                "Ground:LocalPostgres:AdminConnectionString tanımlı değil.");

        var databaseName = BuildIdentifier("namines_db_", spec.ProjectId);
        var roleName = BuildIdentifier("namines_app_", spec.ProjectId);
        var password = GeneratePassword();

        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync(ct);

        // Rol ÖNCE: veritabanının sahibi olarak atanacak. Ters sırada
        // veritabanı sahipsiz kalır ve sahiplik sonradan değiştirilmek zorunda.
        var roleCreated = false;
        var databaseCreated = false;

        try
        {
            roleCreated = await EnsureRoleAsync(connection, roleName, password, ct);
            databaseCreated = await EnsureDatabaseAsync(connection, databaseName, roleName, ct);
            await HardenAsync(admin, databaseName, roleName, ct);
        }
        catch
        {
            // Yarım kalmış kaynak bırakma: aksi hâlde kimsenin bilmediği ama
            // yer kaplayan bir veritabanı/rol kalırdı. Yalnızca BU çağrıda
            // oluşturulanlar siliniyor — zaten var olana dokunulmuyor.
            await CleanupAsync(admin, databaseCreated ? databaseName : null, roleCreated ? roleName : null);
            throw;
        }

        _logger.LogInformation(
            "Ground: {Database} veritabanı ve {Role} rolü hazır ({ProjectId}).",
            databaseName, roleName, spec.ProjectId);

        return new ProvisionedDatabase(
            ProviderProjectId: databaseName,
            ProviderBranchId: roleName,
            Region: "local",
            ConnectionString: BuildTenantConnectionString(admin, databaseName, roleName, password));
    }

    /// <summary>Rolü oluşturur ya da parolasını tazeler. Yeni oluşturulduysa true.</summary>
    private static async Task<bool> EnsureRoleAsync(
        NpgsqlConnection connection, string roleName, string password, CancellationToken ct)
    {
        await using var exists = new NpgsqlCommand(
            "SELECT 1 FROM pg_roles WHERE rolname = @name", connection);
        exists.Parameters.AddWithValue("name", roleName);
        var found = await exists.ExecuteScalarAsync(ct) is not null;

        // CREATE/ALTER ROLE parametre kabul etmiyor. Ad KOD İÇİNDE üretiliyor
        // (BuildIdentifier yalnızca [a-z0-9_] bırakıyor), parola ise tırnak
        // kaçışıyla gömülüyor — ikisi de kullanıcı metninden gelmiyor.
        var sql = found
            ? $"ALTER ROLE {Quote(roleName)} WITH LOGIN PASSWORD {Literal(password)}"
            // NOSUPERUSER/NOCREATEDB/NOCREATEROLE açıkça yazılıyor: kiracının
            // rolü yalnızca kendi veritabanını kullanabilmeli (06-DATA-PLANE §3.3).
            : $"CREATE ROLE {Quote(roleName)} WITH LOGIN PASSWORD {Literal(password)} " +
              "NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT";

        await using var apply = new NpgsqlCommand(sql, connection);
        await apply.ExecuteNonQueryAsync(ct);

        return !found;
    }

    /// <summary>Veritabanını oluşturur. Yeni oluşturulduysa true.</summary>
    private static async Task<bool> EnsureDatabaseAsync(
        NpgsqlConnection connection, string databaseName, string roleName, CancellationToken ct)
    {
        await using var exists = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = @name", connection);
        exists.Parameters.AddWithValue("name", databaseName);
        if (await exists.ExecuteScalarAsync(ct) is not null) return false;

        await using var create = new NpgsqlCommand(
            $"CREATE DATABASE {Quote(databaseName)} OWNER {Quote(roleName)}", connection);
        await create.ExecuteNonQueryAsync(ct);
        return true;
    }

    /// <summary>
    /// Yeni veritabanını kiracıya kapatır.
    ///
    /// <b>PUBLIC'ten CONNECT yetkisi alınıyor</b> ve bu şart: PostgreSQL'de
    /// varsayılan olarak HER rol her veritabanına bağlanabilir. Bu satır
    /// olmadan bir kiracının rolü, diğer kiracının veritabanına bağlanabilirdi —
    /// izolasyon vaadi kağıt üstünde kalırdı.
    ///
    /// <b>public şemasındaki CREATE de alınıyor:</b> PostgreSQL 15 öncesinde
    /// her rol public şemaya tablo yazabiliyordu.
    /// </summary>
    private static async Task HardenAsync(
        string admin, string databaseName, string roleName, CancellationToken ct)
    {
        // Yetkiler veritabanının KENDİSİNE bağlıyken verilmeli; bakım
        // veritabanından şema düzeyi yetkiler görülemez.
        var target = new NpgsqlConnectionStringBuilder(admin) { Database = databaseName }.ConnectionString;

        await using var connection = new NpgsqlConnection(target);
        await connection.OpenAsync(ct);

        var sql =
            $"REVOKE ALL ON DATABASE {Quote(databaseName)} FROM PUBLIC; " +
            $"GRANT CONNECT, TEMPORARY ON DATABASE {Quote(databaseName)} TO {Quote(roleName)}; " +
            "REVOKE ALL ON SCHEMA public FROM PUBLIC; " +
            $"GRANT ALL ON SCHEMA public TO {Quote(roleName)};";

        await using var apply = new NpgsqlCommand(sql, connection);
        await apply.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(ProvisionedDatabase database, CancellationToken ct)
    {
        var admin = AdminConnectionString
            ?? throw new InvalidOperationException(
                "Ground:LocalPostgres:AdminConnectionString tanımlı değil.");

        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync(ct);

        // WITH (FORCE): açık oturumlar silmeyi engeller. Silme arka planda ve
        // gecikmeli çalışıyor; bir kullanıcının unuttuğu bağlantı yüzünden
        // süresiz askıda kalması, kaynağın süresiz faturalanması demek olurdu.
        await using (var drop = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS {Quote(database.ProviderProjectId)} WITH (FORCE)", connection))
        {
            await drop.ExecuteNonQueryAsync(ct);
        }

        if (string.IsNullOrWhiteSpace(database.ProviderBranchId)) return;

        // Rol veritabanından SONRA: sahibi olduğu veritabanı dururken rol silinemez.
        await using var dropRole = new NpgsqlCommand(
            $"DROP ROLE IF EXISTS {Quote(database.ProviderBranchId)}", connection);
        await dropRole.ExecuteNonQueryAsync(ct);
    }

    public async Task<DatabaseMetrics> GetMetricsAsync(ProvisionedDatabase database, CancellationToken ct)
    {
        var admin = AdminConnectionString;
        if (string.IsNullOrWhiteSpace(admin)) return new DatabaseMetrics(null, null);

        try
        {
            await using var connection = new NpgsqlConnection(admin);
            await connection.OpenAsync(ct);

            await using var query = new NpgsqlCommand(
                """
                SELECT pg_database_size(@name),
                       (SELECT count(*) FROM pg_stat_activity WHERE datname = @name)
                """, connection);
            query.Parameters.AddWithValue("name", database.ProviderProjectId);

            await using var reader = await query.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return new DatabaseMetrics(null, null);

            return new DatabaseMetrics(reader.GetInt64(0), reader.GetInt32(1));
        }
        catch (Exception ex)
        {
            // Ölçüm alınamaması provizyonu geçersiz kılmaz; null "bilinmiyor"
            // demek ve arayüz bunu sıfırdan ayırt ediyor.
            _logger.LogWarning(ex, "Ground: {Database} ölçümleri okunamadı.", database.ProviderProjectId);
            return new DatabaseMetrics(null, null);
        }
    }

    private async Task CleanupAsync(string admin, string? databaseName, string? roleName)
    {
        try
        {
            await using var connection = new NpgsqlConnection(admin);
            await connection.OpenAsync(CancellationToken.None);

            if (databaseName is not null)
            {
                await using var drop = new NpgsqlCommand(
                    $"DROP DATABASE IF EXISTS {Quote(databaseName)} WITH (FORCE)", connection);
                await drop.ExecuteNonQueryAsync(CancellationToken.None);
            }

            if (roleName is not null)
            {
                await using var dropRole = new NpgsqlCommand(
                    $"DROP ROLE IF EXISTS {Quote(roleName)}", connection);
                await dropRole.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            // Temizlik hatası ASIL hatayı bastırmamalı; kalan kaynak loglanıyor.
            _logger.LogError(ex,
                "Ground: yarim kalan kaynak temizlenemedi (db={Database}, role={Role}).",
                databaseName, roleName);
        }
    }

    private string BuildTenantConnectionString(
        string admin, string databaseName, string roleName, string password)
    {
        var builder = new NpgsqlConnectionStringBuilder(admin)
        {
            Database = databaseName,
            Username = roleName,
            Password = password,
        };

        if (!string.IsNullOrWhiteSpace(PublicHost)) builder.Host = PublicHost;

        return builder.ConnectionString;
    }

    /// <summary>
    /// Proje kimliğinden geçerli ve ÇAKIŞMAYAN bir PostgreSQL tanımlayıcısı üretir.
    ///
    /// Yalnızca kırpmak yetmez: iki proje kimliği aynı önekle başlıyorsa
    /// kırpılmış adlar çakışır ve <b>iki proje aynı veritabanını paylaşırdı</b>.
    /// Bu yüzden kırpılan ada, tam kimliğin özetinden bir sonek ekleniyor.
    /// </summary>
    internal static string BuildIdentifier(string prefix, string projectId)
    {
        var clean = new StringBuilder(projectId.Length);
        foreach (var c in projectId.ToLowerInvariant())
            clean.Append(char.IsAsciiLetterOrDigit(c) ? c : '_');

        var candidate = prefix + clean;
        if (candidate.Length <= MaxIdentifierLength) return candidate;

        // 8 karakterlik özet: çakışma olasılığını pratikte ortadan kaldırır ve
        // ada bakan birinin projeyi tanımasını sağlayacak yeri de bırakır.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(projectId)))[..8]
            .ToLowerInvariant();

        return string.Concat(candidate.AsSpan(0, MaxIdentifierLength - 9), "_", hash);
    }

    private static string GeneratePassword() =>
        // Base64'teki '+' ve '/' bağlantı dizesinde ve URL'de sorun çıkarabilir;
        // parola gücünü etkilemeden değiştiriliyor.
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(PasswordBytes))
            .Replace('+', 'A').Replace('/', 'B').TrimEnd('=');

    /// <summary>Tanımlayıcıyı çift tırnakla kaçırır.</summary>
    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    /// <summary>Metin sabitini tek tırnakla kaçırır.</summary>
    private static string Literal(string value) => $"'{value.Replace("'", "''")}'";

    private static string Shorten(string message)
    {
        var clean = message.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return clean.Length <= 300 ? clean : clean[..300] + "…";
    }
}
