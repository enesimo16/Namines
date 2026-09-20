using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Security;
using Namines.Infrastructure.AI;
using Namines.Infrastructure.Data;

namespace Namines.Infrastructure.Services;

public interface IAutomationExecutor
{
    Task RunAsync(string projectId, SchemaDiffResult diff, DatabaseSchema oldSchema, DatabaseSchema newSchema, CancellationToken ct = default);

    /// <summary>Tek bir kuralın zincirini çalıştırır — "şimdi test et" bu yolu kullanır.</summary>
    Task RunRuleAsync(
        AutomationRule rule, string userId, DatabaseSchema schema, DatabaseType engine,
        AutomationTriggerContext context, string projectName, bool isTest, CancellationToken ct);
}

/// <summary>
/// Namines Flow'un sunucu tarafı aksiyonlarını çalıştırır (Bölüm 3).
///
/// Bir kuralın aksiyonu başarısız olursa (webhook 5xx, AI hatası, kota reddi)
/// DİĞER KURALLAR etkilenmez — her kural kendi try/catch'i içinde çalışır ve
/// sonucu <see cref="AutomationRunLog"/>'a yazar.
/// </summary>
public sealed class AutomationExecutor : IAutomationExecutor
{
    /// <summary>DBA/seed için sabit tahmin — SchemaController.SchemaRoundTokenEstimate ile aynı büyüklük mertebesi.</summary>
    private const int AutomationTokenEstimate = 2500;

    private readonly AuthDbContext _db;
    private readonly IAiQuotaReserver _quota;
    private readonly GroqAIService _groqDba;
    private readonly IAIService _aiService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILinterService _linter;
    private readonly ILogger<AutomationExecutor> _logger;

    public AutomationExecutor(
        AuthDbContext db, IAiQuotaReserver quota, GroqAIService groqDba, IAIService aiService,
        IHttpClientFactory httpClientFactory, ILinterService linter, ILogger<AutomationExecutor> logger)
    {
        _db = db;
        _quota = quota;
        _groqDba = groqDba;
        _aiService = aiService;
        _httpClientFactory = httpClientFactory;
        _linter = linter;
        _logger = logger;
    }

    public async Task RunAsync(string projectId, SchemaDiffResult diff, DatabaseSchema oldSchema, DatabaseSchema newSchema, CancellationToken ct = default)
    {
        var project = await _db.CloudProjects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return; // Proje bu arada silinmiş olabilir — iş düşer.

        var rules = await _db.AutomationRules.AsNoTracking()
            .Include(r => r.Actions)
            .Where(r => r.ProjectId == projectId && r.Enabled).ToListAsync(ct);
        if (rules.Count == 0) return;

        var matched = AutomationRuleMatcher.Match(diff, oldSchema, newSchema, rules);
        if (matched.Count == 0) return;

        var engine = Enum.TryParse<DatabaseType>(project.DbType, ignoreCase: true, out var parsedEngine)
            ? parsedEngine : DatabaseType.PostgreSQL;

        foreach (var match in matched)
        {
            // Eşleşmeyi SAĞLAYAN bağlam aşağı taşınıyor: aksiyon şablonları
            // "hangi tablo / hangi kolon" sorusunu buradan cevaplıyor.
            await RunRuleAsync(match.Rule, project.UserId, newSchema, engine, match.Context, project.Name, isTest: false, ct);
        }
    }

