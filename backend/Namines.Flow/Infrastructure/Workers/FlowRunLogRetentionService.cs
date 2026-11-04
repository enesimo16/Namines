using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Namines.Flow.Application;

namespace Namines.Flow.Infrastructure.Workers;

/// <summary>
/// Eskimiş Namines Flow çalışma kayıtlarını siler (Kritik Kontrol #6: Background Worker Ownership).
/// </summary>
public sealed class FlowRunLogRetentionService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);
    private const int DefaultRetentionDays = 30;
    private const int DefaultMaxPerRule = 200;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FlowRunLogRetentionService> _logger;

    public FlowRunLogRetentionService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<FlowRunLogRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retentionDays = int.TryParse(_configuration["NaminesFlow:RunLogRetentionDays"], out var d)
            ? d
            : DefaultRetentionDays;
        var maxPerRule = int.TryParse(_configuration["NaminesFlow:RunLogMaxPerRule"], out var m)
            ? m
            : DefaultMaxPerRule;

        _logger.LogInformation(
            "Namines Flow calisma kaydi temizligi basladi ({Days} gun / kural basina {Max} kayit).",
            retentionDays, maxPerRule);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var appService = scope.ServiceProvider.GetRequiredService<IFlowApplicationService>();
                await appService.PurgeRunLogsAsync(retentionDays, maxPerRule, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Namines Flow calisma kaydi temizligi basarisiz oldu.");
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
