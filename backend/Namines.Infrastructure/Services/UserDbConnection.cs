using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Namines.Core.Security;
using Npgsql;
using Oracle.ManagedDataAccess.Client;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Kullanıcının KENDİ veritabanına açılan bağlantıların tek kapısı
/// (new-phase/06-DATA-PLANE.md §5 BYODB).
///
/// Tek kopya olması şart: hem <see cref="DbIntrospectionService"/> hem
/// <see cref="GatewayService"/> aynı sertleştirmeye tabi olmalı. İkisi ayrı ayrı
/// bağlantı açsaydı, TLS zorunluluğu bir tarafa eklenip diğerinde unutulurdu —
/// bu kod tabanı aynı hatayı yetki kontrolünde zaten yaşadı (OrgAccess).
/// </summary>
internal static class UserDbConnection
{
    private const int ConnectTimeoutSeconds = 10;
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// TLS bu host için zorunlu mu?
    ///
    /// Kural: <b>host'un özel/loopback olduğu KESİN değilse evet.</b>
    ///
    /// İlk yazımda kural "public ise zorunlu" idi ve testte düştü: çözülemeyen bir
    /// ad (ör. henüz DNS'i yayılmamış bir sunucu) "public değil" sayılıp TLS'siz
    /// bağlanıyordu — varsayılan güvensiz tarafa düşüyordu. Bilinmeyen host artık
    /// özel sayılmaz.
    ///
    /// Özel/loopback için gevşetmenin sebebi: oralara yalnızca geliştirme politikası
    /// açıkça izin verdiğinde erişiliyor (bkz. <see cref="IDbHostAccessPolicy"/>) ve
    /// yerel bir container'da TLS beklemek özelliği geliştirici makinesinde
    /// kullanılamaz kılardı. Kamuya açık bir veritabanına düz metin bağlanmak ise
    /// kimlik bilgisini ağa yaymak demektir.
    /// </summary>
    public static bool ShouldRequireTls(string? host) => !SsrfGuard.IsHostPrivate(host);

    /// <summary>
    /// Bağlantıyı açar; <paramref name="readOnly"/> ise oturumu salt-okunura çeker.
    ///
    /// <b>Salt-okunur oturum her motorda YOK.</b> PostgreSQL ve MySQL/MariaDB
    /// oturum seviyesinde destekliyor ve orada bu gerçek bir koruma: bizim SQL
    /// üretimimizdeki bir hata bile veri yazamaz. SQL Server ve Oracle'da oturum
    /// seviyesinde karşılığı yok — orada koruma uygulanamaz ve bunu "uygulandı"
    /// gibi göstermek, olmayan bir güvenceye güvenmek olurdu. Bkz.
    /// <see cref="AppliesReadOnlySession"/>.
    /// </summary>
    public static Task<DbConnection> OpenAsync(
        string connectionString, string dbType, bool readOnly, CancellationToken ct) =>
        OpenAsync(connectionString, dbType, readOnly, sessionContext: null, ct);

    public static async Task<DbConnection> OpenAsync(
        string connectionString, string dbType, bool readOnly, DbSessionContext? sessionContext, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(dbType);

        var host = DbIntrospectionService.ExtractHost(connectionString, dbType);
        var requireTls = ShouldRequireTls(host);

        var conn = Create(connectionString, dbType, requireTls);
        await conn.OpenAsync(ct);

        if (readOnly && AppliesReadOnlySession(dbType))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = ReadOnlySql(dbType);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        if (sessionContext is not null)
        {
            await ApplySessionContextAsync(conn, dbType, sessionContext, ct);
        }
        else
        {
            await ResetSessionContextAsync(conn, dbType, ct);
        }

        return conn;
    }

