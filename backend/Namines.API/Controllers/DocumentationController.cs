using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Namines.API.Controllers;

/// <summary>
/// Dökümantasyon çıktıları: PDF (kapak + AI özeti + tablo detayları), Mermaid ER, README.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DocumentationController : ControllerBase
{
    private readonly IDocumentationGenerator _docGenerator;
    private readonly IAIService _aiService;
    private readonly AuthDbContext _context;

    public DocumentationController(
        IDocumentationGenerator docGenerator, 
        IAIService aiService,
        AuthDbContext context)
    {
        _docGenerator = docGenerator;
        _aiService = aiService;
        _context = context;
    }

    /// <summary>
    /// Önce AI yönetici özeti üretir, ardından kurumsal PDF oluşturur.
    /// Body: { schema: DatabaseSchema, projectName: string }
    /// </summary>
    [HttpPost("pdf")]
    public async Task<IActionResult> GeneratePdf([FromBody] PdfRequest request)
    {
        if (request?.Schema == null)
            return BadRequest("Schema bilgisi eksik.");

        var projectName = string.IsNullOrWhiteSpace(request.ProjectName)
            ? (request.Schema.Name ?? "Namines Projesi")
            : request.ProjectName;

        bool forceDeterministic = HttpContext.Items.ContainsKey("FallbackToLocal") && HttpContext.Items["FallbackToLocal"] is true;
        if (!forceDeterministic)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
            {
                var policy = await _context.UserAIPolicies.FirstOrDefaultAsync(p => p.UserId == userId);
                if (policy != null && policy.Documentation == AIMode.DefaultNamines)
                {
                    forceDeterministic = true;
                }
            }
            else
            {
                if (string.IsNullOrEmpty(Request.Headers["X-BYOK-Key"]))
                {
                    forceDeterministic = true;
                }
            }
        }

        // 1) AI'dan yönetici özeti üret (politika deterministik ise veya hata varsa yerel özet kullan)
        string projectSummary;
        if (forceDeterministic)
        {
            var isEn = "en".Equals(request.Language, StringComparison.OrdinalIgnoreCase);
            projectSummary = isEn
                ? $"This database project named '{projectName}' contains {request.Schema.Tables.Count} tables and {request.Schema.Relations.Count} relationships. It was structured using local deterministic documentation rules."
                : $"'{projectName}' isimli bu veritabanı projesi, {request.Schema.Tables.Count} tablo ve {request.Schema.Relations.Count} ilişki barındırmaktadır. Yerel deterministik dökümantasyon kurallarına göre yapılandırılmıştır.";
        }
        else
        {
            try
            {
                projectSummary = await _aiService.GenerateProjectSummaryAsync(request.Schema, projectName);
            }
            catch (Exception ex)
            {
                // AI erişilemez olsa bile PDF üretimi durmamalı
                projectSummary = $"Yönetici özeti üretilemedi: {ex.Message}";
            }
        }

        // 2) PDF üret
        var language = string.IsNullOrWhiteSpace(request.Language) ? "tr" : request.Language;
        var pdfBytes = _docGenerator.GeneratePdf(request.Schema, projectSummary, language);

        var fileName = $"{projectName.Replace(" ", "_")}_DataDictionary.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }

    [HttpPost("mermaid")]
    public IActionResult GenerateMermaid([FromBody] DatabaseSchema schema)
    {
        var mermaid = _docGenerator.GenerateMermaidEr(schema);
        return Ok(new { mermaid });
    }

    [HttpPost("readme")]
    public IActionResult GenerateReadme([FromBody] DatabaseSchema schema, [FromQuery] string language = "tr")
    {
        var readme = _docGenerator.GenerateReadme(schema, language);
        return Ok(new { readme });
    }
}

/// <summary>
/// PDF endpoint için request modeli.
/// </summary>
public class PdfRequest
{
    public DatabaseSchema Schema { get; set; } = new();
    public string ProjectName { get; set; } = string.Empty;
    public string Language { get; set; } = "tr";
}
