using Namines.Core.Security;

namespace Namines.Tests.Security;

/// <summary>
/// Namines Flow Bölüm 4 (Test Stratejisi): özel/loopback bir webhook URL'inin
/// HER ZAMAN reddedildiğinin kanıtı — <see cref="DbHostAccessPolicyTests"/> ile
/// aynı stil (Theory tabanlı, gerekçe yorumlarıyla).
///
/// <see cref="DbHostAccessPolicy"/>'den farkı: burada ortam bazlı bir gevşetme
/// YOK — <see cref="SsrfGuard"/> her zaman, her ortamda aynı kurala göre
/// çalışır (webhook aksiyonu hiçbir zaman "geliştirici modunda localhost'a izin
/// ver" gibi bir kapı açmıyor).
/// </summary>
public class SsrfGuardTests
{
    [Theory]
    [InlineData("http://localhost/hook")]
    [InlineData("http://127.0.0.1/hook")]
    [InlineData("http://127.0.0.1:8080/hook")]
    [InlineData("http://10.0.0.5/hook")]
    [InlineData("http://172.16.0.1/hook")]
    [InlineData("http://172.31.255.255/hook")]
    [InlineData("http://192.168.1.10/hook")]
    [InlineData("http://100.64.0.1/hook")] // CGNAT
    [InlineData("http://169.254.169.254/hook")] // cloud metadata (AWS/GCP/Azure)
    [InlineData("http://0.0.0.0/hook")]
    public void Ozel_ve_loopback_adresler_HER_ZAMAN_reddediliyor(string url)
    {
        Assert.False(SsrfGuard.IsUrlSafe(url));
    }

    [Theory]
    [InlineData("http://[::1]/hook")] // IPv6 loopback
    [InlineData("http://[fe80::1]/hook")] // IPv6 link-local
    [InlineData("http://[fc00::1]/hook")] // IPv6 unique-local
    [InlineData("http://[ff02::1]/hook")] // IPv6 multicast
    public void IPv6_ozel_adresler_de_reddediliyor(string url)
    {
        Assert.False(SsrfGuard.IsUrlSafe(url));
    }

    [Fact]
    public void IPv4_mapped_IPv6_kendi_v4_kurallariyla_tekrar_degerlendiriliyor()
    {
        // ::ffff:127.0.0.1 -- v6 kabugu altina saklanmis bir v4 loopback.
        // SsrfGuard bunu MapToIPv4() ile cozup ayni v4 kurallariyla reddetmeli;
        // aksi halde bu, adres ailesini degistirerek filtreyi atlatmanin
        // bilinen bir yolu olurdu.
        Assert.False(SsrfGuard.IsUrlSafe("http://[::ffff:127.0.0.1]/hook"));
    }

    [Theory]
    [InlineData("http://8.8.8.8/hook")]
    [InlineData("https://93.184.216.34/hook")]
    public void Public_IP_literalleri_kabul_ediliyor(string url)
    {
        // DNS çözümlemesine bağımlı olmamak için literal public adresler
        // kullanılıyor (bkz. DbHostAccessPolicyTests'teki aynı gerekçe).
        Assert.True(SsrfGuard.IsUrlSafe(url));
    }

    [Theory]
    [InlineData("ftp://8.8.8.8/hook")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    public void Http_https_disindaki_semalar_reddediliyor(string url)
    {
        Assert.False(SsrfGuard.IsUrlSafe(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("//missing-scheme.com/hook")]
    public void Bozuk_veya_eksik_URL_fail_closed_reddediliyor(string? url)
    {
        Assert.False(SsrfGuard.IsUrlSafe(url));
    }

    [Fact]
    public void Cozulemeyen_host_adi_guvenli_SAYILMIYOR()
    {
        // IsHostSafe: "hepsi public mi?" diye sorar; çözülemeyen bir ad bu
        // soruya evet diyemez -- fail-closed.
        Assert.False(SsrfGuard.IsHostSafe("bu-alan-adi-kesinlikle-yok.invalid"));
    }

    [Fact]
    public void IsHostPrivate_bilinmeyen_host_u_ozel_SAYMIYOR()
    {
        // IsHostSafe'in tersi DEĞİL: IsHostPrivate "kesinlikle özel mi?" sorusuna
        // cevap verir, çözülemeyen bir host bu soruya da evet diyemez (bkz.
        // SsrfGuard.cs'teki XML doc'un kendi uyarısı). Bilinmeyeni özel saymak,
        // örneğin bir TLS zorunluluğu kararında varsayılanı güvensiz tarafa
        // düşürürdü -- burada test ediyoruz ki bu iki sorunun yanıtı gerçekten
        // birbirinin basit tersi değil.
        Assert.False(SsrfGuard.IsHostPrivate("bu-alan-adi-kesinlikle-yok.invalid"));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("192.168.0.1")]
    [InlineData("169.254.169.254")]
    public void IsHostPrivate_bilinen_ozel_adresler_icin_true_donuyor(string host)
    {
        Assert.True(SsrfGuard.IsHostPrivate(host));
    }

    [Fact]
    public void IsHostPrivate_public_adres_icin_false_donuyor()
    {
        Assert.False(SsrfGuard.IsHostPrivate("8.8.8.8"));
    }
}
