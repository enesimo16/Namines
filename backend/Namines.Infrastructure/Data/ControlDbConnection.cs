using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Linq;

namespace Namines.Infrastructure.Data;

/// <summary>
/// Kontrol DB bağlantı dizesine havuz/zaman aşımı varsayılanlarını ekler (DB-44/45).
/// Bağlantı dizesinde AÇIKÇA yazılan değer her zaman kazanır; yalnızca eksik
/// anahtarlar <c>Database:*</c> yapılandırmasından doldurulur.
/// </summary>
public static class ControlDbConnection
{
    public const int DefaultMaxPoolSize = 50;
    public const int DefaultConnectTimeoutSeconds = 15;
    public const int DefaultKeepaliveSeconds = 30;

    public static string ApplyPoolDefaults(string connectionString, IConfiguration configuration)
    {
        var b = new NpgsqlConnectionStringBuilder(connectionString);
        // NpgsqlConnectionStringBuilder her bilinen anahtar için ContainsKey=true döner;
        // "kullanıcı yazdı mı" sorusu ham dizeden, eş anlamlılarıyla birlikte okunur.
        var raw = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connectionString };
        bool Has(params string[] keys) => keys.Any(raw.ContainsKey);

                if (!Has("Maximum Pool Size", "MaxPoolSize", "Max Pool Size"))
            b.MaxPoolSize = configuration.GetValue("Database:MaxPoolSize", DefaultMaxPoolSize);
        if (!Has("Timeout", "Connect Timeout", "Connection Timeout"))
            b.Timeout = configuration.GetValue("Database:ConnectTimeoutSeconds", DefaultConnectTimeoutSeconds);
        if (!Has("Keepalive", "Keep Alive"))
            b.KeepAlive = configuration.GetValue("Database:KeepaliveSeconds", DefaultKeepaliveSeconds);
        if (!Has("Command Timeout", "CommandTimeout"))
            b.CommandTimeout = configuration.GetValue("Database:CommandTimeoutSeconds", 30);

        return b.ConnectionString;
    }
}
