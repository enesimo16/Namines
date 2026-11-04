using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Namines.API.Filters;
using Namines.BuildingBlocks.Security;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Flow.Abstractions;
using Namines.Infrastructure.Services;

namespace Namines.API.Controllers;

/// <summary>Zincirdeki tek bir adım. Sıra, dizideki konumdan alınıyor.</summary>
public class AutomationActionDto
{
    public string ActionType { get; set; } = string.Empty;
    public string ActionConfigJson { get; set; } = "{}";
}

public class CreateAutomationRuleRequest
{
    public string ProjectId { get; set; } = string.Empty;
    public string? ScopeTableId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TriggerType { get; set; } = string.Empty;
    public string ConditionsJson { get; set; } = "[]";
    public List<AutomationActionDto> Actions { get; set; } = new();
}

/// <summary>
/// Kuralın DÜZENLENEBİLİR alanları. ProjectId bilerek yok: bir kural
/// oluşturulduğu projeden başka bir projeye taşınmıyor.
/// </summary>
public class UpdateAutomationRuleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? ScopeTableId { get; set; }
    public string TriggerType { get; set; } = string.Empty;
    public string ConditionsJson { get; set; } = "[]";
    public List<AutomationActionDto> Actions { get; set; } = new();
    public bool Enabled { get; set; } = true;
}

[ApiController]
[Route("api/automation")]
[Authorize]
[FeatureGate("flow")]
public class AutomationController : ControllerBase
{
    /// <summary>Hız sınırı penceresi ve pencere başına izin verilen test sayısı.</summary>
    private static readonly TimeSpan TestRunWindow = TimeSpan.FromMinutes(1);
    private const int MaxTestRunsPerWindow = 5;

    private readonly ITenantProjectAuthority _authority;
    private readonly IFlowModule _flow;

    public AutomationController(ITenantProjectAuthority authority, IFlowModule flow)
    {
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _flow = flow ?? throw new ArgumentNullException(nameof(flow));
    }

    private static DatabaseSchema DeserializeSchema(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new DatabaseSchema();
        try
        {
            return JsonSerializer.Deserialize<DatabaseSchema>(json, SchemaJsonOptions.Default) ?? new DatabaseSchema();
        }
        catch (JsonException)
        {
            return new DatabaseSchema();
        }
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private static List<AutomationAction> BuildActions(string ruleId, List<AutomationActionDto> actions) =>
        actions.Select((a, index) => new AutomationAction
        {
            RuleId = ruleId,
            SortOrder = index,
            ActionType = a.ActionType,
            ActionConfigJson = string.IsNullOrWhiteSpace(a.ActionConfigJson) ? "{}" : a.ActionConfigJson,
        }).ToList();

    private async Task<bool> OwnsProjectAsync(string projectId, string userId, CancellationToken ct)
    {
        var project = await _authority.GetProjectSnapshotAsync(projectId, ct);
        return project is not null && string.Equals(project.OwnerUserId, userId, StringComparison.Ordinal);
    }

    [HttpGet("rules")]
    public async Task<IActionResult> GetRules([FromQuery] string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await OwnsProjectAsync(projectId, userId, ct)) return Ok(Array.Empty<AutomationRule>());

        var rules = await _flow.Application.GetRulesWithLastRunAsync(projectId, ct);

        return Ok(rules.Select(r => new
        {
            r.Id,
            r.ProjectId,
            r.ScopeTableId,
            r.Name,
            r.TriggerType,
            r.ConditionsJson,
            r.Enabled,
            r.Actions,
            LastRun = r.LastRun is not null
                ? new { r.LastRun.Status, r.LastRun.TriggeredAt, r.LastRun.ActionType, r.LastRun.ErrorMessage }
                : null,
        }));
    }

    [HttpPost("rules")]
    public async Task<IActionResult> CreateRule([FromBody] CreateAutomationRuleRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await OwnsProjectAsync(request.ProjectId, userId, ct)) return NotFound();

