using System;
using System.Security.Claims;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
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
    private readonly SchemaAgentPipeline? _agent;
    private readonly AiQuotaService? _quota;

    public SchemaController(
        IAIFactory aiFactory,
        ISmartSeedService smartSeedService,
        SchemaAgentPipeline? agent = null,
        AiQuotaService? quota = null)
    {
        _aiFactory = aiFactory;
        _smartSeedService = smartSeedService;
        _agent = agent;
        _quota = quota;
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

        // Groq dışındaki sağlayıcılarda (Ollama, yerel) eski tek-çağrı yolu
        // korunuyor: ajan hattı Groq'a bağlı ve olmayan bir yolu varmış gibi
        // davranmak, o sağlayıcıyı sessizce bozardı.
        if (_agent is null || !string.Equals(request.AIProvider, "Groq", StringComparison.OrdinalIgnoreCase))
            return Ok(await aiService.GenerateSchemaAsync(request));

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        // Kaç tur harcayabileceğimizi BÜTÇE söylüyor, hat değil. Bir kullanıcının
        // günlük hakkı bitmişken üç tur çalıştırmak, ona hiçbir şey vermeden
        // parasını harcamak olurdu.
        var budgetRounds = await AffordableRoundsAsync(userId);

        try
        {
            var result = await _agent.RunAsync(request.Prompt, request.DbType, budgetRounds, HttpContext.RequestAborted);

            // Harcanan TUR sayısı kadar ölçüm: bir tur da üç tur da aynı maliyete
            // sayılırsa, düzeltme döngüsü bedava görünür ve bütçe anlamını yitirir.
            // _quota null olabilir (opsiyonel bağımlılık); kontrol etmeden
            // çağırmak, ölçüm servisi kayıtlı değilse üretimi tamamen çökertirdi.
            if (_quota is not null && !string.IsNullOrEmpty(userId))
                await _quota.ConsumeAsync(userId, result.Rounds * SchemaRoundTokenEstimate, HttpContext.RequestAborted);

            // Bulgular GİZLENMİYOR. "Çalışıyor gibi görünen" bir şema vermek, hiç
            // vermemekten kötüdür: kullanıcı onu kullanmaya kalkar ve hata
            // veritabanında patlar.
            return Ok(new
            {
                schema = result.Schema,
                agent = new
                {
                    result.Rounds,
                    result.Clean,
                    result.PortableEverywhere,
                    findings = result.RemainingFindings,
                    portability = result.PortabilityNotes,
                },
            });
        }
        catch (InvalidOperationException ex)
        {
            // Bütçe yetmiyor — 500 değil 429: bu bir arıza değil, bir sınır.
            return StatusCode(429, new { message = ex.Message });
        }
    }

    /// <summary>Bir şema üretim turunun tahmini token maliyeti.</summary>
    private const int SchemaRoundTokenEstimate = 2500;

    /// <summary>
    /// Kullanıcının bütçesinin kaç tura yettiği.
    ///
    /// Kimliği olmayan istekte varsayılan tur sayısı kullanılıyor: bu uç
    /// <c>AIQuotaMiddleware</c>'in arkasında ve orası zaten kimlik istiyor, yani
    /// buraya kimliksiz gelinmiyor. Yine de null'a karşı savunmasız bırakmıyoruz.
    /// </summary>
    private async Task<int> AffordableRoundsAsync(string? userId)
    {
        if (_quota is null || string.IsNullOrEmpty(userId))
            return SchemaAgentPipeline.DefaultRepairRounds + 1;

        var quota = await _quota.EnsureQuotaAsync(userId, HttpContext.RequestAborted);
        var remaining = Math.Max(0, quota.DailyLimit - quota.DailyUsageCount);

        var affordable = remaining / SchemaRoundTokenEstimate;

        // Tavan var: bütçesi çok olan bir kullanıcı için sınırsız tur açmak,
        // modelin çözemediği bir bulguda bütçeyi tek istekte yakardı.
        return Math.Min(affordable, SchemaAgentPipeline.DefaultRepairRounds + 1);
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
