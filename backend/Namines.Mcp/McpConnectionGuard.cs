using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Namines.Core.Security;

namespace Namines.Mcp;

/// <summary>
/// MCP araçlarının bağlandığı veritabanı hedefini GÖRÜNÜR kılar ve isteğe bağlı
/// bir allowlist uygular.
///
/// <b>Bulunan açık (docs/last-review M-16):</b> MCP süreci SSRF politikasını
/// yapısal olarak kapatıyor (<c>Security:AllowPrivateDbHosts</c> varsayılan
/// <c>true</c>, <c>McpHostEnvironment</c> her zaman "Development"). Bu, ürünün
/// hedeflenen kullanımı için <b>doğru</b> — süreç kullanıcının kendi makinesinde
/// ve localhost tam olarak amaçlanan hedef (33 §2). Kapatılmaması gereken şey
/// buydu.
///
/// Kalan gerçek risk şu: bağlantı dizesi <b>MODELİN çıktısından</b> geliyor.
/// Prompt injection'a maruz kalmış bir model, kullanıcının kimlik bilgilerini
/// içeren bir dizeye <c>Host=saldirgan.example.com</c> yazıp onları dışarı
/// taşıyabilir — ve guard tamamen kapalı olduğu için hiçbir şey buna engel değil,
/// hiçbir iz de kalmıyor.
///
/// <b>Varsayılan davranış DEĞİŞMİYOR</b> (yoksa barındırılan bir Postgres'e
/// bakan kullanıcıların akışı kırılırdı); değişen şu:
/// <list type="number">
///   <item>Her bağlantı hedefi (host + veritabanı, <b>asla kimlik bilgisi değil</b>)
///   stderr'a yazılıyor, yani modelin nereyi aradığı kullanıcıya görünür.</item>
///   <item><c>Security:McpAllowedDbHosts</c> tanımlanmışsa hedef o listede olmak
///   zorunda — kimlik bilgilerini korumak isteyen kullanıcı için sert bir kapı.</item>
/// </list>
/// </summary>
public sealed class McpConnectionGuard
{
    private readonly ILogger<McpConnectionGuard> _logger;
    private readonly string[] _allowedHosts;

    /// <summary>
    /// GENEL (internet) veritabanı hostlarına izin verilsin mi. Varsayılan <c>false</c>.
    ///
    /// <b>Neden varsayılan kapalı (docs/last-review M-16):</b> MCP süreci kullanıcının
    /// kendi makinesinde çalışıyor ve bağlantı dizesini MODELİN çıktısından alıyor.
    /// Prompt injection'a maruz kalmış bir model, kullanıcının kimlik bilgilerini
    /// içeren bir dizeye <c>Host=saldirgan.example.com</c> yazıp onları dışarı
    /// taşıyabilir — bir veritabanı sürücüsüyle yapılan, kimsenin beklemediği bir
    /// dışa sızış kanalı. Yerel bir geliştirme aracının varsayılan olarak internete
    /// çıkması için bir sebep yok.
    ///
    /// <b>Meşru kullanım tek satırla geri geliyor:</b> barındırılan bir Postgres'e
    /// (Neon, RDS, Supabase) bakan kullanıcı <c>Security:McpAllowPublicDbHosts=true</c>
    /// veriyor. Bu bir KARAR noktası olduğu için sessizce varsayılmamış olması önemli —
    /// ama kullanıcıyı çıkmaz sokağa da sokmuyor: hata mesajı ne yapacağını söylüyor.
    ///
    /// <c>Security:McpAllowedDbHosts</c> (allowlist) tanımlıysa bu bayrağa GEREK YOK:
    /// allowlist zaten daha dar bir kapı ve hedefi tek tek sınırlıyor.
    /// </summary>
    private readonly bool _allowPublicHosts;