        var rule = new AutomationRule
        {
            ProjectId = request.ProjectId,
            ScopeTableId = request.ScopeTableId,
            Name = request.Name,
            TriggerType = request.TriggerType,
            ConditionsJson = request.ConditionsJson,
        };
        rule.Actions = BuildActions(rule.Id, request.Actions);
        await _flow.Application.CreateRuleAsync(rule, ct);
        return Ok(rule);
    }

    [HttpPut("rules/{id}")]
    public async Task<IActionResult> UpdateRule(string id, [FromBody] UpdateAutomationRuleRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var existing = await _flow.Application.GetRuleAsync(id, includeActions: false, ct);
        if (existing is null) return NotFound();
        if (!await OwnsProjectAsync(existing.ProjectId, userId, ct)) return NotFound();

        var actions = BuildActions(id, request.Actions);
        var updated = await _flow.Application.UpdateRuleAsync(
            id,
            request.Name,
            request.ScopeTableId,
            request.TriggerType,
            request.ConditionsJson,
            request.Enabled,
            actions,
            ct);

        if (updated is null) return NotFound();
        return Ok(updated);
    }

    [HttpGet("rules/{id}/runs")]
    public async Task<IActionResult> GetRuns(string id, [FromQuery] int limit, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var rule = await _flow.Application.GetRuleAsync(id, includeActions: false, ct);
        if (rule is null) return NotFound();
        if (!await OwnsProjectAsync(rule.ProjectId, userId, ct)) return NotFound();

        var take = limit <= 0 ? 20 : Math.Min(limit, 100);
        var runs = await _flow.Application.GetRunsAsync(id, take, ct);
        return Ok(runs);
    }

    [HttpPost("rules/{id}/test")]
    public async Task<IActionResult> TestRule(string id, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var rule = await _flow.Application.GetRuleAsync(id, includeActions: true, ct);
        if (rule is null) return NotFound();

        var project = await _authority.GetProjectSnapshotAsync(rule.ProjectId, ct);
        if (project is null || !string.Equals(project.OwnerUserId, userId, StringComparison.Ordinal))
            return NotFound();

        var since = DateTime.UtcNow - TestRunWindow;
        var serverSideSteps = rule.Actions.Count(a => a.ActionType != "Toast");
        if (serverSideSteps > 0)
        {
            var recentRows = await _flow.Application.CountRecentTestRunsAsync(id, since, ct);
            if (recentRows / serverSideSteps >= MaxTestRunsPerWindow)
                return StatusCode(429, new { error = "Too many test runs for this rule. Try again in a minute." });
        }

        var schema = DeserializeSchema(project.SchemaJson);
        var engine = Enum.TryParse<DatabaseType>(project.TargetDbType, ignoreCase: true, out var parsed)
            ? parsed
            : DatabaseType.PostgreSQL;

        var scopeTableName = rule.ScopeTableId is null
            ? null
            : schema.Tables.FirstOrDefault(t => t.Id == rule.ScopeTableId)?.Name;
        var context = new AutomationTriggerContext(scopeTableName, null, null);

        var runStartedAt = DateTime.UtcNow;
        await _flow.Application.RunRuleAsync(rule, userId, schema, engine, context, project.ProjectName, isTest: true, ct);

        var results = await _flow.Application.GetTestRunLogsSinceAsync(id, runStartedAt, ct);

        return Ok(new
        {
            clientOnlyActions = rule.Actions.Count(a => a.ActionType == "Toast"),
            runs = results,
        });
    }

    [HttpDelete("rules/{id}")]
    public async Task<IActionResult> DeleteRule(string id, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var rule = await _flow.Application.GetRuleAsync(id, includeActions: false, ct);
        if (rule is null) return NotFound();
        if (!await OwnsProjectAsync(rule.ProjectId, userId, ct)) return NotFound();

        await _flow.Application.DeleteRuleAsync(id, ct);
        return Ok(new { deleted = true });
    }
}
