using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Namines.Vault.Storage;

/// <summary>
/// Yedek dosyalarının at-rest şifrelemesi — AES-256-GCM, parça parça.
///
/// <b>Opsiyonel değil</b> (<c>namines-vault/00-GENEL-BAKIS.md</c> §3 madde 8):
/// bir yedek, veritabanının tamamının kopyasıdır; nesne deposuna düz metin
/// yazmak, o deponun erişim kontrolünü tek savunma hattı yapardı.
///
/// <b>Anahtar, bağlantı sırrınınkinden AYRI</b> (<c>Vault:BackupEncryptionKey</c>).
/// Aynı anahtarı kullanmak, birinin sızmasını ikisinin sızması yapardı: bağlantı
/// dizesini çözebilen biri, o bağlantının bütün geçmiş yedeklerini de çözebilirdi.
///
/// <b>Neden bir <see cref="CryptoStream"/> değil:</b> GCM bir akış şifresi gibi
/// kullanılamaz — her parçanın kendi doğrulama etiketi (tag) olmalı ki
/// değiştirilmiş bir dosya çözülürken FARK EDİLSİN. Bu yüzden dosya sabit
/// boyutlu bloklara ayrılıyor ve her blok kendi nonce + tag'iyle yazılıyor.
/// </summary>
public sealed class BackupCipher
{
    /// <summary>
    /// 1 MiB. Küçük tutmak etiket başına ek yükü artırır, büyük tutmak bellekte
    /// tutulan tamponu büyütür — bu değer ikisinin arasında ve sabit.
    /// </summary>
    internal const int PlainBlockSize = 1024 * 1024;

    private const int NonceSize = 12;   // AES-GCM standardı
    private const int TagSize = 16;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("NVLT1\0");

    private readonly byte[] _key;

    public BackupCipher(IConfiguration configuration)
    {
        var raw = configuration["Vault:BackupEncryptionKey"];
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                "Vault:BackupEncryptionKey is not configured. Vault refuses to write unencrypted backups; " +
                "set a high-entropy value of at least 32 characters (for example: openssl rand -base64 32).");

        if (raw.Trim().Length < 32)
            throw new InvalidOperationException(
                "Vault:BackupEncryptionKey must be at least 32 characters. A short key makes the encryption " +
                "real on paper and breakable in practice.");

        // Anahtar metninden sabit 32 baytlık bir anahtar türetiliyor — kullanıcı
        // tam 32 bayt vermek zorunda kalmasın.
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(raw.Trim()));
    }

    /// <summary>Düz akışı şifreleyip <paramref name="destination"/>'a yazar.</summary>
    public async Task EncryptAsync(Stream plain, Stream destination, CancellationToken ct)
    {
        await destination.WriteAsync(Magic, ct);

        var buffer = new byte[PlainBlockSize];
        using var aes = new AesGcm(_key, TagSize);

        while (true)
        {
            var read = await ReadBlockAsync(plain, buffer, ct);
            if (read == 0) break;

            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var cipher = new byte[read];
            var tag = new byte[TagSize];
            aes.Encrypt(nonce, buffer.AsSpan(0, read), cipher, tag);

            // Blok başlığı: uzunluk + nonce + tag. Uzunluk şart — son blok kısa.
            await destination.WriteAsync(BitConverter.GetBytes(read), ct);
            await destination.WriteAsync(nonce, ct);
            await destination.WriteAsync(tag, ct);
            await destination.WriteAsync(cipher, ct);

            if (read < PlainBlockSize) break;
        }
    }

    /// <summary>Şifreli akışı çözüp <paramref name="destination"/>'a yazar.</summary>
    public async Task DecryptAsync(Stream encrypted, Stream destination, CancellationToken ct)
    {
        var magic = new byte[Magic.Length];
        if (await ReadBlockAsync(encrypted, magic, ct) != Magic.Length || !magic.AsSpan().SequenceEqual(Magic))
            throw new InvalidOperationException("This file is not a Namines Vault backup, or it is corrupt.");

        using var aes = new AesGcm(_key, TagSize);
        var header = new byte[4 + NonceSize + TagSize];

        while (true)
        {
            var headerRead = await ReadBlockAsync(encrypted, header, ct);
            if (headerRead == 0) break;
            if (headerRead != header.Length)
                throw new InvalidOperationException("Backup file is truncated.");

            var length = BitConverter.ToInt32(header, 0);
            if (length <= 0 || length > PlainBlockSize)
                throw new InvalidOperationException("Backup file is corrupt (bad block length).");

            var nonce = header.AsSpan(4, NonceSize).ToArray();
            var tag = header.AsSpan(4 + NonceSize, TagSize).ToArray();

            var cipher = new byte[length];
            if (await ReadBlockAsync(encrypted, cipher, ct) != length)
                throw new InvalidOperationException("Backup file is truncated.");

            var plain = new byte[length];
            // Etiket tutmazsa AuthenticationTagMismatchException fırlar —
            // değiştirilmiş bir yedek SESSİZCE geri yüklenmez.
            aes.Decrypt(nonce, cipher, tag, plain);

            await destination.WriteAsync(plain, ct);
        }
    }

    /// <summary>
    /// Tamponu doldurana ya da akış bitene kadar okur.
    ///
    /// <see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)"/> istenenden
    /// AZ döndürebilir; bunu tek çağrıda "blok bitti" saymak, boru hattı
    /// üzerinden gelen bir akışta dosyayı sessizce bölerdi.
    /// </summary>
    private static async Task<int> ReadBlockAsync(Stream source, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await source.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct);
            if (read == 0) break;
            total += read;
        }
        return total;
    }
}
