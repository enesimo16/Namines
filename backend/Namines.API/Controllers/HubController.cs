using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Application;
using Namines.Core.Nsl;

namespace Namines.API.Controllers;

public sealed record UpdateRepoSettingsRequest(
    string? Name,
    string? Slug,
    string? Description,
    ProjectVisibility? Visibility,
    string? Category,
    string? Tags,
    string? LinkedGithubRepo,
    bool? AutoApproveSafeChanges,
    string? DbType);

public sealed record ForkRepoRequest(string? Name);

public sealed record CreateIssueRequest(
    string Title,
    string? Body,
    string? RelatedTableName,
    string? NslRuleCode,
    string? Labels);

public sealed record UpdateIssueStateRequest(bool IsClosed);

public sealed record CreateCommentRequest(string Body);

/// <summary>
/// Hub uçları. Kurallar ve yetki <see cref="IHubRepositoryService"/> ile <see cref="IHubIssueService"/>'te;
/// burada yalnızca HTTP eşlemesi var.
/// </summary>
[ApiController]
[Route("api/hub")]
public class HubController : ControllerBase
{
    private readonly IHubRepositoryService _repositories;
    private readonly IHubIssueService _issues;

    public HubController(IHubRepositoryService repositories, IHubIssueService issues)
    {
        _repositories = repositories;
        _issues = issues;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private IActionResult Fail<T>(ModuleOutcome<T> outcome) => outcome.Kind switch
    {
        ModuleOutcomeKind.NotFound => NotFound(new { error = outcome.Error }),
        ModuleOutcomeKind.Invalid => BadRequest(new { error = outcome.Error }),
        ModuleOutcomeKind.Conflict => Conflict(new { error = outcome.Error }),
        ModuleOutcomeKind.Forbidden => Forbid(),
        ModuleOutcomeKind.Failed => StatusCode(outcome.StatusCode ?? 500, new { error = outcome.Error, code = outcome.Code }),
        _ => StatusCode(500),
    };

    [HttpGet("explore")]
    [AllowAnonymous]
    [EnableRateLimiting("explore-public")]
    public async Task<IActionResult> Explore(
        [FromQuery] string? q,
        [FromQuery] string? category,
        [FromQuery] string? dbType,
        [FromQuery] string? sort = "popular", // "popular" | "recent"
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        [FromQuery] string? tag = null,
        CancellationToken ct = default)
    {
        var criteria = new ExploreCriteria(category, tag, q, dbType, sort?.Equals("recent", StringComparison.OrdinalIgnoreCase) == true);
        return Ok(await _repositories.ExploreAsync(criteria, page, pageSize, ct));
    }

    [HttpGet("repos/{owner}/{slugOrId}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetRepo(string owner, string slugOrId, CancellationToken ct)
    {
        var outcome = await _repositories.GetRepoAsync(CurrentUserId, owner, slugOrId, ct);
        return outcome.IsOk ? Ok(outcome.Value) : Fail(outcome);
    }

    [HttpPatch("repos/{projectId}/settings")]
    [Authorize]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> UpdateSettings(string projectId, [FromBody] UpdateRepoSettingsRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        try
        {
            var outcome = await _repositories.UpdateSettingsAsync(userId, projectId, new RepoSettingsCommand(
                request.Name, request.Slug, request.Description, request.Visibility, request.Category,
                request.Tags, request.LinkedGithubRepo, request.AutoApproveSafeChanges, request.DbType), ct);
            return outcome.IsOk ? Ok(outcome.Value) : Fail(outcome);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict.Result();
        }
        catch (DbUpdateException ex) when (DbUniqueViolation.IsUniqueViolation(ex))
        {
            // Ön kontrol ile kayıt arasında aynı sahibin başka bir isteği slug'ı aldıysa unique index yakalar.
            return Conflict(new { error = "This slug is already taken." });
        }
    }

    [HttpDelete("repos/{projectId}")]
    [Authorize]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> DeleteRepo(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var outcome = await _repositories.DeleteRepoAsync(userId, projectId, ct);
        return outcome.IsOk ? NoContent() : Fail(outcome);
    }

    [HttpPost("repos/{projectId}/star")]
    [Authorize]
    [EnableRateLimiting("hub-write")]
    public async Task<IActionResult> ToggleStar(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var outcome = await _repositories.ToggleStarAsync(userId, projectId, ct);
        return outcome.IsOk
            ? Ok(new { isStarred = outcome.Value.IsStarred, starCount = outcome.Value.StarCount })
            : Fail(outcome);
    }

    [HttpPost("repos/{projectId}/fork")]
    [Authorize]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> Fork(string projectId, [FromBody] ForkRepoRequest? request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var outcome = await _repositories.ForkAsync(userId, projectId, request?.Name, ct);
        if (!outcome.IsOk) return Fail(outcome);

        var fork = outcome.Value!;
        return CreatedAtAction(nameof(GetRepo), new { owner = fork.OwnerName, slugOrId = fork.Slug }, new
        {
            fork.Id,
            fork.Name,
            fork.Slug,
            fork.ForkedFromProjectId,
            fork.Visibility,
            fork.CreatedAt,
        });
    }

    [HttpGet("repos/{projectId}/issues")]
    [AllowAnonymous]
    public async Task<IActionResult> ListIssues(
        string projectId,
        [FromQuery] string? status = "open", // "open", "closed", "all"
        [FromQuery] string? relatedTable = null,
        [FromQuery] string? label = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var outcome = await _issues.ListAsync(CurrentUserId, projectId, status, relatedTable, label, page, pageSize, ct);
        return outcome.IsOk ? Ok(outcome.Value) : Fail(outcome);
    }

    [HttpPost("repos/{projectId}/issues")]
    [Authorize]
    [EnableRateLimiting("hub-write")]
    public async Task<IActionResult> CreateIssue(string projectId, [FromBody] CreateIssueRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var outcome = await _issues.CreateAsync(userId, projectId,
            new CreateIssueCommand(request.Title, request.Body, request.RelatedTableName, request.NslRuleCode, request.Labels), ct);
        return outcome.IsOk
            ? CreatedAtAction(nameof(ListIssues), new { projectId }, outcome.Value)
            : Fail(outcome);
    }

    [HttpPatch("issues/{issueId}/state")]
    [Authorize]
    [EnableRateLimiting("hub-write")]
    public async Task<IActionResult> UpdateIssueState(string issueId, [FromBody] UpdateIssueStateRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var outcome = await _issues.SetStateAsync(userId, issueId, request.IsClosed, ct);
        return outcome.IsOk ? Ok(outcome.Value) : Fail(outcome);
    }

    [HttpGet("issues/{issueId}/comments")]
    [AllowAnonymous]
    public async Task<IActionResult> ListComments(string issueId, CancellationToken ct)
    {
        var outcome = await _issues.ListCommentsAsync(CurrentUserId, issueId, ct);
        return outcome.IsOk ? Ok(outcome.Value) : Fail(outcome);
    }

    [HttpPost("issues/{issueId}/comments")]
    [Authorize]
    [EnableRateLimiting("hub-write")]
    public async Task<IActionResult> CreateComment(string issueId, [FromBody] CreateCommentRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var outcome = await _issues.AddCommentAsync(userId, issueId, request.Body, ct);
        return outcome.IsOk ? Ok(outcome.Value) : Fail(outcome);
    }

    [HttpGet("badge/{owner}/{slug}.svg")]
    [AllowAnonymous]
    public async Task<IActionResult> Badge(string owner, string slug, CancellationToken ct)
    {
        var badge = await _repositories.GetBadgeAsync(CurrentUserId, owner, slug, ct);
        if (badge is null)
        {
            var fallbackSvg = GenerateShieldBadge("namines hub", "private / not found", "#64748b");
            Response.Headers.CacheControl = "public, max-age=60";
            return Content(fallbackSvg, "image/svg+xml; charset=utf-8");
        }

        var schema = badge.Schema;
        var findings = NslValidator.Validate(schema);
        var has3nfIssue = findings.Any(f => f.Code == "NSL020" || f.Code == "NSL019");
        var hasError = findings.Any(f => f.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));

        string rightText;
        string rightColor;

        if (hasError)
        {
            rightText = $"{schema.Tables.Count} tables · schema error";
            rightColor = "#ef4444";
        }
        else if (has3nfIssue)
        {
            rightText = $"{schema.Tables.Count} tables · normalization warnings";
            rightColor = "#f59e0b";
        }
        else
        {
            // "3NF verified" bir iddiaydı: NslValidator'ın NSL019/020 sezgisi 3NF'yi KANITLAMAZ,
            // yalnızca bilinen şüpheli kalıpları arar. Rozet yalnızca gerçekte bilinen şeyi söylüyor.
            rightText = $"{schema.Tables.Count} tables · no normalization warnings";
            rightColor = "#10b981";
        }

        var svg = GenerateShieldBadge("namines hub", rightText, rightColor);
        Response.Headers.CacheControl = "public, max-age=300";
        return Content(svg, "image/svg+xml; charset=utf-8");
    }

    private static string GenerateShieldBadge(string leftText, string rightText, string rightColor)
    {
        var safeLeft = WebUtility.HtmlEncode(leftText);
        var safeRight = WebUtility.HtmlEncode(rightText);

        var leftWidth = (int)Math.Round(safeLeft.Length * 6.5) + 14;
        var rightWidth = (int)Math.Round(safeRight.Length * 6.5) + 14;
        var totalWidth = leftWidth + rightWidth;

        var leftMid = leftWidth / 2;
        var rightMid = leftWidth + (rightWidth / 2);

        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="{totalWidth}" height="20" role="img" aria-label="{safeLeft}: {safeRight}">
              <title>{safeLeft}: {safeRight}</title>
              <linearGradient id="s" x2="0" y2="100%">
                <stop offset="0" stop-color="#bbb" stop-opacity=".1"/>
                <stop offset="1" stop-opacity=".1"/>
              </linearGradient>
              <clipPath id="r">
                <rect width="{totalWidth}" height="20" rx="3" fill="#fff"/>
              </clipPath>
              <g clip-path="url(#r)">
                <rect width="{leftWidth}" height="20" fill="#1e293b"/>
                <rect x="{leftWidth}" width="{rightWidth}" height="20" fill="{rightColor}"/>
                <rect width="{totalWidth}" height="20" fill="url(#s)"/>
              </g>
              <g fill="#fff" text-anchor="middle" font-family="DejaVu Sans,Verdana,Geneva,sans-serif" font-size="11">
                <text x="{leftMid}" y="15" fill="#010101" fill-opacity=".3">{safeLeft}</text>
                <text x="{leftMid}" y="14">{safeLeft}</text>
                <text x="{rightMid}" y="15" fill="#010101" fill-opacity=".3">{safeRight}</text>
                <text x="{rightMid}" y="14">{safeRight}</text>
              </g>
            </svg>
            """;
    }
}
