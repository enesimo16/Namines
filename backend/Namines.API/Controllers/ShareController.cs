using Microsoft.AspNetCore.Authorization;
using System.Text;
using System.Net;
using System.Text.Json.Serialization;
using Namines.Core.Analysis;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Application;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;

namespace Namines.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ShareController : ControllerBase
{
    private readonly AuthDbContext _context;
    private readonly ILinterService _linter;
    private readonly IStudioApplicationService? _studio;
    private readonly IConfiguration? _configuration;

    public ShareController(
        AuthDbContext context,
        ILinterService linter,
        IStudioApplicationService? studio = null,
        IConfiguration? configuration = null)
    {
        _context = context;
        _linter  = linter;
        _studio = studio;
        _configuration = configuration;
    }

    private string CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    /// <summary>
    /// Token üretir (veya mevcut token'ı döndürür).
    /// </summary>
    [Authorize]
    [HttpPost("{projectId}")]
    public async Task<IActionResult> CreateShareLink(string projectId)
    {
        if (string.IsNullOrEmpty(CurrentUserId)) return Unauthorized();
        if (!await _context.CanManageMembersAsync(projectId, CurrentUserId))
            return NotFound();

        string rawToken;
        if (_studio != null)
        {
            try
            {
                rawToken = await _studio.CreateShareLinkAsync(projectId);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }
        else
        {
            var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId);
            if (project is null) return NotFound();

            rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
            project.ShareTokenHash = ShareTokenHasher.Hash(rawToken);
            project.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return Ok(new { token = rawToken });
    }

    /// <summary>
    /// Paylaşım token'ını iptal eder.
    /// </summary>
    [Authorize]
    [HttpDelete("{projectId}")]
    public async Task<IActionResult> RevokeShareLink(string projectId)
    {
        if (string.IsNullOrEmpty(CurrentUserId)) return Unauthorized();
        if (!await _context.CanManageMembersAsync(projectId, CurrentUserId))
            return NotFound();

        if (_studio != null)
        {
            await _studio.RevokeShareLinkAsync(projectId);
        }
        else
        {
            var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId);
            if (project is null) return NotFound();
            project.ShareToken = null;
            project.ShareTokenHash = null;
            project.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return NoContent();
    }

    /// <summary>Herkese açık: token ile şema JSON'ını döndürür. Auth gerektirmez.</summary>
    [AllowAnonymous]
    [HttpGet("view/{token}")]
    public async Task<IActionResult> GetSharedSchema(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return BadRequest();
        var tokenHash = ShareTokenHasher.Hash(token);

        if (_studio != null)
        {
            var project = await _studio.GetSharedProjectByTokenHashAsync(tokenHash);
            if (project is null) return NotFound();

            return Ok(new
            {
                project.Name,
                project.DbType,
                project.SchemaJson,
                project.NodePositionsJson,
            });
        }

        var legacyProject = await _context.CloudProjects
            .AsNoTracking()
            .Where(p => p.ShareTokenHash == tokenHash)
            .Select(p => new
            {
                p.Name,
                p.DbType,
                p.SchemaJson,
                p.NodePositionsJson,
            })
            .FirstOrDefaultAsync();

        if (legacyProject is null) return NotFound();
        return Ok(legacyProject);
    }

    /// <summary>
    /// Returns a shields.io-style SVG badge with the structural DBA score for a shared schema.
    /// No auth required — intended for README embedding.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("badge/{token}")]
    public async Task<IActionResult> GetDbaBadge(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return BadRequest();
        var tokenHash = ShareTokenHasher.Hash(token);

        string? schemaJson = null;
        if (_studio != null)
        {
            var project = await _studio.GetSharedProjectByTokenHashAsync(tokenHash);
            schemaJson = project?.SchemaJson;
        }
        else
        {
            schemaJson = await _context.CloudProjects
                .AsNoTracking()
                .Where(p => p.ShareTokenHash == tokenHash)
                .Select(p => p.SchemaJson)
                .FirstOrDefaultAsync();
        }

        if (schemaJson is null) return NotFound();

        // Deserialise — options mirror the frontend camelCase output
        DatabaseSchema? schema = null;
        try
        {
            schema = JsonSerializer.Deserialize<DatabaseSchema>(schemaJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { /* treat as empty schema */ }

        int score = 100;
        if (schema is not null)
        {
            var result = _linter.Lint(schema);
            int errors   = result.Messages.Count(m => m.Severity == Core.Models.LintSeverity.Error);
            int warnings = result.Messages.Count(m => m.Severity == Core.Models.LintSeverity.Warning);
            score = Math.Max(0, 100 - errors * 10 - warnings * 5);
        }

        var (color, label) = score switch
        {
            >= 90 => ("#22c55e", "excellent"),
            >= 70 => ("#84cc16", "good"),
            >= 50 => ("#f59e0b", "fair"),
            _     => ("#ef4444", "needs work"),
        };

        // Minimal SVG badge modelled after shields.io flat style
        var svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="160" height="20">
              <linearGradient id="s" x2="0" y2="100%">
                <stop offset="0" stop-color="#bbb" stop-opacity=".1"/>
                <stop offset="1" stop-opacity=".1"/>
              </linearGradient>
              <clipPath id="r"><rect width="160" height="20" rx="3" fill="#fff"/></clipPath>
              <g clip-path="url(#r)">
                <rect width="80"  height="20" fill="#555"/>
                <rect x="80" width="80" height="20" fill="{color}"/>
                <rect width="160" height="20" fill="url(#s)"/>
              </g>
              <g fill="#fff" text-anchor="middle" font-family="DejaVu Sans,Verdana,Geneva,sans-serif" font-size="110">
                <text x="405" y="150" fill="#010101" fill-opacity=".3" transform="scale(.1)" textLength="690" lengthAdjust="spacing">DBA Score</text>
                <text x="405" y="140" transform="scale(.1)" textLength="690" lengthAdjust="spacing">DBA Score</text>
                <text x="1195" y="150" fill="#010101" fill-opacity=".3" transform="scale(.1)" textLength="610" lengthAdjust="spacing">{score} · {label}</text>
                <text x="1195" y="140" transform="scale(.1)" textLength="610" lengthAdjust="spacing">{score} · {label}</text>
              </g>
            </svg>
            """;

        return Content(svg, "image/svg+xml");
    }

    // ── Viral döngü: sosyal önizleme ve keşif (23 §2 Döngü 1) ────────────────

    /// <summary>
    /// Paylaşılan şema için sosyal önizleme görseli (SVG).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("og/{token}.svg")]
    public async Task<IActionResult> OgImage(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return BadRequest();
        var tokenHash = ShareTokenHasher.Hash(token);

        string? name = null;
        string? dbType = null;
        string? schemaJson = null;

        if (_studio != null)
        {
            var project = await _studio.GetSharedProjectByTokenHashAsync(tokenHash);
            if (project is not null)
            {
                name = project.Name;
                dbType = project.DbType;
                schemaJson = project.SchemaJson;
            }
        }
        else
        {
            var legacy = await _context.CloudProjects
                .AsNoTracking()
                .Where(p => p.ShareTokenHash == tokenHash)
                .Select(p => new { p.Name, p.DbType, p.SchemaJson })
                .FirstOrDefaultAsync();
            if (legacy is not null)
            {
                name = legacy.Name;
                dbType = legacy.DbType;
                schemaJson = legacy.SchemaJson;
            }
        }

        if (name is null || dbType is null || schemaJson is null) return NotFound();

        DatabaseSchema schema;
        try
        {
            schema = JsonSerializer.Deserialize<DatabaseSchema>(schemaJson,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter() },
                }) ?? new DatabaseSchema();
        }
        catch (JsonException)
        {
            return NotFound();
        }

        schema.Name = name;
        var svg = OgImageGenerator.Generate(schema, dbType);

        Response.Headers.CacheControl = "public, max-age=3600";
        return Content(svg, "image/svg+xml");
    }

    /// <summary>
    /// Sayfanın meta etiketleri için özet — şemanın tamamını indirmeden.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("meta/{token}")]
    public async Task<IActionResult> Meta(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return BadRequest();
        var tokenHash = ShareTokenHasher.Hash(token);

        string? name = null;
        string? dbType = null;
        string? schemaJson = null;

        if (_studio != null)
        {
            var project = await _studio.GetSharedProjectByTokenHashAsync(tokenHash);
            if (project is not null)
            {
                name = project.Name;
                dbType = project.DbType;
                schemaJson = project.SchemaJson;
            }
        }
        else
        {
            var legacy = await _context.CloudProjects
                .AsNoTracking()
                .Where(p => p.ShareTokenHash == tokenHash)
                .Select(p => new { p.Name, p.DbType, p.SchemaJson })
                .FirstOrDefaultAsync();
            if (legacy is not null)
            {
                name = legacy.Name;
                dbType = legacy.DbType;
                schemaJson = legacy.SchemaJson;
            }
        }

        if (name is null || dbType is null || schemaJson is null) return NotFound();

        var tables = 0;
        var relations = 0;
        try
        {
            var schema = JsonSerializer.Deserialize<DatabaseSchema>(schemaJson,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter() },
                });
            tables = schema?.Tables.Count ?? 0;
            relations = schema?.Relations.Count ?? 0;
        }
        catch (JsonException)
        {
            // Şema okunamıyorsa sayfa yine açılmalı; sayılar 0 kalır.
        }

        return Ok(new
        {
            name,
            engine = dbType,
            tables,
            relations,
            description =
                $"{name} — a {dbType} schema with {tables} tables and " +
                $"{relations} relationships, designed in Namines.",
        });
    }
}
