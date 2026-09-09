using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Namines.Core.Models.Auth;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Bekleme penceresi dolmuş yönetilen veritabanlarını KALICI olarak siler (G3).
///
/// <b>Neden gecikmeli silme:</b> bir veritabanının yanlışlıkla silindiği çoğu
/// zaman ancak birileri onu kullanmayı denediğinde anlaşılır — bu da hafta
/// sonunu kapsayabilir. Anında silme, tek bir yanlış tıklamayı geri dönüşü
/// olmayan veri kaybına çevirirdi.
/// </summary>
public class GroundPurgeBackgroundService : BackgroundService
{
    /// <summary>
    /// Kontrol aralığı. Gecikme gün cinsinden ölçüldüğü için saatte bir
    /// kontrol fazlasıyla yeterli — daha sık uyanmak boşa sorgu demek.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GroundPurgeBackgroundService> _logger;

    public GroundPurgeBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<GroundPurgeBackgroundService> logger)
    {
        // BackgroundService singleton, GroundService scoped: doğrudan enjekte
        // etmek tutsak bağımlılık olurdu.
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var graceDays = _configuration.GetValue("Ground:DeleteGraceDays", GroundDefaults.DeleteGraceDays);
        _logger.LogInformation(
            "Ground silme isi basladi (bekleme penceresi: {Days} gun).", graceDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var ground = scope.ServiceProvider.GetRequiredService<GroundService>();

                var purged = await ground.PurgeExpiredAsync(graceDays, stoppingToken);
                if (purged > 0)
                    _logger.LogWarning(
                        "Ground: bekleme penceresi dolan {Count} veritabani kalici olarak silindi.",
                        purged);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Tek bir turun hatası döngüyü öldürmemeli: ölen bir silme işi,
                // silinmiş sayılan kaynakların sessizce faturalanmaya devam
                // etmesi demektir.
                _logger.LogError(ex, "Ground silme turu basarisiz oldu; dongu suruyor.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
