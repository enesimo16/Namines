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
public class CrossDatabaseController : ControllerBase
{
    private readonly AuthDbContext _context;
    private readonly AiQuotaService _quota;

    private static readonly JsonSerializerOptions SchemaJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public CrossDatabaseController(AuthDbContext context, AiQuotaService quota)
    {
        _context = context;
        _quota = quota;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpPost("relations")]
    public async Task<IActionResult> CreateRelation([FromBody] CreateCrossDatabaseRelationRequest request)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (request.SourceProjectId == request.TargetProjectId)
            return BadRequest(new { message = "A cross-database relation must connect two different projects." });

        // İki projenin sınırını da aşıyor — ikisine de en az okuma yetkisi gerekiyor,
        // yoksa erişimi olmayan bir projenin varlığını/adını sızdırmış oluruz.
        if (!await _context.CanViewAsync(request.SourceProjectId, userId) ||
            !await _context.CanViewAsync(request.TargetProjectId, userId))
            return NotFound(new { message = "One of the projects was not found or you don't have access to it." });

        // Plan sınırı — second-phase/10-COKLU-DB.md'nin açık bıraktığı
        // "kaç DB, hangi planda?" sorusunun cevabı. Sınırsız bırakmak, her
        // ilişkinin silme öncesi bir etki analizi ve karşı şema çözümlemesi
        // tetiklediği düşünülünce ücretsiz katmanda açık uçlu bir maliyetti.
        var limit = PlanQuotas.For(await _quota.TierAsync(userId)).CrossDatabaseRelations;
        if (limit >= 0)
        {
            // Kullanıcının KENDİ kurduğu ilişkiler sayılıyor, projeye gelenler
            // değil: bir ekip arkadaşının kurduğu bağ senin hakkını yememeli.
            var used = await _context.CrossDatabaseRelations
                .CountAsync(r => r.CreatedByUserId == userId);

            if (used >= limit)
                return StatusCode(429, new
                {
                    message = $"Your plan allows {limit} cross-database relation(s). Remove one, or upgrade for more.",
                });
        }

        var relation = new CrossDatabaseRelation
        {
            SourceProjectId = request.SourceProjectId,
            SourceTableId = request.SourceTableId,
            SourceColumnId = request.SourceColumnId,
            TargetProjectId = request.TargetProjectId,
            TargetTableId = request.TargetTableId,
            TargetColumnId = request.TargetColumnId,
            Note = request.Note,
            CreatedByUserId = userId,
        };

        _context.CrossDatabaseRelations.Add(relation);
        await _context.SaveChangesAsync();

        return Ok(new { relation.Id });
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

        if (!await _context.CanViewAsync(projectId, userId))
            return NotFound(new { message = "Project not found or you don't have access to it." });

        var relations = await _context.CrossDatabaseRelations
            .Where(r => r.SourceProjectId == projectId || r.TargetProjectId == projectId)
            .ToListAsync();

        if (relations.Count == 0) return Ok(Array.Empty<object>());

        // Görüntülenecek her ilişki için KARŞI projenin adını + şemasını (tablo/kolon
        // adı çözümlemek için) tek seferde çekiyoruz — N ilişki için N ayrı sorgu değil.
        var otherProjectIds = relations
            .SelectMany(r => new[] { r.SourceProjectId, r.TargetProjectId })
            .Where(id => id != projectId)
            .Distinct()
            .ToList();

        var otherProjects = await _context.CloudProjects
            .AsNoTracking()
            .Where(p => otherProjectIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.SchemaJson })
            .ToListAsync();

        var thisProject = await _context.CloudProjects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.Id, p.Name, p.SchemaJson })
            .FirstAsync();

        var schemaCache = new Dictionary<string, DatabaseSchema?>();
        DatabaseSchema? SchemaOf(string id, string json)
        {
            if (schemaCache.TryGetValue(id, out var cached)) return cached;
            DatabaseSchema? parsed;
            try { parsed = JsonSerializer.Deserialize<DatabaseSchema>(json, SchemaJsonOptions); }
            catch { parsed = null; }
            schemaCache[id] = parsed;
            return parsed;
        }

        string? NameOf(DatabaseSchema? schema, string tableId, string? columnId)
        {
            var table = schema?.Tables.FirstOrDefault(t => t.Id == tableId);
            if (table is null) return null;
            if (columnId is null) return table.Name;
            var column = table.Columns.FirstOrDefault(c => c.Id == columnId);
            return column is null ? table.Name : $"{table.Name}.{column.Name}";
        }

        var result = relations.Select(r =>
        {
            var isSource = r.SourceProjectId == projectId;
            var otherId = isSource ? r.TargetProjectId : r.SourceProjectId;
            var other = otherProjects.FirstOrDefault(p => p.Id == otherId);

            var localSchema = SchemaOf(projectId, thisProject.SchemaJson);
            var otherSchema = other is null ? null : SchemaOf(other.Id, other.SchemaJson);

            var localTableId = isSource ? r.SourceTableId : r.TargetTableId;
            var localColumnId = isSource ? r.SourceColumnId : r.TargetColumnId;
            var otherTableId = isSource ? r.TargetTableId : r.SourceTableId;
            var otherColumnId = isSource ? r.TargetColumnId : r.SourceColumnId;

            return new
            {
                r.Id,
                direction = isSource ? "outgoing" : "incoming",
                localColumn = NameOf(localSchema, localTableId, localColumnId) ?? $"{localTableId}.{localColumnId}",
                otherProjectId = otherId,
                otherProjectName = other?.Name ?? "(deleted project)",
                otherColumn = NameOf(otherSchema, otherTableId, otherColumnId) ?? $"{otherTableId}.{otherColumnId}",
                r.Note,
                r.CreatedAt,
            };
        }).ToList();

        return Ok(result);
    }

    [HttpDelete("relations/{id}")]
    public async Task<IActionResult> DeleteRelation(string id)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var relation = await _context.CrossDatabaseRelations.FirstOrDefaultAsync(r => r.Id == id);
        if (relation is null) return NotFound();

        // Silme yetkisi: bu ilişkinin dokunduğu İKİ projeden BİRİNDE düzenleme
        // yetkisi olması yeterli — ilişkinin diğer tarafına erişimi olmayan biri
        // bile, kendi tarafındaki (Editor+ olduğu) bir kaydı temizleyebilmeli.
        var canEditEither = await _context.CanEditAsync(relation.SourceProjectId, userId) ||
                             await _context.CanEditAsync(relation.TargetProjectId, userId);
        if (!canEditEither) return Forbid();

        _context.CrossDatabaseRelations.Remove(relation);
        await _context.SaveChangesAsync();
        return NoContent();
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

        if (!await _context.CanViewAsync(projectId, userId))
            return NotFound(new { message = "Project not found or you don't have access to it." });

        var relations = await _context.CrossDatabaseRelations
            .Where(r => r.SourceProjectId == projectId || r.TargetProjectId == projectId)
            .ToListAsync();

        var impacts = CrossDatabaseImpactAnalyzer.FindAffected(relations, projectId, tableId, columnId);
        if (impacts.Count == 0) return Ok(Array.Empty<object>());

        var otherProjectIds = impacts.Select(i => i.OtherProjectId).Distinct().ToList();
        var otherNames = await _context.CloudProjects
            .AsNoTracking()
            .Where(p => otherProjectIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name);

        var result = impacts.Select(i => new
        {
            i.RelationId,
            i.Direction,
            otherProjectId = i.OtherProjectId,
            otherProjectName = otherNames.GetValueOrDefault(i.OtherProjectId, "(deleted project)"),
            i.Note,
        });

        return Ok(result);
    }
}
