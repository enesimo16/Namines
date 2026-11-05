using Namines.API.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Analysis;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;

namespace Namines.API.Controllers;

public sealed record CreateCrossDatabaseRelationRequest(
    string SourceProjectId, string SourceTableId, string SourceColumnId,
    string TargetProjectId, string TargetTableId, string TargetColumnId,
    string? Note);

/// <summary>
/// second-phase/10-COKLU-DB.md — birden çok veritabanı (proje) arasındaki
/// MANTIKSAL ilişkileri kaydeder ve bir silme/değişiklik öncesi karşı
/// veritabanını da etkileyip etkilemediğini gösterir.
///
/// <b>Yetki sınırı iki projeyi de kapsıyor.</b> Bir ilişki iki projenin
/// sınırını aşıyor — yalnızca birine erişimi olan biri diğerine dair bilgi
/// (tablo/kolon adları) sızdıramamalı.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[FeatureGate("tool_virtual_relations")]
public class CrossDatabaseController : ControllerBase
{
    private readonly ICrossDatabaseRelationService _relations;
    private readonly AiQuotaService _quota;

    private static readonly JsonSerializerOptions SchemaJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public CrossDatabaseController(ICrossDatabaseRelationService relations, AiQuotaService quota)
    {
        _relations = relations;
        _quota = quota;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpPost("relations")]
    public async Task<IActionResult> CreateRelation([FromBody] CreateCrossDatabaseRelationRequest request)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        // Plan sınırı: sınırsız bırakmak, her ilişkinin silme öncesi etki analizi ve karşı şema çözümlemesi
        // tetiklediği düşünülünce ücretsiz katmanda açık uçlu bir maliyetti.
        var limit = PlanQuotas.For(await _quota.TierAsync(userId)).CrossDatabaseRelations;
        var outcome = await _relations.CreateAsync(userId, new CreateRelationCommand(
            request.SourceProjectId, request.SourceTableId, request.SourceColumnId,
            request.TargetProjectId, request.TargetTableId, request.TargetColumnId, request.Note), limit);

        return outcome.Kind switch
        {
            ModuleOutcomeKind.Ok => Ok(new { Id = outcome.Value }),
            ModuleOutcomeKind.Invalid => BadRequest(new { message = outcome.Error }),
            ModuleOutcomeKind.NotFound => NotFound(new { message = outcome.Error }),
            ModuleOutcomeKind.TooMany => StatusCode(429, new { message = outcome.Error }),
            _ => StatusCode(500),
        };
    }

    /// <summary>
    /// <paramref name="projectId"/>'nin taraf olduğu (kaynak ya da hedef) tüm
    /// ilişkiler — tablo/kolon adları KARŞI projenin şemasından çözülmüş olarak.
    /// </summary>
    [HttpGet("relations")]
    public async Task<IActionResult> ListRelations([FromQuery] string projectId)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var outcome = await _relations.ListAsync(userId, projectId);
        return outcome.IsOk ? Ok(outcome.Value) : NotFound(new { message = outcome.Error });
    }

    [HttpDelete("relations/{id}")]
    public async Task<IActionResult> DeleteRelation(string id)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var outcome = await _relations.DeleteAsync(userId, id);
        return outcome.Kind switch
        {
            ModuleOutcomeKind.Ok => NoContent(),
            ModuleOutcomeKind.Forbidden => Forbid(),
            _ => NotFound(),
        };
    }

    /// <summary>
    /// Bir tablo (ve isteğe bağlı kolon) silinmeden/değiştirilmeden ÖNCE sorulur —
    /// kayıtlı hangi karşı-veritabanı ilişkilerinin kırılacağını söyler.
    /// </summary>
    [HttpGet("impact")]
    public async Task<IActionResult> Impact([FromQuery] string projectId, [FromQuery] string tableId, [FromQuery] string? columnId = null)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var outcome = await _relations.ImpactAsync(userId, projectId, tableId, columnId);
        return outcome.IsOk ? Ok(outcome.Value) : NotFound(new { message = outcome.Error });
    }
}