    internal static async Task ResetSessionContextAsync(
        DbConnection conn, string dbType, CancellationToken ct)
    {
        if (!dbType.Equals("POSTGRESQL", StringComparison.OrdinalIgnoreCase) &&
            !dbType.Equals("POSTGRES", StringComparison.OrdinalIgnoreCase))
            return;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.current_tenant', '', false), " +
                          "       set_config('app.user_id', '', false), " +
                          "       set_config('app.user_role', '', false);";

        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal static async Task ApplySessionContextAsync(
        DbConnection conn, string dbType, DbSessionContext context, CancellationToken ct)
    {
        if (!dbType.Equals("POSTGRESQL", StringComparison.OrdinalIgnoreCase) &&
            !dbType.Equals("POSTGRES", StringComparison.OrdinalIgnoreCase))
            return;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.current_tenant', @tenant_id, false), " +
                          "       set_config('app.user_id', @user_id, false), " +
                          "       set_config('app.user_role', @user_role, false);";

        var pTenant = cmd.CreateParameter();
        pTenant.ParameterName = "tenant_id";
        pTenant.Value = context.TenantId ?? string.Empty;
        cmd.Parameters.Add(pTenant);

        var pUser = cmd.CreateParameter();
        pUser.ParameterName = "user_id";
        pUser.Value = context.UserId ?? string.Empty;
        cmd.Parameters.Add(pUser);

        var pRole = cmd.CreateParameter();
        pRole.ParameterName = "user_role";
        pRole.Value = context.Role ?? string.Empty;
        cmd.Parameters.Add(pRole);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static bool AppliesReadOnlySession(string dbType) => dbType.ToUpperInvariant() switch
    {
        "POSTGRESQL" or "POSTGRES" or "MYSQL" or "MARIADB" => true,
        _ => false,
    };

    private static string ReadOnlySql(string dbType) => dbType.ToUpperInvariant() switch
    {
        "POSTGRESQL" or "POSTGRES" => "SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY",
        "MYSQL" or "MARIADB" => "SET SESSION TRANSACTION READ ONLY",
        _ => throw new NotSupportedException($"No read-only session statement for '{dbType}'."),
    };

    /// <summary>Bağlantı dizesini sertleştirir: zaman aşımları ve gerekiyorsa TLS.</summary>
    internal static DbConnection Create(string connectionString, string dbType, bool requireTls) =>
        dbType.ToUpperInvariant() switch
        {
            "MSSQL" or "SQLSERVER" => new SqlConnection(new SqlConnectionStringBuilder(connectionString)
            {
                ConnectTimeout = ConnectTimeoutSeconds,
                // Yerel Windows hesaplarıyla SSPI üzerinden erişimi kapat.
                IntegratedSecurity = false,
                Encrypt = requireTls,
            }.ConnectionString),

            "POSTGRESQL" or "POSTGRES" => new NpgsqlConnection(BuildPostgres(connectionString, requireTls)),

            "MYSQL" or "MARIADB" => new MySqlConnection(BuildMySql(connectionString, requireTls)),

            // Oracle sürücüsünde TLS bağlantı dizesiyle değil, sqlnet.ora/TNS ile
            // yapılandırılır; burada zorlanamaz, sessizce "zorlandı" da denmez.
            "ORACLE" => new OracleConnection(connectionString),

            _ => throw new NotSupportedException($"Database type '{dbType}' is not supported."),
        };

    private static string BuildPostgres(string connectionString, bool requireTls)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = ConnectTimeoutSeconds,
            CommandTimeout = (int)QueryTimeout.TotalSeconds,
        };

        // Kullanıcı daha KATI bir mod seçtiyse (VerifyCA/VerifyFull) ona dokunma —
        // zorlamamız yalnızca tabanı yükseltmek içindir, tavanı düşürmek için değil.
        if (requireTls && builder.SslMode is SslMode.Disable or SslMode.Allow or SslMode.Prefer)
            builder.SslMode = SslMode.Require;

        return builder.ConnectionString;
    }

    private static string BuildMySql(string connectionString, bool requireTls)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            ConnectionTimeout = ConnectTimeoutSeconds,
            DefaultCommandTimeout = (uint)QueryTimeout.TotalSeconds,
        };

        if (requireTls && builder.SslMode is MySqlSslMode.None or MySqlSslMode.Preferred)
            builder.SslMode = MySqlSslMode.Required;

        return builder.ConnectionString;
    }
}
