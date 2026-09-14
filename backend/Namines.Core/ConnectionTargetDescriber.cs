using System;
using Namines.Core.Enums;

namespace Namines.Core;

/// <summary>
/// Bağlantı dizesinden yalnızca host + veritabanı adını çıkarır (parola/kullanıcı
/// adı ASLA). Denetim kayıtlarının (<see cref="Namines.Core.Models.Auth.SqlExecutionAudit"/>)
/// "nerede çalıştı" sorusuna cevap vermesi için — DatabaseExecutorController
/// (Namines.API) ve LaunchService (Namines.Infrastructure) arasında paylaşılıyor,
/// bu yüzden ikisinin de referans edebildiği Namines.Core'da yaşıyor.
/// </summary>
public static class ConnectionTargetDescriber
{
    public static (string? Host, string? Database) Describe(string? connectionString, DatabaseType dbType)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return (null, null);

        string? host = null, database = null;

        foreach (var pair in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0) continue;

            var key = pair[..separator].Trim();
            var value = pair[(separator + 1)..].Trim();

            if (key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Server", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Data Source", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Address", StringComparison.OrdinalIgnoreCase))
                host = value;
            else if (key.Equals("Database", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase))
                database = value;
        }

        // SQLite'ta "Data Source" bir dosya yolu; kullanıcının disk düzenini
        // denetim kaydına yazmamak için yalnızca dosya adı tutuluyor.
        if (dbType == DatabaseType.SQLite && host is not null)
        {
            host = System.IO.Path.GetFileName(host);
        }

        return (host, database);
    }
}
