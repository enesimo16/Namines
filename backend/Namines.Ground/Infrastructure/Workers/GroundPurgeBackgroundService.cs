using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Namines.Core.Models.Auth;
using Namines.Ground.Application;
using Namines.Ground.Domain;

namespace Namines.Ground.Infrastructure.Workers;

/// <summary>
/// Bekleme penceresi dolmuş yönetilen veritabanlarını kalıcı olarak silen Ground modülü arka plan işçisi
/// (Kritik Kontrol #6: Background Worker Ownership).
/// </summary>
public sealed class GroundPurgeBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GroundPurgeBackgroundService> _logger;

    public GroundPurgeBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<GroundPurgeBackgroundService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var graceDaysRaw = _configuration["Ground:DeleteGraceDays"];
        var graceDays = int.TryParse(graceDaysRaw, out var parsed) ? parsed : GroundDefaults.DeleteGraceDays;
        _logger.LogInformation(
            "Ground silme isi basladi (bekleme penceresi: {Days} gun).", graceDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var ground = scope.ServiceProvider.GetRequiredService<IGroundApplicationService>();

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
