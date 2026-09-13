using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;

namespace Namines.API.Controllers;

public sealed record LaunchRequest(string ProjectId, string Provider, string DdlScript);

/// <summary>
/// "/compile"'da onaylanmış bir şemayı tek istekte çalışan bir Desk paneline
/// dönüştüren uç. Orkestrasyonun TAMAMI <see cref="LaunchService"/>'te —
/// burası yalnızca yetki kontrolü, HTTP şekli ve Desk handoff jetonu.
///
/// <b>Owner-only:</b> Ground provizyonu (GroundController) ve SQL konsolu
/// (SqlConsole) ile aynı eşik — bu uç de veritabanı düzeyinde gerçek bir
/// kaynak açıp DDL çalıştırıyor.
/// </summary>
[Authorize]
[ApiController]
[Route("api/launch")]
public class LaunchController : ControllerBase
{
    private readonly AuthDbContext _context;
    private readonly LaunchService _launch;

    public LaunchController(AuthDbContext context, LaunchService launch)
    {
        _context = context;
        _launch = launch;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpPost]
    public async Task<IActionResult> Launch([FromBody] LaunchRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.ProjectId)
            || string.IsNullOrWhiteSpace(request.Provider)
            || string.IsNullOrWhiteSpace(request.DdlScript))
            return BadRequest(new { error = "projectId, provider and ddlScript are required." });

        if (await _context.GetRoleAsync(request.ProjectId, userId, ct) != OrgRole.Owner)
            return Forbid();

        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == request.ProjectId, ct);
        if (project is null) return NotFound(new { error = "Project not found." });

        var result = await _launch.LaunchAsync(project, userId, request.Provider, request.DdlScript, ct);

        switch (result.Status)
        {
            case LaunchStatus.ProvisionFailed:
                return BadRequest(new { status = "ProvisionFailed", error = result.Error });

            case LaunchStatus.DdlFailed:
                return BadRequest(new { status = "DdlFailed", error = result.Error });

            case LaunchStatus.NeedsReview:
                return Ok(new { status = "NeedsReview" });

            case LaunchStatus.Ready:
                // Desk handoff jetonu BURADA, aynı istekte üretiliyor: kullanıcı
                // zaten Owner olarak doğrulandı, ikinci bir tıklama/istek gerekmiyor.
                var deskHandoffToken = await _context.CreateDeskHandoffTokenAsync(userId, ct);
                return Ok(new
                {
                    status = "Ready",
                    projectId = project.Id,
                    deskHandoffToken,
                    backupWarning = result.BackupWarning,
                });

            default:
                throw new InvalidOperationException($"Unhandled LaunchStatus: {result.Status}");
        }
    }
}
