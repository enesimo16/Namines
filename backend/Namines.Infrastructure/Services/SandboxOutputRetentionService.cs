using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Namines.Core.Interfaces;

namespace Namines.Infrastructure.Services;

/// <summary>
/// <c>Outputs/</c> altındaki sandbox ve CoderAI çıktılarını saklama süresi
/// dolduğunda siler, ve süresi geçmiş iş kayıtlarını bellekten düşürür.
///
/// <b>Bulunan açık (docs/last-review M-6):</b> <c>DockerJobManager.EvictExpiredJobs</c>
/// vardı ama <b>hiçbir yerden çağrılmıyordu</b>. Sonuç: iş kayıtları bellekte
/// sınırsız birikiyordu ve <c>Outputs/</c> altındaki zip'ler <b>asla
/// silinmiyordu</b> — oysa indirme ucu kullanıcıya "saklama penceresi (2 saat)
/// geçmiş olabilir" diyordu. Ürün tutmadığı bir söz veriyordu, ve o dosyalar
/// kullanıcının tam şemasını (CoderAI paketlerinde ayrıca bağlantı yapılandırması)
/// taşıyor.
///
/// <b>Neden <see cref="DockerSweeperBackgroundService"/>'e eklenmedi:</b> o servis
/// <c>Sandbox:Enabled</c> ile kapılı ve varsayılan KAPALI, çünkü host'un docker
/// socket'ine ihtiyaç duyuyor. Ama <c>Outputs/</c> dosyaları o bayraktan
/// BAĞIMSIZ olarak birikiyor (<c>/api/docker/run</c> ve CoderAI paketleme uçları
/// bayrağa bakmıyor). Temizliği aynı kapının arkasına koymak, açığı kapatmış gibi
/// görünüp kapatmamak olurdu.
///
/// <b>İki ayrı silme ölçütü var ve ikisi de gerekli:</b>
/// <list type="number">
///   <item>
///     Bellekteki süresi dolmuş işlerin dosyaları — normal akış.
///   </item>
///   <item>
///     <b>Sahipsiz dosyalar:</b> uygulama yeniden başladığında iş sözlüğü boşalıyor,
///     yani o andan sonra hiçbir eski dosya kimlikle eşleşemez ve yalnızca 1. ölçüte
///     bakan bir temizlik onları SONSUZA KADAR bırakırdı. Bu yüzden dosya YAŞINA da
///     bakılıyor. Gerçek bir dağıtımda yeniden başlatma sıradan bir olay olduğu için
///     asıl temizliği yapan ölçüt büyük olasılıkla bu.
///   </item>
/// </list>
/// </summary>
public class SandboxOutputRetentionService : BackgroundService
{
    /// <summary>
    /// Bir çıktı dosyasının diskte tutulma süresi.
    ///
    /// <c>DockerJobManager.JobRetention</c> ile AYNI olmak zorunda ve oradan
    /// okunmuyor, çünkü o tür API katmanında. Değer değişirse ikisi birden
    /// değişmeli; <c>DockerController</c>'ın kullanıcıya söylediği süre de buna
    /// bağlı (bkz. oradaki "retention window" mesajı).
    /// </summary>
    private static readonly TimeSpan OutputRetention = TimeSpan.FromHours(2);

    /// <summary>
    /// Sahipsiz dosyalara verilen EK pay.
    ///
    /// Neden: bir dosya, işi hâlâ çalışırken de diskte duruyor olabilir. Yaş
    /// ölçütünü tam retention süresinde uygulamak, uzun süren ama canlı bir işin
    /// çıktısını altından çekebilirdi. Pay, "kesinlikle kimsenin beklemediği"
    /// eşiğini net kılıyor.
    /// </summary>
    private static readonly TimeSpan OrphanGrace = TimeSpan.FromHours(1);

    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(15);

    private readonly ILogger<SandboxOutputRetentionService> _logger;
    private readonly ISandboxJobRegistry _jobs;
    private readonly string _outputsDirectory;

