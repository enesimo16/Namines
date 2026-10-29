using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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

    private const string OwnerOnlyError =
        "Only an owner can grant, change or remove the owner role.";

    /// <summary>
    /// Owner katmanına dokunan işlemler (Owner atamak, bir Owner'ın rolünü değiştirmek ya da
    /// onu ekipten çıkarmak) yalnızca Owner'a açık. <c>CanManageMembersAsync</c> Admin'i de
    /// kabul ediyor; kapı burada daraltılmazsa bir Admin kendi rolünü Owner'a yükseltip
    /// "Admin bile yetmez" diye Owner'a bağlanmış her şeye (Desk raw SQL, Ground provision/
    /// delete, Vault restore) ulaşırdı. TeamController de aynı kuralı davet için koyuyor:
    /// "yükseltme ancak var olan bir Owner tarafından yapılabilir".
    /// </summary>
    private async Task<bool> IsCallerOwnerAsync(string projectId, string userId, CancellationToken ct) =>
        await _context.GetRoleAsync(projectId, userId, ct) == OrgRole.Owner;

    [HttpGet]
    public async Task<IActionResult> List(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!await _context.CanViewAsync(projectId, userId, ct))
            return NotFound(new { error = "Project not found." });

        var orgId = await ResolveOrgIdAsync(projectId, ct);
        if (orgId is null) return NotFound(new { error = "Project not found." });

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
            return NotFound(new { error = "Project not found, or you do not have permission to manage members." });

        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { error = "An email address is required." });

        if (!Enum.IsDefined(request.Role))
            return BadRequest(new { error = "Unknown role." });

        if (request.Role == OrgRole.Owner && !await IsCallerOwnerAsync(projectId, userId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = OwnerOnlyError });

        var invitee = await _userManager.FindByEmailAsync(request.Email.Trim());
        if (invitee is null)
            return NotFound(new { error = "No user is registered with this email. (Invites are not available yet - the user must sign up first.)" });

        var orgId = await ResolveOrgIdAsync(projectId, ct);
        if (orgId is null) return NotFound(new { error = "Project not found." });

        var already = await _context.OrganizationMembers
            .AnyAsync(m => m.OrganizationId == orgId && m.UserId == invitee.Id, ct);
        if (already)
            return Conflict(new { error = "This user is already on the team." });

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
            return NotFound(new { error = "Project not found, or you do not have permission to manage members." });

        var orgId = await ResolveOrgIdAsync(projectId, ct);
        if (orgId is null) return NotFound(new { error = "Project not found." });

        var member = await _context.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == orgId && m.UserId == memberUserId, ct);
        if (member is null) return NotFound(new { error = "Member not found." });

        if (!Enum.IsDefined(request.Role))
            return BadRequest(new { error = "Unknown role." });

        // Owner'a yükseltme VE bir Owner'ın rolünü düşürme: ikisi de Owner işi.
        if ((request.Role == OrgRole.Owner || member.Role == OrgRole.Owner)
            && !await IsCallerOwnerAsync(projectId, userId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = OwnerOnlyError });

        // Son Owner'ı düşürme — org sahipsiz kalırsa üye yönetimi kilitlenir.
        if (member.Role == OrgRole.Owner && request.Role != OrgRole.Owner)
        {
            var ownerCount = await _context.OrganizationMembers
                .CountAsync(m => m.OrganizationId == orgId && m.Role == OrgRole.Owner, ct);
            if (ownerCount <= 1)
                return Conflict(new { error = "The last owner's role cannot be lowered - assign another owner first." });
        }

        var wasOwner = member.Role == OrgRole.Owner;
        member.Role = request.Role;

        // Owner'lıktan düşürülen üyenin ham-SQL / dışa-aktarım anahtarları iptal edilir:
        // bu yetkiler yalnızca Owner'a verilebiliyor (bkz. GatewayAccess.AuthenticateAsync,
        // aynı kural orada da uygulanıyor). Yalnızca listede "aktif" görünüp
        // yeniden Owner yapılınca canlanmasın diye açıkça iptal ediliyor.
        var revokedKeys = 0;
        if (wasOwner && request.Role != OrgRole.Owner)
        {
            var privilegedKeys = await _context.GatewayApiKeys
                .Where(k => k.ProjectId == projectId
                            && k.CreatedByUserId == memberUserId
                            && k.RevokedAt == null
                            && (k.CanExecuteSql || k.CanExport))
                .ToListAsync(ct);
            var now = DateTime.UtcNow;
            foreach (var key in privilegedKeys) key.RevokedAt = now;
            revokedKeys = privilegedKeys.Count;
        }

        await _context.SaveChangesAsync(ct);
        return Ok(new { member.UserId, member.Role, revokedKeys });
    }

    [HttpDelete("{memberUserId}")]
    public async Task<IActionResult> Remove(string projectId, string memberUserId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!await _context.CanManageMembersAsync(projectId, userId, ct))
            return NotFound(new { error = "Project not found, or you do not have permission to manage members." });

        var orgId = await ResolveOrgIdAsync(projectId, ct);
        if (orgId is null) return NotFound(new { error = "Project not found." });

        var member = await _context.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == orgId && m.UserId == memberUserId, ct);
        if (member is null) return NotFound(new { error = "Member not found." });

        if (member.Role == OrgRole.Owner)
        {
            if (!await IsCallerOwnerAsync(projectId, userId, ct))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = OwnerOnlyError });

            var ownerCount = await _context.OrganizationMembers
                .CountAsync(m => m.OrganizationId == orgId && m.Role == OrgRole.Owner, ct);
            if (ownerCount <= 1)
                return Conflict(new { error = "The last owner cannot be removed from the team." });
        }

        _context.OrganizationMembers.Remove(member);

        // ÇIKARILAN ÜYENİN ANAHTARLARI DA İPTAL EDİLİYOR.
        //
        // <b>Bulunan açık (docs/last-review H-4a):</b> üyelik kaldırmak hiçbir
        // anahtara dokunmuyordu. `GatewayAccess.AuthenticateAsync` artık üretenin
        // güncel üyeliğini de kontrol ediyor — yani erişim o kontrolle zaten
        // kesiliyor — ama anahtarı AÇIKÇA iptal etmek ayrı bir şey: aksi hâlde
        // anahtar listesinde "aktif" görünmeye devam eder ve üye geri eklendiğinde
        // kimsenin niyet etmediği bir anda yeniden canlanır.
        //
        // Yalnızca BU projenin anahtarları: üye başka bir projede kalabilir.
        var now = DateTime.UtcNow;
        var orphanedKeys = await _context.GatewayApiKeys
            .Where(k => k.ProjectId == projectId
                        && k.CreatedByUserId == memberUserId
                        && k.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var key in orphanedKeys) key.RevokedAt = now;

        await _context.SaveChangesAsync(ct);
        return Ok(new { removed = memberUserId, revokedKeys = orphanedKeys.Count });
    }
}
