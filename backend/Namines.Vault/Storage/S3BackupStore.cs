using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Namines.Vault.Abstractions;

namespace Namines.Vault.Storage;

/// <summary>
/// Yedekleri <b>S3 uyumlu bir nesne deposunda</b> tutar.
///
/// <b>Açık kararı kapatan yer burası</b> (<c>02-ERISIM-VE-DEPLOY.md</c>):
/// "S3 mi MinIO mu" diye seçmek gerekmiyordu — MinIO S3 protokolünü konuşuyor,
/// dolayısıyla tek uygulama ikisini de karşılıyor. Ayrı iki sınıf yazmak, aynı
/// protokolü iki kez uygulamak olurdu.
///
/// <b><see cref="FileSystemBackupStore"/> yerine bunu kullanmanın sebebi</b>
/// onun kendi yorumunda yazıyor: sunucu diski yedek boyutuyla doluyor, birden
/// fazla instance aynı dosyayı göremiyor ve sunucu yeniden kurulursa yedekler
/// gidiyor. Üçü de nesne deposunda yok.
///
/// <b>Şifreleme burada DEĞİL:</b> içerik depoya gelmeden
/// <see cref="BackupCipher"/> ile şifreleniyor. Deponun kendi şifrelemesine
/// güvenmek, o deponun erişim kontrolünü tek savunma hattı yapardı.
/// </summary>
public sealed class S3BackupStore : IBackupStore, IDisposable
{
    /// <summary>
    /// Çok parçalı yüklemede parça boyutu. S3'ün alt sınırı 5 MiB.
    ///
    /// Akış boyutu ÖNCEDEN bilinmiyor (dump doğrudan boruya akıyor), bu yüzden
    /// SDK'nın parçalı yüklemesi şart: tek istekle göndermek, içeriğin tamamını
    /// belleğe alıp uzunluğunu ölçmek demekti.
    /// </summary>
    private const int PartSizeBytes = 8 * 1024 * 1024;

    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly string _description;

    public S3BackupStore(IConfiguration configuration)
    {
        _bucket = configuration["Vault:S3:Bucket"]
            ?? throw new InvalidOperationException("Vault:S3:Bucket tanımlı değil.");

        var accessKey = configuration["Vault:S3:AccessKey"]
            ?? throw new InvalidOperationException("Vault:S3:AccessKey tanımlı değil.");
        var secretKey = configuration["Vault:S3:SecretKey"]
            ?? throw new InvalidOperationException("Vault:S3:SecretKey tanımlı değil.");

        var serviceUrl = configuration["Vault:S3:ServiceUrl"];
        var region = configuration["Vault:S3:Region"] ?? "us-east-1";

        var config = new AmazonS3Config { AuthenticationRegion = region };

        if (string.IsNullOrWhiteSpace(serviceUrl))
        {
            // Gerçek AWS.
            config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region);
            _description = $"S3 ({_bucket}, {region})";
        }
        else
        {
            // MinIO ve diğer S3 uyumlu sunucular.
            config.ServiceURL = serviceUrl;
            // ForcePathStyle ŞART: MinIO varsayılan olarak sanal-host adresini
            // (bucket.host) desteklemiyor ve o adres DNS'te yok — istek
            // çözülemeyen bir ada gider ve hata bağlantı hatası gibi görünür.
            config.ForcePathStyle = true;
            _description = $"S3 uyumlu depo ({_bucket} @ {serviceUrl})";
        }

        _client = new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), config);
    }

    public string Description => _description;

    public async Task<long> PutAsync(string key, Stream content, CancellationToken ct)
    {
        using var transfer = new Amazon.S3.Transfer.TransferUtility(_client);

        await transfer.UploadAsync(new Amazon.S3.Transfer.TransferUtilityUploadRequest
        {
            BucketName = _bucket,
            Key = Normalize(key),
            InputStream = content,
            PartSize = PartSizeBytes,
            AutoCloseStream = false,
        }, ct);

        // Boyut YÜKLEMEDEN SONRA depodan okunuyor, akıştan sayılmıyor: burada
        // saklanan sayı kullanıcıya "diskte ne kadar yer tutuyor" diye
        // gösteriliyor ve doğru kaynak deponun kendisi.
        var meta = await _client.GetObjectMetadataAsync(_bucket, Normalize(key), ct);
        return meta.ContentLength;
    }

    public async Task<Stream?> OpenAsync(string key, CancellationToken ct)
    {
        try
        {
            var response = await _client.GetObjectAsync(_bucket, Normalize(key), ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Sözleşme: anahtar yoksa null. İstisna fırlatmak, çağıranı
            // "dosya yok" ile "depo erişilemez" arasında kör bırakırdı.
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        // S3 zaten idempotent: olmayan bir anahtarı silmek hata değil.
        await _client.DeleteObjectAsync(_bucket, Normalize(key), ct);
    }

    /// <summary>
    /// Anahtarı S3'ün beklediği biçime getirir.
    ///
    /// Baştaki <c>/</c> kaldırılıyor: S3'te bu, adı boş bir klasörle başlayan
    /// AYRI bir nesne demek — yazdığınızı okuyamazdınız.
    /// </summary>
    private static string Normalize(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key is required.", nameof(key));

        return key.Replace('\\', '/').TrimStart('/');
    }

    public void Dispose() => _client.Dispose();
}
