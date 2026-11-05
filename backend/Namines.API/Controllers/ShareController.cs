using Microsoft.AspNetCore.Authorization;
using System.Text;
using System.Net;
using System.Text.Json.Serialization;
using Namines.Core.Analysis;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Namines.BuildingBlocks.Application;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using System.Security.Claims;
using System.Text.Json;

namespace Namines.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ShareController : ControllerBase
{
    private readonly IShareService _shares;
    private readonly ILinterService _linter;
    private readonly IStudioApplicationService _studio;

    public ShareController(
        IShareService shares,
        ILinterService linter,
        IStudioApplicationService studio)
    {
        _shares = shares;
        _linter  = linter;
        _studio = studio;
    }

    private string CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    /// <summary>
    /// Yeni bir paylaşım bağlantısı üretir ve ham token'ı bu tek yanıtta döndürür; veritabanında
    /// yalnızca SHA-256 özeti kalır. Arayüz linki her tıklamada bu uçtan aldığı için önceki
    /// bağlantılar geçerli kalır (bkz. ProjectShare); hepsini kapatmak için DELETE.
    /// </summary>
    [Authorize]
    [HttpPost("{projectId}")]
    [EnableRateLimiting("share-create")]
    public async Task<IActionResult> CreateShareLink(string projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(CurrentUserId)) return Unauthorized();
        var outcome = await _shares.CreateAsync(CurrentUserId, projectId, ct);
        if (!outcome.IsOk) return NotFound();

        // Tavan aşılınca en eski bağlantılar kapanır; bu istemciye de söylenir, sessiz kalmaz.
        return outcome.Value!.ClosedOldestLinks > 0
            ? Ok(new { token = outcome.Value.Token, closedOldestLinks = outcome.Value.ClosedOldestLinks })
            : Ok(new { token = outcome.Value.Token });
    }

    /// <summary>
    /// Projenin tüm paylaşım bağlantılarını iptal eder.
    /// </summary>
    [Authorize]
    [HttpDelete("{projectId}")]
    public async Task<IActionResult> RevokeShareLink(string projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(CurrentUserId)) return Unauthorized();
        var outcome = await _shares.RevokeAsync(CurrentUserId, projectId, ct);
        return outcome.IsOk ? NoContent() : NotFound();
    }

    /// <summary>Herkese açık: token ile şema JSON'ını döndürür. Auth gerektirmez.</summary>
    [AllowAnonymous]
    [HttpGet("view/{token}")]
    [EnableRateLimiting("share-public")]
    public async Task<IActionResult> GetSharedSchema(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return BadRequest();

        var project = await _studio.GetSharedProjectByTokenHashAsync(ShareTokenHasher.Hash(token), ct);
        if (project is null) return NotFound();

        return Ok(new
        {
            project.Name,
            project.DbType,
            project.SchemaJson,
            project.NodePositionsJson,
        });
    }

    /// <summary>
    /// Returns a shields.io-style SVG badge with the structural DBA score for a shared schema.
    /// No auth required — intended for README embedding.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("badge/{token}")]
    [EnableRateLimiting("share-public")]
    public async Task<IActionResult> GetDbaBadge(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return BadRequest();

        var project = await _studio.GetSharedProjectByTokenHashAsync(ShareTokenHasher.Hash(token), ct);
        var schemaJson = project?.SchemaJson;
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
    [EnableRateLimiting("share-public")]
    public async Task<IActionResult> OgImage(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return BadRequest();
        var project = await _studio.GetSharedProjectByTokenHashAsync(ShareTokenHasher.Hash(token), ct);
        if (project is null) return NotFound();

        var (name, dbType, schemaJson) = (project.Name, project.DbType, project.SchemaJson);

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
    [EnableRateLimiting("share-public")]
    public async Task<IActionResult> Meta(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return BadRequest();
        var project = await _studio.GetSharedProjectByTokenHashAsync(ShareTokenHasher.Hash(token), ct);
        if (project is null) return NotFound();

        var (name, dbType, schemaJson) = (project.Name, project.DbType, project.SchemaJson);

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
