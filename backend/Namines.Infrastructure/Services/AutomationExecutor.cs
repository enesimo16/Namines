using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Security;
using Namines.Infrastructure.AI;
using Namines.Infrastructure.Data;

namespace Namines.Infrastructure.Services;

public interface IAutomationExecutor
{
    Task RunAsync(string projectId, SchemaDiffResult diff, DatabaseSchema oldSchema, DatabaseSchema newSchema, CancellationToken ct = default);

    /// <summary>Tek bir kuralın zincirini çalıştırır — "şimdi test et" bu yolu kullanır.</summary>
    Task RunRuleAsync(AutomationRule rule, string userId, DatabaseSchema schema, DatabaseType engine, bool isTest, CancellationToken ct);
}

/// <summary>
/// Namines Flow'un sunucu tarafı aksiyonlarını çalıştırır (Bölüm 3).
///
/// Bir kuralın aksiyonu başarısız olursa (webhook 5xx, AI hatası, kota reddi)
/// DİĞER KURALLAR etkilenmez — her kural kendi try/catch'i içinde çalışır ve
/// sonucu <see cref="AutomationRunLog"/>'a yazar.
/// </summary>
public sealed class AutomationExecutor : IAutomationExecutor
{
    /// <summary>DBA/seed için sabit tahmin — SchemaController.SchemaRoundTokenEstimate ile aynı büyüklük mertebesi.</summary>
    private const int AutomationTokenEstimate = 2500;

    private readonly AuthDbContext _db;
    private readonly IAiQuotaReserver _quota;
    private readonly GroqAIService _groqDba;
    private readonly IAIService _aiService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AutomationExecutor> _logger;

