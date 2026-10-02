using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Mcp;
using Xunit;

namespace Namines.Tests.Mcp;

/// <summary>
/// <see cref="McpConnectionGuard"/> — MCP'nin bağlanabileceği veritabanı hedefleri.
///
/// <b>Bulunan açık (docs/last-review M-16):</b> MCP süreci SSRF politikasını yapısal
/// olarak kapatıyor ve bu, ürünün hedeflenen kullanımı için DOĞRU — süreç
/// kullanıcının kendi makinesinde ve localhost tam olarak amaçlanan hedef.
/// Kalan risk şu: bağlantı dizesi <b>MODELİN çıktısından</b> geliyor. Prompt
/// injection'a maruz kalmış bir model, kullanıcının kimlik bilgilerini içeren bir
/// dizeye <c>Host=saldirgan.example.com</c> yazıp onları dışarı taşıyabilir —
/// bir veritabanı sürücüsüyle açılan, kimsenin beklemediği bir sızış kanalı.
///
/// Varsayılan artık yalnızca yerel/özel ağ hedeflerine izin veriyor; barındırılan
/// bir veritabanına bakan kullanıcı tek satır yapılandırmayla geri dönüyor.
/// </summary>
public class McpConnectionGuardTests
{
    private static McpConnectionGuard Guard(
        string? allowedHosts = null, bool? allowPublic = null)
    {
        var settings = new Dictionary<string, string?>();
        if (allowedHosts is not null) settings["Security:McpAllowedDbHosts"] = allowedHosts;
        if (allowPublic is not null) settings["Security:McpAllowPublicDbHosts"] = allowPublic.Value ? "true" : "false";

        return new McpConnectionGuard(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            NullLogger<McpConnectionGuard>.Instance);
    }

    private static string Cs(string host) =>
        $"Host={host};Port=5432;Database=app;Username=u;Password=secret";

    // ── Varsayılan: yalnızca yerel/özel ────────────────────────────────────────

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.1.50")]
    [InlineData("10.0.0.7")]
    [InlineData("172.16.3.9")]
    public void Yerel_ve_ozel_ag_hedefleri_VARSAYILAN_olarak_geciyor(string host)
    {
        // Ürünün hedeflenen kullanımı bu (33 §2) ve kapatılmaması gereken şey buydu.
        Guard().EnsureAllowed(Cs(host), "PostgreSQL");
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("203.0.113.5")]
    public void GENEL_adresler_varsayilan_olarak_REDDEDILIYOR(string host)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Guard().EnsureAllowed(Cs(host), "PostgreSQL"));

        // Mesaj kullanıcıyı çıkmaz sokağa sokmamalı: NE yapacağını söylemeli.
        Assert.Contains("McpAllowPublicDbHosts", error.Message);
    }

    [Fact]
    public void Cozulemeyen_ad_REDDEDILIYOR()
    {
        // `IsHostPrivate` bilerek `IsHostSafe`'in tersi DEĞİL: çözülemeyen bir ad
        // özel SAYILMAZ. Aksi hâlde var olmayan bir alan adıyla kapı atlanırdı.
        Assert.Throws<InvalidOperationException>(
            () => Guard().EnsureAllowed(Cs("bu-ad-kesinlikle-yok.invalid"), "PostgreSQL"));
    }

    [Fact]
    public void Host_ayiklanamayan_dize_REDDEDILIYOR()
    {
        // "Okuyamadım"ı "izinli" saymak, kapıyı bozuk bir dizeyle atlamanın yolu olurdu.
        Assert.Throws<InvalidOperationException>(
            () => Guard().EnsureAllowed("Database=app;Username=u;Password=secret", "PostgreSQL"));
    }

    // ── Açık opt-in ───────────────────────────────────────────────────────────

    [Fact]
    public void McpAllowPublicDbHosts_ile_barindirilan_veritabani_GERI_DONUYOR()
    {
        // Neon/RDS/Supabase kullanan kullanıcının akışı tek satırla çalışıyor.
        Guard(allowPublic: true).EnsureAllowed(Cs("8.8.8.8"), "PostgreSQL");
    }

    // ── Allowlist daha dar kapı ───────────────────────────────────────────────

    [Fact]
    public void Allowlist_varsa_genel_host_bayragina_GEREK_YOK()
    {
        // Allowlist zaten hedefi tek tek sınırlıyor; ikinci bir kapı sormak
        // kullanıcıyı iki ayrı ayarı birlikte doğru yapmaya zorlardı.
        Guard(allowedHosts: "8.8.8.8").EnsureAllowed(Cs("8.8.8.8"), "PostgreSQL");
    }

    [Fact]
    public void Allowlist_disindaki_host_GENEL_IZIN_olsa_bile_reddediliyor()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Guard(allowedHosts: "db.benim.com", allowPublic: true)
                .EnsureAllowed(Cs("saldirgan.example.com"), "PostgreSQL"));

        Assert.Contains("McpAllowedDbHosts", error.Message);
    }

    [Fact]
    public void Allowlist_varken_yerel_host_da_listede_olmak_ZORUNDA()
    {
        // Allowlist "yalnızca bunlar" demek; localhost'u örtük geçirmek, operatörün
        // yazdığı kısıtı sessizce gevşetmek olurdu.
        Assert.Throws<InvalidOperationException>(
            () => Guard(allowedHosts: "db.benim.com").EnsureAllowed(Cs("localhost"), "PostgreSQL"));
    }
}
