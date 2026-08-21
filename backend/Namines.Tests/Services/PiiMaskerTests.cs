using Namines.Core.Analysis;

namespace Namines.Tests.Services;

/// <summary>
/// PII maskeleme (new-phase/06-DATA-PLANE.md §4).
///
/// İki özellik aynı anda doğru olmalı ve birbirine ters çeker:
/// <b>deterministik</b> (aynı girdi → aynı çıktı, yoksa maskelenmiş veri üzerinde
/// gruplama/birleştirme yapılamaz) ve <b>geri döndürülemez</b> (çıktıdan orijinal
/// elde edilemez). Testler ikisini birden kilitliyor.
/// </summary>
public class PiiMaskerTests
{
    private const string Secret = "test-secret";

    [Fact]
    public void The_same_input_always_masks_to_the_same_value()
    {
        // Rastgele olsaydı "aynı kullanıcı" bilgisi kaybolur, maskelenmiş veri
        // üzerinde birleştirme yapılamazdı — geliştirici verinin ŞEKLİYLE
        // çalışamaz hâle gelirdi.
        Assert.Equal(
            PiiMasker.Mask("ali@example.org", Secret),
            PiiMasker.Mask("ali@example.org", Secret));
    }

    [Fact]
    public void Different_inputs_mask_to_different_values()
    {
        Assert.NotEqual(
            PiiMasker.Mask("ali@example.org", Secret),
            PiiMasker.Mask("veli@example.org", Secret));
    }

    [Fact]
    public void A_different_secret_produces_a_different_mask()
    {
        // Proje başına ayrı sır: bir projenin çıktısı diğeriyle eşleştirilerek
        // kimlik çözülemesin.
        Assert.NotEqual(
            PiiMasker.Mask("ali@example.org", "secret-a"),
            PiiMasker.Mask("ali@example.org", "secret-b"));
    }

    [Fact]
    public void The_original_value_never_survives_in_the_output()
    {
        var masked = PiiMasker.Mask("ali@example.org", Secret)!;

        Assert.DoesNotContain("ali", masked);
        Assert.DoesNotContain("example.org", masked);
    }

    [Fact]
    public void An_email_still_looks_like_an_email()
    {
        // Biçim korunmazsa uygulama doğrulamaları maskelenmiş veriyle çalışmaz ve
        // geliştirici gerçek veri istemek zorunda kalır.
        var masked = PiiMasker.Mask("ali@example.org", Secret)!;

        Assert.Contains("@", masked);
        Assert.EndsWith("@example.com", masked);
    }

    [Theory]
    [InlineData("+90 532 111 22 33")]
    [InlineData("5321112233")]
    public void A_phone_number_keeps_its_shape(string phone)
    {
        var masked = PiiMasker.Mask(phone, Secret)!;

        Assert.Equal(phone.Length, masked.Length);
        Assert.NotEqual(phone, masked);
        // Biçimlendirme karakterleri yerinde kalmalı.
        for (var i = 0; i < phone.Length; i++)
            if (!char.IsDigit(phone[i]))
                Assert.Equal(phone[i], masked[i]);
    }

    [Fact]
    public void A_plain_string_keeps_its_length()
    {
        var masked = PiiMasker.Mask("Ahmet Yılmaz", Secret)!;
        Assert.Equal("Ahmet Yılmaz".Length, masked.Length);
        Assert.NotEqual("Ahmet Yılmaz", masked);
    }

    [Fact]
    public void Null_and_empty_pass_through_unchanged()
    {
        // Boş bir değeri maskelemek, "veri yok" bilgisini "veri var ama gizli"ye
        // çevirirdi; satır sayıları ve NULL oranları gerçek kalmalı.
        Assert.Null(PiiMasker.Mask(null, Secret));
        Assert.Equal(string.Empty, PiiMasker.Mask(string.Empty, Secret));
    }

    [Fact]
    public void Numbers_mask_deterministically_and_keep_their_sign()
    {
        Assert.Equal(PiiMasker.MaskNumber(42, Secret), PiiMasker.MaskNumber(42, Secret));
        Assert.NotEqual(PiiMasker.MaskNumber(42, Secret), PiiMasker.MaskNumber(43, Secret));
        Assert.True(PiiMasker.MaskNumber(-42, Secret) < 0);
        Assert.True(PiiMasker.MaskNumber(42, Secret) > 0);
    }

    [Fact]
    public void A_missing_secret_is_rejected()
    {
        // Boş sırla maskelemek, herkesin aynı çıktıyı üretebilmesi demek — yani
        // sözlük saldırısıyla orijinali bulmak.
        Assert.Throws<ArgumentException>(() => PiiMasker.Mask("ali@example.org", ""));
    }
}
