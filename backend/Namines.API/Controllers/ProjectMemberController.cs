using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Namines.BuildingBlocks.Application;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Services;

namespace Namines.API.Controllers;

public sealed record AddMemberRequest(string Email, OrgRole Role = OrgRole.Editor);
public sealed record ChangeMemberRoleRequest(OrgRole Role);

/// <summary>
/// Proje üyeleri. Yetki, üyelik ve anahtar iptali <see cref="IProjectMembershipService"/>'te; controller yalnızca HTTP'ye çevirir.
/// </summary>
[ApiController]
[Route("api/project/{projectId}/members")]
[Authorize]
public class ProjectMemberController : ControllerBase
{
    private readonly IProjectMembershipService _members;

    public ProjectMemberController(IProjectMembershipService members) => _members = members;

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private IActionResult Map<T>(ModuleOutcome<T> outcome, Func<T, object> ok) => outcome.Kind switch
    {
        ModuleOutcomeKind.Ok => Ok(ok(outcome.Value!)),
        ModuleOutcomeKind.NotFound => NotFound(new { error = outcome.Error }),
        ModuleOutcomeKind.Invalid => BadRequest(new { error = outcome.Error }),
        ModuleOutcomeKind.Conflict => Conflict(new { error = outcome.Error }),
        ModuleOutcomeKind.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = outcome.Error }),
        _ => StatusCode(outcome.StatusCode ?? 500, new { error = outcome.Error }),
    };

    [HttpGet]
    public async Task<IActionResult> List(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        return Map(await _members.ListAsync(userId, projectId, ct),
            members => members.Select(m => new { m.UserId, m.Username, m.Email, m.Role, m.JoinedAt }).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Add(string projectId, [FromBody] AddMemberRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        return Map(await _members.AddAsync(userId, projectId, request.Email, request.Role, ct),
            m => new { m.Id, m.UserName, m.Email, m.Role });
    }

    [HttpPut("{memberUserId}")]
    public async Task<IActionResult> ChangeRole(string projectId, string memberUserId, [FromBody] ChangeMemberRoleRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        return Map(await _members.ChangeRoleAsync(userId, projectId, memberUserId, request.Role, ct),
            m => new { m.UserId, m.Role, m.RevokedKeys });
    }

    [HttpDelete("{memberUserId}")]
    public async Task<IActionResult> Remove(string projectId, string memberUserId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        return Map(await _members.RemoveAsync(userId, projectId, memberUserId, ct),
            m => new { removed = m.Removed, revokedKeys = m.RevokedKeys });
    }
}
