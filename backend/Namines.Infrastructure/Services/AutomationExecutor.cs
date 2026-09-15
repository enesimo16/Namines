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
            .Where(r => r.ProjectId == projectId && r.Enabled).ToListAsync(ct);
        if (rules.Count == 0) return;

        var matched = AutomationRuleMatcher.Match(diff, oldSchema, newSchema, rules);
        if (matched.Count == 0) return;

        var engine = Enum.TryParse<DatabaseType>(project.DbType, ignoreCase: true, out var parsedEngine)
            ? parsedEngine : DatabaseType.PostgreSQL;

        foreach (var rule in matched)
        {
            // "Toast" sunucuda HİÇ işlenmiyor — istemci kendi event bus'ından
            // dinliyor. Log dahi yazılmıyor: sunucunun hiç bilmediği bir şeyin
            // "çalıştı" kaydı tutması yanıltıcı olurdu.
            if (rule.ActionType == "Toast") continue;

            try
            {
                await RunOneAsync(rule, project.UserId, newSchema, engine, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Namines Flow: {RuleId} kurali calistirilirken beklenmeyen hata.", rule.Id);
                await LogAsync(rule.Id, "Failed", ex.Message, null, ct);
            }
        }
    }

    private async Task RunOneAsync(AutomationRule rule, string userId, DatabaseSchema schema, DatabaseType engine, CancellationToken ct)
    {
        switch (rule.ActionType)
        {
            case "Webhook":
                await RunWebhookAsync(rule, ct);
                return;
            case "DbaCheck":
                await RunDbaCheckAsync(rule, userId, schema, engine, ct);
                return;
            case "SeedData":
                await RunSeedDataAsync(rule, userId, schema, ct);
                return;
            default:
                await LogAsync(rule.Id, "Failed", $"Unknown action type: {rule.ActionType}", null, ct);
                return;
        }
    }

    private async Task RunWebhookAsync(AutomationRule rule, CancellationToken ct)
    {
        var url = ExtractUrl(rule.ActionConfigJson);

        // HER ÇALIŞTIRMADA yeniden doğrulanıyor — kaydedilen bir URL zamanla
        // farklı bir IP'ye çözülebilir (DNS rebinding).
        if (url is null || !SsrfGuard.IsUrlSafe(url))
        {
            await LogAsync(rule.Id, "Skipped", "Webhook URL is missing or not a safe public target.", null, ct);
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
            await LogAsync(rule.Id, "Success", null, null, ct);
        else
            await LogAsync(rule.Id, "Failed", $"Webhook returned {(int)response.StatusCode}.", null, ct);
    }

    private async Task RunDbaCheckAsync(AutomationRule rule, string userId, DatabaseSchema schema, DatabaseType engine, CancellationToken ct)
    {
        var decision = await _quota.TryReserveAsync(userId, AutomationTokenEstimate, ct);
        if (decision != AiQuotaDecision.Allowed)
        {
            await LogAsync(rule.Id, "Skipped", "Quota exceeded.", null, ct);
            return;
        }

        var issues = await _groqDba.AnalyzeSchemaDbaAsync(schema, engine);
        var summary = issues.Count == 0
            ? "No issues found."
            : $"{issues.Count} issue(s) found.";
        await LogAsync(rule.Id, "Success", null, summary, ct);
    }

    private async Task RunSeedDataAsync(AutomationRule rule, string userId, DatabaseSchema schema, CancellationToken ct)
    {
        var decision = await _quota.TryReserveAsync(userId, AutomationTokenEstimate, ct);
        if (decision != AiQuotaDecision.Allowed)
        {
            await LogAsync(rule.Id, "Skipped", "Quota exceeded.", null, ct);
            return;
        }

        // v1'de otomatik veritabanına YAZILMAZ — yalnızca üretilip özetlenir.
        var sql = await _aiService.GenerateMockDataAsync(schema);
        var summary = sql.Length > 500 ? sql[..500] + "…" : sql;
        await LogAsync(rule.Id, "Success", null, summary, ct);
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

    private async Task LogAsync(string ruleId, string status, string? error, string? summary, CancellationToken ct)
    {
        _db.AutomationRunLogs.Add(new AutomationRunLog
        {
            RuleId = ruleId, Status = status, ErrorMessage = error, ResultSummary = summary,
        });
        await _db.SaveChangesAsync(ct);
    }
}
