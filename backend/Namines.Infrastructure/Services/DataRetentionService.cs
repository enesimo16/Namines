using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;
using Namines.Core.Models.Auth;
using Namines.Desk.Abstractions;
using Namines.Desk.Application;
using Namines.Infrastructure.Data;
using Namines.Ops.Application;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Kontrol DB'deki sınırsız büyüyen tabloları günde bir, PARTİ PARTİ temizler
/// (DB-12b/39/40). Tek büyük DELETE hem kilit tutar hem WAL şişirir; bu yüzden
/// her parti <see cref="BatchSize"/> satırdır ve partiler arasında kısa bekleme vardır.
///
/// Yapılandırma <c>Retention:*</c> altında; 0 veya negatif değer o tablonun
/// temizliğini KAPATIR (veri kaybına doğru varsayılan yok). Faturalama/vergi
/// kaydı olan UsageEvents için <see cref="MinUsageEventDays"/> altına inilmez.
/// AutomationRunLogs ayrı serviste (<c>FlowRunLogRetentionService</c>) kalır,
/// çift çalışma olmasın diye burada ele alınmaz.
/// </summary>
public class DataRetentionService : BackgroundService
{
    public const int BatchSize = 5000;

    /// <summary>Fatura dönemi + itiraz süresi altına inmeyi engelleyen taban.</summary>
    public const int MinUsageEventDays = 400;

    public const int DefaultGatewayAuditDays = 365;
    public const int DefaultSqlExecutionAuditDays = 365;
    public const int DefaultUsageEventDays = 2557; // ~7 yıl (vergi/muhasebe)
    public const int DefaultChangeRequestAuditDays = 730;
    public const int MinChangeRequestAuditDays = 90;

    // 0 ya da negatif = temizlik kapalı; açıksa taban uygulanır.
    private static int ClampAuditDays(int days) => days <= 0 ? days : Math.Max(days, MinChangeRequestAuditDays);
    public const int DefaultStripeWebhookDays = 180;
    public const int DefaultFeedbackDays = 0; // varsayılan: sakla
    public const int ExpiredTokenGraceDays = 7;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DataRetentionService> _logger;

