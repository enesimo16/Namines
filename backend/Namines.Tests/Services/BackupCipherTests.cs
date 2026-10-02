using System;
using System.IO;
using System.Threading;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Namines.Vault.Storage;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// <see cref="BackupCipher"/> — NVLT2 biçimi.
///
/// <b>Neden bu testler var:</b> her blok kendi etiketini taşıyordu ama HİÇBİR ŞEY bir bloğu
/// konumuna, toplam sayıya ya da dosyaya bağlamıyordu ve EOF "başarı" sayılıyordu. Yedek
/// deposuna yazma erişimi olan (anahtarı olmayan) biri dosyanın kuyruğunu atıp ya da blokları
/// yer değiştirip geri yüklemeyi tetikleyebilirdi; her etiket geçerli çıkar,
/// <c>pg_restore --clean</c> ise canlı veritabanını kasten kesilmiş bir dökümden kurardı.
/// Bu sınıf o yolun KAPALI olduğunu kanıtlıyor.
/// </summary>
public class BackupCipherTests
{
    private const int Block = BackupCipher.PlainBlockSize;
    // Sabit başlık: magic(6) + tuz(16). Blok başlığı: uzunluk(4) + final(1) + nonce(12) + tag(16).
    private const int FileHeader = 6 + 16;
    private const int BlockHeader = 4 + 1 + 12 + 16;

