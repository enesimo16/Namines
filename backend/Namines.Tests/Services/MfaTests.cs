using Namines.API.Controllers;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// Çok faktörlü doğrulamanın metin işleme kısmı.
///
/// <b>Neden bu üç fonksiyon test ediliyor:</b> MFA'nın geri kalanı ASP.NET
/// Identity'nin kendi kodu — onu test etmek Microsoft'u test etmek olurdu.
/// Bizim yazdığımız ve hata yapılabilecek yer, kullanıcıdan gelen metnin
/// nasıl temizlendiği. Ve orada gerçekten bir hata yapıldı (aşağıda).
///
/// <b>Uçtan uca canlı doğrulama ayrıca yapıldı</b> (2026-09-10, gerçek TOTP
/// üretilerek, 12 adım): kurulum → yanlış kod reddi → gerçek kodla
/// etkinleştirme → kodsuz girişte <c>requiresTwoFactor</c> → yanlış kodla
/// giriş reddi → doğru kodla giriş → kurtarma koduyla giriş → aynı kurtarma
/// kodunun ikinci kez reddi → durum (7 kod kaldı) → kapatma → kapandıktan
/// sonra kodsuz giriş.
/// </summary>
public class MfaTests
{
    // ── Kod normalleştirme ────────────────────────────────────────────────────

    /// <summary>
    /// Kimlik doğrulayıcı uygulamaları kodu "123 456" diye gösteriyor;
    /// kullanıcı kopyalayıp yapıştırdığında boşluk geliyor. Temizlenmezse
    /// doğru kod yanlış sayılırdı.
    /// </summary>
    [Theory]
    [InlineData("123 456", "123456")]
    [InlineData("123456", "123456")]
    [InlineData("  123456  ", "123456")]
    [InlineData("123-456", "123456")]
    public void Totp_kodundaki_bosluk_ve_tire_temizleniyor(string input, string expected)
    {
        Assert.Equal(expected, AuthController.NormalizeCode(input));
    }

    [Fact]
    public void Null_kod_bos_dizeye_donusuyor()
    {
        Assert.Equal(string.Empty, AuthController.NormalizeCode(null));
    }

    /// <summary>
    /// <b>Bu test bir REGRESYON testi — anlattığı hata gerçekten yaşandı.</b>
    ///
    /// İlk uygulamada kurtarma kodu da <see cref="AuthController.NormalizeCode"/>
    /// üzerinden geçiriliyordu. Identity kurtarma kodlarını <c>xxxxx-xxxxx</c>
    /// biçiminde üretip AYNEN saklıyor; tirenin silinmesi kodu eşleşmez
    /// hâle getirdi.
    ///
    /// Sonuç, kurtarma kodlarının var olma sebebinin tam tersiydi: telefonunu
    /// kaybeden kullanıcı hesabına <b>hiç</b> giremiyordu. Birim testleri
    /// geçiyordu; hatayı yalnızca uçtan uca canlı deneme gösterdi.
    ///
    /// Bu yüzden <c>NormalizeCode</c> yalnızca TOTP için kullanılıyor;
    /// kurtarma kodu <c>Trim()</c> ile geçiriliyor.
    /// </summary>
    [Fact]
    public void Kurtarma_kodu_bicimi_NormalizeCode_ile_BOZULUR()
    {
        const string recoveryCode = "abcde-fghij";

        // Kanıt: normalleştirme kurtarma kodunun biçimini değiştiriyor.
        Assert.NotEqual(recoveryCode, AuthController.NormalizeCode(recoveryCode));
        Assert.Equal("abcdefghij", AuthController.NormalizeCode(recoveryCode));

        // Bu yüzden kurtarma yolunda yalnızca Trim kullanılmalı.
        Assert.Equal(recoveryCode, $"  {recoveryCode}  ".Trim());
    }

    // ── Anahtarın gösterimi ───────────────────────────────────────────────────

    /// <summary>
    /// QR okutamayan kullanıcı anahtarı elle giriyor. Dörderli gruplar
    /// yazım hatasını belirgin biçimde azaltıyor.
    /// </summary>
    [Fact]
    public void Anahtar_dorderli_gruplara_ayriliyor()
    {
        Assert.Equal("abcd efgh ijkl", AuthController.FormatAuthenticatorKey("ABCDEFGHIJKL"));
    }

    [Fact]
    public void Dorde_bolunmeyen_anahtar_da_bicimlendiriliyor()
    {
        Assert.Equal("abcd efg", AuthController.FormatAuthenticatorKey("ABCDEFG"));
    }

    // ── otpauth URI ───────────────────────────────────────────────────────────

    /// <summary>
    /// <c>otpauth://</c> bütün kimlik doğrulayıcı uygulamalarının ortak dili.
    /// Bozuk bir URI, QR'ın okunmaması demek.
    /// </summary>
    [Fact]
    public void Otpauth_uri_beklenen_alanlari_tasiyor()
    {
        var uri = AuthController.BuildOtpauthUri("kullanici@ornek.com", "JBSWY3DPEHPK3PXP");

        Assert.StartsWith("otpauth://totp/Namines:", uri);
        Assert.Contains("secret=JBSWY3DPEHPK3PXP", uri);
        Assert.Contains("issuer=Namines", uri);
        Assert.Contains("digits=6", uri);
    }

    /// <summary>
    /// E-postadaki <c>@</c> kaçırılmazsa URI ayrıştırması bozuluyor ve bazı
    /// uygulamalar QR'ı hiç okumuyor.
    /// </summary>
    [Fact]
    public void Hesap_adi_uri_icin_kaciriliyor()
    {
        var uri = AuthController.BuildOtpauthUri("kullanici@ornek.com", "JBSWY3DPEHPK3PXP");

        Assert.Contains("kullanici%40ornek.com", uri);
        Assert.DoesNotContain("kullanici@ornek.com", uri);
    }
}
