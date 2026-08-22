using Microsoft.AspNetCore.Authorization;
using System.Text;
using System.Net;
using System.Text.Json.Serialization;
using Namines.Core.Analysis;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Interfaces;
using Namines.Core.Models;
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

    private readonly IConfiguration _configuration;

    public ShareController(AuthDbContext context, ILinterService linter, IConfiguration configuration)
    {
        _context = context;
        _linter  = linter;
        _configuration = configuration;
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

    /// <summary>
    /// Returns a shields.io-style SVG badge with the structural DBA score for a shared schema.
    /// No auth required — intended for README embedding.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("badge/{token}")]
    public async Task<IActionResult> GetDbaBadge(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return BadRequest();

        var schemaJson = await _context.CloudProjects
            .AsNoTracking()
            .Where(p => p.ShareToken == token)
            .Select(p => p.SchemaJson)
            .FirstOrDefaultAsync();

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
    ///
    /// Önizlemesiz bir bağlantı akışta düz bir URL olarak görünür ve tıklanmaz;
    /// döngünün tamamı bu görselin dikkat çekmesine bağlı.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("og/{token}.svg")]
    public async Task<IActionResult> OgImage(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return BadRequest();

        var project = await _context.CloudProjects
            .AsNoTracking()
            .Where(p => p.ShareToken == token)
            .Select(p => new { p.Name, p.DbType, p.SchemaJson })
            .FirstOrDefaultAsync();

        if (project is null) return NotFound();

        DatabaseSchema schema;
        try
        {
            schema = JsonSerializer.Deserialize<DatabaseSchema>(project.SchemaJson,
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

        schema.Name = project.Name;
        var svg = OgImageGenerator.Generate(schema, project.DbType);

        // Uzun önbellek: görsel yalnızca şema değişince değişir ve sosyal ağ
        // tarayıcıları aynı URL'yi tekrar tekrar çeker. Önbelleksiz bırakmak,
        // bir bağlantı yayıldığında her paylaşımda yeniden üretim demek.
        Response.Headers.CacheControl = "public, max-age=3600";
        return Content(svg, "image/svg+xml");
    }

    /// <summary>
    /// Paylaşılan şemaların sitemap'i (23 §3).
    ///
    /// Yalnızca AÇIKÇA paylaşılanlar listeleniyor — paylaşım jetonu olan proje,
    /// sahibinin bilerek herkese açtığı projedir. Tüm projeleri listelemek, özel
    /// şemaları arama motorlarına vermek olurdu.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("sitemap.xml")]
    public async Task<IActionResult> Sitemap()
    {
        var baseUrl = (_configuration["PublicSite:BaseUrl"] ?? "https://namines.com").TrimEnd('/');

        var tokens = await _context.CloudProjects
            .AsNoTracking()
            .Where(p => p.ShareToken != null)
            .OrderByDescending(p => p.Id)
            // Sitemap'te üst sınır: arama motorları 50.000 URL'den fazlasını tek
            // dosyada kabul etmiyor ve sınırsız sorgu, tablo büyüdükçe yavaşlar.
            .Take(5000)
            .Select(p => p.ShareToken!)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        foreach (var token in tokens)
        {
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{baseUrl}/share/{WebUtility.UrlEncode(token)}</loc>");
            sb.AppendLine("    <changefreq>weekly</changefreq>");
            sb.AppendLine("  </url>");
        }
        sb.AppendLine("</urlset>");

        return Content(sb.ToString(), "application/xml");
    }

    /// <summary>
    /// Sayfanın meta etiketleri için özet — şemanın tamamını indirmeden.
    ///
    /// Ayrı bir uç, çünkü meta etiketler SUNUCUDA render edilmeli: sosyal ağ
    /// tarayıcıları JavaScript çalıştırmaz, istemcide eklenen etiketleri görmez.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("meta/{token}")]
    public async Task<IActionResult> Meta(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return BadRequest();

        var project = await _context.CloudProjects
            .AsNoTracking()
            .Where(p => p.ShareToken == token)
            .Select(p => new { p.Name, p.DbType, p.SchemaJson })
            .FirstOrDefaultAsync();

        if (project is null) return NotFound();

        var tables = 0;
        var relations = 0;
        try
        {
            var schema = JsonSerializer.Deserialize<DatabaseSchema>(project.SchemaJson,
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
            name = project.Name,
            engine = project.DbType,
            tables,
            relations,
            description =
                $"{project.Name} — a {project.DbType} schema with {tables} tables and " +
                $"{relations} relationships, designed in Namines.",
        });
    }
}
