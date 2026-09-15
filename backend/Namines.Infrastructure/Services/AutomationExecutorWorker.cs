using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Kuyruğa alınmış Namines Flow tetiklemelerini çalıştırır (Bölüm 3).
///
/// KENDİ DI kapsamını açıyor — <see cref="VaultBackupWorker"/> ile aynı
/// gerekçe: HTTP isteğinin DbContext'i yanıt döndüğünde atılıyor.
/// </summary>
public sealed class AutomationExecutorWorker : BackgroundService
{
    private readonly IAutomationJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutomationExecutorWorker> _logger;

    public AutomationExecutorWorker(IAutomationJobQueue queue, IServiceScopeFactory scopeFactory, ILogger<AutomationExecutorWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            AutomationJob job;
            try
            {
                job = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var executor = scope.ServiceProvider.GetRequiredService<IAutomationExecutor>();
                await executor.RunAsync(job.ProjectId, job.Diff, job.OldSchema, job.NewSchema, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Namines Flow: {ProjectId} icin otomasyon calistirilirken beklenmeyen hata; isci devam ediyor.", job.ProjectId);
            }
        }
    }
}
