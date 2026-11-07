using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.Core.Models.Auth;
using Namines.Identity.Application;

namespace Namines.API.Controllers;

public sealed record CreateInviteRequest(OrgRole Role = OrgRole.Editor, int ExpiresInDays = 7);

/// <summary>
/// Ekip yönetimi: üyeler, davetler, aktif organizasyon ve proje paylaşımı. Kural Identity'de (<see cref="ITeamService"/>),
/// paylaşım Studio'da; controller yalnızca HTTP'ye çevirir.
/// </summary>
[ApiController]
[Route("api/team")]
[Authorize]
public class TeamController : ControllerBase
{
    private readonly ITeamService _team;
    private readonly IStudioProjects _studioProjects;
    private readonly IIdentityDirectory _identity;
    private readonly Namines.Studio.Application.IProjectLifecycleService _lifecycle;

    public TeamController(ITeamService team, IStudioProjects studioProjects, IIdentityDirectory identity, Namines.Studio.Application.IProjectLifecycleService lifecycle)
    {
        _team = team;
        _studioProjects = studioProjects;
        _lifecycle = lifecycle;
        _identity = identity;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private IActionResult Map<T>(ModuleOutcome<T> outcome, Func<T, object> ok) => outcome.Kind switch
    {
        ModuleOutcomeKind.Ok => Ok(ok(outcome.Value!)),
        ModuleOutcomeKind.NotFound => NotFound(new { error = outcome.Error }),
        ModuleOutcomeKind.Invalid => BadRequest(new { error = outcome.Error }),
        ModuleOutcomeKind.Conflict => Conflict(new { error = outcome.Error }),
        ModuleOutcomeKind.Forbidden => StatusCode(403, new { error = outcome.Error }),
        _ => StatusCode(outcome.StatusCode ?? 500, new { error = outcome.Error }),
    };

    // GET /api/team — aktif ekip, üyeler, bekleyen davetler, koltuk durumu.
    [HttpGet]
    public async Task<IActionResult> GetTeam(CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var team = await _team.GetTeamAsync(userId, ct);
        return Ok(new
        {
            plan = team.Plan,
            teamEnabled = team.TeamEnabled,
            seats = new { total = team.Seats.Total, used = team.Seats.Used, available = team.Seats.Available },
            organizationId = team.OrganizationId,
            members = team.Members.Select(m => new { m.UserId, username = m.Username, email = m.Email, role = m.Role, m.JoinedAt, isYou = m.IsYou }),
            pendingInvites = team.PendingInvites.Select(i => new { i.Id, role = i.Role, i.CreatedAt, i.ExpiresAt }),
        });
    }

    // POST /api/team/invites
    [HttpPost("invites")]
    public async Task<IActionResult> CreateInvite([FromBody] CreateInviteRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        return Map(await _team.CreateInviteAsync(userId, request.Role, request.ExpiresInDays, ct),
            invite => new { invite.Id, token = invite.Token, role = invite.Role, invite.ExpiresAt });
    }

    // DELETE /api/team/invites/{inviteId}
    [HttpDelete("invites/{inviteId}")]
    public async Task<IActionResult> RevokeInvite(string inviteId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        return Map(await _team.RevokeInviteAsync(userId, inviteId, ct), revoked => new { revoked });
    }

    // GET /api/team/invites/{token}/preview — anonim: davet linkine tıklayan henüz giriş yapmamış olabilir.
    [AllowAnonymous]
    [HttpGet("invites/{token}/preview")]
    public async Task<IActionResult> PreviewInvite(string token, CancellationToken ct) =>
        Map(await _team.PreviewInviteAsync(token, ct),
            p => new { organization = p.Organization, role = p.Role, p.ExpiresAt });

    // POST /api/team/invites/{token}/accept
    [HttpPost("invites/{token}/accept")]
    public async Task<IActionResult> AcceptInvite(string token, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        return Map(await _team.AcceptInviteAsync(userId, token, ct), a => new { joined = a.Joined, role = a.Role });
    }

    // GET /api/team/activity — ekibin son projeleri.
    [HttpGet("activity")]
    public async Task<IActionResult> GetActivity(CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var orgId = await _team.GetActiveOrganizationIdAsync(userId, ct);

        var headers = await _studioProjects.ListInOrganizationAsync(orgId, 50, ct);
        var ownerNames = await _identity.GetUserNamesAsync(headers.Select(p => p.UserId).Distinct().ToList(), ct);
        var projects = headers.Select(p => new
        {
            p.Id,
            p.Name,
            p.DbType,
            p.UpdatedAt,
            ownerUserId = p.UserId,
            ownerName = ownerNames.GetValueOrDefault(p.UserId),
        }).ToList();

        return Ok(new { projects });
    }

    // POST /api/team/projects/{projectId}/share
    [HttpPost("projects/{projectId}/share")]
    public async Task<IActionResult> ShareProject(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var orgId = await _team.GetActiveOrganizationIdAsync(userId, ct);
        var moved = await _lifecycle.ShareToOrganizationAsync(userId, projectId, orgId, ct);
        if (!moved.IsOk)
            return NotFound(new { error = "Project not found or you do not have permission to share it." });

        return Ok(new { projectId, organizationId = moved.Value });
    }

    // POST /api/team/projects/{projectId}/unshare
    [HttpPost("projects/{projectId}/unshare")]
    public async Task<IActionResult> UnshareProject(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var moved = await _lifecycle.UnshareAsync(userId, projectId, ct);
        if (!moved.IsOk)
            return NotFound(new { error = "Project not found or you do not have permission to unshare it." });

        return Ok(new { projectId, organizationId = moved.Value });
    }
}
