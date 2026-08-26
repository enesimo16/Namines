using System;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Namines.Core.Analysis;
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

    /// <summary>
    /// Kullanıcının cümlesine göre netleştirici sorular (36 §2).
    ///
    /// <b>HİÇ AI kullanmıyor ve bu bilinçli.</b> İş türü anahtar kelimelerden
    /// çıkarılıyor, sorular sabit bir bankadan geliyor. Soruları modele
    /// ürettirmek daha "akıllı" görünürdü ama üç bedeli vardı: kullanıcı daha
    /// hiçbir şey görmeden token harcanırdı, aynı isteğe her seferinde farklı
    /// sorular sorulurdu (kararsız bir ürün), ve model cevaplanamaz soru
    /// üretebilirdi.
    ///
    /// Sonuç: bu uç <b>bedava</b> ve kotayı hiç etkilemiyor.
    /// </summary>
    [HttpPost("clarify")]
    [AllowAnonymous]
    public IActionResult Clarify([FromBody] ClarifyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Prompt))
            return BadRequest(new { message = "Prompt cannot be empty." });

        var archetype = ArchetypeDetector.Detect(request.Prompt);
        var questions = ClarifyingQuestions.For(archetype);

        return Ok(new
        {
            archetype = archetype.ToString(),
            // Kullanıcı tanınmadığını görebilmeli: "Generic" dönerse sorular
            // geneldir ve bunu gizlemek, alakasız görünen soruları açıklamasız
            // bırakır.
            recognised = archetype != ProjectArchetype.Generic,
            questions = questions.Select(q => new
            {
                q.Id,
                q.Text,
                q.Options,
                q.Why,
                q.DefaultOption,
            }),
        });
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

        // Netleştirme cevapları prompt'a ekleniyor. Cevaplanmamış sorular
        // VARSAYILANIYLA yazılıyor: atlamak, modelin o boşluğu yine kendi
        // doldurması demek olurdu ve sormanın amacı tam olarak buydu.
        var archetype = ArchetypeDetector.Detect(request.Prompt);
        var context = ClarifyingQuestions.ToPromptContext(
            archetype, ClarifyingQuestions.For(archetype), ParseAnswers(request.Answers));

        // Ture ozel uzmanlik rolu de ekleniyor: "iyi bir sema tasarla" her alanda
        // ayni sonucu getiriyordu, oysa parayi kayan noktali sayida tutmamak ya
        // da envanter tablosunu dar birakmak o alanda calisan birinin bildigi
        // seyler. Rol secmek icin ikinci bir modele danismak, kullanici hicbir
        // sey gormeden bir tur harcamak olurdu -- tur zaten anahtar kelimeden
        // cikarildi.
        var role = ArchetypeRoles.For(archetype);

        var enrichedPrompt = request.Prompt;
        if (!string.IsNullOrWhiteSpace(role))
            enrichedPrompt += "\n\n--- Domain guidance ---\n" + role;
        if (!string.IsNullOrWhiteSpace(context))
            enrichedPrompt += "\n\n--- Requirements gathered from the user ---\n" + context;

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        // Kaç tur harcayabileceğimizi BÜTÇE söylüyor, hat değil. Bir kullanıcının
        // günlük hakkı bitmişken üç tur çalıştırmak, ona hiçbir şey vermeden
        // parasını harcamak olurdu.
        var budgetRounds = await AffordableRoundsAsync(userId);

        // Akış isteniyor mu? EventSource yalnızca GET destekler, bu uç POST
        // olduğu için istemci fetch + ReadableStream kullanıyor ve isteği bu
        // başlıkla işaretliyor (bkz. second-phase/04-LOADING-EKRANI.md).
        // İstenmezse eski tek-seferlik yanıt AYNEN korunuyor — RegionalPromptPanel
        // gibi akışı hiç bilmeyen çağıranlar hiçbir şey değiştirmeden çalışmaya
        // devam ediyor.
        var wantsStream = Request.Headers.Accept.Any(a =>
            a is not null && a.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase));

        if (!wantsStream)
        {
            try
            {
                var result = await _agent.RunAsync(enrichedPrompt, request.DbType, budgetRounds, HttpContext.RequestAborted);
                if (_quota is not null && !string.IsNullOrEmpty(userId))
                    await _quota.ConsumeAsync(userId, result.Rounds * SchemaRoundTokenEstimate, HttpContext.RequestAborted);

                return Ok(BuildResultPayload(archetype, result));
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(429, new { message = ex.Message });
            }
            catch (AiRateLimitException ex)
            {
                Response.Headers.RetryAfter = ex.RetryAfterSeconds;
                return StatusCode(429, new { message = ex.Message, retryAfterSeconds = ex.RetryAfterSeconds });
            }
        }

        // ── Akış yolu ────────────────────────────────────────────────────────
        // Başlıklar YAZILDIKTAN sonra HTTP durum kodu değiştirilemez, bu yüzden
        // hata durumları da 429/500 yerine bir "error" olayı olarak akıyor.
        // İstemci bunu kendi tarafında yorumlayıp aynı 429 davranışını (toast,
        // Retry-After) uyguluyor.
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no"; // ters proxy'lerin arabelleğe almasını engelle

        async Task WriteEventAsync(string eventName, object data)
        {
            var json = JsonSerializer.Serialize(data);
            await Response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", HttpContext.RequestAborted);
            await Response.Body.FlushAsync(HttpContext.RequestAborted);
        }

        var progress = new AsyncProgress<AgentStep>(step => WriteEventAsync("step", step));

        try
        {
            var result = await _agent.RunAsync(
                enrichedPrompt, request.DbType, budgetRounds, HttpContext.RequestAborted, progress);

            if (_quota is not null && !string.IsNullOrEmpty(userId))
                await _quota.ConsumeAsync(userId, result.Rounds * SchemaRoundTokenEstimate, HttpContext.RequestAborted);

            await WriteEventAsync("result", BuildResultPayload(archetype, result));
        }
        catch (InvalidOperationException ex)
        {
            await WriteEventAsync("error", new { message = ex.Message });
        }
        catch (AiRateLimitException ex)
        {
            await WriteEventAsync("error", new { message = ex.Message, retryAfterSeconds = ex.RetryAfterSeconds });
        }

        return new EmptyResult();
    }

    private static object BuildResultPayload(ProjectArchetype archetype, SchemaAgentResult result) =>
        // Bulgular GİZLENMİYOR. "Çalışıyor gibi görünen" bir şema vermek, hiç
        // vermemekten kötüdür: kullanıcı onu kullanmaya kalkar ve hata
        // veritabanında patlar.
        new
        {
            schema = result.Schema,
            agent = new
            {
                archetype = archetype.ToString(),
                result.Rounds,
                result.Clean,
                result.PortableEverywhere,
                findings = result.RemainingFindings,
                portability = result.PortabilityNotes,
            },
        };

    /// <summary>
    /// Cevap sözlüğünü JSON'dan okur.
    ///
    /// Bozuk JSON isteği REDDETTİRMİYOR: cevaplar bir iyileştirme, zorunluluk
    /// değil. Kullanıcının şema üretme isteği, formun bir alanı bozuk diye
    /// tamamen düşmemeli.
    /// </summary>
    private static Dictionary<string, string>? ParseAnswers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (JsonException)
        {
            return null;
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
