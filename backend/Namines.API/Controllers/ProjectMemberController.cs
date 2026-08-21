using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;

namespace Namines.API.Controllers;

public sealed record AddMemberRequest(string Email, OrgRole Role = OrgRole.Editor);
public sealed record ChangeMemberRoleRequest(OrgRole Role);

/// <summary>
/// 05 §6 — proje ekibi (organizasyon üyeliği).
///
/// Neden var: new-phase/29 §3'ün "Destructive/Breaking → 2 FARKLI kişi onaylamalı"
/// kuralı kodlanmış ve test edilmişti ama UYGULANAMIYORDU — projeye ikinci bir
/// kullanıcı eklemenin hiçbir yolu yoktu, dolayısıyla o risk seviyesindeki her
/// change request kalıcı olarak kilitleniyordu (sahibi kendi değişikliğini
/// onaylayamaz, başkası da erişemez). Bu controller o kilidi açar.
///
/// Kapsam sadeleştirmesi: doc'ta e-posta davetli `org_invites` akışı var
/// (token + expiry). E-posta altyapısı henüz yok, o yüzden üye DOĞRUDAN
/// e-posta adresiyle eklenir — kullanıcının önceden kayıtlı olması gerekir.
/// Davet akışı e-posta servisi geldiğinde eklenir.
/// </summary>
[ApiController]
[Route("api/project/{projectId}/members")]
[Authorize]
public class ProjectMemberController : ControllerBase
{
    private readonly AuthDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public ProjectMemberController(AuthDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>Projenin org'unu bul; yoksa (eski satır) sahibinin kişisel org'una taşı.</summary>
    private async Task<string?> ResolveOrgIdAsync(string projectId, CancellationToken ct)
    {
        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return null;
        if (!string.IsNullOrEmpty(project.OrganizationId)) return project.OrganizationId;

        // Migration öncesinden kalmış satır — tembel taşıma.
        var owner = await _userManager.FindByIdAsync(project.UserId);
        var org = await _context.GetOrCreatePersonalOrgAsync(project.UserId, owner?.UserName ?? "Personal", ct);
        project.OrganizationId = org.Id;
        await _context.SaveChangesAsync(ct);
        return org.Id;
    }

    [HttpGet]
    public async Task<IActionResult> List(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!await _context.CanViewAsync(projectId, userId, ct))
            return NotFound(new { error = "Proje bulunamadı." });

        var orgId = await ResolveOrgIdAsync(projectId, ct);
        if (orgId is null) return NotFound(new { error = "Proje bulunamadı." });

        var members = await _context.OrganizationMembers
            .Where(m => m.OrganizationId == orgId)
            .Include(m => m.User)
            .OrderBy(m => m.JoinedAt)
            .Select(m => new { m.UserId, Username = m.User.UserName, Email = m.User.Email, m.Role, m.JoinedAt })
            .ToListAsync(ct);

        return Ok(members);
    }

    [HttpPost]
    public async Task<IActionResult> Add(string projectId, [FromBody] AddMemberRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!await _context.CanManageMembersAsync(projectId, userId, ct))
            return NotFound(new { error = "Proje bulunamadı veya üye yönetme yetkiniz yok." });

        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { error = "E-posta zorunludur." });

        var invitee = await _userManager.FindByEmailAsync(request.Email.Trim());
        if (invitee is null)
            return NotFound(new { error = "Bu e-posta ile kayıtlı bir kullanıcı yok. (Davet akışı henüz yok — kullanıcı önce kaydolmalı.)" });

        var orgId = await ResolveOrgIdAsync(projectId, ct);
        if (orgId is null) return NotFound(new { error = "Proje bulunamadı." });

        var already = await _context.OrganizationMembers
            .AnyAsync(m => m.OrganizationId == orgId && m.UserId == invitee.Id, ct);
        if (already)
            return Conflict(new { error = "Bu kullanıcı zaten ekipte." });

        await _context.OrganizationMembers.AddAsync(new OrganizationMember
        {
            OrganizationId = orgId,
            UserId = invitee.Id,
            Role = request.Role,
            JoinedAt = DateTime.UtcNow
        }, ct);
        await _context.SaveChangesAsync(ct);

        return Ok(new { invitee.Id, invitee.UserName, invitee.Email, Role = request.Role });
    }

    [HttpPut("{memberUserId}")]
    public async Task<IActionResult> ChangeRole(string projectId, string memberUserId, [FromBody] ChangeMemberRoleRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!await _context.CanManageMembersAsync(projectId, userId, ct))
            return NotFound(new { error = "Proje bulunamadı veya üye yönetme yetkiniz yok." });

        var orgId = await ResolveOrgIdAsync(projectId, ct);
        if (orgId is null) return NotFound(new { error = "Proje bulunamadı." });

        var member = await _context.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == orgId && m.UserId == memberUserId, ct);
        if (member is null) return NotFound(new { error = "Üye bulunamadı." });

        // Son Owner'ı düşürme — org sahipsiz kalırsa üye yönetimi kilitlenir.
        if (member.Role == OrgRole.Owner && request.Role != OrgRole.Owner)
        {
            var ownerCount = await _context.OrganizationMembers
                .CountAsync(m => m.OrganizationId == orgId && m.Role == OrgRole.Owner, ct);
            if (ownerCount <= 1)
                return Conflict(new { error = "Son sahibin rolü düşürülemez — önce başka bir sahip atayın." });
        }

        member.Role = request.Role;
        await _context.SaveChangesAsync(ct);
        return Ok(new { member.UserId, member.Role });
    }

    [HttpDelete("{memberUserId}")]
    public async Task<IActionResult> Remove(string projectId, string memberUserId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!await _context.CanManageMembersAsync(projectId, userId, ct))
            return NotFound(new { error = "Proje bulunamadı veya üye yönetme yetkiniz yok." });

        var orgId = await ResolveOrgIdAsync(projectId, ct);
        if (orgId is null) return NotFound(new { error = "Proje bulunamadı." });

        var member = await _context.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == orgId && m.UserId == memberUserId, ct);
        if (member is null) return NotFound(new { error = "Üye bulunamadı." });

        if (member.Role == OrgRole.Owner)
        {
            var ownerCount = await _context.OrganizationMembers
                .CountAsync(m => m.OrganizationId == orgId && m.Role == OrgRole.Owner, ct);
            if (ownerCount <= 1)
                return Conflict(new { error = "Son sahip ekipten çıkarılamaz." });
        }

        _context.OrganizationMembers.Remove(member);
        await _context.SaveChangesAsync(ct);
        return Ok(new { removed = memberUserId });
    }
}