    public McpConnectionGuard(IConfiguration configuration, ILogger<McpConnectionGuard> logger)
    {
        _logger = logger;

        // Virgülle ayrılmış liste. Boşsa kapı yok, yalnızca günlük tutulur.
        _allowedHosts = (configuration["Security:McpAllowedDbHosts"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(h => h.ToLowerInvariant())
            .ToArray();

        _allowPublicHosts = configuration.GetValue("Security:McpAllowPublicDbHosts", defaultValue: false);
    }

    /// <summary>
    /// Hedefi kaydeder ve allowlist tanımlıysa uygular. İzin verilmiyorsa
    /// <see cref="InvalidOperationException"/> fırlatır.
    /// </summary>
    public void EnsureAllowed(string connectionString, string engine)
    {
        var host = ExtractHost(connectionString);

        // Hedef HER ZAMAN kaydediliyor, allowlist olsun olmasın: asıl kazanç,
        // modelin nereyi aradığının görünür olması. Kimlik bilgisi YAZILMIYOR —
        // ham bağlantı dizesini loglamak, açığı kapatmak yerine ikinci bir
        // sızıntı yüzeyi açmak olurdu.
        _logger.LogWarning(
            "MCP veritabani baglantisi: host={Host} engine={Engine}", host ?? "(bilinmiyor)", engine);

        // Allowlist tanımlıysa TEK kapı o: daha dar ve hedefi tek tek sınırlıyor,
        // yani "genel host" sorusunu ayrıca sormaya gerek yok.
        if (_allowedHosts.Length > 0)
        {
            // Host ayıklanamadıysa REDDEDİLİYOR: "okuyamadım"ı "izinli" saymak,
            // kapıyı bozuk bir dizeyle atlamanın yolu olurdu.
            if (host is null)
                throw new InvalidOperationException(
                    "The connection string does not name a host that could be checked against " +
                    "Security:McpAllowedDbHosts.");

            if (!_allowedHosts.Contains(host.ToLowerInvariant()))
                throw new InvalidOperationException(
                    $"Host '{host}' is not in Security:McpAllowedDbHosts. " +
                    "Add it there if this database is really yours.");

            return;
        }

        if (_allowPublicHosts) return;

        // Buradan sonrası: allowlist yok ve genel hostlara izin verilmemiş.
        // Yalnızca yerel/özel ağ hedefleri geçiyor — bkz. _allowPublicHosts notu.
        if (host is null)
            throw new InvalidOperationException(
                "The connection string does not name a host that could be checked. " +
                "Set Security:McpAllowPublicDbHosts=true if you meant to reach a hosted database.");

        // <c>IsHostPrivate</c> bilerek <c>IsHostSafe</c>'in tersi DEĞİL: çözülemeyen
        // bir ad burada özel SAYILMAZ, yani geçemez. Çözülemeyen bir adı "yerel
        // olabilir" diye geçirmek, kapıyı var olmayan bir alan adıyla atlamak olurdu.
        if (SsrfGuard.IsHostPrivate(host)) return;

        throw new InvalidOperationException(
            $"Host '{host}' is not a local or private-network address, and reaching public " +
            "database hosts from the MCP server is turned off. The connection string comes " +
            "from model output, so a public target could carry your credentials off the " +
            "machine. If this database really is yours, set " +
            "Security:McpAllowPublicDbHosts=true (or list it in Security:McpAllowedDbHosts).");
    }

    /// <summary>
    /// Bağlantı dizesinden host alanını ayıklar. Sürücüye özel builder
    /// KULLANILMIYOR: bu sınıf motorlar arası ve hatalı bir dizede bile bir şey
    /// kaydetmek, hiç kaydetmemekten iyi.
    /// </summary>
    private static string? ExtractHost(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;

        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;

            var key = part[..eq].Trim().ToLowerInvariant().Replace(" ", string.Empty);
            if (key is not ("host" or "server" or "datasource" or "address" or "addr" or "networkaddress"))
                continue;

            var value = part[(eq + 1)..].Trim();
            if (value.Length == 0) continue;

            // "tcp:host,1433" / "host:5432" gibi biçimleri sadeleştir.
            var host = value.Split(',')[0].Trim();
            var colon = host.LastIndexOf(':');
            if (colon > 0 && host.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
                host = host[4..].Split(':')[0];
            else if (colon > 0 && !host.Contains("::", StringComparison.Ordinal))
                host = host[..colon];

            return host.Length == 0 ? null : host;
        }

        return null;
    }
}
