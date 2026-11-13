using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Namines.Core.Models.Auth;
using Namines.Vault.Application;
using Namines.Vault.Infrastructure.Persistence;
using Namines.Vault.Domain;

namespace Namines.Vault.Infrastructure.Workers;

/// <summary>
/// Vault modülünün kendi <see cref="VaultDbContext"/>'i ve <see cref="IVaultApplicationService"/>'i
/// üzerinde çalışan arka plan yedekleme işçisi (Kritik Kontrol #6: Background Worker Ownership).
/// </summary>
public sealed class VaultBackupWorker : BackgroundService
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(6);

    private readonly IVaultJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VaultBackupWorker> _logger;

    public VaultBackupWorker(
        IVaultJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<VaultBackupWorker> logger)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ReconcileStrandedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            VaultBackupJob job;
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
                await RunAsync(job, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Vault: {BackupId} yedegi calistirilirken beklenmeyen hata; isci devam ediyor.",
                    job.BackupId);
            }
        }
    }

    private async Task RunAsync(VaultBackupJob job, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VaultDbContext>();
        var vault = scope.ServiceProvider.GetRequiredService<IVaultApplicationService>();

        var record = await db.VaultBackups.FirstOrDefaultAsync(b => b.Id == job.BackupId, ct);
        if (record is null)
        {
            _logger.LogInformation("Vault: {BackupId} kaydi bulunamadi, is atlandi.", job.BackupId);
            return;
        }

        if (record.Status != VaultBackupStatus.Running)
        {
            _logger.LogInformation(
                "Vault: {BackupId} zaten {Status} durumunda, tekrar calistirilmadi.",
                record.Id, record.Status);
            return;
        }

        await vault.RunQueuedBackupAsync(record, ct);
    }

    private async Task ReconcileStrandedAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VaultDbContext>();

            var cutoff = DateTime.UtcNow - StaleAfter;
            var stranded = await db.VaultBackups
                .Where(b => b.Status == VaultBackupStatus.Running && b.CreatedAt < cutoff)
                .ToListAsync(ct);

            if (stranded.Count == 0) return;

            foreach (var record in stranded)
            {
                record.Status = VaultBackupStatus.Failed;
                record.ErrorMessage =
                    "The server restarted while this backup was running, so it could not be completed. " +
                    "It is safe to start a new backup.";
                record.CompletedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);

            _logger.LogWarning(
                "Vault: acilista asili kalmis {Count} yedek kaydi Failed olarak kapatildi.",
                stranded.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Vault: asili yedek kayitlari uzlastirilamadi.");
        }
    }
}
