using System;
using System.IO;
using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Interfaces;
using Namines.Core.Models;
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

    [HttpPost("{projectId}/download")]
    public async Task<IActionResult> Download(
        string projectId,
        [FromServices] IScaffolderService scaffolder,
        CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();

        if (await _context.GetRoleAsync(projectId, userId, ct) != OrgRole.Owner)
            return Forbid();

        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFound(new { error = "Project not found." });

        DatabaseSchema schema;
        try
        {
            schema = JsonSerializer.Deserialize<DatabaseSchema>(project.SchemaJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new DatabaseSchema();
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "The project's stored schema is not valid." });
        }

        var (entity, rawKey) = GatewayAccess.CreateKey(
            projectId, "Downloaded project", userId, canWrite: true, expiresAt: null);
        _context.GatewayApiKeys.Add(entity);
        await _context.SaveChangesAsync(ct);

        var zipBytes = await scaffolder.GenerateFullStackProjectAsync(schema);
        var apiOrigin = $"{Request.Scheme}://{Request.Host}";
        var withReadme = AppendGatewayReadme(zipBytes, $"{apiOrigin}/api/gateway", rawKey);

        return File(withReadme, "application/zip", "namines-project.zip");
    }

    /// <summary>
    /// Var olan zip'e TEK bir kök dosya ekler. Beş üretici sınıfın
    /// (DotnetBackendScaffold / PythonScaffold / FrontendSdkScaffold / ...)
    /// hiçbiri değişmiyor — bu, "ham parola asla zip'e girmez" kararının
    /// TEK dokunduğu yer.
    /// </summary>
    private static byte[] AppendGatewayReadme(byte[] zipBytes, string gatewayUrl, string rawKey)
    {
        using var stream = new MemoryStream();
        stream.Write(zipBytes, 0, zipBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.CreateEntry("NAMINES-GATEWAY.md");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write($"""
                # Connecting to your database

                This project's database connection is managed by Namines and its raw
                credentials are never written to a file — not even this one.

                Instead, a Gateway API key was created for this project. It reads and
                writes through Namines' own API, is scoped to this project, and can be
                revoked at any time from Desk's "API keys" screen.

                ```
                NAMINES_GATEWAY_URL={gatewayUrl}
                NAMINES_GATEWAY_KEY={rawKey}
                ```

                **Store this key now** — Namines cannot show it to you again. If you
                lose it, revoke it in Desk and create a new one.
                """);
        }
        return stream.ToArray();
    }
}
