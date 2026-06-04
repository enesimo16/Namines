using Microsoft.AspNetCore.Mvc;
using Namines.Core.Interfaces;
using Namines.Core.Models;

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

    public DocumentationController(IDocumentationGenerator docGenerator, IAIService aiService)
    {
        _docGenerator = docGenerator;
        _aiService = aiService;
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

        // 1) AI'dan yönetici özeti üret (hata varsa boş string ile devam et)
        string projectSummary;
        try
        {
            projectSummary = await _aiService.GenerateProjectSummaryAsync(request.Schema, projectName);
        }
        catch (Exception ex)
        {
            // AI erişilemez olsa bile PDF üretimi durmamalı
            projectSummary = $"Yönetici özeti üretilemedi: {ex.Message}";
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