    public AutomationExecutor(
        AuthDbContext db, IAiQuotaReserver quota, GroqAIService groqDba, IAIService aiService,
        IHttpClientFactory httpClientFactory, ILogger<AutomationExecutor> logger)
    {
        _db = db;
        _quota = quota;
        _groqDba = groqDba;
        _aiService = aiService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task RunAsync(string projectId, SchemaDiffResult diff, DatabaseSchema oldSchema, DatabaseSchema newSchema, CancellationToken ct = default)
    {
        var project = await _db.CloudProjects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return; // Proje bu arada silinmiş olabilir — iş düşer.

        var rules = await _db.AutomationRules.AsNoTracking()
            .Include(r => r.Actions)
            .Where(r => r.ProjectId == projectId && r.Enabled).ToListAsync(ct);
        if (rules.Count == 0) return;

        var matched = AutomationRuleMatcher.Match(diff, oldSchema, newSchema, rules);
        if (matched.Count == 0) return;

        var engine = Enum.TryParse<DatabaseType>(project.DbType, ignoreCase: true, out var parsedEngine)
            ? parsedEngine : DatabaseType.PostgreSQL;

        foreach (var match in matched)
        {
            await RunRuleAsync(match.Rule, project.UserId, newSchema, engine, isTest: false, ct);
        }
    }

    /// <summary>
    /// Bir kuralın aksiyon zincirini sırayla çalıştırır.
    ///
    /// Her adım KENDİ try/catch'inde: bir adımın hatası (webhook 5xx, AI
    /// hatası, kota reddi) sonraki adımları düşürmüyor. "Bir kuralın hatası
    /// diğer kuralları etkilemez" ilkesinin adım seviyesindeki karşılığı —
    /// zincirin ikinci adımı ilk adım patladı diye sessizce atlanırsa
    /// kullanıcı bunu hiçbir yerden anlayamazdı.
    /// </summary>
    public async Task RunRuleAsync(
        AutomationRule rule, string userId, DatabaseSchema schema, DatabaseType engine, bool isTest, CancellationToken ct)
    {
        foreach (var action in rule.Actions.OrderBy(a => a.SortOrder))
        {
            // "Toast" sunucuda HİÇ işlenmiyor — istemci kendi event bus'ından
            // dinliyor. Log dahi yazılmıyor: sunucunun hiç bilmediği bir şeyin
            // "çalıştı" kaydı tutması yanıltıcı olurdu.
            if (action.ActionType == "Toast") continue;

            try
            {
                await RunOneAsync(rule, action, userId, schema, engine, isTest, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Namines Flow: {RuleId} kuralinin {ActionType} adimi calistirilirken beklenmeyen hata.",
                    rule.Id, action.ActionType);
                await LogAsync(rule.Id, action.ActionType, "Failed", ex.Message, null, isTest, ct);
            }
        }
    }

    private async Task RunOneAsync(
        AutomationRule rule, AutomationAction action, string userId, DatabaseSchema schema,
        DatabaseType engine, bool isTest, CancellationToken ct)
    {
        switch (action.ActionType)
        {
            case "Webhook":
                await RunWebhookAsync(rule, action, isTest, ct);
                return;
            case "DbaCheck":
                await RunDbaCheckAsync(rule, action, userId, schema, engine, isTest, ct);
                return;
            case "SeedData":
                await RunSeedDataAsync(rule, action, userId, schema, isTest, ct);
                return;
            default:
                await LogAsync(rule.Id, action.ActionType, "Failed", $"Unknown action type: {action.ActionType}", null, isTest, ct);
                return;
        }
    }

    private async Task RunWebhookAsync(AutomationRule rule, AutomationAction action, bool isTest, CancellationToken ct)
    {
        var url = ExtractUrl(action.ActionConfigJson);

        // HER ÇALIŞTIRMADA yeniden doğrulanıyor — kaydedilen bir URL zamanla
        // farklı bir IP'ye çözülebilir (DNS rebinding). Test çalıştırması da
        // bu kontrolden MUAF DEĞİL, aksi hâlde "test" bir SSRF kapısı olurdu.
        if (url is null || !SsrfGuard.IsUrlSafe(url))
        {
            await LogAsync(rule.Id, action.ActionType, "Skipped", "Webhook URL is missing or not a safe public target.", null, isTest, ct);
            return;
        }

        var client = _httpClientFactory.CreateClient("AutomationWebhook");
        var response = await client.PostAsJsonAsync(url, new
        {
            trigger = rule.TriggerType,
            table = rule.ScopeTableId,
            timestamp = DateTime.UtcNow,
        }, ct);

        if (response.IsSuccessStatusCode)
            await LogAsync(rule.Id, action.ActionType, "Success", null, null, isTest, ct);
        else
            await LogAsync(rule.Id, action.ActionType, "Failed", $"Webhook returned {(int)response.StatusCode}.", null, isTest, ct);
    }

    private async Task RunDbaCheckAsync(
        AutomationRule rule, AutomationAction action, string userId, DatabaseSchema schema,
        DatabaseType engine, bool isTest, CancellationToken ct)
    {
        // Kota rezervasyonu test çalıştırmasında da yapılıyor: gerçek bir AI
        // çağrısı gidiyor, bedeli de gerçek.
        var decision = await _quota.TryReserveAsync(userId, AutomationTokenEstimate, ct);
        if (decision != AiQuotaDecision.Allowed)
        {
            await LogAsync(rule.Id, action.ActionType, "Skipped", "Quota exceeded.", null, isTest, ct);
            return;
        }

        var issues = await _groqDba.AnalyzeSchemaDbaAsync(schema, engine);
        var summary = issues.Count == 0
            ? "No issues found."
            : $"{issues.Count} issue(s) found.";
        await LogAsync(rule.Id, action.ActionType, "Success", null, summary, isTest, ct);
    }

    private async Task RunSeedDataAsync(
        AutomationRule rule, AutomationAction action, string userId, DatabaseSchema schema, bool isTest, CancellationToken ct)
    {
        var decision = await _quota.TryReserveAsync(userId, AutomationTokenEstimate, ct);
        if (decision != AiQuotaDecision.Allowed)
        {
            await LogAsync(rule.Id, action.ActionType, "Skipped", "Quota exceeded.", null, isTest, ct);
            return;
        }

        // v1'de otomatik veritabanına YAZILMAZ — yalnızca üretilip özetlenir.
        var sql = await _aiService.GenerateMockDataAsync(schema);
        var summary = sql.Length > 500 ? sql[..500] + "…" : sql;
        await LogAsync(rule.Id, action.ActionType, "Success", null, summary, isTest, ct);
    }

    private static string? ExtractUrl(string actionConfigJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(actionConfigJson);
            return doc.RootElement.TryGetProperty("url", out var v) ? v.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task LogAsync(
        string ruleId, string actionType, string status, string? error, string? summary, bool isTest, CancellationToken ct)
    {
        _db.AutomationRunLogs.Add(new AutomationRunLog
        {
            RuleId = ruleId, ActionType = actionType, Status = status,
            ErrorMessage = error, ResultSummary = summary, IsTest = isTest,
        });
        await _db.SaveChangesAsync(ct);
    }
}
