using System;
using System.Security.Cryptography;
using System.Text;

namespace Namines.Core.Analysis;

/// <summary>
/// Kişisel veriyi okurken maskeler (new-phase/06-DATA-PLANE.md §4 PII maskeleme).
///
/// <b>Dokümandaki biçimden farkı, bilinçli.</b> 06 §4 maskelemeyi "branch
/// oluştururken gerçek veriden kopyalarken" tarif ediyor; o akış Neon'un
/// copy-on-write klonlamasına bağlı ve henüz yok. Bugün uygulanabilir karşılığı
/// OKUMA ANINDA maskeleme: bir API anahtarı bir tabloyu görebiliyor ama o
/// tablodaki e-posta/telefon kolonlarını gerçek hâliyle görmesi gerekmiyor.
/// Klonlama geldiğinde aynı fonksiyon orada da kullanılır.
///
/// <b>Deterministik:</b> aynı girdi her zaman aynı çıktıyı verir. Rastgele olsaydı
/// maskelenmiş veri üzerinde gruplama/birleştirme yapılamaz, "aynı kullanıcı" bilgisi
/// kaybolurdu — geliştirici verinin ŞEKLİYLE çalışmalı, içeriğiyle değil.
///
/// <b>Geri döndürülemez:</b> HMAC-SHA256, proje başına gizli anahtarla. Şifreleme
/// değil özet; maskelenmiş çıktıdan orijinali geri getirmenin yolu yok. Gizli
/// anahtar olmadan aynı çıktıyı üretmek de mümkün değil, yani bir sözlük saldırısı
/// ("ali@x.com'un maskesi nedir?") çalışmaz.
/// </summary>
public static class PiiMasker
{
    /// <summary>
    /// Değeri maskeler ve BİÇİMİNİ korur: e-posta e-posta gibi, telefon telefon
    /// gibi görünür. Biçim korunmazsa uygulama doğrulamaları maskelenmiş veriyle
    /// çalışmaz ve geliştirici gerçek veri istemek zorunda kalır.
    /// </summary>
    public static string? Mask(string? value, string secret)
    {
        if (value is null) return null;
        if (value.Length == 0) return value;

        var digest = Digest(value, secret);

        // E-posta: yerel kısım maskelenir, alan adı example.com'a çekilir. Gerçek
        // alan adını bırakmak, kimin müşterisi olduğunu ele verirdi.
        var at = value.IndexOf('@');
        if (at > 0 && at < value.Length - 1)
            return $"{digest[..10]}@example.com";

        // Telefon gibi görünen değerlerde rakam sayısı ve biçim korunur.
        //
        // Ölçüt "çoğunluğu rakam" DEĞİL, "rakam dışındaki her karakter bilinen bir
        // ayırıcı": ilk yazımda oran eşiği kullandım ve "+90 532 111 22 33" (12 rakam,
        // 5 ayırıcı) eşiği geçemeyip genel dala düştü, yani "+" işareti de bozuldu.
        // Ayırıcı kümesine bakmak hem daha kesin hem de eşik ayarlamayı gerektirmiyor.
        if (LooksLikePhone(value)) return MaskDigits(value, digest);

        // Genel durum: uzunluk korunur, karakter sınıfı korunmaz.
        return digest.Length >= value.Length ? digest[..value.Length] : digest;
    }

    /// <summary>Sayısal değerler için: aynı girdi aynı çıktıyı verir, aralık korunur.</summary>
    public static long MaskNumber(long value, string secret)
    {
        var digest = Digest(value.ToString(), secret);
        // İlk 8 karakteri sayıya çevir; işareti koru ki negatif değerler negatif kalsın.
        var magnitude = Math.Abs(Convert.ToInt64(digest[..8], 16));
        return value < 0 ? -magnitude : magnitude;
    }

    private const string PhoneSeparators = " +-()./";

    private static bool LooksLikePhone(string value)
    {
        var digits = 0;
        foreach (var c in value)
        {
            if (char.IsDigit(c)) { digits++; continue; }
            if (!PhoneSeparators.Contains(c)) return false;
        }
        // 7 rakam: en kısa yerel numara. Altında kalan değerler (ör. "2024") tarih
        // ya da kod olabilir; onları telefon sanıp biçim korumak yanıltıcı olurdu.
        return digits >= 7;
    }

    private static string MaskDigits(string value, string digest)
    {
        var sb = new StringBuilder(value.Length);
        var index = 0;
        foreach (var c in value)
        {
            if (char.IsDigit(c))
            {
                // Özetin rakama çevrilmiş hâli — biçimlendirme karakterleri
                // (boşluk, +, -) olduğu gibi kalır.
                sb.Append((char)('0' + digest[index % digest.Length] % 10));
                index++;
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static string Digest(string value, string secret)
    {
        if (string.IsNullOrEmpty(secret))
            throw new ArgumentException("A masking secret is required.", nameof(secret));

        var bytes = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
