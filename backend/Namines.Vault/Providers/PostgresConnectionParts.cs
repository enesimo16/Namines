using System;
using System.Collections.Generic;
using System.Linq;

namespace Namines.Vault.Providers;

/// <summary>
/// Bağlantı dizesinden <c>pg_dump</c>'ın ihtiyaç duyduğu parçalar.
///
/// <b>Neden Npgsql'in kendi ayrıştırıcısı değil:</b> <c>Namines.Vault</c>
/// yalnızca <c>Namines.Core</c>'a bağlı — modül sınırı derleyiciyle korunuyor
/// (bkz. <c>Namines.Vault.csproj</c>). Buradaki ihtiyaç dört alandan ibaret,
/// bunun için bir veri erişim kütüphanesi çekmek sınırı bozmaya değmez.
/// </summary>
internal sealed record PostgresConnectionParts(
    string Host, int Port, string Database, string Username, string Password)
{
    private const int DefaultPort = 5432;

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

    public static PostgresConnectionParts Parse(string connectionString)
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

        // Npgsql birden çok eş anlamlıyı kabul ediyor; kullanıcı bağlantısını
        // Vault'a taşırken yazım biçimi değiştirmek zorunda kalmasın.
        var host = First(values, "Host", "Server", "Data Source");
        var database = First(values, "Database", "Initial Catalog");
        var username = First(values, "Username", "User ID", "UserId", "Uid", "User");
        var password = First(values, "Password", "Pwd");

        if (host is null || database is null || username is null || password is null)
            throw new ArgumentException(
                "PostgreSQL connection string must contain Host, Database, Username and Password.",
                nameof(connectionString));

        var port = DefaultPort;
        if (First(values, "Port") is { } rawPort && int.TryParse(rawPort, out var parsed))
            port = parsed;

        return new PostgresConnectionParts(host, port, database, username, password);
    }

    private static string? First(IReadOnlyDictionary<string, string> values, params string[] keys) =>
        keys.Select(key => values.TryGetValue(key, out var value) ? value : null)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