    /// <summary>
    /// Bir kuralın aksiyon zincirini sırayla çalıştırır.
    ///
    /// Her adım KENDİ try/catch'inde: bir adımın hatası (webhook 5xx, AI
    /// hatası, kota reddi) sonraki adımları düşürmüyor. "Bir kuralın hatası
    /// diğer kuralları etkilemez" ilkesinin adım seviyesindeki karşılığı —
    /// zincirin ikinci adımı ilk adım patladı diye sessizce atlanırsa
    /// kullanıcı bunu hiçbir yerden anlayamazdı.
    /// </summary>
    public async Task RunRuleAsync(
        AutomationRule rule, string userId, DatabaseSchema schema, DatabaseType engine,
        AutomationTriggerContext context, string projectName, bool isTest, CancellationToken ct)
    {
        foreach (var action in rule.Actions.OrderBy(a => a.SortOrder))
        {
            // "Toast" sunucuda HİÇ işlenmiyor — istemci kendi event bus'ından
            // dinliyor. Log dahi yazılmıyor: sunucunun hiç bilmediği bir şeyin
            // "çalıştı" kaydı tutması yanıltıcı olurdu.
            if (action.ActionType == "Toast") continue;

            try
            {
                await RunOneAsync(rule, action, userId, schema, engine, context, projectName, isTest, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Namines Flow: {RuleId} kuralinin {ActionType} adimi calistirilirken beklenmeyen hata.",
                    rule.Id, action.ActionType);
                await LogAsync(rule.Id, action.ActionType, "Failed", ex.Message, null, isTest, ct);
            }
        }
    }

    private async Task RunOneAsync(
        AutomationRule rule, AutomationAction action, string userId, DatabaseSchema schema,
        DatabaseType engine, AutomationTriggerContext context, string projectName, bool isTest, CancellationToken ct)
    {
        switch (action.ActionType)
        {
            // Slack ve Discord ayrı bir yürütücü DEĞİL: ikisi de "bir URL'e
            // JSON gönder" işi. Fark yalnızca gövdenin şekli — ve o da burada,
            // kullanıcı JSON yazmak zorunda kalmasın diye hazırlanıyor.
            case "Webhook":
            case "Slack":
            case "Discord":
                await RunHttpAsync(rule, action, context, projectName, isTest, ct);
                return;
            case "Lint":
                await RunLintAsync(rule, action, schema, isTest, ct);
                return;
            case "DbaCheck":
                await RunDbaCheckAsync(rule, action, userId, schema, engine, isTest, ct);
                return;
            case "SeedData":
                await RunSeedDataAsync(rule, action, userId, schema, isTest, ct);
                return;
            default:
                await LogAsync(rule.Id, action.ActionType, "Failed", $"Unknown action type: {action.ActionType}", null, isTest, ct);
                return;
        }
    }

