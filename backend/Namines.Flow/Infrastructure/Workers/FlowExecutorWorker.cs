using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Namines.Flow.Application;

namespace Namines.Flow.Infrastructure.Workers;

/// <summary>
/// Kuyruğa alınmış Namines Flow tetiklemelerini çalıştırır (Kritik Kontrol #6: Background Worker Ownership).
/// </summary>
public sealed class FlowExecutorWorker : BackgroundService
{
    private readonly IFlowJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FlowExecutorWorker> _logger;

    public FlowExecutorWorker(
        IFlowJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<FlowExecutorWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            FlowAutomationJob job;
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
                var appService = scope.ServiceProvider.GetRequiredService<IFlowApplicationService>();
                await appService.RunAsync(job.ProjectId, job.Diff, job.OldSchema, job.NewSchema, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Namines Flow: {ProjectId} icin otomasyon calistirilirken beklenmeyen hata; isci devam ediyor.", job.ProjectId);
            }
        }
    }
}
