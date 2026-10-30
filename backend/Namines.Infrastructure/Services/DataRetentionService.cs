using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Kontrol DB'deki sınırsız büyüyen tabloları günde bir, PARTİ PARTİ temizler
/// (DB-12b/39/40). Tek büyük DELETE hem kilit tutar hem WAL şişirir; bu yüzden
/// her parti <see cref="BatchSize"/> satırdır ve partiler arasında kısa bekleme vardır.
///
/// Yapılandırma <c>Retention:*</c> altında; 0 veya negatif değer o tablonun
/// temizliğini KAPATIR (veri kaybına doğru varsayılan yok). Faturalama/vergi
/// kaydı olan UsageEvents için <see cref="MinUsageEventDays"/> altına inilmez.
/// AutomationRunLogs ayrı serviste (<see cref="AutomationRunLogRetentionService"/>) kalır,
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
                var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
                await PurgeAsync(db, RetentionOptions.From(_configuration), _logger, DateTime.UtcNow,
                    TimeSpan.FromMilliseconds(200), stoppingToken);
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

    public sealed record RetentionOptions(
        int GatewayAuditDays, int SqlExecutionAuditDays, int UsageEventDays,
        int ChangeRequestAuditDays, int StripeWebhookDays, int FeedbackDays)
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
                c.GetValue("Retention:ChangeRequestAuditDays", DefaultChangeRequestAuditDays),
                c.GetValue("Retention:StripeWebhookDays", DefaultStripeWebhookDays),
                c.GetValue("Retention:FeedbackDays", DefaultFeedbackDays));
        }
    }

    /// <summary>Test edilebilir temizlik adımı; tablo başına silinen satır sayısını döndürür.</summary>
    public static async Task<IReadOnlyDictionary<string, int>> PurgeAsync(
        AuthDbContext db, RetentionOptions o, ILogger logger, DateTime nowUtc,
        TimeSpan pause, CancellationToken ct)
    {
        var result = new Dictionary<string, int>();

        async Task Run<T>(string name, int days, Func<DateTime, Expression<Func<T, bool>>> expired,
            Expression<Func<T, object?>> orderKey) where T : class
        {
            if (days <= 0) return;
            var cutoff = nowUtc.AddDays(-days);
            var n = await DeleteInBatchesAsync(db.Set<T>().Where(expired(cutoff)), orderKey, pause, ct);
            result[name] = n;
            if (n > 0)
                logger.LogInformation("Veri saklama: {Table} tablosundan {Rows} satir silindi (> {Days} gun).", name, n, days);
        }

        await Run<GatewayAuditEntry>("GatewayAuditEntries", o.GatewayAuditDays, c => e => e.CreatedAt < c, e => e.Id);
        await Run<SqlExecutionAudit>("SqlExecutionAudits", o.SqlExecutionAuditDays, c => e => e.CreatedAt < c, e => e.Id);
        await Run<UsageEvent>("UsageEvents", o.UsageEventDays, c => e => e.OccurredAt < c, e => e.Id);
        await Run<ChangeRequestAuditLog>("ChangeRequestAuditLogs", o.ChangeRequestAuditDays, c => e => e.CreatedAt < c, e => e.Id);
        await Run<StripeWebhookEvent>("StripeWebhookEvents", o.StripeWebhookDays, c => e => e.ProcessedAt < c, e => e.Id);
        await Run<Namines.Core.Models.Feedback>("Feedbacks", o.FeedbackDays, c => e => e.CreatedAt < c, e => e.Id);

        // Süresi dolmuş tek kullanımlık jetonlar: gün ayarı yok, kısa sabit tolerans.
        var tokenCutoff = nowUtc.AddDays(-ExpiredTokenGraceDays);
        var handoff = await DeleteInBatchesAsync(
            db.DeskHandoffTokens.Where(t => t.ExpiresAt < tokenCutoff), t => t.Id, pause, ct);
        result["desk_handoff_tokens"] = handoff;
        // Kabul edilmiş davetler üyelik geçmişi olarak kalır; yalnızca kabul EDİLMEMİŞ ve süresi dolanlar silinir.
        var invites = await DeleteInBatchesAsync(
            db.TeamInvites.Where(i => i.AcceptedAt == null && i.ExpiresAt < tokenCutoff), i => i.Id, pause, ct);
        result["TeamInvites"] = invites;
        if (handoff > 0 || invites > 0)
            logger.LogInformation("Veri saklama: desk_handoff_tokens={Handoff}, TeamInvites={Invites} silindi.", handoff, invites);

        return result;
    }

    private static async Task<int> DeleteInBatchesAsync<T>(
        IQueryable<T> expired, Expression<Func<T, object?>> orderKey, TimeSpan pause, CancellationToken ct)
        where T : class
    {
        var total = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var n = await expired.OrderBy(orderKey).Take(BatchSize).ExecuteDeleteAsync(ct);
            total += n;
            if (n < BatchSize) return total;
            if (pause > TimeSpan.Zero) await Task.Delay(pause, ct);
        }
    }
}
