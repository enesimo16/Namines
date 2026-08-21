using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;

namespace Namines.API.Controllers;

public sealed record CreateGatewayKeyRequest(
    string Name,
    bool CanWrite = false,
    DateTime? ExpiresAt = null,
    string? AllowedOrigins = null,
    string? AllowedIps = null,
    int? RateLimitPerMinute = null);
public sealed record SetTablePermissionRequest(
    string TableName, bool CanRead, bool CanWrite, string? MaskedColumns = null);

/// <summary>
/// Gateway API anahtarları ve tablo izinleri (08 §4.3).
///
/// Bu uçlar oturum (JWT) ile korunur, API anahtarıyla DEĞİL: bir anahtarın kendi
/// yetkisini genişletebilmesi ya da yeni anahtar üretebilmesi, anahtar
/// sınırlamasının tamamını anlamsız kılardı.
/// </summary>
[Authorize]
[ApiController]
[Route("api/gateway/keys")]
public class GatewayKeyController : ControllerBase
{
    private readonly AuthDbContext _context;

    public GatewayKeyController(AuthDbContext context) => _context = context;

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>
    /// Anahtar üretmek ve tablo açmak yönetim işidir — 05 §6'ya göre Admin ve üstü.
    /// Editor yetkisi şema yazmaya yeter ama bir tabloyu internete açmaya yetmemeli.
    /// </summary>
    private async Task<bool> CanManageAsync(string projectId, string userId) =>
        await _context.CanManageMembersAsync(projectId, userId);

    [HttpPost("{projectId}")]
    public async Task<IActionResult> Create(string projectId, [FromBody] CreateGatewayKeyRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await CanManageAsync(projectId, userId))
            return NotFound(new { error = "Proje bulunamadı." });

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Anahtar için bir ad gerekli." });

        // Sıfır ya da negatif bir limit anahtarı tamamen kullanılamaz kılardı; bunu
        // "limit yok" saymak da yanlış olurdu, o yüzden açıkça reddediliyor.
        // Doğrulama anahtar üretilmeden ÖNCE: reddedilecek bir istek için entropi
        // harcamak ve yarı kurulmuş bir nesne bırakmak gereksiz.
        if (request.RateLimitPerMinute is <= 0)
            return BadRequest(new { error = "Rate limit must be greater than zero." });

        var (entity, rawKey) = GatewayAccess.CreateKey(
            projectId, request.Name.Trim(), userId, request.CanWrite, request.ExpiresAt);

        entity.AllowedOrigins = Normalize(request.AllowedOrigins);
        entity.AllowedIps = Normalize(request.AllowedIps);
        entity.RateLimitPerMinute = request.RateLimitPerMinute;

        _context.GatewayApiKeys.Add(entity);
        await _context.SaveChangesAsync(ct);

        // Ham anahtar SADECE BURADA döner. Kayıtta yalnızca özeti var; bu yanıt
        // kaybolursa anahtar geri getirilemez, yenisi üretilir.
        return Ok(new
        {
            entity.Id,
            entity.Name,
            entity.Prefix,
            entity.CanWrite,
            entity.ExpiresAt,
            entity.AllowedOrigins,
            entity.AllowedIps,
            entity.RateLimitPerMinute,
            key = rawKey,
            warning = "This is the only time the key is shown. Store it now.",
        });
    }

    [HttpGet("{projectId}")]
    public async Task<IActionResult> List(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await CanManageAsync(projectId, userId))
            return NotFound(new { error = "Proje bulunamadı." });

        var keys = await _context.GatewayApiKeys
            .Where(k => k.ProjectId == projectId)
            .OrderByDescending(k => k.CreatedAt)
            // KeyHash bilinçli olarak dışarıda: gösterilecek bir değer değil.
            .Select(k => new
            {
                k.Id, k.Name, k.Prefix, k.CanWrite,
                k.AllowedOrigins, k.AllowedIps, k.RateLimitPerMinute,
                k.CreatedAt, k.ExpiresAt, k.RevokedAt, k.LastUsedAt,
            })
            .ToListAsync(ct);

        return Ok(keys);
    }

    [HttpDelete("{projectId}/{keyId}")]
    public async Task<IActionResult> Revoke(string projectId, string keyId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await CanManageAsync(projectId, userId))
            return NotFound(new { error = "Proje bulunamadı." });

        var key = await _context.GatewayApiKeys
            .FirstOrDefaultAsync(k => k.Id == keyId && k.ProjectId == projectId, ct);
        if (key is null) return NotFound(new { error = "Anahtar bulunamadı." });

        // Silinmez, işaretlenir: "ne zaman iptal edildi" sorusu cevapsız kalmasın.
        key.RevokedAt ??= DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>Boş/whitespace listeyi null'a indirger: "" ile null aynı anlama gelmeli.</summary>
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ── Tablo izinleri ───────────────────────────────────────────────────────

    [HttpGet("{projectId}/tables")]
    public async Task<IActionResult> ListTablePermissions(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await CanManageAsync(projectId, userId))
            return NotFound(new { error = "Proje bulunamadı." });

        var permissions = await _context.GatewayTablePermissions
            .Where(p => p.ProjectId == projectId)
            .OrderBy(p => p.TableName)
            .ToListAsync(ct);

        return Ok(permissions.Select(p => new
        {
            p.TableName, p.CanRead, p.CanWrite, p.MaskedColumns, p.UpdatedAt,
        }));
    }

    /// <summary>
    /// Bir tablonun API anahtarlarına açıklığını belirler.
    ///
    /// Kayıt yokluğu = erişim yok (08 §1). Bu yüzden hem <c>canRead</c> hem
    /// <c>canWrite</c> false verildiğinde satır SİLİNİR: "her ikisi de kapalı" ile
    /// "hiç kayıt yok" aynı anlama gelir, iki farklı temsil tutmak listeyi
    /// yanıltıcı kılardı.
    /// </summary>
    [HttpPut("{projectId}/tables")]
    public async Task<IActionResult> SetTablePermission(string projectId, [FromBody] SetTablePermissionRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await CanManageAsync(projectId, userId))
            return NotFound(new { error = "Proje bulunamadı." });

        if (string.IsNullOrWhiteSpace(request.TableName))
            return BadRequest(new { error = "Tablo adı gerekli." });

        var existing = await _context.GatewayTablePermissions
            .FirstOrDefaultAsync(p => p.ProjectId == projectId && p.TableName == request.TableName, ct);

        if (!request.CanRead && !request.CanWrite)
        {
            if (existing is not null) _context.GatewayTablePermissions.Remove(existing);
            await _context.SaveChangesAsync(ct);
            return NoContent();
        }

        if (existing is null)
        {
            existing = new GatewayTablePermission
            {
                ProjectId = projectId,
                TableName = request.TableName,
            };
            _context.GatewayTablePermissions.Add(existing);
        }

        existing.CanRead = request.CanRead;
        // Yazma okumayı ima eder: yazabilen ama okuyamayan bir istemci, yazdığını
        // doğrulayamaz ve bu neredeyse her zaman istenmeyen bir yapılandırmadır.
        if (request.CanWrite) existing.CanRead = true;
        existing.CanWrite = request.CanWrite;
        existing.MaskedColumns = Normalize(request.MaskedColumns);
        existing.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
        return Ok(new { existing.TableName, existing.CanRead, existing.CanWrite, existing.MaskedColumns });
    }
}
