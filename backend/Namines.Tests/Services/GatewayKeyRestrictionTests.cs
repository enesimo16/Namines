using System.Net;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;

namespace Namines.Tests.Services;

/// <summary>
/// API anahtarı kaynak kısıtları ve anahtar başına rate limit (08 §4.3, §5).
///
/// Testlerin çoğu tek bir ilkeyi koruyor: <b>kararsız kalınan durumda REDDET.</b>
/// Bir kısıt sessizce uygulanamaz hâle geldiğinde, onu hiç istememekten kötü bir
/// durum doğar — kullanıcı korunduğunu sanır.
/// </summary>
public class GatewayKeyRestrictionTests
{
    private static GatewayApiKey Key(string? origins = null, string? ips = null, int? rate = null) => new()
    {
        Id = "k1", ProjectId = "p1", Name = "k", Prefix = "nmn_x", KeyHash = "h",
        CreatedByUserId = "u1",
        AllowedOrigins = origins, AllowedIps = ips, RateLimitPerMinute = rate,
    };

    // ── Origin ───────────────────────────────────────────────────────────────

    [Fact]
    public void No_origin_list_means_no_origin_restriction()
    {
        Assert.True(GatewayKeyRestrictions.IsOriginAllowed(Key(), null));
        Assert.True(GatewayKeyRestrictions.IsOriginAllowed(Key(), "https://anything.example"));
    }

    [Fact]
    public void A_listed_origin_is_allowed_and_others_are_not()
    {
        var key = Key(origins: "https://app.musteri.com, https://admin.musteri.com");

        Assert.True(GatewayKeyRestrictions.IsOriginAllowed(key, "https://app.musteri.com"));
        Assert.True(GatewayKeyRestrictions.IsOriginAllowed(key, "https://admin.musteri.com"));
        Assert.False(GatewayKeyRestrictions.IsOriginAllowed(key, "https://evil.example"));
    }

    [Fact]
    public void A_missing_origin_header_is_rejected_when_a_list_exists()
    {
        // "Origin kısıtla" diyen biri, başlığı hiç göndermeyen bir istemciye kapıyı
        // açık bırakmayı kastetmez.
        Assert.False(GatewayKeyRestrictions.IsOriginAllowed(Key(origins: "https://app.musteri.com"), null));
        Assert.False(GatewayKeyRestrictions.IsOriginAllowed(Key(origins: "https://app.musteri.com"), ""));
    }

    // ── IP ───────────────────────────────────────────────────────────────────

    [Fact]
    public void No_ip_list_means_no_ip_restriction()
    {
        Assert.True(GatewayKeyRestrictions.IsIpAllowed(
            Key(), IPAddress.Parse("8.8.8.8"), clientAddressIsTrustworthy: false, out _));
    }

