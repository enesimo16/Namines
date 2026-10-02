using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Namines.Infrastructure.Data;

namespace Namines.API.Hubs;

/// <summary>
/// Açık hub bağlantılarını düzenli aralıklarla YENİDEN yetkilendirir ve yetkisi
/// kalmayanları kapatır.
///
/// <b>Kapattığı iki açık (docs/last-review M-13, M-14):</b> yetki yalnızca bağlantı
/// bir hub metodu çağırdığında kontrol ediliyordu, yani PASİF bir dinleyici hiç
/// yeniden doğrulanmıyordu (M-14) ve güvenlik damgası kurulmuş bir soket için bir
/// daha hiç okunmuyordu (M-13). Ayrıntılı gerekçe
/// <see cref="LiveConnectionRegistry"/>'nin sınıf notunda.
///
/// <b>İki ölçüt ayrı ayrı uygulanıyor</b> ve ikisi de gerekli: üyelik kaybı
/// (projeden çıkarılma) ile oturum iptali (parola değişimi / "tüm oturumları
/// sonlandır") farklı olaylar ve biri diğerini kapsamıyor.
///
/// <b>Bağlantı gruptan çıkarılmakla YETİNİLMİYOR, ABORT ediliyor:</b> gruptan
/// çıkarmak yayınları keser ama soket açık kalır ve istemci sessizce yeniden
/// katılabilir. İptalin anlamı bağlantının kapanması.
/// </summary>
public sealed class LiveConnectionAuthSweeper : BackgroundService
{
    /// <summary>
    /// Tarama aralığı. <c>SecurityStampValidation.CacheDuration</c> (60 sn) ile
    /// aynı mertebede tutuldu: iptalin devreye girme gecikmesi iki yolda da benzer
    /// olsun, biri "dakikalar", diğeri "saatler" olmasın.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

    private readonly LiveConnectionRegistry _registry;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<LiveConnectionAuthSweeper> _logger;

    public LiveConnectionAuthSweeper(
        LiveConnectionRegistry registry,
        IServiceScopeFactory scopes,
        ILogger<LiveConnectionAuthSweeper> logger)
    {
        _registry = registry;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Tarama bir güvenlik işi: hata vermesi uygulamayı durdurmamalı ama
                // SESSİZ de kalmamalı. Sessiz kalan bir iptal, çalışmadığı fark
                // edilmeyen bir iptaldir — bu bulgunun kök nedeni tam olarak buydu.
                _logger.LogError(ex, "Canli baglanti yetki taramasi basarisiz oldu.");
            }

            try { await Task.Delay(SweepInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var connections = _registry.Snapshot();
        if (connections.Count == 0) return;

        // Her tarama KENDİ kapsamını açıyor: AuthDbContext scoped ve uzun ömürlü
        // bir arka plan servisinde tutmak, değişiklik izleyicisini süresiz
        // büyütüp bayat veri okumak demekti.
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        var aborted = 0;

        // Kullanıcı başına TEK sorgu: aynı kullanıcının birden çok sekmesi olması
        // sıradan, ve her bağlantı için ayrı sorgu atmak taramayı bağlantı
        // sayısıyla doğrusal pahalı kılardı.
        foreach (var byUser in connections.GroupBy(c => c.UserId, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            var currentStamp = await db.Users.AsNoTracking()
                .Where(u => u.Id == byUser.Key)
                .Select(u => u.SecurityStamp)
                .FirstOrDefaultAsync(ct);

            var userExists = await db.Users.AsNoTracking().AnyAsync(u => u.Id == byUser.Key, ct);

            foreach (var entry in byUser)
            {
                var reason = await DenialReasonAsync(db, entry, userExists, currentStamp, ct);
                if (reason is null) continue;

                _logger.LogInformation(
                    "Canli baglanti kapatiliyor: {ConnectionId} (kullanici {UserId}) — {Reason}",
                    entry.ConnectionId, entry.UserId, reason);

                // Abort() istisna firlatabilir (baglanti bu arada kapanmis olabilir);
                // bir baglantinin kapatilamamasi digerlerini etkilememeli.
                try { entry.Context.Abort(); } catch (Exception ex)
                {
                    _logger.LogDebug(ex, "{ConnectionId} abort edilemedi.", entry.ConnectionId);
                }

                _registry.Remove(entry.ConnectionId);
                aborted++;
            }
        }

        if (aborted > 0)
            _logger.LogInformation("Yetki taramasi: {Count} baglanti kapatildi.", aborted);
    }

    /// <summary>Kapatma sebebi; bağlantı hâlâ yetkiliyse <c>null</c>.</summary>
    private static async Task<string?> DenialReasonAsync(
        AuthDbContext db,
        LiveConnectionRegistry.Entry entry,
        bool userExists,
        string? currentStamp,
        CancellationToken ct)
    {
        if (!userExists) return "kullanici silinmis";

        // M-13: oturum iptali. Damga taşımayan ESKİ jetonlar bu ölçütten
        // geçirilmiyor — SecurityStampValidation'daki aynı geriye uyumluluk
        // kararı; aksi hâlde dağıtım anında herkes düşerdi.
        if (entry.SecurityStamp is not null
            && !string.Equals(currentStamp ?? string.Empty, entry.SecurityStamp, StringComparison.Ordinal))
            return "guvenlik damgasi degismis (oturum iptal edildi)";

        // M-14: üyelik kaybı. Guest odaları (ProjectId null) kasıtlı olarak üyelik
        // istemiyor, bkz. CanvasHub sınıf notu — onları taramak, tasarlanmış bir
        // özelliği kırmak olurdu.
        if (entry.ProjectId is not null && !await db.CanViewAsync(entry.ProjectId, entry.UserId, ct))
            return "proje uyeligi kaldirilmis";

        return null;
    }
}
