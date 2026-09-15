using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models;
using Namines.Infrastructure.Data;

namespace Namines.API.Controllers;

public class CreateAutomationRuleRequest
{
    public string ProjectId { get; set; } = string.Empty;
    public string? ScopeTableId { get; set; }
    public string TriggerType { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string ActionConfigJson { get; set; } = "{}";
}

[ApiController]
[Route("api/automation")]
[Authorize]
public class AutomationController : ControllerBase
{
    private readonly AuthDbContext _context;

    public AutomationController(AuthDbContext context) => _context = context;

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private async Task<bool> OwnsProjectAsync(string projectId, string userId, CancellationToken ct)
    {
        // Bölüm 3 v1: "sahiplik" = kullanıcının bu projeye erişimi olan bir
        // organizasyonun üyesi olması. AuthController.GetProjects'teki
        // myOrgIds desenini tekrar kullanmak yerine burada en dar hâliyle
        // (proje.UserId == userId) tutuluyor — takım paylaşımlı düzenleme
        // yetkisi bu planın kapsamı dışında, spec de bunu ayırt etmiyor.
        return await _context.CloudProjects.AsNoTracking()
            .AnyAsync(p => p.Id == projectId && p.UserId == userId, ct);
    }

    [HttpGet("rules")]
    public async Task<IActionResult> GetRules([FromQuery] string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await OwnsProjectAsync(projectId, userId, ct)) return NotFound();

        var rules = await _context.AutomationRules.AsNoTracking()
            .Where(r => r.ProjectId == projectId).ToListAsync(ct);
        return Ok(rules);
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
            TriggerType = request.TriggerType,
            ActionType = request.ActionType,
            ActionConfigJson = request.ActionConfigJson,
        };
        _context.AutomationRules.Add(rule);
        await _context.SaveChangesAsync(ct);
        return Ok(rule);
    }

    [HttpDelete("rules/{id}")]
    public async Task<IActionResult> DeleteRule(string id, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var rule = await _context.AutomationRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return NotFound();
        if (!await OwnsProjectAsync(rule.ProjectId, userId, ct)) return NotFound();

        _context.AutomationRules.Remove(rule);
        await _context.SaveChangesAsync(ct);
        return Ok(new { deleted = true });
    }
}
