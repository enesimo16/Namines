using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Kuyruğa alınmış yedekleme işlerini çalıştırır (PERF-003 / B-35).
///
/// <b>KENDİ DI kapsamını açıyor ve bu zorunlu.</b> HTTP isteğinin
/// <c>DbContext</c>'i yanıt döndüğünde atılır; ona arka planda dokunmak
/// <c>ObjectDisposedException</c> demek — ve bunu yalnızca yavaş yedeklerde,
/// yani tam olarak üretimde görürsünüz. <c>Task.Run(() => _vault.BackupAsync(…))</c>
/// yazmak bu hatayı yapmanın en kolay yolu.
///
/// <b>Tek işçi (paralel değil), bilinçli:</b> Yedekleme Docker konteyneri açıp
/// disk yazıyor. Aynı anda beş yedek almak, beşini de yavaşlatmaktan ve diski
/// doldurmaktan başka bir şey yapmaz. Kuyruk sıralı çalışıyor.
/// </summary>
public sealed class VaultBackupWorker : BackgroundService
{
    /// <summary>
    /// Açılışta bir yedeğin "asılı kalmış" sayılması için gereken süre.
    ///
    /// Açılış anında <c>Running</c> olan her kayıt asılı DEĞİLDİR: birden fazla
    /// instance varsa başka bir instance onu şu anda çalıştırıyor olabilir.
    /// Bu eşik, o durumu yanlışlıkla "başarısız" işaretlemeyi engelliyor —
    /// çalışan bir yedeği başarısız göstermek, kullanıcıyı gereksiz yere
    /// yeniden yedek almaya iter.
    /// </summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(6);

    private readonly IVaultJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VaultBackupWorker> _logger;

    public VaultBackupWorker(
        IVaultJobQueue queue, IServiceScopeFactory scopeFactory, ILogger<VaultBackupWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
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
                return;   // kapanış
            }

            // Bir işin patlaması işçiyi ÖLDÜRMEMELİ: ölen bir işçi, sonraki
            // bütün yedeklerin sessizce hiç çalışmaması demek.
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
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var vault = scope.ServiceProvider.GetRequiredService<VaultService>();

        var record = await db.VaultBackups.FirstOrDefaultAsync(b => b.Id == job.BackupId, ct);
        if (record is null)
        {
            // Kullanıcı kaydı silmiş olabilir. Bu bir hata değil; iş düşüyor.
            _logger.LogInformation("Vault: {BackupId} kaydi bulunamadi, is atlandi.", job.BackupId);
            return;
        }

        if (record.Status != VaultBackupStatus.Running)
        {
            // Aynı iş iki kez kuyruğa girmiş olabilir. İkinci çalıştırma
            // tamamlanmış bir yedeğin üzerine yazmamalı.
            _logger.LogInformation(
                "Vault: {BackupId} zaten {Status} durumunda, tekrar calistirilmadi.",
                record.Id, record.Status);
            return;
        }

        var project = await db.CloudProjects.FirstOrDefaultAsync(p => p.Id == job.ProjectId, ct);
        if (project is null)
        {
            record.Status = VaultBackupStatus.Failed;
            record.ErrorMessage = "The project no longer exists.";
            record.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            return;
        }

        await vault.RunQueuedBackupAsync(record, project, ct);
    }

    /// <summary>
    /// Açılışta, bir çökme/yeniden başlatma yüzünden <c>Running</c> kalmış
    /// kayıtları kapatır.
    ///
    /// <b>Bu olmadan arka plana taşımak bir gerileme olurdu:</b> senkron uçta
    /// istek düşse bile <c>finally</c> bloğu kaydı kapatıyordu. Arka planda
    /// süreç ölürse kaydı kapatacak kimse kalmaz ve arayüz sonsuza kadar
    /// "sürüyor" gösterir — kullanıcı ne bekleyeceğini ne de yeniden
    /// deneyeceğini bilir.
    /// </summary>
    private async Task ReconcileStrandedAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

            var cutoff = DateTime.UtcNow - StaleAfter;
            var stranded = await db.VaultBackups
                .Where(b => b.Status == VaultBackupStatus.Running && b.CreatedAt < cutoff)
                .ToListAsync(ct);

            if (stranded.Count == 0) return;

            foreach (var record in stranded)
            {
                record.Status = VaultBackupStatus.Failed;
                // Mesaj kullanıcıya gösteriliyor: "bilinmeyen hata" demek,
                // yeniden denemenin güvenli olup olmadığını söylemezdi.
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
            // Uzlaştırma başarısız olsa bile işçi çalışmaya devam etmeli:
            // asılı kalmış eski kayıtlar yeni yedekleri engellememeli.
            _logger.LogError(ex, "Vault: asili yedek kayitlari uzlastirilamadi.");
        }
    }
}
