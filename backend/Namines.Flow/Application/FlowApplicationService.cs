using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Namines.BuildingBlocks.Security;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Security;

namespace Namines.Flow.Application;

public sealed record FlowRuleLastRunSummary(
    string Status,
    DateTime TriggeredAt,
    string ActionType,
    string? ErrorMessage);

public sealed record FlowRuleListItem(
    string Id,
    string ProjectId,
    string? ScopeTableId,
    string Name,
    string TriggerType,
    string ConditionsJson,
    bool Enabled,
    List<AutomationAction> Actions,
    FlowRuleLastRunSummary? LastRun);

public interface IFlowAiQuotaGate
{
    Task<bool> TryReserveAsync(string userId, int estimatedTokens, CancellationToken ct = default);
}

public interface IFlowApplicationService
{
    Task<IReadOnlyList<FlowRuleListItem>> GetRulesWithLastRunAsync(string projectId, CancellationToken ct = default);
    Task<AutomationRule?> GetRuleAsync(string ruleId, bool includeActions = false, CancellationToken ct = default);
    Task<AutomationRule> CreateRuleAsync(AutomationRule rule, CancellationToken ct = default);
    Task<AutomationRule?> UpdateRuleAsync(
        string ruleId,
        string name,
        string? scopeTableId,
        string triggerType,
        string conditionsJson,
        bool enabled,
        List<AutomationAction> actions,
        CancellationToken ct = default);
    Task<bool> DeleteRuleAsync(string ruleId, CancellationToken ct = default);
    Task<IReadOnlyList<AutomationRunLog>> GetRunsAsync(string ruleId, int take, CancellationToken ct = default);
    Task<int> CountRecentTestRunsAsync(string ruleId, DateTime since, CancellationToken ct = default);
    Task<IReadOnlyList<AutomationRunLog>> GetTestRunLogsSinceAsync(string ruleId, DateTime runStartedAt, CancellationToken ct = default);
    Task<IReadOnlySet<string>> GetProjectIdsWithEnabledRulesAsync(IReadOnlyCollection<string> projectIds, CancellationToken ct = default);
    Task<(int RulesCount, int TotalExecutions, DateTime? LastTriggeredAt, string[] ActiveTriggers)> GetProjectFlowSummaryAsync(
        string projectId,
        CancellationToken ct = default);
    Task RunAsync(string projectId, SchemaDiffResult diff, DatabaseSchema oldSchema, DatabaseSchema newSchema, CancellationToken ct = default);
    Task RunRuleAsync(
        AutomationRule rule,
        string userId,
        DatabaseSchema schema,
        DatabaseType engine,
        AutomationTriggerContext context,
        string projectName,
        bool isTest,
        CancellationToken ct = default);
    Task<int> PurgeRunLogsAsync(int retentionDays, int maxPerRule, CancellationToken ct = default);
}

/// <summary>
/// Namines Flow modülünün uygulama katmanı servisi.
/// <see cref="IFlowData"/> üzerinden kural ve çalışma kayıtlarını yönetir;
/// proje üst verisine <see cref="ITenantProjectAuthority"/> sözleşmesiyle erişir.
/// </summary>
internal sealed class FlowApplicationService : IFlowApplicationService
{
    private const int AutomationTokenEstimate = 2500;
    private const string DefaultMessage = "Namines Flow: {{trigger}} on {{tableName}} in {{projectName}}";
    private static readonly JsonSerializerOptions ConfigJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IFlowData _db;
    private readonly ITenantProjectAuthority _authority;
    private readonly IFlowAiQuotaGate _quota;
    private readonly IDbaSchemaAnalyzer? _dbaAnalyzer;
    private readonly IAIService? _aiService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILinterService _linter;
    private readonly ILogger<FlowApplicationService> _logger;