    private async Task RunHttpAsync(
        AutomationRule rule, AutomationAction action, AutomationTriggerContext context,
        string projectName, bool isTest, CancellationToken ct)
    {
        var config = ParseConfig(action.ActionConfigJson);
        var url = config.Url;

        // HER ÇALIŞTIRMADA yeniden doğrulanıyor — kaydedilen bir URL zamanla
        // farklı bir IP'ye çözülebilir (DNS rebinding). Test çalıştırması da
        // bu kontrolden MUAF DEĞİL, aksi hâlde "test" bir SSRF kapısı olurdu.
        if (string.IsNullOrWhiteSpace(url) || !SsrfGuard.IsUrlSafe(url))
        {
            await LogAsync(rule.Id, action.ActionType, "Skipped", "Webhook URL is missing or not a safe public target.", null, isTest, ct);
            return;
        }

        var now = DateTime.UtcNow;
        string Render(string? t) => AutomationTemplate.Render(t, rule.TriggerType, context, projectName, now);

        var payload = action.ActionType switch
        {
            // Slack ve Discord'un gelen-kutusu webhook'ları farklı alan
            // bekliyor: Slack "text", Discord "content". Kullanıcının bunu
            // bilmesi gerekmesin diye mesaj tek bir alandan alınıp doğru
            // zarfa konuyor.
            "Slack" => JsonSerializer.Serialize(new { text = Render(config.Message ?? DefaultMessage) }),
            "Discord" => JsonSerializer.Serialize(new { content = Render(config.Message ?? DefaultMessage) }),
            // Gövde boş bırakılırsa ESKİ varsayılan gövde korunuyor: bu alan
            // eklenmeden önce kurulmuş webhook'ların alıcıları aynı şekli
            // beklemeye devam ediyor.
            _ => string.IsNullOrWhiteSpace(config.Body)
                ? JsonSerializer.Serialize(new
                {
                    trigger = rule.TriggerType,
                    table = rule.ScopeTableId,
                    tableName = context.TableName,
                    columnName = context.ColumnName,
                    timestamp = now,
                })
                : Render(config.Body),
        };

        var method = action.ActionType == "Webhook" ? ParseMethod(config.Method) : HttpMethod.Post;
        using var request = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        if (config.Headers is not null)
        {
            foreach (var (key, value) in config.Headers)
            {
                // Başlık ADI kullanıcıdan geliyor; satır sonu ya da iki nokta
                // içeren bir ad protokol seviyesinde bozuk istek üretir.
                if (!IsSafeHeaderName(key)) continue;

                // DEĞER şablondan geçiyor ve şablon tablo/kolon ADINI
                // dolduruyor — yani hedef sunucuya giden başlık değerine
                // kullanıcının şema içeriği giriyor. İçinde CR/LF olan bir ad
                // başlık enjeksiyonu denemesi olurdu; satır sonları atılıyor.
                var safeValue = StripLineBreaks(Render(value));

                // `TryAddWithoutValidation`: doğrulamaya takılan bir başlık
                // istisna fırlatıp TÜM isteği düşürürdü — bu adım sessizce
                // atlanmalı, zincirin geri kalanı çalışmaya devam etmeli.
                request.Headers.TryAddWithoutValidation(key, safeValue);
            }
        }

        var client = _httpClientFactory.CreateClient("AutomationWebhook");
        // `using`: yanıt atılmazsa gövde arabelleği ve bağlantı, çöp
        // toplayıcıya kadar tutulur. Otomasyonlar toplu tetiklendiğinde
        // havuzdaki bağlantıları gereksiz yere meşgul ederdi.
        using var response = await client.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
            await LogAsync(rule.Id, action.ActionType, "Success", null, null, isTest, ct);
        }
        else if ((int)response.StatusCode is >= 300 and < 400)
        {
            // Yönlendirme TAKİP EDİLMİYOR (bkz. AutomationWebhook istemcisinin
            // kaydı): SsrfGuard yalnızca yazılan URL'i doğruluyor, hedefin
            // gösterdiği adresi değil. Kullanıcı sessiz bir başarısızlıkla
            // kalmasın diye sebep açıkça yazılıyor.
            await LogAsync(rule.Id, action.ActionType, "Skipped",
                $"Webhook redirected ({(int)response.StatusCode}). Redirects are not followed — use the final URL.",
                null, isTest, ct);
        }
        else
        {
            await LogAsync(rule.Id, action.ActionType, "Failed", $"Webhook returned {(int)response.StatusCode}.", null, isTest, ct);
        }
    }

    private async Task RunLintAsync(
        AutomationRule rule, AutomationAction action, DatabaseSchema schema, bool isTest, CancellationToken ct)
    {
        // Linter saf ve yerel: AI çağrısı yok, dolayısıyla kota da harcamıyor.
        var result = _linter.Lint(schema);
        var errors = result.Messages.Count(m => m.Severity == LintSeverity.Error);
        var warnings = result.Messages.Count(m => m.Severity == LintSeverity.Warning);
        var summary = result.Messages.Count == 0
            ? "No lint findings."
            : $"{errors} error(s), {warnings} warning(s).";

        await LogAsync(rule.Id, action.ActionType, "Success", null, summary, isTest, ct);
    }

    private async Task RunDbaCheckAsync(
        AutomationRule rule, AutomationAction action, string userId, DatabaseSchema schema,
        DatabaseType engine, bool isTest, CancellationToken ct)
    {
        // Kota rezervasyonu test çalıştırmasında da yapılıyor: gerçek bir AI
        // çağrısı gidiyor, bedeli de gerçek.
        var decision = await _quota.TryReserveAsync(userId, AutomationTokenEstimate, ct);
        if (decision != AiQuotaDecision.Allowed)
        {
            await LogAsync(rule.Id, action.ActionType, "Skipped", "Quota exceeded.", null, isTest, ct);
            return;
        }

        var issues = await _groqDba.AnalyzeSchemaDbaAsync(schema, engine);
        var summary = issues.Count == 0
            ? "No issues found."
            : $"{issues.Count} issue(s) found.";
        await LogAsync(rule.Id, action.ActionType, "Success", null, summary, isTest, ct);
    }

    private async Task RunSeedDataAsync(
        AutomationRule rule, AutomationAction action, string userId, DatabaseSchema schema, bool isTest, CancellationToken ct)
    {
        var decision = await _quota.TryReserveAsync(userId, AutomationTokenEstimate, ct);
        if (decision != AiQuotaDecision.Allowed)
        {
            await LogAsync(rule.Id, action.ActionType, "Skipped", "Quota exceeded.", null, isTest, ct);
            return;
        }

        // v1'de otomatik veritabanına YAZILMAZ — yalnızca üretilip özetlenir.
        var sql = await _aiService.GenerateMockDataAsync(schema);
        var summary = sql.Length > 500 ? sql[..500] + "…" : sql;
        await LogAsync(rule.Id, action.ActionType, "Success", null, summary, isTest, ct);
    }

    /// <summary>Varsayılan Slack/Discord mesajı — kullanıcı bir şey yazmadıysa.</summary>
    private const string DefaultMessage = "Namines Flow: {{trigger}} on {{tableName}} in {{projectName}}";

    /// <summary>Aksiyon yapılandırmasının serbest JSON'unun tanınan alanları.</summary>
    private sealed class ActionConfig
    {
        public string? Url { get; set; }
        public string? Method { get; set; }
        public string? Body { get; set; }
        public string? Message { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
    }

    private static readonly JsonSerializerOptions ConfigJsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// RFC 9110'un izin verdiği token karakterleri. Boşluk, iki nokta ve satır
    /// sonu içeren bir ad bozuk istek üretir.
    /// </summary>
    private static bool IsSafeHeaderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        foreach (var c in name)
        {
            var ok = char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '!' or '#' or '$' or '%'
                or '&' or '\'' or '*' or '+' or '^' or '`' or '|' or '~';
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>Başlık değerinden satır sonlarını atar (başlık enjeksiyonu).</summary>
    private static string StripLineBreaks(string value) =>
        value.Replace("\r", string.Empty).Replace("\n", string.Empty);

    private static ActionConfig ParseConfig(string actionConfigJson)
    {
        if (string.IsNullOrWhiteSpace(actionConfigJson)) return new ActionConfig();
        try
        {
            return JsonSerializer.Deserialize<ActionConfig>(actionConfigJson, ConfigJsonOptions) ?? new ActionConfig();
        }
        catch (JsonException)
        {
            // Bozuk yapılandırma URL'siz sayılıyor; çağıran bunu "Skipped"
            // olarak, sebebiyle birlikte loglayacak.
            return new ActionConfig();
        }
    }

    /// <summary>
    /// Yalnızca gövde taşıyan güvenli metotlara izin veriliyor. Serbest bırakmak
    /// (ör. DELETE) bir otomasyonun uzak sistemde beklenmedik yıkıcı çağrı
    /// yapmasına kapı açardı; tanınmayan değer POST'a düşüyor.
    /// </summary>
    private static HttpMethod ParseMethod(string? method) => method?.ToUpperInvariant() switch
    {
        "PUT" => HttpMethod.Put,
        "PATCH" => HttpMethod.Patch,
        _ => HttpMethod.Post,
    };

    private async Task LogAsync(
        string ruleId, string actionType, string status, string? error, string? summary, bool isTest, CancellationToken ct)
    {
        _db.AutomationRunLogs.Add(new AutomationRunLog
        {
            RuleId = ruleId, ActionType = actionType, Status = status,
            ErrorMessage = error, ResultSummary = summary, IsTest = isTest,
        });
        await _db.SaveChangesAsync(ct);
    }
}
