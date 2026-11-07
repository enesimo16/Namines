using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Namines.BuildingBlocks.Application;
using Namines.Core.Enums;
using Namines.Governance.Application;

namespace Namines.API.Controllers;

public class QuickChangeRequestRequest
{
    public string ProjectId { get; set; } = string.Empty;
    public string SchemaJson { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Message { get; set; }
}

public class DecideChangeRequestRequest
{
    public ApprovalDecision Decision { get; set; }
    public string? Comment { get; set; }
}

public class CodeFileInput
{
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class ScanAffectedCodeRequest
{
    public List<CodeFileInput> Files { get; set; } = new();
}

public class SetAutoApproveSafeRequest
{
    public bool Enabled { get; set; }
}

/// <summary>
/// Veritabanı değişiklik incelemesi (Database PR). Yetki, durum geçişi ve eşzamanlılık Governance'ta
/// (<see cref="IChangeRequestService"/>); burada yalnızca HTTP eşlemesi var.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChangeRequestController : ControllerBase
{
    private readonly IChangeRequestService _changeRequests;
    private readonly Namines.Studio.Application.IProjectLifecycleService _lifecycle;

    public ChangeRequestController(IChangeRequestService changeRequests, Namines.Studio.Application.IProjectLifecycleService lifecycle)
    {
        _changeRequests = changeRequests;
        _lifecycle = lifecycle;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private IActionResult Map<T>(ModuleOutcome<T> outcome, Func<T, object> ok) => outcome.Kind switch
    {
        ModuleOutcomeKind.Ok => Ok(ok(outcome.Value!)),
        ModuleOutcomeKind.NotFound => NotFound(new { error = outcome.Error }),
        ModuleOutcomeKind.Invalid => BadRequest(new { error = outcome.Error }),
        ModuleOutcomeKind.Conflict => Conflict(new { error = outcome.Error }),
        ModuleOutcomeKind.Forbidden => Forbid(),
        _ => StatusCode(outcome.StatusCode ?? 500, new { error = outcome.Error }),
    };

    // POST /api/changerequest/quick — "Request Review": son şemayı sürüm olarak kaydeder ve bir istek açar.
    [HttpPost("quick")]
    [RequestSizeLimit(SchemaRequestLimits.SingleSchemaBytes)]
    public async Task<IActionResult> CreateQuick([FromBody] QuickChangeRequestRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        return Map(
            await _changeRequests.CreateQuickAsync(userId,
                new QuickChangeRequestInput(request.ProjectId, request.SchemaJson, request.Title, request.Message), ct),
            created => new { created.Id });
    }

    // PUT /api/changerequest/project/{projectId}/auto-approve-safe
    [HttpPut("project/{projectId}/auto-approve-safe")]
    public async Task<IActionResult> SetAutoApproveSafe(string projectId, [FromBody] SetAutoApproveSafeRequest request)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        // Yetki (Admin/Owner) Studio'da: onay zorunluluğunu gevşeten ayar.
        if (!(await _lifecycle.SetAutoApproveAsync(userId, projectId, request.Enabled)).IsOk)
            return NotFound(new { error = "Project not found, or it does not belong to this user." });

        return Ok(new { Id = projectId, AutoApproveSafeChanges = request.Enabled });
    }

    [HttpGet("{id}/audit")]
    public async Task<IActionResult> GetAuditLog(string id, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        return Map(await _changeRequests.GetAuditAsync(userId, id, ct),
            entries => entries.Select(a => new { a.Id, a.Action, a.ActorUserId, a.ActorUsername, a.Details, a.CreatedAt }).ToList());
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDetail(string id, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        return Map(await _changeRequests.GetDetailAsync(userId, id, ct), d => new
        {
            d.Id,
            d.ProjectId,
            d.BranchId,
            d.BranchName,
            d.Title,
            d.Status,
            d.RiskLevel,
            d.CreatedByUserId,
            d.CreatedAt,
            d.ResolvedAt,
            HeadVersion = new { d.HeadVersion.Id, d.HeadVersion.Version, d.HeadVersion.TableCount, d.HeadVersion.CreatedAt },
            BaseVersion = d.BaseVersion is null ? null : new { d.BaseVersion.Id, d.BaseVersion.Version, d.BaseVersion.TableCount, d.BaseVersion.CreatedAt },
            Impact = d.Impact,
            AiExplanation = d.AiExplanation,
            Migration = d.Migration,
            TestRun = d.TestRun is null ? null : new
            {
                d.TestRun.TestRunSupported,
                d.TestRun.TestRunSuccess,
                d.TestRun.TestRunMessage,
                d.TestRun.TestRunFailedStatement,
                d.TestRun.TestRunDurationMs,
                d.TestRun.TestRunAt,
            },
            d.RequiredApprovals,
            d.ApprovedCount,
            d.RejectedCount,
            Approvals = d.Approvals.Select(a => new { a.Id, a.UserId, a.Username, a.Decision, a.Comment, a.CreatedAt }),
        });
    }

    [HttpGet("project/{projectId}")]
    public async Task<IActionResult> ListForProject(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        return Map(await _changeRequests.ListForProjectAsync(userId, projectId, ct),
            list => list.Select(c => new
            {
                c.Id, c.Title, c.Status, c.RiskLevel, c.BranchName, c.TableCount, c.CreatedAt, c.ResolvedAt, c.ApprovedCount, c.RequiredApprovals,
            }).ToList());
    }

    [HttpPost("{id}/decide")]
    public async Task<IActionResult> Decide(string id, [FromBody] DecideChangeRequestRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        return Map(await _changeRequests.DecideAsync(userId, id, request.Decision, request.Comment, ct),
            r => new { r.Id, r.Status });
    }

    [EnableRateLimiting("sensitive")]
    [HttpPost("{id}/run-tests")]
    public async Task<IActionResult> RunTests(string id, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        return Map(await _changeRequests.RunTestsAsync(userId, id, cancellationToken), result => result);
    }

    [HttpPost("{id}/scan-affected-code")]
    public async Task<IActionResult> ScanAffectedCode(string id, [FromBody] ScanAffectedCodeRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        return Map(
            await _changeRequests.ScanAffectedCodeAsync(userId, id, request.Files.Select(f => new AffectedCodeFile(f.FileName, f.Content)).ToList(), ct),
            scan => new { candidateIdentifiers = scan.CandidateIdentifiers, matches = scan.Matches, filesScanned = scan.FilesScanned });
    }
}
