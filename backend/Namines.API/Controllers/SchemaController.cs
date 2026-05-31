using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Namines.Core.Interfaces;
using Namines.Core.Models;

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
            try
            {
                using var httpClient = new HttpClient();
                var html = await httpClient.GetStringAsync(request.ReferenceUrl);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);
                var text = htmlDoc.DocumentNode.InnerText;
                // Basic cleanup
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
                
                request.Prompt += $"\n\nReferans alınan web sitesi içeriği: {text}";
            }
            catch (System.Exception ex)
            {
                return BadRequest($"Failed to scrape Reference URL: {ex.Message}");
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

        try
        {
            var aiService = _aiFactory.GetService(request.AIProvider);
            var schema = await aiService.ReviseSchemaAsync(request);
            return Ok(schema);
        }
        catch (System.Exception ex)
        {
            System.Console.WriteLine($"[SchemaController] AI Revision failed: {ex.Message}. Falling back to programmatic schema optimizer.");
            
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