    /// <param name="allowLegacyV1">
    /// NVLT1 okuma izni. Varsayılan <c>false</c> — biçim düşürme yolu kapalı
    /// (docs/last-review M-8). Eski yedeği okumak isteyen test bunu açıkça açıyor.
    /// </param>
    private static BackupCipher NewCipher(string? key = null, bool allowLegacyV1 = false) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vault:BackupEncryptionKey"] = key ?? new string('k', 40),
                ["Vault:AllowLegacyV1"] = allowLegacyV1 ? "true" : "false",
            })
            .Build());

    private static byte[] Sample(int length)
    {
        var data = new byte[length];
        new Random(1234).NextBytes(data);
        return data;
    }

    private static async Task<byte[]> EncryptAsync(BackupCipher cipher, byte[] plain)
    {
        using var source = new MemoryStream(plain);
        using var target = new MemoryStream();
        await cipher.EncryptAsync(source, target, CancellationToken.None);
        return target.ToArray();
    }

    private static async Task<byte[]> DecryptAsync(BackupCipher cipher, byte[] encrypted)
    {
        using var source = new MemoryStream(encrypted);
        using var target = new MemoryStream();
        await cipher.DecryptAsync(source, target, CancellationToken.None);
        return target.ToArray();
    }

    /// <summary>Şifreli dosyadaki blokları (başlık dahil) ayırır.</summary>
    private static List<byte[]> SplitBlocks(byte[] encrypted)
    {
        var blocks = new List<byte[]>();
        var offset = FileHeader;
        while (offset < encrypted.Length)
        {
            var length = BitConverter.ToInt32(encrypted, offset);
            var size = BlockHeader + length;
            blocks.Add(encrypted.AsSpan(offset, size).ToArray());
            offset += size;
        }
        return blocks;
    }

    private static byte[] Join(byte[] fileHeader, IEnumerable<byte[]> blocks) =>
        fileHeader.Concat(blocks.SelectMany(b => b)).ToArray();

    [Theory]
    [InlineData(0)]                  // boş girdi: tek, sıfır uzunluklu FINAL blok
    [InlineData(1)]
    [InlineData(Block - 1)]
    [InlineData(Block)]              // tam sınır: son blok TAM boyutlu ama yine de FINAL işaretli olmalı
    [InlineData(Block + 1)]
    [InlineData(2 * Block + 12345)]
    public async Task Round_trips_at_every_block_boundary(int length)
    {
        var cipher = NewCipher();
        var plain = Sample(length);

        var encrypted = await EncryptAsync(cipher, plain);

        Assert.Equal(plain, await DecryptAsync(cipher, encrypted));
    }

    [Fact]
    public async Task The_same_input_never_encrypts_to_the_same_bytes()
    {
        // Dosya başına rastgele tuz + blok başına rastgele nonce: aynı içerik iki kez yedeklenince
        // eşleşen bir şifreli metin, saldırgana "bu iki yedek aynı" bilgisini verirdi.
        var cipher = NewCipher();
        var plain = Sample(1000);

        Assert.NotEqual(await EncryptAsync(cipher, plain), await EncryptAsync(cipher, plain));
    }

    [Fact]
    public async Task Dropping_the_tail_of_the_file_is_detected()
    {
        // ASIL SALDIRI: son blok(lar)ı at, kalan blokların etiketleri hâlâ geçerli. Eskiden
        // EOF başarı sayılıyor ve kesilmiş döküm geri yükleniyordu.
        var cipher = NewCipher();
        var encrypted = await EncryptAsync(cipher, Sample(3 * Block));
        var blocks = SplitBlocks(encrypted);
        Assert.Equal(3, blocks.Count);

        var truncated = Join(encrypted[..FileHeader], blocks.Take(2));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => DecryptAsync(cipher, truncated));
        Assert.Contains("truncated", ex.Message);
    }

    [Fact]
    public async Task Cutting_the_file_inside_a_block_is_detected()
    {
        var cipher = NewCipher();
        var encrypted = await EncryptAsync(cipher, Sample(Block + 500));

        await Assert.ThrowsAsync<InvalidOperationException>(() => DecryptAsync(cipher, encrypted[..^10]));
    }

    [Fact]
    public async Task Reordering_blocks_is_detected()
    {
        var cipher = NewCipher();
        var encrypted = await EncryptAsync(cipher, Sample(3 * Block));
        var blocks = SplitBlocks(encrypted);

        // İlk iki (tam boyutlu, final olmayan) bloğun yeri değişiyor: uzunluklar eşit olduğu için
        // yapısal kontrol geçer; yalnızca indeks bağlaması yakalar.
        var swapped = Join(encrypted[..FileHeader], new[] { blocks[1], blocks[0], blocks[2] });

        await Assert.ThrowsAnyAsync<CryptographicException>(() => DecryptAsync(cipher, swapped));
    }

    [Fact]
    public async Task Duplicating_a_block_is_detected()
    {
        var cipher = NewCipher();
        var encrypted = await EncryptAsync(cipher, Sample(3 * Block));
        var blocks = SplitBlocks(encrypted);

        var duplicated = Join(encrypted[..FileHeader], new[] { blocks[0], blocks[0], blocks[1], blocks[2] });

        await Assert.ThrowsAnyAsync<CryptographicException>(() => DecryptAsync(cipher, duplicated));
    }

    [Fact]
    public async Task Blocks_pasted_from_another_backup_are_rejected()
    {
        // Anahtar aynı olduğu için, tuza bağlanmasaydı başka bir yedeğin herhangi bir bloğu bu
        // yedeğe yapıştırılıp geçerli bir etiketle çözülürdü.
        var cipher = NewCipher();
        var first = await EncryptAsync(cipher, Sample(2 * Block));
        var second = await EncryptAsync(cipher, Sample(2 * Block));

        var firstBlocks = SplitBlocks(first);
        var secondBlocks = SplitBlocks(second);

        var franken = Join(first[..FileHeader], new[] { firstBlocks[0], secondBlocks[1] });

        await Assert.ThrowsAnyAsync<CryptographicException>(() => DecryptAsync(cipher, franken));
    }

    [Fact]
    public async Task Flipping_the_final_flag_is_detected()
    {
        var cipher = NewCipher();
        var encrypted = await EncryptAsync(cipher, Sample(2 * Block));
        var blocks = SplitBlocks(encrypted);

        // İlk bloğu "final" diye işaretlemek dosyayı erken bitirmeye çalışır.
        var tampered = blocks[0].ToArray();
        tampered[4] = 1;

        await Assert.ThrowsAnyAsync<Exception>(() =>
            DecryptAsync(cipher, Join(encrypted[..FileHeader], new[] { tampered, blocks[1] })));
    }

    [Fact]
    public async Task Bytes_appended_after_the_final_block_are_rejected()
    {
        var cipher = NewCipher();
        var encrypted = await EncryptAsync(cipher, Sample(100));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DecryptAsync(cipher, encrypted.Concat(new byte[] { 0x00 }).ToArray()));
        Assert.Contains("after the final block", ex.Message);
    }

    [Fact]
    public async Task A_bit_flip_inside_a_block_is_detected()
    {
        var cipher = NewCipher();
        var encrypted = await EncryptAsync(cipher, Sample(5000));
        encrypted[^1] ^= 0x01;

        await Assert.ThrowsAnyAsync<CryptographicException>(() => DecryptAsync(cipher, encrypted));
    }

    [Fact]
    public async Task A_different_passphrase_cannot_decrypt()
    {
        var encrypted = await EncryptAsync(NewCipher(new string('a', 40)), Sample(2000));

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => DecryptAsync(NewCipher(new string('b', 40)), encrypted));
    }

    [Fact]
    public async Task The_key_is_not_a_bare_sha256_of_the_passphrase()
    {
        // Eski türetme: tuzsuz tek tur SHA-256. Yeni dosya, o anahtarla ÇÖZÜLEMEMELİ; çözülebilseydi
        // KDF gerçekte devrede olmazdı.
        var passphrase = new string('k', 40);
        var encrypted = await EncryptAsync(NewCipher(passphrase), Sample(1000));

        var legacyKey = SHA256.HashData(Encoding.UTF8.GetBytes(passphrase));
        using var aes = new AesGcm(legacyKey, 16);

        var nonce = encrypted[(FileHeader + 5)..(FileHeader + 5 + 12)];
        var tag = encrypted[(FileHeader + 5 + 12)..(FileHeader + 5 + 12 + 16)];
        var cipherText = encrypted[(FileHeader + BlockHeader)..(FileHeader + BlockHeader + 1000)];

        Assert.ThrowsAny<CryptographicException>(() => aes.Decrypt(nonce, cipherText, tag, new byte[1000]));
    }

    [Fact]
    public async Task Legacy_nvlt1_backups_stay_readable_WHEN_EXPLICITLY_ENABLED()
    {
        // Geriye dönük uyumluluk: NVLT2'den önce alınmış yedekler, anahtar değişmeden
        // geri yüklenebilmeli. Burada eski yazıcı (NVLT1) test içinde yeniden kuruluyor.
        //
        // <b>Sözleşme DEĞİŞTİ (docs/last-review M-8):</b> okuma artık açık bir
        // yapılandırma gerektiriyor. Gerçekten V1 yedeği olan bir kurulumda okuyucuyu
        // tamamen kaldırmak o yedekleri kalıcı erişilemez kılardı, o yüzden yol
        // KAPATILMADI — kapıya bağlandı.
        var passphrase = new string('k', 40);
        var plain = Sample(Block + 777);
        var legacy = WriteLegacyV1(passphrase, plain);

        Assert.Equal(plain, await DecryptAsync(NewCipher(passphrase, allowLegacyV1: true), legacy));
    }

    [Fact]
    public async Task Legacy_nvlt1_is_REFUSED_by_default()
    {
        // <b>Bulunan açık (docs/last-review M-8):</b> V1 okuyucusu KOŞULSUZ açıktı.
        // V1 biçimi NVLT2'nin eklediği korumaları taşımıyor (dosyaya özel tuz, blok
        // indeksi/final bayrağı AAD'si), yani saldırgan yalnızca MAGIC BAYTLARINI
        // değiştirerek zayıf biçime düşürebiliyordu — V2'nin getirdiği korumalar tek
        // bir baytla atlanabiliyordu.
        var passphrase = new string('k', 40);
        var legacy = WriteLegacyV1(passphrase, Sample(Block + 777));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DecryptAsync(NewCipher(passphrase), legacy));

        // Hata mesajı operatöre NE yapacağını söylemeli: sessiz bir ret, eski
        // yedeğini geri yüklemeye çalışan birini çıkmaz sokağa sokardı.
        Assert.Contains("Vault:AllowLegacyV1", error.Message);
    }

    [Fact]
    public async Task Garbage_is_not_mistaken_for_a_backup()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DecryptAsync(NewCipher(), Encoding.ASCII.GetBytes("this is not a backup file at all")));
    }

    [Fact]
    public void A_short_passphrase_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => NewCipher("too-short"));
    }

    /// <summary>NVLT1 yazıcısının birebir kopyası: tuzsuz SHA-256 anahtarı, AAD'siz bloklar.</summary>
    private static byte[] WriteLegacyV1(string passphrase, byte[] plain)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(passphrase));
        using var aes = new AesGcm(key, 16);
        using var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes("NVLT1\0"));

        for (var offset = 0; offset < plain.Length; offset += Block)
        {
            var count = Math.Min(Block, plain.Length - offset);
            var nonce = RandomNumberGenerator.GetBytes(12);
            var cipher = new byte[count];
            var tag = new byte[16];
            aes.Encrypt(nonce, plain.AsSpan(offset, count), cipher, tag);

            output.Write(BitConverter.GetBytes(count));
            output.Write(nonce);
            output.Write(tag);
            output.Write(cipher);
        }

        return output.ToArray();
    }

    // ── NVLT3: dosyayı BİR KAYDA bağlama (docs/last-review M-8) ───────────

    private static BackupIdentity Identity(string projectId = "proj-1", string backupId = "backup-1") =>
        new(projectId, backupId);

    private static async Task<byte[]> EncryptAsync(
        BackupCipher cipher, byte[] plain, BackupIdentity? identity)
    {
        await using var source = new MemoryStream(plain);
        await using var destination = new MemoryStream();
        await cipher.EncryptAsync(source, destination, CancellationToken.None, identity);
        return destination.ToArray();
    }

    private static async Task<byte[]> DecryptAsync(
        BackupCipher cipher, byte[] encrypted, BackupIdentity? identity)
    {
        await using var source = new MemoryStream(encrypted);
        await using var destination = new MemoryStream();
        await cipher.DecryptAsync(source, destination, CancellationToken.None, identity);
        return destination.ToArray();
    }

    [Fact]
    public async Task Kimlikli_yedek_AYNI_kimlikle_cozuluyor()
    {
        var plain = Sample(Block + 321);
        var cipher = NewCipher();

        var encrypted = await EncryptAsync(cipher, plain, Identity());

        Assert.Equal(plain, await DecryptAsync(cipher, encrypted, Identity()));
    }

    [Fact]
    public async Task Kimlikli_yedek_NVLT3_basligiyla_yaziliyor()
    {
        var encrypted = await EncryptAsync(NewCipher(), Sample(64), Identity());

        Assert.Equal("NVLT3", Encoding.ASCII.GetString(encrypted, 0, 5));
    }

    [Fact]
    public async Task Kimlik_verilmezse_ESKİ_NVLT2_bicimi_yaziliyor()
    {
        // Kimliği olmayan bir çağıranı bağlamak mümkün değil; uydurma bir kimlik
        // yazmak bağlamayı var göstermek olurdu.
        var encrypted = await EncryptAsync(NewCipher(), Sample(64), identity: null);

        Assert.Equal("NVLT2", Encoding.ASCII.GetString(encrypted, 0, 5));
    }

    [Fact]
    public async Task BASKA_projenin_yedegi_bu_kayda_KONAMIYOR()
    {
        // <b>Bulunan açığın ta kendisi:</b> parola SUNUCU GENELİ olduğu için A
        // projesinin yedeği B projesinin kaydındaki anahtarın yerine konsa sorunsuz
        // çözülüyordu — bir kiracı, başka bir kiracının yedeğini kendi geri
        // yüklemesinde kullanabilirdi.
        var cipher = NewCipher();
        var encrypted = await EncryptAsync(cipher, Sample(Block + 7), Identity(projectId: "kurban-proje"));

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => DecryptAsync(cipher, encrypted, Identity(projectId: "saldirgan-proje")));
    }

    [Fact]
    public async Task AYNI_projenin_BASKA_yedegi_de_konamiyor()
    {
        // Proje tek başına yetmez: "dünkü yedeği bugünün kaydına koymak" da sessiz
        // bir veri değişimi.
        var cipher = NewCipher();
        var encrypted = await EncryptAsync(cipher, Sample(Block + 7), Identity(backupId: "dunku-yedek"));

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => DecryptAsync(cipher, encrypted, Identity(backupId: "bugunku-yedek")));
    }

    [Fact]
    public async Task NVLT3_kimlik_VERILMEZSE_okunmuyor()
    {
        // "Kimlik verilmedi, bağlamayı atla" demek, biçimin getirdiği korumayı
        // çağıranın ihmaliyle kapatmak olurdu — ve saldırganın yapacağı şey tam
        // olarak o çağrıyı bulmak olurdu.
        var cipher = NewCipher();
        var encrypted = await EncryptAsync(cipher, Sample(64), Identity());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DecryptAsync(cipher, encrypted, identity: null));

        Assert.Contains("identity", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NVLT2_yedekleri_kimlik_verilse_de_OKUNABILİYOR()
    {
        // Geriye dönük uyumluluk: eski biçim kimlik taşımıyor ve geriye dönük bir
        // bağlama uydurulamaz; o dosyalar okunmaya devam etmeli.
        var plain = Sample(Block + 11);
        var cipher = NewCipher();
        var legacy = await EncryptAsync(cipher, plain, identity: null);

        Assert.Equal(plain, await DecryptAsync(cipher, legacy, Identity()));
    }

    [Fact]
    public async Task NVLT3_bicim_dusurmeyle_atlatilamiyor()
    {
        // Saldırganın en kolay denemesi: magic'i NVLT2 yapıp bağlamayı düşürmek.
        // AAD magic'i de içerdiği için etiket tutmuyor.
        var cipher = NewCipher();
        var encrypted = await EncryptAsync(cipher, Sample(Block + 5), Identity());

        Encoding.ASCII.GetBytes("NVLT2\0").CopyTo(encrypted, 0);

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => DecryptAsync(cipher, encrypted, Identity()));
    }

    /// <summary>
    /// <b>Kimliğin İÇERİĞİ bağlıyor, UZUNLUĞU değil.</b>
    ///
    /// Bu test, ilk yazdığım NVLT3 testlerinin <b>yanlış sebepten</b> geçtiğini
    /// mutasyon testi gösterdikten sonra eklendi: kimliği AAD'nin İÇERİĞİNDEN
    /// çıkardığımda testler hâlâ yeşildi, çünkü AAD dizisinin BOYUTU kimlik
    /// uzunluğuna göre hesaplanıyor ve farklı uzunluktaki iki kimlik zaten farklı
    /// AAD veriyordu. Yani bağlama "çalışıyor" görünüyordu ama kanıt uzunluk
    /// tesadüfünden geliyordu.
    ///
    /// AYNI uzunlukta, farklı içerikli kimlikler bunu kesin olarak ölçüyor.
    /// </summary>
    [Fact]
    public async Task AYNI_uzunlukta_farkli_kimlik_de_cozemiyor()
    {
        var cipher = NewCipher();

        // "proj-aaa" ve "proj-bbb" aynı uzunlukta.
        var encrypted = await EncryptAsync(
            cipher, Sample(Block + 3), new BackupIdentity("proj-aaa", "backup-1"));

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => DecryptAsync(cipher, encrypted, new BackupIdentity("proj-bbb", "backup-1")));
    }

    [Fact]
    public async Task AYNI_uzunlukta_farkli_yedek_kimligi_de_cozemiyor()
    {
        var cipher = NewCipher();

        var encrypted = await EncryptAsync(
            cipher, Sample(Block + 3), new BackupIdentity("proj-1", "backup-aaa"));

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => DecryptAsync(cipher, encrypted, new BackupIdentity("proj-1", "backup-bbb")));
    }

    /// <summary>
    /// Alanların S I N I R I da bağlıyor: <c>"ab" + "c"</c> ile <c>"a" + "bc"</c>
    /// birleştirildiğinde aynı bayt dizisini verir. Uzunluk önekleri olmasa bu iki
    /// farklı yedek AYNI bağlamı paylaşır ve biri diğerinin yerine konabilirdi.
    /// </summary>
    [Fact]
    public async Task Alan_siniri_kaymasi_ayni_baglami_URETMIYOR()
    {
        var cipher = NewCipher();

        var encrypted = await EncryptAsync(
            cipher, Sample(Block + 3), new BackupIdentity("ab", "c"));

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => DecryptAsync(cipher, encrypted, new BackupIdentity("a", "bc")));
    }
}