    public SandboxOutputRetentionService(
        ILogger<SandboxOutputRetentionService> logger,
        ISandboxJobRegistry jobs,
        IConfiguration configuration)
    {
        _logger = logger;
        _jobs = jobs;

        // Uçlarla AYNI yol: `Path.Combine(Directory.GetCurrentDirectory(), "Outputs")`.
        // Yapılandırmadan okunabilir olması, dağıtımda çıktıların ayrı bir birime
        // alınmasını mümkün kılıyor; varsayılan mevcut davranışı koruyor.
        _outputsDirectory = configuration["Sandbox:OutputsDirectory"]
            ?? Path.Combine(Directory.GetCurrentDirectory(), "Outputs");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Sandbox cikti temizligi basliyor: {Directory}, saklama {Hours} saat.",
            _outputsDirectory, OutputRetention.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Sweep();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Temizlik bir yan iş: hata vermesi uygulamayı etkilememeli, ama
                // sessiz de kalmamalı — sessiz kalan bir temizlik, \xe7alışmadığı
                // fark edilmeyen bir temizliktir (bu bulgunun ta kendisi).
                _logger.LogError(ex, "Sandbox cikti temizligi basarisiz oldu.");
            }

            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Sandbox cikti temizligi duruyor.");
    }

    private void Sweep()
    {
        // Bellekteki tahliye, dizin olmasa bile yapılmalı: iş kayıtları diskten
        // bağımsız birikiyor.
        var expiredJobIds = _jobs.EvictExpiredJobs();

        if (!Directory.Exists(_outputsDirectory))
        {
            if (expiredJobIds.Count > 0)
                _logger.LogInformation("{Count} suresi dolmus is kaydi bellekten dusuruldu.", expiredJobIds.Count);
            return;
        }

        var deleted = 0;

        // 1) Tahliye edilen işlerin dosyaları — ad kalıbı uçlarla aynı:
        //    `backup_{jobId}.zip` / `coderai_{jobId}.zip`. Kalıp yerine kimliği
        //    İÇEREN dosyayı aramak, yeni bir önek eklendiğinde sessizce
        //    temizlenmeyen bir dosya türü bırakmamak için.
        foreach (var jobId in expiredJobIds)
        {
            foreach (var path in SafeEnumerate($"*{jobId}*").Where(IsOwnedOutput))
                if (TryDelete(path)) deleted++;
        }

        // 2) Sahipsiz dosyalar — yeniden başlatmadan sonra 1. ölçüt hiçbir şey
        //    bulamaz (bkz. sınıf notu). Aktif işlerin dosyalarına DOKUNULMUYOR.
        var activeJobIds = _jobs.GetActiveJobIds();
        var orphanCutoff = DateTime.UtcNow - (OutputRetention + OrphanGrace);

        foreach (var path in SafeEnumerate("*").Where(IsOwnedOutput))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (activeJobIds.Any(id => name.Contains(id, StringComparison.OrdinalIgnoreCase)))
                continue;

            // Yazma zamanı, oluşturma zamanı DEĞİL: bazı dosya sistemlerinde
            // kopyalama oluşturma zamanını tazeliyor ve dosya hiç yaşlanmıyor gibi
            // görünüyor.
            DateTime lastWrite;
            try { lastWrite = File.GetLastWriteTimeUtc(path); }
            catch (Exception ex) { _logger.LogDebug(ex, "{Path} yasi okunamadi.", path); continue; }

            if (lastWrite < orphanCutoff && TryDelete(path)) deleted++;
        }

        if (deleted > 0 || expiredJobIds.Count > 0)
            _logger.LogInformation(
                "Sandbox temizligi: {Files} dosya silindi, {Jobs} is kaydi dusuruldu.",
                deleted, expiredJobIds.Count);
    }

    /// <summary>
    /// Dosya bu servisin kendi ürettiği çıktı kalıplarından biri mi.
    ///
    /// <b>Neden:</b> sahipsiz dosya taraması eskiden dizindeki HER dosyayı yaşına göre
    /// siliyordu; <c>Sandbox:OutputsDirectory</c> ortak bir birimi gösterirse (ya da biri
    /// oraya başka bir şey koyarsa) ilgisiz dosyalar 3 saat sonra kaybolurdu. Kalıplar
    /// üreticilerle aynı: <c>{jobId}.bak|sql|tar</c> (DockerBackupService),
    /// <c>coderai_*.zip</c> (CoderAI paketleme) ve <c>backup_*.zip</c>.
    /// </summary>
    private static bool IsOwnedOutput(string path)
    {
        var name = Path.GetFileName(path);

        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
            (name.StartsWith("coderai_", StringComparison.OrdinalIgnoreCase) ||
             name.StartsWith("backup_", StringComparison.OrdinalIgnoreCase)))
            return true;

        var extension = Path.GetExtension(name);
        return (extension.Equals(".bak", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".sql", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".tar", StringComparison.OrdinalIgnoreCase)) &&
               Guid.TryParse(Path.GetFileNameWithoutExtension(name), out _);
    }

    private string[] SafeEnumerate(string pattern)
    {
        try
        {
            // TopDirectoryOnly: alt dizinlere inmek, bu servisin sahibi olmadığı
            // bir şeyi silmesi riskini açardı.
            return Directory.GetFiles(_outputsDirectory, pattern, SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Directory} okunamadi.", _outputsDirectory);
            return Array.Empty<string>();
        }
    }

    private bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            _logger.LogDebug("Silindi: {Path}", path);
            return true;
        }
        catch (Exception ex)
        {
            // Dosya kilitli olabilir (indirme sürüyor). Bir sonraki turda yeniden
            // denenecek; hatayı yükseltmek tüm temizliği durdururdu.
            _logger.LogDebug(ex, "{Path} silinemedi, sonraki turda denenecek.", path);
            return false;
        }
    }
}
