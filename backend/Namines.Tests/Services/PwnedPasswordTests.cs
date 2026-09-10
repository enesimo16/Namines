using Namines.API.Security;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// HIBP yanıtının ayrıştırılması.
///
/// <b>Neden yalnızca ayrıştırma test ediliyor:</b> ağ çağrısının kendisi
/// birim testine sokulmamalı — testi HIBP'nin çalışma süresine bağlamak,
/// suite'i dış bir servisin arızasında kırmızıya çeviren tam olarak o
/// "yeşil ama hiçbir şey kanıtlamayan" duruma yol açardı. Ayrıştırma ise saf
/// bir fonksiyon ve hata yapılabilecek tek yer orası.
///
/// <b>Canlı doğrulama ayrıca yapıldı</b> (2026-09-10, gerçek API'ye karşı):
/// <list type="bullet">
/// <item><c>Password123456</c> → 42.513 sızıntı → reddedilir</item>
/// <item><c>Parola1!</c> → 21.753 sızıntı → reddedilir</item>
/// <item>18 karakterlik rastgele parola → 0 → kabul edilir</item>
/// </list>
/// Birincisi bu özelliğin NEDEN gerektiğini gösteriyor: 14 karakter, yani
/// 12 karakterlik uzunluk kuralını geçiyor — ama kırk iki bin sızıntıda var.
/// </summary>
public class PwnedPasswordTests
{
    /// <summary>HIBP yanıt biçimi: <c>SUFFIX:COUNT</c>, satır başına bir kayıt.</summary>
    private const string SampleBody =
        "0018A45C4D1DEF81644B54AB7F969B88D65:1\n" +
        "00D4F6E8FA6EECAD2A3AA415EEC418D38EC:2\n" +
        "011053FD0102E94D6AE2F8B83D76FAF94F6:5228\n" +
        "012A7CA357541F0AC487871FEEC1891C49C:3\n";

    [Fact]
    public void Bulunan_parolanin_sayisi_donuyor()
    {
        Assert.Equal(5228, PwnedPasswordValidator.FindCount(SampleBody, "011053FD0102E94D6AE2F8B83D76FAF94F6"));
    }

    [Fact]
    public void Bulunmayan_parola_sifir_donuyor()
    {
        Assert.Equal(0, PwnedPasswordValidator.FindCount(SampleBody, "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"));
    }

    /// <summary>
    /// HIBP hash'leri BÜYÜK harfle döndürüyor ama bu bir garanti değil;
    /// karşılaştırma büyük/küçük harf duyarsız olmalı. Duyarlı olsaydı hata
    /// sessizce "bu parola temiz" derdi — yanlış yöne düşen bir hata.
    /// </summary>
    [Fact]
    public void Karsilastirma_buyuk_kucuk_harf_duyarsiz()
    {
        Assert.Equal(5228, PwnedPasswordValidator.FindCount(SampleBody, "011053fd0102e94d6ae2f8b83d76faf94f6"));
    }

    /// <summary>
    /// <c>Add-Padding: true</c> ile gelen sahte kayıtların sayısı 0'dır.
    /// Bunlar eşleşse bile parolanın sızdığı ANLAMINA GELMEZ — sayı 0
    /// döndüğü için doğrulayıcı onu "temiz" sayıyor.
    /// </summary>
    [Fact]
    public void Padding_kaydi_sizinti_sayilmaz()
    {
        const string withPadding = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA:0\n";
        Assert.Equal(0, PwnedPasswordValidator.FindCount(withPadding, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));
    }

    /// <summary>
    /// Yanıt CRLF ile gelebilir. Trim edilmezse hiçbir satır eşleşmez ve
    /// kontrol sessizce her parolayı "temiz" göstermeye başlar — bu, testi
    /// olmasa fark edilmesi çok zor bir arıza biçimi.
    /// </summary>
    [Fact]
    public void CRLF_satir_sonlari_ayristirilabiliyor()
    {
        const string crlf = "011053FD0102E94D6AE2F8B83D76FAF94F6:5228\r\n0018A45C4D1DEF81644B54AB7F969B88D65:1\r\n";
        Assert.Equal(5228, PwnedPasswordValidator.FindCount(crlf, "011053FD0102E94D6AE2F8B83D76FAF94F6"));
    }

    [Fact]
    public void Bozuk_satirlar_atlaniyor()
    {
        const string malformed = "gecersiz-satir\n\n:::\n011053FD0102E94D6AE2F8B83D76FAF94F6:5228\n";
        Assert.Equal(5228, PwnedPasswordValidator.FindCount(malformed, "011053FD0102E94D6AE2F8B83D76FAF94F6"));
    }

    [Fact]
    public void Bos_govde_sifir_donuyor()
    {
        Assert.Equal(0, PwnedPasswordValidator.FindCount(string.Empty, "011053FD0102E94D6AE2F8B83D76FAF94F6"));
    }
}
