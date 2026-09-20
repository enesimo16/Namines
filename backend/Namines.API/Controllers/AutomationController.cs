using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;

namespace Namines.API.Controllers;

/// <summary>Zincirdeki tek bir adım. Sıra, dizideki konumdan alınıyor.</summary>
public class AutomationActionDto
{
    public string ActionType { get; set; } = string.Empty;
    public string ActionConfigJson { get; set; } = "{}";
}

public class CreateAutomationRuleRequest
{
    public string ProjectId { get; set; } = string.Empty;
    public string? ScopeTableId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TriggerType { get; set; } = string.Empty;
    public string ConditionsJson { get; set; } = "[]";
    public List<AutomationActionDto> Actions { get; set; } = new();
}

/// <summary>
/// Kuralın DÜZENLENEBİLİR alanları. ProjectId bilerek yok: bir kural
/// oluşturulduğu projeden başka bir projeye taşınmıyor.
///
/// <b>ScopeTableId artık düzenlenebilir</b> — çekmecede "bu tablo ↔ tüm proje"
/// kapsam seçicisi var ve proje geneline geçmek ilişki tetikleyicilerini
/// açıyor; kapsam sabit kalsaydı o tetikleyiciler erişilemez kalırdı.
/// </summary>
public class UpdateAutomationRuleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? ScopeTableId { get; set; }
    public string TriggerType { get; set; } = string.Empty;
    public string ConditionsJson { get; set; } = "[]";
    public List<AutomationActionDto> Actions { get; set; } = new();
    public bool Enabled { get; set; } = true;
}

[ApiController]
[Route("api/automation")]
[Authorize]
public class AutomationController : ControllerBase
{
    /// <summary>Hız sınırı penceresi ve pencere başına izin verilen test sayısı.</summary>
    private static readonly TimeSpan TestRunWindow = TimeSpan.FromMinutes(1);
    private const int MaxTestRunsPerWindow = 5;

    private readonly AuthDbContext _context;
    private readonly IAutomationExecutor _executor;

    public AutomationController(AuthDbContext context, IAutomationExecutor executor)
    {
        _context = context;
        _executor = executor;
    }

