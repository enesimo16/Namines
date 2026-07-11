using System;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Security;

namespace Namines.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SchemaController : ControllerBase
{
    private readonly IAIFactory _aiFactory;
    private readonly ISmartSeedService _smartSeedService;

    public SchemaController(IAIFactory aiFactory, ISmartSeedService smartSeedService)
    {
        _aiFactory = aiFactory;
        _smartSeedService = smartSeedService;
    }

    [HttpPost("generate")]
    public async Task<IActionResult> GenerateSchema([FromForm] GenerateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return BadRequest("Prompt cannot be empty.");
        }

        if (!string.IsNullOrWhiteSpace(request.ReferenceUrl))
        {
            // SSRF koruması: yalnızca dışa dönük http(s) hedeflerine izin ver; iç ağ/loopback/metadata reddedilir.
            if (!SsrfGuard.IsUrlSafe(request.ReferenceUrl))
            {
                return BadRequest("Reference URL is not allowed.");
            }

            try
            {
                // Otomatik yönlendirmeyi kapat (redirect ile SSRF filtresini atlamayı engelle).
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
                var html = await httpClient.GetStringAsync(request.ReferenceUrl);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);
                var text = htmlDoc.DocumentNode.InnerText;
                // Basic cleanup
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

                request.Prompt += $"\n\nReferans alınan web sitesi içeriği: {text}";
            }
            catch (System.Exception)
            {
                // İç detayı sızdırma.
                return BadRequest("Failed to scrape Reference URL.");
            }
        }

        var aiService = _aiFactory.GetService(request.AIProvider);
        var schema = await aiService.GenerateSchemaAsync(request);
        return Ok(schema);
    }

    [HttpPost("revise")]
    public async Task<IActionResult> ReviseSchema([FromBody] ReviseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RevisionPrompt))
        {
            return BadRequest("Prompt cannot be empty.");
        }

        if (request.SelectedTables == null || request.SelectedTables.Count == 0)
        {
            return BadRequest("Selected tables cannot be empty for revision.");
        }

        bool forceLocal = HttpContext.Items.ContainsKey("FallbackToLocal") && HttpContext.Items["FallbackToLocal"] is true;
        bool isOllama = string.Equals(request.AIProvider, "Ollama", StringComparison.OrdinalIgnoreCase);

        if (forceLocal && !isOllama)
        {
            bool isRegionalRevision = !request.RevisionPrompt.Contains("DBA Analysis") && !request.RevisionPrompt.Contains("Automatically resolve the following");
            if (isRegionalRevision)
            {
                return BadRequest(new { message = "Local engine (Default/Namines) does not support prompt-based schema revisions. Please switch your Schema Revision policy to a cloud AI model or local Ollama in preferences." });
            }

            System.Console.WriteLine("[SchemaController] Local Fallback active: executing programmatic schema optimizer.");
            var optimizedSchema = Namines.Infrastructure.Services.ProgrammaticSchemaOptimizer.Optimize(
                request.SelectedTables, 
                request.ExistingRelations, 
                request.RevisionPrompt
            );
            return Ok(optimizedSchema);
        }

        try
        {
            var aiService = _aiFactory.GetService(request.AIProvider);
            var schema = await aiService.ReviseSchemaAsync(request);
            return Ok(schema);
        }
        catch (System.Exception ex)
        {
            System.Console.WriteLine($"[SchemaController] AI Revision failed: {ex.Message}. Falling back to programmatic schema optimizer.");
            
            bool isRegionalRevision = !request.RevisionPrompt.Contains("DBA Analysis") && !request.RevisionPrompt.Contains("Automatically resolve the following");
            if (isRegionalRevision)
            {
                return StatusCode(500, new { message = $"AI Revision failed: {ex.Message}" });
            }

            // Invoke the high-performance local optimization engine to fix all DBA findings
            var optimizedSchema = Namines.Infrastructure.Services.ProgrammaticSchemaOptimizer.Optimize(
                request.SelectedTables, 
                request.ExistingRelations, 
                request.RevisionPrompt
            );
            
            return Ok(optimizedSchema);
        }
    }

    [HttpPost("mockdata")]
    public async Task<IActionResult> GenerateMockData([FromBody] DatabaseSchema schema)
    {
        if (schema == null || schema.Tables.Count == 0)
        {
            return BadRequest("Schema is empty.");
        }

        bool forceLocal = HttpContext.Items.ContainsKey("FallbackToLocal") && HttpContext.Items["FallbackToLocal"] is true;
        if (forceLocal)
        {
            System.Console.WriteLine("[SchemaController] Local Fallback active: generating mock data programmatically.");
            var seedRes = await _smartSeedService.GenerateSmartSeedAsync(schema, Core.Enums.DatabaseType.SQLite, null, 10);
            return Ok(new { sql = seedRes.SqlScript });
        }

        try
        {
            var aiService = _aiFactory.GetService("Groq");
            var sql = await aiService.GenerateMockDataAsync(schema);
            return Ok(new { sql });
        }
        catch (System.Exception ex)
        {
            System.Console.WriteLine($"[SchemaController Warning] AI mock data generation failed ({ex.Message}). Falling back to robust C# programmatic engine.");
            var seedRes = await _smartSeedService.GenerateSmartSeedAsync(schema, Core.Enums.DatabaseType.SQLite, null, 10);
            return Ok(new { sql = seedRes.SqlScript });
        }
    }
}
