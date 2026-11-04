using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Namines.BuildingBlocks.Security;
using Namines.Core.Models.Auth;
using Namines.Vault.Application;
using Namines.Vault.Infrastructure.Persistence;

namespace Namines.Vault.Infrastructure.Workers;

/// <summary>
/// Zamanlanmış yedekleri çalıştıran Vault modülü arka plan işi
/// (Kritik Kontrol #6: Background Worker Ownership).
/// </summary>
public sealed class VaultScheduleBackgroundService : BackgroundService
{
    private const string SystemActorId = "system:vault-scheduler";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VaultScheduleBackgroundService> _logger;

    public VaultScheduleBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<VaultScheduleBackgroundService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Vault zamanlayici basladi (kontrol araligi: {Interval}).", PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDueSchedulesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Vault zamanlayici turu basarisiz oldu; dongu suruyor.");
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

    private async Task RunDueSchedulesAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<VaultDbContext>();

        var candidates = await context.VaultSchedules
            .Where(s => s.Enabled)
            .ToListAsync(ct);

        foreach (var schedule in candidates.Where(s => s.IsDue(now)))
        {
            if (ct.IsCancellationRequested) return;

            using var runScope = _scopeFactory.CreateScope();
            await RunOneAsync(runScope.ServiceProvider, schedule, now, ct);
        }
    }

    private async Task RunOneAsync(
        IServiceProvider services, VaultSchedule schedule, DateTime now, CancellationToken ct)
    {
        var context = services.GetRequiredService<VaultDbContext>();
        var authority = services.GetRequiredService<ITenantProjectAuthority>();

        var claimed = await context.VaultSchedules
            .Where(s => s.Id == schedule.Id && s.LastRunAt == schedule.LastRunAt)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.LastRunAt, now), ct);

        if (claimed == 0)
        {
            _logger.LogDebug("Vault zamanlamasi {Id} baska bir instance tarafindan alindi.", schedule.Id);
            return;
        }

        var projectSnapshot = await authority.GetProjectSnapshotAsync(schedule.ProjectId, ct);
        if (projectSnapshot is null)
        {
            _logger.LogWarning(
                "Vault zamanlamasi {Id} silinmis bir projeye ({ProjectId}) bakiyor; devre disi birakiliyor.",
                schedule.Id, schedule.ProjectId);

            await context.VaultSchedules
                .Where(s => s.Id == schedule.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.Enabled, false), ct);
            return;
        }

        var vault = services.GetRequiredService<IVaultApplicationService>();
        var result = await vault.BackupProjectAsync(projectSnapshot.ProjectId, SystemActorId, VaultBackupKind.Scheduled, ct);

        if (!result.Ok)
        {
            _logger.LogError(
                "Vault zamanli yedegi basarisiz: {ProjectId} — {Error}", projectSnapshot.ProjectId, result.Error);
            return;
        }

        await VerifyQuietlyAsync(vault, context, result.BackupId!, ct);
        await ApplyRetentionQuietlyAsync(vault, schedule, ct);
    }

    private async Task VerifyQuietlyAsync(
        IVaultApplicationService vault, VaultDbContext context, string backupId, CancellationToken ct)
    {
        try
        {
            var backup = await context.VaultBackups.FirstOrDefaultAsync(b => b.Id == backupId, ct);
            if (backup is not null) await vault.VerifyAsync(backup, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vault: zamanli yedek {BackupId} dogrulanamadi.", backupId);
        }
    }

    private async Task ApplyRetentionQuietlyAsync(
        IVaultApplicationService vault, VaultSchedule schedule, CancellationToken ct)
    {
        try
        {
            var removed = await vault.ApplyRetentionAsync(schedule.ProjectId, schedule.RetainCount, ct);
            if (removed > 0)
                _logger.LogInformation(
                    "Vault saklama politikasi: {ProjectId} icin {Count} eski otomatik yedek silindi.",
                    schedule.ProjectId, removed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vault: {ProjectId} icin saklama politikasi uygulanamadi.", schedule.ProjectId);
        }
    }
}