    /// <summary>
    /// Projenin kayıtlı şeması. Bozuk/boş JSON boş şemaya düşüyor — test
    /// çalıştırması bu yüzden patlamamalı; aksiyonların çoğu şemaya hiç
    /// bakmıyor zaten.
    /// </summary>
    private static DatabaseSchema DeserializeSchema(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new DatabaseSchema();
        try
        {
            return JsonSerializer.Deserialize<DatabaseSchema>(json, SchemaJsonOptions.Default) ?? new DatabaseSchema();
        }
        catch (JsonException)
        {
            return new DatabaseSchema();
        }
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>
    /// İstek gövdesindeki adımları kalıcı varlıklara çevirir. Sıra, dizideki
    /// konumdan alınıyor — istemcinin ayrıca bir sıra numarası göndermesi
    /// gerekmiyor ve iki tarafın sıralaması ayrışamıyor.
    /// </summary>
    private static List<AutomationAction> BuildActions(string ruleId, List<AutomationActionDto> actions) =>
        actions.Select((a, index) => new AutomationAction
        {
            RuleId = ruleId,
            SortOrder = index,
            ActionType = a.ActionType,
            ActionConfigJson = string.IsNullOrWhiteSpace(a.ActionConfigJson) ? "{}" : a.ActionConfigJson,
        }).ToList();

    private async Task<bool> OwnsProjectAsync(string projectId, string userId, CancellationToken ct)
    {
        // Bölüm 3 v1: "sahiplik" = kullanıcının bu projeye erişimi olan bir
        // organizasyonun üyesi olması. AuthController.GetProjects'teki
        // myOrgIds desenini tekrar kullanmak yerine burada en dar hâliyle
        // (proje.UserId == userId) tutuluyor — takım paylaşımlı düzenleme
        // yetkisi bu planın kapsamı dışında, spec de bunu ayırt etmiyor.
        return await _context.CloudProjects.AsNoTracking()
            .AnyAsync(p => p.Id == projectId && p.UserId == userId, ct);
    }

    /// <summary>
    /// Bir projenin kuralları.
    ///
    /// <b>Tanınmayan/başkasına ait proje için BOŞ LİSTE, 404 değil.</b> Yazma
    /// uçları 404 döndürmeye devam ediyor; orada sessiz kalmak yanlış olurdu.
    /// Ama bu bir LİSTE ucu ve iki davranış aynı derecede bilgi saklıyor:
    /// saldırgan <c>[]</c> gördüğünde projenin var olup boş mu olduğunu, size mi
    /// ait olmadığını, yoksa hiç var olmadığını ayırt EDEMİYOR — tıpkı 404'te
    /// olduğu gibi.
    ///
    /// <b>404'ün gerçek bedeli:</b> projeler istemcide üretilip yerel olarak
    /// saklanıyor ve sunucuya ancak kaydedilince yazılıyor. Yeni üretilmiş her
    /// şemada canvas, sunucunun hiç bilmediği bir proje kimliğiyle bu ucu
    /// çağırıyordu; sonuç, her açılışta iki adet 404 ve konsolda hata satırları
    /// oluyordu. Canlı testte tam olarak bu görüldü. "Henüz kaydedilmemiş bir
    /// projenin kuralı yok" bir arıza değil, normal durum.
    /// </summary>
    [HttpGet("rules")]
    public async Task<IActionResult> GetRules([FromQuery] string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await OwnsProjectAsync(projectId, userId, ct)) return Ok(Array.Empty<AutomationRule>());

        var rules = await _context.AutomationRules.AsNoTracking()
            .Include(r => r.Actions)
            .Where(r => r.ProjectId == projectId).ToListAsync(ct);
        // Aksiyonlar istemciye SIRALI gitmeli — sıra zincirin anlamının parçası.
        foreach (var rule in rules) rule.Actions = rule.Actions.OrderBy(a => a.SortOrder).ToList();

        // Son çalışma durumu listeyle BİRLİKTE gidiyor. Arayüz her satır için
        // ayrı istek atsaydı, on kurallı bir projede panelin açılması on
        // istek demek olurdu (N+1) — üstelik yalnızca bir rozet için.
        var ruleIds = rules.Select(r => r.Id).ToList();
        var lastRuns = (await _context.AutomationRunLogs.AsNoTracking()
                .Where(l => ruleIds.Contains(l.RuleId))
                .GroupBy(l => l.RuleId)
                .Select(g => g.OrderByDescending(l => l.TriggeredAt).First())
                .ToListAsync(ct))
            .ToDictionary(l => l.RuleId);

        return Ok(rules.Select(r => new
        {
            r.Id, r.ProjectId, r.ScopeTableId, r.Name, r.TriggerType, r.ConditionsJson, r.Enabled, r.Actions,
            LastRun = lastRuns.TryGetValue(r.Id, out var run)
                ? new { run.Status, run.TriggeredAt, run.ActionType, run.ErrorMessage }
                : null,
        }));
    }

    [HttpPost("rules")]
    public async Task<IActionResult> CreateRule([FromBody] CreateAutomationRuleRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await OwnsProjectAsync(request.ProjectId, userId, ct)) return NotFound();