    [Fact]
    public void An_ip_list_is_refused_when_the_client_address_cannot_be_trusted()
    {
        // ASIL KURAL. Uygulama güvenilen proxy ağları tanımlanmadan bir proxy
        // arkasındaysa istemci X-Forwarded-For'u istediği gibi yazabilir; böyle bir
        // ortamda listeyi "uyguluyormuş gibi" davranmak yanlış güven verir.
        var allowed = GatewayKeyRestrictions.IsIpAllowed(
            Key(ips: "1.2.3.4"), IPAddress.Parse("1.2.3.4"),
            clientAddressIsTrustworthy: false, out var reason);

        Assert.False(allowed);
        Assert.Contains("cannot determine", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_exact_ip_matches()
    {
        Assert.True(GatewayKeyRestrictions.IsIpAllowed(
            Key(ips: "1.2.3.4"), IPAddress.Parse("1.2.3.4"), true, out _));

        Assert.False(GatewayKeyRestrictions.IsIpAllowed(
            Key(ips: "1.2.3.4"), IPAddress.Parse("1.2.3.5"), true, out _));
    }

    [Theory]
    [InlineData("10.0.0.0/8", "10.1.2.3", true)]
    [InlineData("10.0.0.0/8", "11.1.2.3", false)]
    [InlineData("192.168.1.0/24", "192.168.1.255", true)]
    [InlineData("192.168.1.0/24", "192.168.2.1", false)]
    [InlineData("1.2.3.4/32", "1.2.3.4", true)]
    [InlineData("1.2.3.4/32", "1.2.3.5", false)]
    // Bayt sınırına düşmeyen prefix: /20 → ikinci baytın üst 4 biti.
    [InlineData("172.16.0.0/20", "172.16.15.1", true)]
    [InlineData("172.16.0.0/20", "172.16.16.1", false)]
    public void Cidr_rules_match_correctly(string rule, string address, bool expected)
    {
        Assert.Equal(expected, GatewayKeyRestrictions.Matches(rule, IPAddress.Parse(address)));
    }

    [Fact]
    public void An_ipv4_mapped_ipv6_address_matches_an_ipv4_rule()
    {
        // Kestrel bazı yapılandırmalarda ::ffff:1.2.3.4 verir; kural yazan kişi
        // "1.2.3.4" yazar ve eşleşmesini bekler.
        Assert.True(GatewayKeyRestrictions.IsIpAllowed(
            Key(ips: "1.2.3.4"), IPAddress.Parse("::ffff:1.2.3.4"), true, out _));
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("10.0.0.0/notanumber")]
    [InlineData("10.0.0.0/99")]
    [InlineData("")]
    public void An_unparseable_rule_never_matches(string rule)
    {
        // Bozuk bir kuralı "her şeye uyar" saymak, tek bir yazım hatasıyla kısıtı
        // tamamen kaldırırdı.
        Assert.False(GatewayKeyRestrictions.Matches(rule, IPAddress.Parse("10.0.0.1")));
    }

    [Fact]
    public void The_denial_message_does_not_echo_the_caller_address()
    {
        // Hangi IP olarak görüldüğünü söylemek, kuralı atlatmayı deneyen birine
        // geri bildirim verir.
        GatewayKeyRestrictions.IsIpAllowed(
            Key(ips: "1.2.3.4"), IPAddress.Parse("9.9.9.9"), true, out var reason);

        Assert.DoesNotContain("9.9.9.9", reason);
    }

    // ── Rate limit ───────────────────────────────────────────────────────────

    [Fact]
    public void A_key_without_a_limit_is_never_throttled_here()
    {
        // Sunucunun genel politikası zaten devrede; anahtar sınırı ek bir katman.
        var key = Key();
        for (var i = 0; i < 100; i++) Assert.True(GatewayRateLimiter.TryAcquire(key));
    }

    [Fact]
    public void A_key_is_throttled_once_its_limit_is_reached()
    {
        GatewayRateLimiter.Reset();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 3; i++)
            Assert.True(GatewayRateLimiter.TryAcquire("throttle-test", 3, now));

        Assert.False(GatewayRateLimiter.TryAcquire("throttle-test", 3, now));
    }

    [Fact]
    public void The_window_resets_after_a_minute()
    {
        GatewayRateLimiter.Reset();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.True(GatewayRateLimiter.TryAcquire("window-test", 1, now));
        Assert.False(GatewayRateLimiter.TryAcquire("window-test", 1, now.AddSeconds(59)));
        Assert.True(GatewayRateLimiter.TryAcquire("window-test", 1, now.AddSeconds(60)));
    }

    [Fact]
    public void Keys_do_not_share_a_window()
    {
        GatewayRateLimiter.Reset();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.True(GatewayRateLimiter.TryAcquire("key-a", 1, now));
        Assert.False(GatewayRateLimiter.TryAcquire("key-a", 1, now));
        // Bir anahtarın limiti diğerini etkilememeli.
        Assert.True(GatewayRateLimiter.TryAcquire("key-b", 1, now));
    }
}
