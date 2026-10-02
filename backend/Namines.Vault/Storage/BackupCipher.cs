using System;
using System.Buffers.Binary;
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
///
/// <b>Biçim sürümleri:</b>
/// <list type="bullet">
/// <item><c>NVLT2</c> (yazılan): anahtar PBKDF2-SHA256 ile DOSYA BAŞINA rastgele tuzdan
/// türetilir; her bloğun AAD'si <c>magic | tuz | blok indeksi | final bayrağı</c> içerir.
/// Yalnızca blok İÇİ değişiklik değil, blokların SİLİNMESİ, YER DEĞİŞTİRMESİ, ÇOĞALTILMASI,
/// dosyanın KESİLMESİ ve başka bir yedeğe ait blokların yapıştırılması da etiketi bozar;
/// "final" bayraklı blok görülmeden biten dosya <b>kesilmiş</b> sayılır. Depoya yazma
/// erişimi olan (ama anahtarı olmayan) biri, kesilmiş bir dökümün canlı veritabanına
/// <c>pg_restore --clean</c> ile geri yüklenmesini artık sağlayamaz.</item>
/// <item><c>NVLT1</c> (yalnızca OKUNUR): tuzsuz tek turlu SHA-256 anahtarı ve konuma
/// bağlanmayan bloklar. Eski yedekler çözülebilsin diye korunuyor; bu biçimde kesme/
/// yeniden sıralama tespit EDİLEMEZ — o yedeklerin yeniden alınması önerilir.</item>
/// </list>
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
    private const int SaltSize = 16;

    /// <summary>OWASP (2023) PBKDF2-HMAC-SHA256 önerisi.</summary>
    internal const int Pbkdf2Iterations = 600_000;

    private static readonly byte[] MagicV1 = Encoding.ASCII.GetBytes("NVLT1\0");
    private static readonly byte[] MagicV2 = Encoding.ASCII.GetBytes("NVLT2\0");

    /// <summary>
    /// NVLT3: AAD'ye YEDEĞİN KİMLİĞİ (proje + yedek kimliği) de giriyor.
    ///
    /// <b>Bulunan açık (docs/last-review M-8):</b> parola SUNUCU GENELİ olduğu için
    /// A projesinin şifreli yedeği B projesinin kaydındaki anahtarın yerine konsa
    /// sorunsuz çözülüyordu — şifreleme dosyanın BİR KAYDA ait olduğunu hiç
    /// kanıtlamıyordu. Kayıtın özeti (<c>VaultBackup.CipherTextSha256</c>) bu saldırıyı
    /// yakalıyor, ama özet <b>kaydın</b> doğru olmasına güveniyor: kaydı da
    /// değiştirebilen biri ikisini birlikte tutarlı yapabilir. AAD bağlaması o
    /// varsayımı ortadan kaldırıyor — kimlik ŞIFRELEMENİN kendisine giriyor, yani
    /// yanlış kimlikle çözmek GCM etiketini bozuyor.
    ///
    /// <b>Kimlik dosyaya YAZILMIYOR,</b> kayıttan geliyor: dosyaya yazılsa onu
    /// değiştiren biri AAD'yi de kendi lehine kurardı ve bağlama anlamsız olurdu.
    /// </summary>
    private static readonly byte[] MagicV3 = Encoding.ASCII.GetBytes("NVLT3\0");

    private readonly string _passphrase;
    private readonly byte[] _legacyKey;

    /// <summary>
    /// Eski NVLT1 biçimini okumaya izin verilsin mi (docs/last-review M-8).
    /// Varsayılan <c>false</c>: biçim düşürme yolu kapalı.
    /// </summary>
    private readonly bool _allowLegacyV1;

    public BackupCipher(IConfiguration configuration)
    {
        _allowLegacyV1 = configuration.GetValue("Vault:AllowLegacyV1", defaultValue: false);

        var raw = configuration["Vault:BackupEncryptionKey"];
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                "Vault:BackupEncryptionKey is not configured. Vault refuses to write unencrypted backups; " +
                "set a high-entropy value of at least 32 characters (for example: openssl rand -base64 32).");

        if (raw.Trim().Length < 32)
            throw new InvalidOperationException(
                "Vault:BackupEncryptionKey must be at least 32 characters. A short key makes the encryption " +
                "real on paper and breakable in practice.");

        _passphrase = raw.Trim();

        // YALNIZCA NVLT1 (eski) yedekleri çözmek için. Yeni yedekler bu anahtarla YAZILMAZ.
        _legacyKey = SHA256.HashData(Encoding.UTF8.GetBytes(_passphrase));
    }

    /// <summary>
    /// Yapılandırılmış değerden dosyaya özel anahtar türetir. Yapılandırma değeri bir
    /// PAROLA cümlesi gibi davranılıyor (uzunluğu doğrulanıyor, entropisi değil), bu yüzden
    /// tek tur SHA-256 yerine tuzlu ve yavaş bir KDF şart: çalınmış tek bir dosyaya karşı
    /// çevrimdışı deneme hızı iterasyon sayısı kadar düşer ve tuz, önceden hesaplanmış
    /// sözlüklerin tüm kurulumlara karşı işe yaramasını engeller.
    /// </summary>
    private byte[] DeriveKey(byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(_passphrase), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);

    /// <summary>
    /// Blok bağlamı (AAD): dosya kimliği (magic + tuz) + blok indeksi + final bayrağı.
    /// İndeks SUNUCUDA yeniden hesaplanır, dosyadan OKUNMAZ — yer değiştirmiş/çoğaltılmış
    /// bir blok kendi indeksiyle değil beklenen indeksle doğrulanır ve düşer.
    /// </summary>
    /// <summary>
    /// NVLT3 blok bağlamı: V2'nin taşıdığı her şey + yedeğin kimliği.
    ///
    /// Kimlik uzunluğuyla birlikte yazılıyor: <c>"ab" + "c"</c> ile <c>"a" + "bc"</c>
    /// aksi hâlde AYNI bayt dizisini verirdi ve iki farklı yedek aynı bağlamı
    /// paylasabilirdi.
    /// </summary>
    private static byte[] BuildAadV3(
        byte[] salt, long blockIndex, bool isFinal, BackupIdentity identity)
    {
        var projectId = Encoding.UTF8.GetBytes(identity.ProjectId);
        var backupId = Encoding.UTF8.GetBytes(identity.BackupId);

        var aad = new byte[MagicV3.Length + SaltSize + 8 + 1 + 4 + projectId.Length + 4 + backupId.Length];
        var offset = 0;

        MagicV3.CopyTo(aad, offset); offset += MagicV3.Length;
        salt.CopyTo(aad, offset); offset += SaltSize;
        BinaryPrimitives.WriteInt64BigEndian(aad.AsSpan(offset, 8), blockIndex); offset += 8;
        aad[offset++] = isFinal ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32BigEndian(aad.AsSpan(offset, 4), projectId.Length); offset += 4;
        projectId.CopyTo(aad, offset); offset += projectId.Length;
        BinaryPrimitives.WriteInt32BigEndian(aad.AsSpan(offset, 4), backupId.Length); offset += 4;
        backupId.CopyTo(aad, offset);

        return aad;
    }

    private static byte[] BuildAad(byte[] salt, long blockIndex, bool isFinal)
    {
        var aad = new byte[MagicV2.Length + SaltSize + 8 + 1];
        MagicV2.CopyTo(aad, 0);
        salt.CopyTo(aad, MagicV2.Length);
        BinaryPrimitives.WriteInt64BigEndian(aad.AsSpan(MagicV2.Length + SaltSize, 8), blockIndex);
        aad[^1] = isFinal ? (byte)1 : (byte)0;
        return aad;
    }

    /// <summary>Düz akışı şifreleyip <paramref name="destination"/>'a yazar.</summary>
    /// <param name="identity">
    /// Yedeğin kimliği. Verilirse dosya <b>NVLT3</b> olarak yazılır ve kimlik
    /// şifrelemenin AAD'sine girer (bkz. <see cref="MagicV3"/>). <c>null</c> ise
    /// eski <b>NVLT2</b> biçimi yazılır — kimliği olmayan bir çağıranı bağlamak
    /// mümkün değil ve uydurma bir kimlik yazmak, bağlamayı var göstermek olurdu.
    /// </param>
    public async Task EncryptAsync(
        Stream plain, Stream destination, CancellationToken ct, BackupIdentity? identity = null)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        await destination.WriteAsync(identity is null ? MagicV2 : MagicV3, ct);
        await destination.WriteAsync(salt, ct);

        var key = DeriveKey(salt);
        using var aes = new AesGcm(key, TagSize);
        CryptographicOperations.ZeroMemory(key);

        // Bir blok ileriye bakılıyor: bir bloğun SON olup olmadığı, ondan sonra veri gelip
        // gelmediğine bağlı. Son bloğu işaretlemek, "dosya erken bitti"yi ayırt etmenin tek yolu.
        var current = new byte[PlainBlockSize];
        var next = new byte[PlainBlockSize];
        var currentLength = await ReadBlockAsync(plain, current, ct);
        long index = 0;

        while (true)
        {
            var nextLength = 0;
            bool isFinal;
            if (currentLength < PlainBlockSize)
            {
                isFinal = true;
            }
            else
            {
                nextLength = await ReadBlockAsync(plain, next, ct);
                isFinal = nextLength == 0;
            }

            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var cipher = new byte[currentLength];
            var tag = new byte[TagSize];
            var aad = identity is null
                ? BuildAad(salt, index, isFinal)
                : BuildAadV3(salt, index, isFinal, identity);

            aes.Encrypt(nonce, current.AsSpan(0, currentLength), cipher, tag, aad);

            // Blok başlığı: uzunluk + final bayrağı + nonce + tag. Uzunluk şart — son blok kısa.
            await destination.WriteAsync(BitConverter.GetBytes(currentLength), ct);
            await destination.WriteAsync(new[] { isFinal ? (byte)1 : (byte)0 }, ct);
            await destination.WriteAsync(nonce, ct);
            await destination.WriteAsync(tag, ct);
            await destination.WriteAsync(cipher, ct);

            if (isFinal) break;

            (current, next) = (next, current);
            currentLength = nextLength;
            index++;
        }
    }

    /// <summary>Şifreli akışı çözüp <paramref name="destination"/>'a yazar.</summary>
    /// <param name="identity">
    /// Beklenen yedek kimliği. <b>NVLT3 dosyaları için ZORUNLU</b>: biçimin varlık
    /// sebebi, dosyanın BİR KAYDA ait olduğunu kanıtlamak ve kimlik olmadan bu
    /// doğrulanamaz. NVLT2/NVLT1 dosyalarında yok sayılır (o biçimler kimlik
    /// taşımıyor; geriye dönük bir bağlama uydurmak mümkün değil).
    /// </param>
    public async Task DecryptAsync(
        Stream encrypted, Stream destination, CancellationToken ct, BackupIdentity? identity = null)
    {
        var magic = new byte[MagicV2.Length];
        var magicRead = await ReadBlockAsync(encrypted, magic, ct);

        if (magicRead == MagicV3.Length && magic.AsSpan().SequenceEqual(MagicV3))
        {
            // Kimlik YOKSA reddediliyor. "Kimlik verilmedi, bağlamayı atla" demek,
            // biçimin getirdiği korumayı çağıranın ihmaliyle kapatmak olurdu — ve
            // saldırganın yapacağı şey tam olarak o çağrıyı bulmak olurdu.
            if (identity is null)
                throw new InvalidOperationException(
                    "This backup is bound to a specific record (NVLT3) and cannot be read " +
                    "without that record's identity.");

            await DecryptV3Async(encrypted, destination, identity, ct);
            return;
        }

        if (magicRead == MagicV2.Length && magic.AsSpan().SequenceEqual(MagicV2))
        {
            await DecryptV2Async(encrypted, destination, ct);
            return;
        }

        if (magicRead == MagicV1.Length && magic.AsSpan().SequenceEqual(MagicV1))
        {
            // NVLT1 okuma VARSAYILAN OLARAK KAPALI (docs/last-review M-8).
            //
            // <b>Neden:</b> V1 biçimi NVLT2'nin eklediği korumaları taşımıyor
            // (dosyaya özel tuz, blok indeksi/final bayrağı AAD'si). Okuyucunun
            // koşulsuz açık kalması, saldırganın yalnızca magic baytlarını
            // değiştirerek ZAYIF biçime düşürmesine izin veriyordu — yani
            // V2'nin getirdiği korumalar bir baytla atlanabiliyordu.
            //
            // Tamamen silmek yerine yapılandırmaya bağlamak bilinçli: gerçekten
            // V1 yedeği olan bir kurulumda okuyucuyu kaldırmak, o yedekleri
            // KALICI olarak erişilemez kılardı. Operatör açıkça açıyor.
            if (!_allowLegacyV1)
                throw new InvalidOperationException(
                    "This backup uses the legacy NVLT1 format, which is disabled. " +
                    "Set Vault:AllowLegacyV1=true to read it, then take a fresh backup.");

            await DecryptV1Async(encrypted, destination, ct);
            return;
        }

        throw new InvalidOperationException("This file is not a Namines Vault backup, or it is corrupt.");
    }

    /// <summary>
    /// NVLT3 çözme. V2 ile AYNI dosya düzeni; tek fark AAD'nin yedeğin kimliğini de
    /// taşıması — yanlış kimlikle çözmek GCM etiketini bozuyor, yani başka bir
    /// kaydın dosyası bu kaydın yerine konamyor (docs/last-review M-8).
    ///
    /// Gövde V2'den KOPYALANDI, ortak bir yardımcıya çıkarılmadı: iki biçimin
    /// çözücüsünü tek koda bağlamak, ileride birinde yapılan bir değişikliğin
    /// diğerinin şifreleme bağlamını sessizce değiştirmesi demek — eski dosyaların
    /// okunamaz hâle gelmesinin en sessiz yolu.
    /// </summary>
    private async Task DecryptV3Async(
        Stream encrypted, Stream destination, BackupIdentity identity, CancellationToken ct)
    {
        var salt = new byte[SaltSize];
        if (await ReadBlockAsync(encrypted, salt, ct) != SaltSize)
            throw new InvalidOperationException("Backup file is truncated.");

        var key = DeriveKey(salt);
        using var aes = new AesGcm(key, TagSize);
        CryptographicOperations.ZeroMemory(key);

        var header = new byte[4 + 1 + NonceSize + TagSize];
        long index = 0;

        while (true)
        {
            // "Final" bayraklı blok görülmeden EOF = kesilmiş dosya. Eskiden burada
            // `break` vardı ve kesilmiş bir dosya BAŞARILI sayılıyordu.
            var headerRead = await ReadBlockAsync(encrypted, header, ct);
            if (headerRead != header.Length)
                throw new InvalidOperationException("Backup file is truncated.");

            var length = BitConverter.ToInt32(header, 0);
            if (length < 0 || length > PlainBlockSize)
                throw new InvalidOperationException("Backup file is corrupt (bad block length).");

            var flag = header[4];
            if (flag > 1)
                throw new InvalidOperationException("Backup file is corrupt (bad block flag).");
            var isFinal = flag == 1;

            // Final olmayan bir blok her zaman TAM boyutlu; kısa bir blok ancak sonuncu olabilir.
            if (!isFinal && length != PlainBlockSize)
                throw new InvalidOperationException("Backup file is corrupt (short block in the middle of the stream).");

            var nonce = header.AsSpan(5, NonceSize).ToArray();
            var tag = header.AsSpan(5 + NonceSize, TagSize).ToArray();

            var cipher = new byte[length];
            if (await ReadBlockAsync(encrypted, cipher, ct) != length)
                throw new InvalidOperationException("Backup file is truncated.");

            var plain = new byte[length];
            // Etiket tutmazsa AuthenticationTagMismatchException fırlar — değiştirilmiş,
            // yeniden sıralanmış ya da başka bir yedekten yapıştırılmış bir blok SESSİZCE geri yüklenmez.
            aes.Decrypt(nonce, cipher, tag, plain, BuildAadV3(salt, index, isFinal, identity));

            await destination.WriteAsync(plain, ct);

            if (isFinal)
            {
                // Final bloktan sonra fazladan bayt olmamalı: sona eklenmiş çöp de bir oynama.
                var probe = new byte[1];
                if (await encrypted.ReadAsync(probe, ct) != 0)
                    throw new InvalidOperationException("Backup file is corrupt (data after the final block).");
                return;
            }

            index++;
        }
    }

    /// <summary>
    /// NVLT1 — YALNIZCA geriye dönük okuma. Bu biçimde blokların konumu ve dosyanın sonu
    /// doğrulanmıyor (bkz. sınıf yorumu); yeni yedek yazılmıyor.
    /// </summary>
    private async Task DecryptV2Async(Stream encrypted, Stream destination, CancellationToken ct)
    {
        var salt = new byte[SaltSize];
        if (await ReadBlockAsync(encrypted, salt, ct) != SaltSize)
            throw new InvalidOperationException("Backup file is truncated.");

        var key = DeriveKey(salt);
        using var aes = new AesGcm(key, TagSize);
        CryptographicOperations.ZeroMemory(key);

        var header = new byte[4 + 1 + NonceSize + TagSize];
        long index = 0;

        while (true)
        {
            // "Final" bayraklı blok görülmeden EOF = kesilmiş dosya. Eskiden burada
            // `break` vardı ve kesilmiş bir dosya BAŞARILI sayılıyordu.
            var headerRead = await ReadBlockAsync(encrypted, header, ct);
            if (headerRead != header.Length)
                throw new InvalidOperationException("Backup file is truncated.");

            var length = BitConverter.ToInt32(header, 0);
            if (length < 0 || length > PlainBlockSize)
                throw new InvalidOperationException("Backup file is corrupt (bad block length).");

            var flag = header[4];
            if (flag > 1)
                throw new InvalidOperationException("Backup file is corrupt (bad block flag).");
            var isFinal = flag == 1;

            // Final olmayan bir blok her zaman TAM boyutlu; kısa bir blok ancak sonuncu olabilir.
            if (!isFinal && length != PlainBlockSize)
                throw new InvalidOperationException("Backup file is corrupt (short block in the middle of the stream).");

            var nonce = header.AsSpan(5, NonceSize).ToArray();
            var tag = header.AsSpan(5 + NonceSize, TagSize).ToArray();

            var cipher = new byte[length];
            if (await ReadBlockAsync(encrypted, cipher, ct) != length)
                throw new InvalidOperationException("Backup file is truncated.");

            var plain = new byte[length];
            // Etiket tutmazsa AuthenticationTagMismatchException fırlar — değiştirilmiş,
            // yeniden sıralanmış ya da başka bir yedekten yapıştırılmış bir blok SESSİZCE geri yüklenmez.
            aes.Decrypt(nonce, cipher, tag, plain, BuildAad(salt, index, isFinal));

            await destination.WriteAsync(plain, ct);

            if (isFinal)
            {
                // Final bloktan sonra fazladan bayt olmamalı: sona eklenmiş çöp de bir oynama.
                var probe = new byte[1];
                if (await encrypted.ReadAsync(probe, ct) != 0)
                    throw new InvalidOperationException("Backup file is corrupt (data after the final block).");
                return;
            }

            index++;
        }
    }

    /// <summary>
    /// NVLT1 — YALNIZCA geriye dönük okuma. Bu biçimde blokların konumu ve dosyanın sonu
    /// doğrulanmıyor (bkz. sınıf yorumu); yeni yedek yazılmıyor.
    /// </summary>
    private async Task DecryptV1Async(Stream encrypted, Stream destination, CancellationToken ct)
    {
        using var aes = new AesGcm(_legacyKey, TagSize);
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