        var rule = new AutomationRule
        {
            ProjectId = request.ProjectId,
            ScopeTableId = request.ScopeTableId,
            Name = request.Name,
            TriggerType = request.TriggerType,
            ConditionsJson = request.ConditionsJson,
        };
        rule.Actions = BuildActions(rule.Id, request.Actions);
        _context.AutomationRules.Add(rule);
        await _context.SaveChangesAsync(ct);
        return Ok(rule);
    }

    /// <summary>
    /// Bir kuralın tetikleyici/aksiyon/konfig/enabled alanlarını günceller.
    ///
    /// Bu uç OLMADAN istemcideki her düzenleme yalnızca yereldeydi: kural
    /// sunucuda sonsuza dek ActionType="Toast" kalıyordu ve AutomationExecutor
    /// Toast kurallarını atladığı için webhook/DBA/seed hiç çalışmıyordu.
    /// </summary>
    [HttpPut("rules/{id}")]
    public async Task<IActionResult> UpdateRule(string id, [FromBody] UpdateAutomationRuleRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        // DeleteRule ile AYNI sıra ve AYNI yardımcı: önce kaydı bul, sonra
        // sahipliği doğrula; ikisinden biri tutmazsa 404 (varlığı sızdırmamak
        // için 403 değil) — diğer üç uçla tutarlı.
        var rule = await _context.AutomationRules
            .Include(r => r.Actions)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return NotFound();
        if (!await OwnsProjectAsync(rule.ProjectId, userId, ct)) return NotFound();

        rule.Name = request.Name;
        rule.ScopeTableId = request.ScopeTableId;
        rule.TriggerType = request.TriggerType;
        rule.ConditionsJson = request.ConditionsJson;
        rule.Enabled = request.Enabled;

        // Aksiyonlar yerinde eşleştirilmek yerine TOPTAN değiştiriliyor: istemci
        // adımları yeniden sıralayabiliyor, araya ekleyip silebiliyor ve
        // adımların istemci tarafında kalıcı bir kimliği yok. Tek tek eşleştirme
        // bu yüzden yanlış adımı güncellemeye açık olurdu.
        _context.AutomationActions.RemoveRange(rule.Actions);
        rule.Actions = BuildActions(rule.Id, request.Actions);

        await _context.SaveChangesAsync(ct);
        return Ok(rule);
    }

    /// <summary>
    /// Bir kuralın son çalışmaları — teşhis için.
    ///
    /// Bu uç OLMADAN <see cref="AutomationRunLog"/> yalnızca sunucuda birikiyor
    /// ve kullanıcı hiç göremiyordu: SSRF kontrolüne takılıp "Skipped" olan bir
    /// webhook ile hiç tetiklenmemiş bir kural, arayüzde birbirinden
    /// ayırt edilemiyordu — ikisi de sessizdi.
    /// </summary>
    [HttpGet("rules/{id}/runs")]
    public async Task<IActionResult> GetRuns(string id, [FromQuery] int limit, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var rule = await _context.AutomationRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return NotFound();
        if (!await OwnsProjectAsync(rule.ProjectId, userId, ct)) return NotFound();

        // Sınır kullanıcıdan geliyor: 0/negatif değer boş liste, aşırı büyük
        // değer ise tek istekte tüm geçmişi çeker. İkisi de kelepçeleniyor.
        var take = limit <= 0 ? 20 : Math.Min(limit, 100);

        var runs = await _context.AutomationRunLogs.AsNoTracking()
            .Where(l => l.RuleId == id)
            .OrderByDescending(l => l.TriggeredAt)
            .Take(take)
            .ToListAsync(ct);

        return Ok(runs);
    }

    /// <summary>
    /// Kuralı ELLE, hemen çalıştırır ve sonucunu döner.
    ///
    /// <b>Neden gerekli:</b> sunucu tarafı aksiyonlar senkronizasyon
    /// döngüsünde, en geç ~30 saniye içinde çalışıyor. Kullanıcı bir webhook
    /// tanımladıktan sonra hiçbir şey görmüyor ve çalışıp çalışmadığını
    /// anlamanın yolu yok — bu düğme o boşluğu kapatıyor.
    ///
    /// <b>Test gerçek bir çalıştırmadır:</b> SSRF doğrulaması ve AI kota
    /// rezervasyonu ATLANMIYOR. Atlansaydı "test" düğmesi hem bir SSRF kapısı
    /// hem de kota bypass'ı olurdu.
    /// </summary>
    [HttpPost("rules/{id}/test")]
    public async Task<IActionResult> TestRule(string id, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var rule = await _context.AutomationRules.AsNoTracking()
            .Include(r => r.Actions)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return NotFound();

        var project = await _context.CloudProjects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == rule.ProjectId && p.UserId == userId, ct);
        if (project is null) return NotFound();

        // Kural başına hız sınırı: her adım gerçek bir webhook çağrısı ya da
        // AI isteği demek, düğmeye basılı tutmak pahalı.
        //
        // Sınır ÇALIŞTIRMA sayar, log SATIRI değil. Satır sayılsaydı beş adımlı
        // bir zincir tek testte sınırı doldururdu; yalnızca Toast içeren bir
        // kural ise hiç satır yazmadığı için hiç sınırlanmazdı.
        // Satır sayısı, sunucuda çalışan adım sayısına bölünüyor: bu, yeni bir
        // sütun eklemeden çalıştırma sayısına en yakın ölçü. Zincirde hiç
        // sunucu adımı yoksa (yalnızca Toast) hiç satır yazılmıyor ve sınır
        // uygulanmıyor — sınırlanacak bir maliyet de yok.
        var since = DateTime.UtcNow - TestRunWindow;
        var serverSideSteps = rule.Actions.Count(a => a.ActionType != "Toast");
        if (serverSideSteps > 0)
        {
            var recentRows = await _context.AutomationRunLogs.AsNoTracking()
                .CountAsync(l => l.RuleId == id && l.IsTest && l.TriggeredAt >= since, ct);
            if (recentRows / serverSideSteps >= MaxTestRunsPerWindow)
                return StatusCode(429, new { error = "Too many test runs for this rule. Try again in a minute." });
        }

        var schema = DeserializeSchema(project.SchemaJson);
        var engine = Enum.TryParse<DatabaseType>(project.DbType, ignoreCase: true, out var parsed)
            ? parsed : DatabaseType.PostgreSQL;

        // Test çalıştırmasının gerçek bir olayı yok; şablon değişkenleri için
        // elimizdeki en doğru bağlam kuralın kapsam tablosu. Proje geneli bir
        // kuralda tablo adı da yok — şablonlar o alanları boş görüyor, ki
        // gerçek tetiklenmede de ilişki olaylarında durum tam olarak budur.
        var scopeTableName = rule.ScopeTableId is null
            ? null
            : schema.Tables.FirstOrDefault(t => t.Id == rule.ScopeTableId)?.Name;
        var context = new AutomationTriggerContext(scopeTableName, null, null);

        // Çalıştırmadan HEMEN ÖNCEKİ an: sonuçlar bununla sınırlanıyor.
        // Pencereye göre filtreleyip adım sayısı kadar satır almak, zincirde
        // Toast varken eksik kalan satırları ÖNCEKİ bir test çalıştırmasının
        // satırlarıyla doldururdu.
        var runStartedAt = DateTime.UtcNow;
        await _executor.RunRuleAsync(rule, userId, schema, engine, context, project.Name, isTest: true, ct);

        var results = await _context.AutomationRunLogs.AsNoTracking()
            .Where(l => l.RuleId == id && l.IsTest && l.TriggeredAt >= runStartedAt)
            .OrderByDescending(l => l.TriggeredAt)
            .ToListAsync(ct);

        return Ok(new
        {
            // Zincirdeki Toast adımları sunucuda hiç çalışmıyor (istemci
            // tarafı aksiyon), dolayısıyla log satırı da üretmiyorlar —
            // kullanıcı "2 adım tanımladım ama 1 sonuç var" demesin diye
            // bu açıkça bildiriliyor.
            clientOnlyActions = rule.Actions.Count(a => a.ActionType == "Toast"),
            runs = results,
        });
    }

    [HttpDelete("rules/{id}")]
    public async Task<IActionResult> DeleteRule(string id, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var rule = await _context.AutomationRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return NotFound();
        if (!await OwnsProjectAsync(rule.ProjectId, userId, ct)) return NotFound();

        _context.AutomationRules.Remove(rule);
        await _context.SaveChangesAsync(ct);
        return Ok(new { deleted = true });
    }
}
