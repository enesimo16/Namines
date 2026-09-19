using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models;
using Namines.Infrastructure.Data;

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
    private readonly AuthDbContext _context;

    public AutomationController(AuthDbContext context) => _context = context;

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
        return Ok(rules);
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
