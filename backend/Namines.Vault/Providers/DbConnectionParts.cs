using System;
using System.Collections.Generic;
using System.Linq;

namespace Namines.Vault.Providers;

/// <summary>
/// Bağlantı dizesinden dump/restore araçlarının ihtiyaç duyduğu parçalar.
///
/// <b>Motordan bağımsız</b> — PostgreSQL, MySQL ve MariaDB'nin bağlantı
/// dizeleri farklı anahtar adları kullanıyor ama TAŞIDIKLARI bilgi aynı
/// (host, port, veritabanı, kullanıcı, parola). Eş anlamlılar aşağıda tek
/// yerde toplandı; motor başına ayrı bir ayrıştırıcı yazmak aynı beş alanı
/// üç kez çözmek olurdu.
///
/// <b>Neden Npgsql/MySqlConnector'ın kendi ayrıştırıcısı değil:</b>
/// <c>Namines.Vault</c> yalnızca <c>Namines.Core</c>'a bağlı — modül sınırı
/// derleyiciyle korunuyor (bkz. <c>Namines.Vault.csproj</c>). Buradaki ihtiyaç
/// beş alandan ibaret; bunun için iki veri erişim kütüphanesi çekmek sınırı
/// bozmaya değmez.
/// </summary>
internal sealed record DbConnectionParts(
    string Host, int Port, string Database, string Username, string Password)
{
    /// <summary>
    /// Konteynerden host makinesine ulaşmanın taşınabilir yolu.
    ///
    /// Konteynerin içinde <c>localhost</c> KONTEYNERİN kendisidir; kullanıcının
    /// veritabanı değil. Docker Desktop'ta (Windows/macOS) <c>NetworkMode=host</c>
    /// yok, bu yüzden host ağı yerine bu ad + <c>host-gateway</c> kullanılıyor:
    /// üç platformda da çalışan tek çözüm bu.
    /// </summary>
    internal const string HostGatewayName = "host.docker.internal";

    private static readonly HashSet<string> LoopbackNames =
        new(StringComparer.OrdinalIgnoreCase) { "localhost", "127.0.0.1", "::1" };

    /// <summary>Konteynerin içinden kullanılacak host adı.</summary>
    public string ContainerVisibleHost => LoopbackNames.Contains(Host) ? HostGatewayName : Host;

    /// <param name="defaultPort">
    /// Bağlantı dizesinde port yoksa kullanılacak değer — motor başına farklı
    /// (PostgreSQL 5432, MySQL/MariaDB 3306). Yanlış varsayılan, kullanıcının
    /// portu yazmadığı her bağlantıda sessiz bir "connection refused" demekti.
    /// </param>
    public static DbConnectionParts Parse(string connectionString, int defaultPort)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0) continue;
            values[pair[..separator].Trim()] = pair[(separator + 1)..].Trim();
        }

        // Sürücüler birden çok eş anlamlıyı kabul ediyor; kullanıcı bağlantısını
        // Vault'a taşırken yazım biçimi değiştirmek zorunda kalmasın.
        var host = First(values, "Host", "Server", "Data Source", "Address", "Addr");
        var database = First(values, "Database", "Initial Catalog");
        var username = First(values, "Username", "User ID", "UserId", "Uid", "User");
        var password = First(values, "Password", "Pwd");

        if (host is null || database is null || username is null || password is null)
            throw new ArgumentException(
                "The connection string must contain host, database, username and password.",
                nameof(connectionString));

        var port = defaultPort;
        if (First(values, "Port") is { } rawPort && int.TryParse(rawPort, out var parsed))
            port = parsed;

        return new DbConnectionParts(host, port, database, username, password);
    }

    private static string? First(IReadOnlyDictionary<string, string> values, params string[] keys) =>
        keys.Select(key => values.TryGetValue(key, out var value) ? value : null)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