    public FlowApplicationService(
        IFlowData db,
        ITenantProjectAuthority authority,
        IFlowAiQuotaGate quota,
        IDbaSchemaAnalyzer? dbaAnalyzer,
        IAIService? aiService,
        IHttpClientFactory httpClientFactory,
        ILinterService linter,
        ILogger<FlowApplicationService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _quota = quota ?? throw new ArgumentNullException(nameof(quota));
        _dbaAnalyzer = dbaAnalyzer;
        _aiService = aiService;
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _linter = linter ?? throw new ArgumentNullException(nameof(linter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<FlowRuleListItem>> GetRulesWithLastRunAsync(string projectId, CancellationToken ct = default)
    {
        var rules = await _db.AutomationRules.AsNoTracking()
            .Include(r => r.Actions)
            .Where(r => r.ProjectId == projectId)
            .ToListAsync(ct);

        foreach (var rule in rules)
            rule.Actions = rule.Actions.OrderBy(a => a.SortOrder).ToList();

        var ruleIds = rules.Select(r => r.Id).ToList();
        var lastRuns = ruleIds.Count == 0
            ? new Dictionary<string, AutomationRunLog>()
            : (await _db.AutomationRunLogs.AsNoTracking()
                    .Where(l => ruleIds.Contains(l.RuleId))
                    .GroupBy(l => l.RuleId)
                    .Select(g => g.OrderByDescending(l => l.TriggeredAt).First())
                    .ToListAsync(ct))
                .ToDictionary(l => l.RuleId);

        return rules.Select(r => new FlowRuleListItem(
            r.Id,
            r.ProjectId,
            r.ScopeTableId,
            r.Name,
            r.TriggerType,
            r.ConditionsJson,
            r.Enabled,
            r.Actions,
            lastRuns.TryGetValue(r.Id, out var run)
                ? new FlowRuleLastRunSummary(run.Status, run.TriggeredAt, run.ActionType, run.ErrorMessage)
                : null)).ToList();
    }

    public async Task<AutomationRule?> GetRuleAsync(string ruleId, bool includeActions = false, CancellationToken ct = default)
    {
        IQueryable<AutomationRule> query = _db.AutomationRules.AsNoTracking();
        if (includeActions)
            query = query.Include(r => r.Actions);

        var rule = await query.FirstOrDefaultAsync(r => r.Id == ruleId, ct);
        if (rule is not null && includeActions)
            rule.Actions = rule.Actions.OrderBy(a => a.SortOrder).ToList();

        return rule;
    }

    public async Task<AutomationRule> CreateRuleAsync(AutomationRule rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _db.AutomationRules.Add(rule);
        await _db.SaveChangesAsync(ct);
        return rule;
    }

    public async Task<AutomationRule?> UpdateRuleAsync(
        string ruleId,
        string name,
        string? scopeTableId,
        string triggerType,
        string conditionsJson,
        bool enabled,
        List<AutomationAction> actions,
        CancellationToken ct = default)
    {
        var rule = await _db.AutomationRules
            .Include(r => r.Actions)
            .FirstOrDefaultAsync(r => r.Id == ruleId, ct);
        if (rule is null) return null;

        rule.Name = name;
        rule.ScopeTableId = scopeTableId;
        rule.TriggerType = triggerType;
        rule.ConditionsJson = conditionsJson;
        rule.Enabled = enabled;

        _db.AutomationActions.RemoveRange(rule.Actions);
        rule.Actions = actions;

        await _db.SaveChangesAsync(ct);
        return rule;
    }

    public async Task<bool> DeleteRuleAsync(string ruleId, CancellationToken ct = default)
    {
        var rule = await _db.AutomationRules.FirstOrDefaultAsync(r => r.Id == ruleId, ct);
        if (rule is null) return false;

        _db.AutomationRules.Remove(rule);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<AutomationRunLog>> GetRunsAsync(string ruleId, int take, CancellationToken ct = default)
    {
        var effectiveTake = take <= 0 ? 20 : Math.Min(take, 100);
        return await _db.AutomationRunLogs.AsNoTracking()
            .Where(l => l.RuleId == ruleId)
            .OrderByDescending(l => l.TriggeredAt)
            .Take(effectiveTake)
            .ToListAsync(ct);
    }

    public Task<int> CountRecentTestRunsAsync(string ruleId, DateTime since, CancellationToken ct = default) =>
        _db.AutomationRunLogs.AsNoTracking()
            .CountAsync(l => l.RuleId == ruleId && l.IsTest && l.TriggeredAt >= since, ct);

    public async Task<IReadOnlyList<AutomationRunLog>> GetTestRunLogsSinceAsync(
        string ruleId, DateTime runStartedAt, CancellationToken ct = default) =>
        await _db.AutomationRunLogs.AsNoTracking()
            .Where(l => l.RuleId == ruleId && l.IsTest && l.TriggeredAt >= runStartedAt)
            .OrderByDescending(l => l.TriggeredAt)
            .ToListAsync(ct);

    public async Task<IReadOnlySet<string>> GetProjectIdsWithEnabledRulesAsync(
        IReadOnlyCollection<string> projectIds, CancellationToken ct = default)
    {
        if (projectIds is null || projectIds.Count == 0)
            return new HashSet<string>();

        var ids = await _db.AutomationRules.AsNoTracking()
            .Where(r => projectIds.Contains(r.ProjectId) && r.Enabled)
            .Select(r => r.ProjectId)
            .Distinct()
            .ToListAsync(ct);

        return ids.ToHashSet();
    }

    public async Task<(int RulesCount, int TotalExecutions, DateTime? LastTriggeredAt, string[] ActiveTriggers)> GetProjectFlowSummaryAsync(
        string projectId,
        CancellationToken ct = default)
    {
        var flowRules = await _db.AutomationRules.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .ToListAsync(ct);

        var ruleIds = flowRules.Select(r => r.Id).ToList();
        var flowExecs = ruleIds.Count > 0
            ? await _db.AutomationRunLogs.AsNoTracking().CountAsync(l => ruleIds.Contains(l.RuleId), ct)
            : 0;
        var lastTriggeredAt = ruleIds.Count > 0
            ? await _db.AutomationRunLogs.AsNoTracking()
                .Where(l => ruleIds.Contains(l.RuleId))
                .OrderByDescending(l => l.TriggeredAt)
                .Select(l => (DateTime?)l.TriggeredAt)
                .FirstOrDefaultAsync(ct)
            : null;

        var activeTriggers = flowRules.Select(r => r.TriggerType).Distinct().ToArray();
        return (flowRules.Count, flowExecs, lastTriggeredAt, activeTriggers);
    }

    public async Task RunAsync(
        string projectId,
        SchemaDiffResult diff,
        DatabaseSchema oldSchema,
        DatabaseSchema newSchema,
        CancellationToken ct = default)
    {
        var project = await _authority.GetProjectSnapshotAsync(projectId, ct);
        if (project is null) return;

        var rules = await _db.AutomationRules.AsNoTracking()
            .Include(r => r.Actions)
            .Where(r => r.ProjectId == projectId && r.Enabled)
            .ToListAsync(ct);
        if (rules.Count == 0) return;

        var matched = AutomationRuleMatcher.Match(diff, oldSchema, newSchema, rules);
        if (matched.Count == 0) return;

        var engine = Enum.TryParse<DatabaseType>(project.TargetDbType, ignoreCase: true, out var parsedEngine)
            ? parsedEngine
            : DatabaseType.PostgreSQL;

        foreach (var match in matched)
        {
            await RunRuleAsync(match.Rule, project.OwnerUserId, newSchema, engine, match.Context, project.ProjectName, isTest: false, ct);
        }
    }

    public async Task RunRuleAsync(
        AutomationRule rule,
        string userId,
        DatabaseSchema schema,
        DatabaseType engine,
        AutomationTriggerContext context,
        string projectName,
        bool isTest,
        CancellationToken ct = default)
    {
        foreach (var action in rule.Actions.OrderBy(a => a.SortOrder))
        {
            if (action.ActionType == "Toast") continue;

            try
            {
                await RunOneAsync(rule, action, userId, schema, engine, context, projectName, isTest, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Namines Flow: {RuleId} kuralinin {ActionType} adimi calistirilirken beklenmeyen hata.",
                    rule.Id, action.ActionType);
                await LogAsync(rule.Id, action.ActionType, "Failed", ex.Message, null, isTest, ct);
            }
        }
    }

    public async Task<int> PurgeRunLogsAsync(int retentionDays, int maxPerRule, CancellationToken ct = default)
    {
        var deleted = 0;

        if (retentionDays > 0)
        {
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            var old = await _db.AutomationRunLogs.Where(l => l.TriggeredAt < cutoff).ToListAsync(ct);
            if (old.Count > 0)
            {
                _db.AutomationRunLogs.RemoveRange(old);
                deleted += old.Count;
                await _db.SaveChangesAsync(ct);
            }
        }

        if (maxPerRule > 0)
        {
            var noisyRuleIds = await _db.AutomationRunLogs
                .GroupBy(l => l.RuleId)
                .Where(g => g.Count() > maxPerRule)
                .Select(g => g.Key)
                .ToListAsync(ct);

            foreach (var ruleId in noisyRuleIds)
            {
                var surplus = await _db.AutomationRunLogs
                    .Where(l => l.RuleId == ruleId)
                    .OrderByDescending(l => l.TriggeredAt)
                    .Skip(maxPerRule)
                    .ToListAsync(ct);

                if (surplus.Count == 0) continue;
                _db.AutomationRunLogs.RemoveRange(surplus);
                deleted += surplus.Count;
            }
        }

        if (deleted > 0) await _db.SaveChangesAsync(ct);
        return deleted;
    }

    private async Task RunOneAsync(
        AutomationRule rule,
        AutomationAction action,
        string userId,
        DatabaseSchema schema,
        DatabaseType engine,
        AutomationTriggerContext context,
        string projectName,
        bool isTest,
        CancellationToken ct)
    {
        switch (action.ActionType)
        {
            case "Webhook":
            case "Slack":
            case "Discord":
                await RunHttpAsync(rule, action, context, projectName, isTest, ct);
                return;
            case "Lint":
                await RunLintAsync(rule, action, schema, isTest, ct);
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

    private async Task RunHttpAsync(
        AutomationRule rule,
        AutomationAction action,
        AutomationTriggerContext context,
        string projectName,
        bool isTest,
        CancellationToken ct)
    {
        var config = ParseConfig(action.ActionConfigJson);
        var url = config.Url;

        if (string.IsNullOrWhiteSpace(url) || !SsrfGuard.IsUrlSafe(url))
        {
            await LogAsync(rule.Id, action.ActionType, "Skipped", "Webhook URL is missing or not a safe public target.", null, isTest, ct);
            return;
        }

        var now = DateTime.UtcNow;
        string Render(string? t) => AutomationTemplate.Render(t, rule.TriggerType, context, projectName, now);

        var payload = action.ActionType switch
        {
            "Slack" => JsonSerializer.Serialize(new { text = Render(config.Message ?? DefaultMessage) }),
            "Discord" => JsonSerializer.Serialize(new { content = Render(config.Message ?? DefaultMessage) }),
            _ => string.IsNullOrWhiteSpace(config.Body)
                ? JsonSerializer.Serialize(new
                {
                    trigger = rule.TriggerType,
                    table = rule.ScopeTableId,
                    tableName = context.TableName,
                    columnName = context.ColumnName,
                    timestamp = now,
                })
                : Render(config.Body),
        };

        var method = action.ActionType == "Webhook" ? ParseMethod(config.Method) : HttpMethod.Post;
        using var request = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        if (config.Headers is not null)
        {
            foreach (var (key, value) in config.Headers)
            {
                if (!IsSafeHeaderName(key)) continue;
                var safeValue = StripLineBreaks(Render(value));
                request.Headers.TryAddWithoutValidation(key, safeValue);
            }
        }

        var client = _httpClientFactory.CreateClient("AutomationWebhook");
        using var response = await client.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
            await LogAsync(rule.Id, action.ActionType, "Success", null, null, isTest, ct);
        }
        else if ((int)response.StatusCode is >= 300 and < 400)
        {
            await LogAsync(rule.Id, action.ActionType, "Skipped",
                $"Webhook redirected ({(int)response.StatusCode}). Redirects are not followed — use the final URL.",
                null, isTest, ct);
        }
        else
        {
            await LogAsync(rule.Id, action.ActionType, "Failed", $"Webhook returned {(int)response.StatusCode}.", null, isTest, ct);
        }
    }

    private async Task RunLintAsync(
        AutomationRule rule, AutomationAction action, DatabaseSchema schema, bool isTest, CancellationToken ct)
    {
        var result = _linter.Lint(schema);
        var errors = result.Messages.Count(m => m.Severity == LintSeverity.Error);
        var warnings = result.Messages.Count(m => m.Severity == LintSeverity.Warning);
        var summary = result.Messages.Count == 0
            ? "No lint findings."
            : $"{errors} error(s), {warnings} warning(s).";

        await LogAsync(rule.Id, action.ActionType, "Success", null, summary, isTest, ct);
    }

    private async Task RunDbaCheckAsync(
        AutomationRule rule, AutomationAction action, string userId, DatabaseSchema schema,
        DatabaseType engine, bool isTest, CancellationToken ct)
    {
        if (_dbaAnalyzer is null)
        {
            await LogAsync(rule.Id, action.ActionType, "Skipped", "DBA Analyzer service is not configured.", null, isTest, ct);
            return;
        }

        if (!await _quota.TryReserveAsync(userId, AutomationTokenEstimate, ct))
        {
            await LogAsync(rule.Id, action.ActionType, "Skipped", "Quota exceeded.", null, isTest, ct);
            return;
        }

        var issues = await _dbaAnalyzer.AnalyzeSchemaDbaAsync(schema, engine);
        var summary = issues.Count == 0
            ? "No issues found."
            : $"{issues.Count} issue(s) found.";
        await LogAsync(rule.Id, action.ActionType, "Success", null, summary, isTest, ct);
    }

    private async Task RunSeedDataAsync(
        AutomationRule rule, AutomationAction action, string userId, DatabaseSchema schema, bool isTest, CancellationToken ct)
    {
        if (_aiService is null)
        {
            await LogAsync(rule.Id, action.ActionType, "Skipped", "AI service is not configured.", null, isTest, ct);
            return;
        }

        if (!await _quota.TryReserveAsync(userId, AutomationTokenEstimate, ct))
        {
            await LogAsync(rule.Id, action.ActionType, "Skipped", "Quota exceeded.", null, isTest, ct);
            return;
        }

        var sql = await _aiService.GenerateMockDataAsync(schema);
        var summary = sql.Length > 500 ? sql[..500] + "…" : sql;
        await LogAsync(rule.Id, action.ActionType, "Success", null, summary, isTest, ct);
    }

    private sealed class ActionConfig
    {
        public string? Url { get; set; }
        public string? Method { get; set; }
        public string? Body { get; set; }
        public string? Message { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
    }

    private static bool IsSafeHeaderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        foreach (var c in name)
        {
            var ok = char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '!' or '#' or '$' or '%'
                or '&' or '\'' or '*' or '+' or '^' or '`' or '|' or '~';
            if (!ok) return false;
        }
        return true;
    }

    private static string StripLineBreaks(string value) =>
        value.Replace("\r", string.Empty).Replace("\n", string.Empty);

    private static ActionConfig ParseConfig(string actionConfigJson)
    {
        if (string.IsNullOrWhiteSpace(actionConfigJson)) return new ActionConfig();
        try
        {
            return JsonSerializer.Deserialize<ActionConfig>(actionConfigJson, ConfigJsonOptions) ?? new ActionConfig();
        }
        catch (JsonException)
        {
            return new ActionConfig();
        }
    }

    private static HttpMethod ParseMethod(string? method) => method?.ToUpperInvariant() switch
    {
        "PUT" => HttpMethod.Put,
        "PATCH" => HttpMethod.Patch,
        _ => HttpMethod.Post,
    };

    private async Task LogAsync(
        string ruleId, string actionType, string status, string? error, string? summary, bool isTest, CancellationToken ct)
    {
        _db.AutomationRunLogs.Add(new AutomationRunLog
        {
            RuleId = ruleId,
            ActionType = actionType,
            Status = status,
            ErrorMessage = error,
            ResultSummary = summary,
            IsTest = isTest,
        });
        await _db.SaveChangesAsync(ct);
    }
}
