using Namines.Core.Security;
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
    /// <summary>
    /// Konteynerin içinden kullanılacak host.
    ///
    /// <b>Bulunan açık (docs/last-review M-12):</b> SSRF guard'ı host adını API
    /// sürecinin çözücüsüyle doğrularken, bağlantı KONTEYNERIN içinden ve KONTEYNERIN
    /// çözücüsüyle kuruluyordu. İki farklı çözücü, iki farklı cevap verebilir: guard'ın
    /// "bu ad genel bir adrese çözülüyor, geçebilir" kararı, konteynerde aynı adın İÇ
    /// bir adrese çözülmesini engellemiyordu — yani guard'ın kararı gerçek bağlantıyı
    /// BAĞLAMIYORDU (klasik DNS rebinding / çözücü ayrışması).
    ///
    /// <see cref="ResolvedAddress"/> doluysa ad değil <b>doğrulanan IP</b> geçiliyor:
    /// konteynerde çözümleme HIÇ yapılmıyor, yani guard'ın gördüğü adres ile
    /// bağlanılan adres AYNI oluyor.
    ///
    /// Loopback bunun DIŞINDA ve zorunlu olarak: konteynerin kendi loopback'i host'un
    /// loopback'i değil, o yüzden oraya IP geçmek işe yaramaz — <c>host-gateway</c>
    /// tam olarak bu yüzden var. O dal zaten geliştirme çift kapısıyla kapılı.
    /// </summary>
    public string ContainerVisibleHost =>
        LoopbackNames.Contains(Host)
            ? HostGatewayName
            : (ResolvedAddress ?? Host);

    /// <summary>
    /// API tarafında ÇÖZÜLMÜŞ ve SSRF guard'ından GEÇMİŞ IP adresi.
    ///
    /// <c>null</c> = çözümleme yapılmadı (guard'ın atlandığı yollar, ya da
    /// loopback). O durumda eski davranış korunuyor: ad geçiliyor. Uydurma bir
    /// adres yazmak, doğrulanmamış bir şeyi doğrulanmış gibi göstermek olurdu.
    /// </summary>
    public string? ResolvedAddress { get; set; }

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

        var result = new PostgresConnectionParts(host, port, database, username, password);
        // Ad yerine DOGRULANAN ADRESI sabitle — bkz. ResolveAndValidate.
        result.ResolvedAddress = ResolveAndValidate(result.Host);
        return result;
    }

    private static string? First(IReadOnlyDictionary<string, string> values, params string[] keys) =>
        keys.Select(key => values.TryGetValue(key, out var value) ? value : null)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    /// <summary>
    /// Host adını API tarafında çözer ve adresi <c>ResolvedAddress</c>'e sabitler —
    /// ama yalnızca adres GÜVENLİYSE.
    ///
    /// <b>Bulunan açık (docs/last-review M-12):</b> SSRF guard'ı adı API sürecinin
    /// çözücüsüyle doğrularken bağlantı KONTEYNERIN içinden ve KONTEYNERIN çözücüsüyle
    /// kuruluyordu. İki farklı çözücü iki farklı cevap verebilir, yani guard'ın kararı
    /// gerçek bağlantıyı bağlamıyordu. Ad yerine ÇÖZÜLMÜŞ ADRESİ geçirmek, konteynerde
    /// çözümlemeyi tamamen ortadan kaldırıyor: guard'ın gördüğü adres ile bağlanılan
    /// adres aynı oluyor.
    ///
    /// <b>Adresin kendisi ayrıca doğrulanıyor</b> (<see cref="SsrfGuard.IsHostSafe"/>):
    /// adın bir kez geçmiş olması, ŞU ANDA çözüldüğü adresin güvenli olduğunu
    /// kanıtlamaz — aradaki DNS değişimi tam olarak rebinding'in tanımı.
    ///
    /// Çözülemezse ya da adres güvensizse <c>ResolvedAddress</c> null kalır ve eski
    /// davranış (adı geçirmek) sürüyor. Uydurma bir adres yazmak, doğrulanmamış bir
    /// şeyi doğrulanmış gibi göstermek olurdu; bağlantıyı tamamen reddetmek ise
    /// çözümlemenin geçici olarak başarısız olduğu her durumda yedeği kesecekti.
    /// </summary>
    private static string? ResolveAndValidate(string host)
    {
        if (LoopbackNames.Contains(host)) return null;

        // Zaten IP ise çözümlemeye gerek yok; yine de doğrulanıyor.
        if (System.Net.IPAddress.TryParse(host, out var literal))
            return SsrfGuard.IsHostSafe(host) ? literal.ToString() : null;

        System.Net.IPAddress[] addresses;
        try { addresses = System.Net.Dns.GetHostAddresses(host); }
        catch { return null; }

        if (addresses.Length == 0) return null;

        // TEK adres sabitleniyor ama guard TÜM adreslerin güvenli olmasını istiyor
        // (IsHostSafe'in kendi davranışı): bir adın adreslerinden biri iç ağa
        // çözülüyorsa o ad hiç kullanılmamalı.
        if (!SsrfGuard.IsHostSafe(host)) return null;

        return addresses[0].ToString();
    }
}
