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
/// <b>Anahtar rotasyonu (DB-36):</b> <c>Security:ConnectionEncryptionKeys</c> listesi
/// (<c>{ Id, Key }</c> öğeleri) tanımlıysa YENİ yazımlar listenin SON anahtarıyla
/// <c>k.&lt;id&gt;:…</c> biçiminde yapılır; çözümde ön ekteki kimlik anahtarı seçer.
/// Eski <c>v1:…</c> çıktıları <c>Security:ConnectionEncryptionKey</c> (tekil) ile
/// okunmaya devam eder. Eski anahtarlar, <see cref="ConnectionSecretReencryptor"/>
/// ile tüm kayıtlar yeniden şifrelenene kadar yapılandırmada KALMALIDIR.
/// </summary>
public sealed class AesGcmConnectionSecretProtector : IRotatableSecretProtector
{
    private const string LegacyVersion = "v1";
    private const string KeyedPrefix = "k.";
    private const int NonceSize = 12;   // AES-GCM standardı
    private const int TagSize   = 16;

    private readonly byte[]? _legacyKey;
    private readonly Dictionary<string, byte[]> _keys = new(StringComparer.Ordinal);
    private readonly string? _activeId;

    public AesGcmConnectionSecretProtector(IConfiguration configuration)
    {
        var secret = configuration["Security:ConnectionEncryptionKey"];
        var keyed = configuration.GetSection("Security:ConnectionEncryptionKeys").GetChildren().ToList();

        if (string.IsNullOrWhiteSpace(secret) && keyed.Count == 0)
        {
            throw new InvalidOperationException(
                "Security:ConnectionEncryptionKey is not configured. Storing connection strings " +
                "unencrypted would leave database access in plain text — so the service fails " +
                "loudly instead of silently disabling itself. Provide a high-entropy value of at " +
                "least 32 characters (e.g. `openssl rand -base64 32`).");
        }

        if (!string.IsNullOrWhiteSpace(secret))
            _legacyKey = DeriveKey(secret, "Security:ConnectionEncryptionKey");

        foreach (var entry in keyed)
        {
            var id = entry["Id"];
            if (string.IsNullOrWhiteSpace(id) || id.Contains(':') || id.Contains('.') || id == LegacyVersion)
                throw new InvalidOperationException(
                    "Security:ConnectionEncryptionKeys: each entry needs a non-empty Id without ':' or '.' (and not 'v1').");
            if (_keys.ContainsKey(id))
                throw new InvalidOperationException($"Security:ConnectionEncryptionKeys: duplicate key id '{id}'.");
            _keys[id] = DeriveKey(entry["Key"], $"Security:ConnectionEncryptionKeys[{id}]");
            _activeId = id; // SON girdi = en yeni
        }
    }

    private static byte[] DeriveKey(string? secret, string name)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
        {
            throw new InvalidOperationException(
                $"{name} must be at least 32 characters. A short or weak " +
                "secret makes the encryption exist on paper while remaining breakable in practice.");
        }

        // PBKDF2: yapılandırmadan gelen metin bir parola olabilir. Doğrudan SHA-256
        // almak zayıf bir sırrı zayıf bir anahtara çevirirdi; türetme maliyeti
        // saldırganın deneme hızını da aynı oranda düşürüyor.
        //
        // Sabit salt bilinçli: anahtarın DETERMİNİSTİK olması gerekiyor, yoksa
        // yeniden başlatmadan sonra eski kayıtlar çözülemezdi. Salt'ın amacı burada
        // gökkuşağı tablolarını bu uygulamaya özgü kılmak, benzersizlik değil.
        return Rfc2898DeriveBytes.Pbkdf2(
            password: Encoding.UTF8.GetBytes(secret),
            salt: Encoding.UTF8.GetBytes("namines.connection.protector.v1"),
            iterations: 100_000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32);
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        // Anahtarlı liste yoksa davranış eskisiyle birebir aynı (v1 + tekil anahtar).
        var label = _activeId is null ? LegacyVersion : KeyedPrefix + _activeId;
        var key   = _activeId is null ? _legacyKey! : _keys[_activeId];

        var nonce      = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher     = new byte[plainBytes.Length];
        var tag        = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        return string.Join(':',
            label,
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag),
            Convert.ToBase64String(cipher));
    }

    public bool IsEncryptedWithCurrentKey(string ciphertext)
    {
        var label = ciphertext.Split(':', 2)[0];
        return _activeId is null ? label == LegacyVersion : label == KeyedPrefix + _activeId;
    }

    public string Unprotect(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertext);

        var parts = ciphertext.Split(':', 4);
        if (parts.Length != 4)
            throw new CryptographicException("The format of the encrypted connection string is not recognized.");

        byte[]? key = null;
        if (parts[0] == LegacyVersion)
            key = _legacyKey;
        else if (parts[0].StartsWith(KeyedPrefix, StringComparison.Ordinal))
            _keys.TryGetValue(parts[0][KeyedPrefix.Length..], out key);

        if (key is null)
            throw new CryptographicException("The format of the encrypted connection string is not recognized.");

        var nonce  = Convert.FromBase64String(parts[1]);
        var tag    = Convert.FromBase64String(parts[2]);
        var cipher = Convert.FromBase64String(parts[3]);
        var plain  = new byte[cipher.Length];

        // Anahtar değişmiş ya da veri kurcalanmışsa AesGcm burada
        // CryptographicException fırlatır — bilinçli olarak yakalanmıyor.
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }
}