    public DataRetentionService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<DataRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("Retention:Enabled", true))
        {
            _logger.LogInformation("Veri saklama temizligi kapali (Retention:Enabled=false).");
            return;
        }

        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var deskApp = scope.ServiceProvider.GetService<IDeskApplicationService>()
                              ?? scope.ServiceProvider.GetService<IDeskModule>()?.Application;
                await PurgeAsync(RetentionOptions.From(_configuration), _logger, DateTime.UtcNow,
                    TimeSpan.FromMilliseconds(200), stoppingToken,
                    scope.ServiceProvider.GetRequiredService<IOpsRetentionService>(),
                    scope.ServiceProvider.GetRequiredService<Namines.Gateway.Application.IGatewayMaintenance>(),
                    scope.ServiceProvider.GetRequiredService<Namines.BuildingBlocks.Contracts.IBillingMaintenance>(),
                    scope.ServiceProvider.GetRequiredService<Namines.BuildingBlocks.Contracts.IGovernanceMaintenance>(),
                    scope.ServiceProvider.GetRequiredService<Namines.BuildingBlocks.Contracts.IIdentityMaintenance>(), deskApp);

                // İptal edilmiş paylaşım satırı yalnızca "ne zaman kapatıldı" izi: sınırsız birikmesin.
                var shareDays = _configuration.GetValue("Retention:RevokedShareDays", DefaultRevokedShareDays);
                if (shareDays > 0)
                {
                    var removed = await scope.ServiceProvider
                        .GetRequiredService<Namines.BuildingBlocks.Application.IStudioApplicationService>()
                        .PurgeRevokedSharesAsync(DateTime.UtcNow.AddDays(-shareDays), stoppingToken);
                    if (removed > 0)
                        _logger.LogInformation("Veri saklama: {Count} iptal edilmis paylasim satiri silindi.", removed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Temizlik ikincil: patlaması servisi düşürmesin, ertesi gün yeniden denenir.
                _logger.LogError(ex, "Veri saklama temizligi basarisiz oldu.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public const int DefaultRevokedShareDays = 90;
    public const int DefaultCompletedSubscriptionCancellationDays = 90;

    public sealed record RetentionOptions(
        int GatewayAuditDays, int SqlExecutionAuditDays, int UsageEventDays,
        int ChangeRequestAuditDays, int StripeWebhookDays, int FeedbackDays,
        int CompletedSubscriptionCancellationDays)
    {
        public static RetentionOptions From(IConfiguration c)
        {
            var usage = c.GetValue("Retention:UsageEventDays", DefaultUsageEventDays);
            // Faturalama kaydı: kapatılabilir (<=0) ama kısaltılamaz.
            if (usage > 0 && usage < MinUsageEventDays) usage = MinUsageEventDays;
            return new RetentionOptions(
                c.GetValue("Retention:GatewayAuditDays", DefaultGatewayAuditDays),
                c.GetValue("Retention:SqlExecutionAuditDays", DefaultSqlExecutionAuditDays),
                usage,
                // Veritabanı işlevi 90 günden yeni denetim satırı silmez; daha kısa ayar sessizce boşa çalışmasın diye tabana çekilir.
                ClampAuditDays(c.GetValue("Retention:ChangeRequestAuditDays", DefaultChangeRequestAuditDays)),
                c.GetValue("Retention:StripeWebhookDays", DefaultStripeWebhookDays),
                c.GetValue("Retention:FeedbackDays", DefaultFeedbackDays),
                c.GetValue("Retention:CompletedSubscriptionCancellationDays", DefaultCompletedSubscriptionCancellationDays));
        }
    }

    /// <summary>Test edilebilir temizlik adımı; tablo başına silinen satır sayısını döndürür.</summary>
    public static async Task<IReadOnlyDictionary<string, int>> PurgeAsync(
        RetentionOptions o, ILogger logger, DateTime nowUtc,
        TimeSpan pause, CancellationToken ct, IOpsRetentionService ops, Namines.Gateway.Application.IGatewayMaintenance gateway,
        Namines.BuildingBlocks.Contracts.IBillingMaintenance billing, Namines.BuildingBlocks.Contracts.IGovernanceMaintenance governance,
        Namines.BuildingBlocks.Contracts.IIdentityMaintenance identity, IDeskApplicationService? deskApp = null)
    {
        var result = new Dictionary<string, int>();

        async Task RunModule(string name, int days, Func<Task<int>> purge)
        {
            if (days <= 0) return;
            var n = await purge();
            result[name] = n;
            if (n > 0)
                logger.LogInformation("Veri saklama: {Table} tablosundan {Rows} satir silindi (> {Days} gun).", name, n, days);
        }

        await RunModule("GatewayAuditEntries", o.GatewayAuditDays, () => gateway.PurgeAuditAsync(o.GatewayAuditDays, nowUtc, pause, ct));
        await RunModule("SqlExecutionAudits", o.SqlExecutionAuditDays, () => ops.PurgeSqlExecutionAuditsAsync(o.SqlExecutionAuditDays, nowUtc, pause, ct));
        await RunModule("UsageEvents", o.UsageEventDays, () => billing.PurgeUsageEventsAsync(nowUtc.AddDays(-o.UsageEventDays), pause, ct));
        // Denetim satırları değiştirilemez; yalnızca saklama süresi dolanlar, Governance'ın silme izniyle gider.
        await RunModule("ChangeRequestAuditLogs", o.ChangeRequestAuditDays, () => governance.PurgeAuditAsync(nowUtc.AddDays(-o.ChangeRequestAuditDays), pause, ct));
        await RunModule("StripeWebhookEvents", o.StripeWebhookDays, () => billing.PurgeStripeWebhookEventsAsync(nowUtc.AddDays(-o.StripeWebhookDays), pause, ct));
        // Yalnızca TAMAMLANANLAR: açık satır, henüz iptal edilmemiş bir abonelik demek; ne kadar eski olursa olsun silinmez.
        await RunModule("PendingSubscriptionCancellations", o.CompletedSubscriptionCancellationDays,
            () => billing.PurgeCompletedCancellationsAsync(nowUtc.AddDays(-o.CompletedSubscriptionCancellationDays), pause, ct));
        await RunModule("Feedbacks", o.FeedbackDays, () => ops.PurgeFeedbacksAsync(o.FeedbackDays, nowUtc, pause, ct));

        // Süresi dolmuş tek kullanımlık jetonlar: Desk modülü üzerinden temizlenir.
        var tokenCutoff = nowUtc.AddDays(-ExpiredTokenGraceDays);
        var handoff = deskApp is not null
            ? await deskApp.PurgeExpiredHandoffTokensAsync(tokenCutoff, ct, BatchSize, pause)
            : 0;
        result["desk_handoff_tokens"] = handoff;
        // Kabul edilmiş davetler üyelik geçmişi olarak kalır; yalnızca kabul EDİLMEMİŞ ve süresi dolanlar silinir.
        var invites = await identity.PurgeExpiredInvitesAsync(tokenCutoff, pause, ct);
        result["TeamInvites"] = invites;
        if (handoff > 0 || invites > 0)
            logger.LogInformation("Veri saklama: desk_handoff_tokens={Handoff}, TeamInvites={Invites} silindi.", handoff, invites);

        return result;
    }

}
