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
    private readonly Namines.Infrastructure.Generators.DdlGenerator.IDdlGeneratorFactory _ddlFactory;

    public SchemaController(
        IAIFactory aiFactory,
        ISmartSeedService smartSeedService,
        Namines.Infrastructure.Generators.DdlGenerator.IDdlGeneratorFactory ddlFactory,
        SchemaAgentPipeline? agent = null,
        AiQuotaService? quota = null)
    {
        _aiFactory = aiFactory;
        _smartSeedService = smartSeedService;
        _ddlFactory = ddlFactory;
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

    /// <summary>
    /// Cevaplardan deterministik bir plan üretir — second-phase/05-PLAN-MODU.md.
    ///
    /// <b>Bu da bedava, /clarify gibi.</b> Tablo listesi <see cref="PlanBuilder"/>'da
    /// kural tabanlı çıkıyor; AI'ya hiç gidilmiyor. Kullanıcı planı görüp
    /// reddedebiliyor ya da bir takip sorusuna cevap verip yeniden isteyebiliyor
    /// — hiçbiri üretim turunu tüketmiyor.
    ///
    /// <b>Dönen `followUp` doluysa plan henüz KESİN değil.</b> İstemci bu soruyu
    /// gösterip cevabı `Answers`'a ekleyerek uçu tekrar çağırmalı. Üç turdan
    /// sonra (bkz. <see cref="PlanBuilder"/>) hiç soru dönmez.
    /// </summary>
    [HttpPost("plan")]
    [AllowAnonymous]
    public IActionResult Plan([FromBody] PlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Prompt))
            return BadRequest(new { message = "Prompt cannot be empty." });

        var archetype = ArchetypeDetector.Detect(request.Prompt);
        var answers = request.Answers ?? new Dictionary<string, string>();
        var plan = PlanBuilder.Build(archetype, answers, Math.Max(1, request.Round));

        return Ok(new
        {
            archetype = plan.Archetype.ToString(),
            tables = plan.Tables.Select(t => new { t.Name, t.Reason }),
            assumptions = plan.Assumptions,
            followUp = plan.FollowUp is null ? null : new
            {
                plan.FollowUp.Id,
                plan.FollowUp.Text,
                plan.FollowUp.Options,
                plan.FollowUp.Why,
                plan.FollowUp.DefaultOption,
            },
            round = plan.Round,
            // İstemci onayladığında bu metni prompt'a ekleyip /generate'i
            // çağırıyor — plan ile üretim arasındaki tek köprü bu metin,
            // ikinci bir "planı hatırla" durumu sunucuda tutulmuyor.
            planSummary = BuildPlanSummaryText(plan),
        });
    }

    /// <summary>
    /// Planı, üretim prompt'una eklenecek tek bir metne çevirir.
    ///
    /// Sunucu turlar arasında hiçbir şey saklamıyor (bkz. Plan uç notu) —
    /// bu metin kullanıcının onayladığı planın YERİNE geçiyor ve prompt'a
    /// eklendiğinde modelin planı YENİDEN İCAT ETMESİ değil, gerçekleştirmesi
    /// bekleniyor.
    /// </summary>
    private static string BuildPlanSummaryText(SchemaPlan plan)
    {
        var lines = new List<string>
        {
            $"Planned tables ({plan.Tables.Count}):",
        };
        lines.AddRange(plan.Tables.Select(t => $"- {t.Name}: {t.Reason}"));

        if (plan.Assumptions.Count > 0)
        {
            lines.Add("");
            lines.Add("Assumptions used (user did not answer these):");
            lines.AddRange(plan.Assumptions.Select(a => $"- {a}"));
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// İki motor arasında kayıp raporu — second-phase/07-MOTOR-DONUSUMU.md.
    ///
    /// <b>Bedava, /clarify ve /plan gibi.</b> <see cref="EngineConversionAnalyzer"/>
    /// tamamen deterministik, AI'ya hiç gidilmiyor.
    /// </summary>
    [HttpPost("convert/analyze")]
    [AllowAnonymous]
    public IActionResult ConvertAnalyze([FromBody] ConvertAnalyzeRequest request)
    {
        if (request?.Schema is null)
            return BadRequest(new { message = "Schema cannot be empty." });

        var report = EngineConversionAnalyzer.Analyze(request.Schema, request.Source, request.Target);

        return Ok(new
        {
            source = report.Source.ToString(),
            target = report.Target.ToString(),
            hasFindings = report.HasFindings,
            findings = report.Findings.Select(f => new
            {
                f.Id,
                category = f.Category.ToString(),
                f.TableName,
                f.ColumnName,
                f.Description,
                options = f.Options.Select(o => new { o.Key, o.Label, o.DataLossRisk }),
            }),
        });
    }

    /// <summary>
    /// Kullanıcının kararlarını uygular, dönüştürülmüş şema + hedef motorun DDL'ini
    /// döner — second-phase/07-MOTOR-DONUSUMU.md.
    ///
    /// <b>Yalnızca şema; veri taşımaz.</b> "manual" seçilen ya da hiç
    /// çözülmeyen bulgular şemayı DEĞİŞTİRMEZ — o kolon için DDL üretimi hâlâ
    /// hata verebilir, bu kasıtlı (bkz. <see cref="SchemaConverter"/>).
    /// </summary>
    [HttpPost("convert/apply")]
    [AllowAnonymous]
    public IActionResult ConvertApply([FromBody] ConvertApplyRequest request)
    {
        if (request?.Schema is null)
            return BadRequest(new { message = "Schema cannot be empty." });

        var report = EngineConversionAnalyzer.Analyze(request.Schema, request.Source, request.Target);
        var resolutions = request.Resolutions ?? new Dictionary<string, string>();
        var converted = SchemaConverter.Apply(request.Schema, request.Target, report.Findings, resolutions);

        string? ddl = null;
        string? ddlError = null;
        try
        {
            ddl = _ddlFactory.GetGenerator(request.Target).Generate(converted);
        }
        catch (Exception ex)
        {
            // Kullanıcı bir bulguyu "elle çözeceğim" dediyse ya da hiç
            // çözmediyse DDL üretimi burada hata verebilir — bu bir sunucu
            // hatası değil, henüz çözülmemiş bir karar var demek.
            ddlError = ex.Message;
        }

        var unresolved = report.Findings.Count(f => !resolutions.ContainsKey(f.Id) || resolutions[f.Id] == "manual");

        return Ok(new
        {
            schema = converted,
            ddl,
            ddlError,
            unresolvedFindings = unresolved,
        });
    }

    [HttpPost("generate")]
    public async Task<IActionResult> GenerateSchema([FromForm] GenerateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return BadRequest("Prompt cannot be empty.");
        }

        // Eski davranış (sayfanın GÖRÜNEN METNİNİ kazımak) tamamen kaldırıldı —
        // second-phase/06-VERI-KAYNAKLARI.md. Üç yerden kırıktı: uzunluk sınırı
        // yoktu, bir sitenin pazarlama metni şema hakkında neredeyse hiçbir şey
        // söylemiyordu, JS ile render olan sitelerde sunucu boş bir kabuk
        // indiriyordu. Yerine YAPILANDIRILMIŞ kaynaklara (GraphQL introspection,
        // OpenAPI/Swagger) bakan ApiSpecExtractor geldi; hiçbiri yoksa DÜRÜSTÇE
        // hata dönüyor, sayfa metnine düşmüyor.
        if (!string.IsNullOrWhiteSpace(request.ApiSpecUrl))
        {
            var extraction = await ApiSpecExtractor.ExtractAsync(request.ApiSpecUrl, HttpContext.RequestAborted);
            if (!extraction.Success)
                return BadRequest(new { message = extraction.FailureReason });

            // "Tahmin" etiketi bilinçli: çıkarılan şey sitenin GERÇEK veritabanı
            // değil, API'sinin dışa açtığı görünüm — iç tablolar, hesaplanan
            // alanlar burada görünmez. Bunu gizlemek eski özelliğin yalanının
            // yerine yenisini koymak olurdu.
            request.Prompt += "\n\n--- Inferred data model from " + extraction.SourceKind + " at " +
                extraction.SourceUrl + " (a guess, not the real database) ---\n" +
                string.Join('\n', extraction.Tables.Select(t => $"- {t.Name}: {t.Reason}"));
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

        // Kullanıcı Plan modundan geldiyse (bkz. /schema/plan) aynı cevaplar
        // burada da elde — deterministik tablo listesi YENİDEN hesaplanıp
        // prompt'a ekleniyor. Round yüksek veriliyor (takip sorusu hiç
        // istenmesin diye): üretim anında artık soru sorma turu yok, elde
        // olan cevaplarla en iyi planı çıkar. Model bu listeyi İCAT ETMİYOR,
        // GERÇEKLEŞTİRİYOR — kullanıcının onayladığı plan buysa üretilen şema
        // ondan sapmamalı.
        var parsedAnswers = ParseAnswers(request.Answers);
        if (parsedAnswers is { Count: > 0 })
        {
            var plan = PlanBuilder.Build(archetype, parsedAnswers, round: int.MaxValue);
            if (plan.Tables.Count > 0)
                enrichedPrompt += "\n\n--- Planned tables (build exactly these, plus obvious join/lookup tables) ---\n" +
                    string.Join('\n', plan.Tables.Select(t => $"- {t.Name}: {t.Reason}"));
        }

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
