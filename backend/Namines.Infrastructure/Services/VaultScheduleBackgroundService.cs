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
/// Zamanlanmış yedekleri çalıştıran arka plan işi (V4).
///
/// <b>Neden dakikada bir uyanıp saate bakıyor, "sonraki çalıştırmaya kadar
/// uyu" demiyor:</b> uzun bir uyku, sunucunun yeniden başlatıldığı her seferde
/// sıfırlanır — ve gerçek bir dağıtımda sunucu sık sık yeniden başlar. Sık ve
/// ucuz bir kontrol, kaçırılan yedeğe göre çok daha ucuz.
/// </summary>
public class VaultScheduleBackgroundService : BackgroundService
{
    /// <summary>
    /// Uyanma aralığı. Bir dakika, "saat 03:00'te" sözünü tutmaya fazlasıyla
    /// yeter ve boştaki maliyeti bir sorgudan ibarettir.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VaultScheduleBackgroundService> _logger;

    public VaultScheduleBackgroundService(
        IServiceScopeFactory scopeFactory, ILogger<VaultScheduleBackgroundService> logger)
    {
        // BackgroundService singleton, AuthDbContext ise scoped: doğrudan
        // enjekte etmek tutsak bağımlılık (captive dependency) olurdu — tek bir
        // DbContext örneği uygulamanın ömrü boyunca paylaşılırdı.
        _scopeFactory = scopeFactory;
        _logger = logger;
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
                // Tek bir turun hatası döngüyü ÖLDÜRMEMELİ: ölen bir zamanlayıcı,
                // sessizce hiç yedek alınmaması demektir — yedeklemenin en kötü
                // başarısızlık biçimi.
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
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        var candidates = await context.VaultSchedules
            .Where(s => s.Enabled)
            .ToListAsync(ct);

        foreach (var schedule in candidates.Where(s => s.IsDue(now)))
        {
            if (ct.IsCancellationRequested) return;

            // Her zamanlama kendi kapsamında: biri hata alırsa diğerlerinin
            // DbContext'i kirlenmiş olmasın.
            using var runScope = _scopeFactory.CreateScope();
            await RunOneAsync(runScope.ServiceProvider, schedule, now, ct);
        }
    }

    private async Task RunOneAsync(
        IServiceProvider services, VaultSchedule schedule, DateTime now, CancellationToken ct)
    {
        var context = services.GetRequiredService<AuthDbContext>();

        // ── Çoklu instance güvenliği ────────────────────────────────────────
        // Satırı ÖNCE kap, sonra çalış. Tek bir koşullu UPDATE atomik: iki
        // instance aynı anda denerse yalnızca biri 1 satır etkiler, diğeri 0
        // alır ve çekilir. Önce yedek alıp sonra işaretlemek, aynı yedeğin iki
        // kez alınmasına açık kapı bırakırdı.
        var claimed = await context.VaultSchedules
            .Where(s => s.Id == schedule.Id && s.LastRunAt == schedule.LastRunAt)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.LastRunAt, now), ct);

        if (claimed == 0)
        {
            _logger.LogDebug("Vault zamanlamasi {Id} baska bir instance tarafindan alindi.", schedule.Id);
            return;
        }

        var project = await context.CloudProjects
            .FirstOrDefaultAsync(p => p.Id == schedule.ProjectId, ct);

        if (project is null)
        {
            _logger.LogWarning(
                "Vault zamanlamasi {Id} silinmis bir projeye ({ProjectId}) bakiyor; devre disi birakiliyor.",
                schedule.Id, schedule.ProjectId);

            await context.VaultSchedules
                .Where(s => s.Id == schedule.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.Enabled, false), ct);
            return;
        }

        var vault = services.GetRequiredService<VaultService>();

        // Zamanlanmış yedeği başlatan "kullanıcı" yok; kaydın sahibi olarak
        // zamanlamayı kuran projenin sahibi değil, açık bir sistem kimliği
        // yazılıyor — denetim kaydında bunun otomatik olduğu görünsün.
        var result = await vault.BackupAsync(project, SystemActorId, VaultBackupKind.Scheduled, ct);

        if (!result.Ok)
        {
            _logger.LogError(
                "Vault zamanli yedegi basarisiz: {ProjectId} — {Error}", project.Id, result.Error);
            return;
        }

        // Doğrulama ve temizlik yedeğin KENDİSİNDEN sonra: ikisi de başarısız
        // olsa bile yedek alınmış olur ve kaybolmaz.
        await VerifyQuietlyAsync(vault, context, result.BackupId!, ct);
        await ApplyRetentionQuietlyAsync(vault, schedule, ct);
    }

    /// <summary>Sistem tarafından başlatılan işlerin denetim kaydındaki kimliği.</summary>
    private const string SystemActorId = "system:vault-scheduler";

    private async Task VerifyQuietlyAsync(
        VaultService vault, AuthDbContext context, string backupId, CancellationToken ct)
    {
        try
        {
            var backup = await context.VaultBackups.FirstOrDefaultAsync(b => b.Id == backupId, ct);
            if (backup is not null) await vault.VerifyAsync(backup, ct);
        }
        catch (Exception ex)
        {
            // Doğrulanamaması yedeği geçersiz kılmaz — yalnızca kanıtlanmamış
            // bırakır. Alınmış bir yedeği bu yüzden hata saymak yanlış olurdu.
            _logger.LogWarning(ex, "Vault: zamanli yedek {BackupId} dogrulanamadi.", backupId);
        }
    }

    private async Task ApplyRetentionQuietlyAsync(
        VaultService vault, VaultSchedule schedule, CancellationToken ct)
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
