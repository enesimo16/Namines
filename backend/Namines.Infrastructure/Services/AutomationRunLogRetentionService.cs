using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Namines.Infrastructure.Data;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Eskimiş Namines Flow çalışma kayıtlarını siler.
///
/// <b>Neden gerekli:</b> <see cref="Namines.Core.Models.AutomationRunLog"/>
/// her aksiyon çalıştırmasında bir satır yazıyor ve hiçbir şey silmiyordu.
/// Sık tetiklenen tek bir kural (ör. her kolon değişiminde webhook) tabloyu
/// süresiz büyütür; teşhis için yalnızca son çalışmalar okunduğu hâlde.
///
/// İki sınır birlikte uygulanıyor:
/// <list type="bullet">
/// <item>YAŞ — belirli günden eski kayıtlar gider.</item>
/// <item>KURAL BAŞINA SAYI — yaş sınırı içinde kalsa bile, bir kuralın en yeni
/// N kaydından fazlası tutulmaz. Tek başına yaş sınırı, dakikada tetiklenen
/// bir kuralda hâlâ on binlerce satır bırakırdı.</item>
/// </list>
/// </summary>
public class AutomationRunLogRetentionService : BackgroundService
{
    /// <summary>
    /// Kontrol aralığı. Sınırlar gün/adet cinsinden; saatte bir uyanmak
    /// fazlasıyla yeterli ve boşa sorguyu önlüyor.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

    private const int DefaultRetentionDays = 30;
    private const int DefaultMaxPerRule = 200;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AutomationRunLogRetentionService> _logger;

    public AutomationRunLogRetentionService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<AutomationRunLogRetentionService> logger)
    {
        // BackgroundService singleton, AuthDbContext scoped: doğrudan enjekte
        // etmek tutsak bağımlılık olurdu.
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retentionDays = _configuration.GetValue("NaminesFlow:RunLogRetentionDays", DefaultRetentionDays);
        var maxPerRule = _configuration.GetValue("NaminesFlow:RunLogMaxPerRule", DefaultMaxPerRule);

        _logger.LogInformation(
            "Namines Flow calisma kaydi temizligi basladi ({Days} gun / kural basina {Max} kayit).",
            retentionDays, maxPerRule);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
                await PurgeAsync(db, retentionDays, maxPerRule, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Temizlik ikincil bir iş: patlaması servisi düşürmemeli,
                // bir sonraki turda yeniden denenir.
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

    /// <summary>
    /// Saf olmayan ama TEST EDİLEBİLİR temizlik adımı — zamanlayıcıdan ayrı
    /// tutuluyor ki testler bir saat beklemeden çağırabilsin.
    /// </summary>
    public static async Task<int> PurgeAsync(AuthDbContext db, int retentionDays, int maxPerRule, CancellationToken ct)
    {
        var deleted = 0;

        if (retentionDays > 0)
        {
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            var old = await db.AutomationRunLogs.Where(l => l.TriggeredAt < cutoff).ToListAsync(ct);
            if (old.Count > 0)
            {
                db.AutomationRunLogs.RemoveRange(old);
                deleted += old.Count;
                // HEMEN kaydediliyor: aşağıdaki kural-başına geçiş VERİTABANINI
                // sorguluyor, yalnızca "Removed" işaretlenmiş satırları değil.
                // Kaydetmeden devam edilirse o satırlar hem sayımda hem de
                // fazlalık listesinde yeniden görünür ve `deleted` şişerdi.
                await db.SaveChangesAsync(ct);
            }
        }

        if (maxPerRule > 0)
        {
            // Yalnızca sınırı AŞAN kurallar taranıyor; her kuralın tüm
            // kayıtlarını belleğe çekmek büyük tablolarda anlamsız olurdu.
            var noisyRuleIds = await db.AutomationRunLogs
                .GroupBy(l => l.RuleId)
                .Where(g => g.Count() > maxPerRule)
                .Select(g => g.Key)
                .ToListAsync(ct);

            foreach (var ruleId in noisyRuleIds)
            {
                var surplus = await db.AutomationRunLogs
                    .Where(l => l.RuleId == ruleId)
                    .OrderByDescending(l => l.TriggeredAt)
                    .Skip(maxPerRule)
                    .ToListAsync(ct);

                if (surplus.Count == 0) continue;
                db.AutomationRunLogs.RemoveRange(surplus);
                deleted += surplus.Count;
            }
        }

        if (deleted > 0) await db.SaveChangesAsync(ct);
        return deleted;
    }
}
