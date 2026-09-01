using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Namines.Core.Security;

namespace Namines.Infrastructure.Security;

/// <summary>
/// <see cref="IConnectionSecretProtector"/>'ın AES-256-GCM uygulaması.
///
/// <b>Neden AES-GCM:</b> kimlik doğrulamalı şifreleme (AEAD) — yalnızca gizlilik
/// değil, <i>bütünlük</i> de sağlıyor. Kurcalanmış bir şifreli metin çözülmez,
/// sessizce bozuk bir bağlantı dizesi üretmez.
///
/// <b>Neden ASP.NET Data Protection değil (şimdilik):</b> Data Protection'ın
/// varsayılan anahtar deposu yerel dosya sistemi — container'da her yeniden
/// başlatmada kaybolur, çok instance'ta paylaşılmaz. Anahtar kaybı = saklanan
/// TÜM bağlantıların kaybı. Onu doğru kurmak ayrı bir paket + ayrı bir migration
/// demek. Bu proje zaten yapılandırmadan sır okuma desenine sahip
/// (<c>GatewayController.MaskingSecret</c> <c>Jwt:Key</c>'i böyle kullanıyor),
/// bu yüzden ilk sürüm aynı deseni izliyor: açık, denetlenebilir, çok
/// instance'ta ilk günden doğru çalışır. Değiştirmek istendiğinde
/// <see cref="IConnectionSecretProtector"/>'ın ikinci bir uygulamasını yazmak
/// yeterli — çağıran kod değişmez.
///
/// <b>Anahtar rotasyonu:</b> çıktı <c>v1:</c> ön ekiyle sürümlenmiş. Algoritma
/// ya da anahtar değişirse <c>v2</c> yazılır ve <see cref="Unprotect"/> eski
/// sürümü okumaya devam edebilir. Bugün tek sürüm var; rotasyon gerçekten
/// gerektiğinde eklenecek — kullanılmayan bir kod yolunu şimdiden yazmak,
/// test edilmemiş bir güvenlik yolu bırakmak olurdu.
/// </summary>
public sealed class AesGcmConnectionSecretProtector : IConnectionSecretProtector
{
    private const string Version = "v1";
    private const int NonceSize = 12;   // AES-GCM standardı
    private const int TagSize   = 16;

    private readonly byte[] _key;

    public AesGcmConnectionSecretProtector(IConfiguration configuration)
    {
        var secret = configuration["Security:ConnectionEncryptionKey"];

        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException(
                "Security:ConnectionEncryptionKey tanımlı değil. Bağlantı dizelerini şifrelemeden " +
                "saklamak, veritabanı erişimini düz metin bırakırdı — bu yüzden servis sessizce " +
                "devre dışı kalmak yerine açıkça durur. En az 32 karakterlik yüksek entropili bir " +
                "değer verin (ör. `openssl rand -base64 32`).");
        }

        if (secret.Length < 32)
        {
            throw new InvalidOperationException(
                "Security:ConnectionEncryptionKey en az 32 karakter olmalı. Kısa/zayıf bir sır, " +
                "şifrelemeyi kağıt üzerinde var ama pratikte kırılabilir hâle getirir.");
        }

        // PBKDF2: yapılandırmadan gelen metin bir parola olabilir. Doğrudan SHA-256
        // almak zayıf bir sırrı zayıf bir anahtara çevirirdi; türetme maliyeti
        // saldırganın deneme hızını da aynı oranda düşürüyor.
        //
        // Sabit salt bilinçli: anahtarın DETERMİNİSTİK olması gerekiyor, yoksa
        // yeniden başlatmadan sonra eski kayıtlar çözülemezdi. Salt'ın amacı burada
        // gökkuşağı tablolarını bu uygulamaya özgü kılmak, benzersizlik değil.
        _key = Rfc2898DeriveBytes.Pbkdf2(
            password: Encoding.UTF8.GetBytes(secret),
            salt: Encoding.UTF8.GetBytes("namines.connection.protector.v1"),
            iterations: 100_000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32);
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var nonce      = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher     = new byte[plainBytes.Length];
        var tag        = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        return string.Join(':',
            Version,
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag),
            Convert.ToBase64String(cipher));
    }

    public string Unprotect(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertext);

        var parts = ciphertext.Split(':', 4);
        if (parts.Length != 4 || parts[0] != Version)
            throw new CryptographicException("Şifreli bağlantı dizesinin biçimi tanınmıyor.");

        var nonce  = Convert.FromBase64String(parts[1]);
        var tag    = Convert.FromBase64String(parts[2]);
        var cipher = Convert.FromBase64String(parts[3]);
        var plain  = new byte[cipher.Length];

        // Anahtar değişmiş ya da veri kurcalanmışsa AesGcm burada
        // CryptographicException fırlatır — bilinçli olarak yakalanmıyor.
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }
}
