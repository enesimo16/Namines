using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namines.Infrastructure.Data;
using System.Security.Claims;
using System.Security.Cryptography;

namespace Namines.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ShareController : ControllerBase
{
    private readonly AuthDbContext _context;

    public ShareController(AuthDbContext context)
    {
        _context = context;
    }

    private string CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    /// <summary>Token üretir (veya mevcut token'ı döndürür). Proje sahibine özel.</summary>
    [Authorize]
    [HttpPost("{projectId}")]
    public async Task<IActionResult> CreateShareLink(string projectId)
    {
        var project = await _context.CloudProjects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == CurrentUserId);

        if (project is null) return NotFound();

        if (string.IsNullOrEmpty(project.ShareToken))
        {
            project.ShareToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
                .Replace('+', '-').Replace('/', '_').TrimEnd('='); // URL-safe base64
            project.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return Ok(new { token = project.ShareToken });
    }

    /// <summary>Paylaşım token'ını iptal eder. Proje sahibine özel.</summary>
    [Authorize]
    [HttpDelete("{projectId}")]
    public async Task<IActionResult> RevokeShareLink(string projectId)
    {
        var project = await _context.CloudProjects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == CurrentUserId);

        if (project is null) return NotFound();

        project.ShareToken = null;
        project.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>Herkese açık: token ile şema JSON'ını döndürür. Auth gerektirmez.</summary>
    [AllowAnonymous]
    [HttpGet("view/{token}")]
    public async Task<IActionResult> GetSharedSchema(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return BadRequest();

        var project = await _context.CloudProjects
            .AsNoTracking()
            .Where(p => p.ShareToken == token)
            .Select(p => new
            {
                p.Name,
                p.DbType,
                p.SchemaJson,
                p.NodePositionsJson,
            })
            .FirstOrDefaultAsync();

        if (project is null) return NotFound();

        return Ok(project);
    }
}
