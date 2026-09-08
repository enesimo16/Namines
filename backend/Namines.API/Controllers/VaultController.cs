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
using Namines.Infrastructure.Services;

namespace Namines.API.Controllers;

/// <param name="ConfirmDatabaseName">
/// Kullanıcının ELİYLE yazdığı proje adı. Geri yükleme mevcut veriyi siler;
/// tek tıkla tetiklenebilmesi, yanlış satıra basan biri için geri dönüşü
/// olmayan bir kayıp demekti.
/// </param>
public sealed record RestoreBackupRequest(string ConfirmDatabaseName);

/// <summary>
/// Namines Vault — yedekleme ve geri yükleme uçları.
///
/// Oturum (JWT) ile korunuyor, Gateway API anahtarıyla DEĞİL: bir panel
/// anahtarının veritabanının tamamını indirebilmesi ya da üzerine yazabilmesi,
/// anahtar sınırlamasının tamamını anlamsız kılardı.
/// </summary>
[Authorize]
[ApiController]
[Route("api/vault")]
public class VaultController : ControllerBase
{
    private readonly AuthDbContext _context;
    private readonly VaultService _vault;

    public VaultController(AuthDbContext context, VaultService vault)
    {
        _context = context;
        _vault = vault;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>
    /// Vault'un ayakta olduğunu ve yedeklerin NEREDE durduğunu söyler.
    ///
    /// Depo açıklaması bilerek dönülüyor: v1 sunucu diskini kullanıyor ve
    /// kullanıcının bunu ekranda görmeden yedeğe güvenmesi doğru olmaz.
    /// </summary>
    [HttpGet("health")]
    [AllowAnonymous]
    public IActionResult Health() => Ok(new { ok = true, store = _vault.StoreDescription });

    /// <summary>Projenin yedekleri, en yenisi üstte.</summary>
    [HttpGet("{projectId}/backups")]
    public async Task<IActionResult> List(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _context.CanViewAsync(projectId, userId, ct)) return Forbid();

        var backups = await _context.VaultBackups
            .Where(b => b.ProjectId == projectId)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new
            {
                id = b.Id,
                databaseName = b.DatabaseName,
                engine = b.Engine,
                status = b.Status.ToString(),
                kind = b.Kind.ToString(),
                sizeBytes = b.SizeBytes,
                error = b.ErrorMessage,
                createdAt = b.CreatedAt,
                completedAt = b.CompletedAt,
                store = b.StoreDescription,
            })
            .ToListAsync(ct);

        return Ok(new { store = _vault.StoreDescription, backups });
    }

    /// <summary>Geri yükleme geçmişi — "bu veritabanına en son kim ne yazdı".</summary>
    [HttpGet("{projectId}/restores")]
    public async Task<IActionResult> Restores(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _context.CanViewAsync(projectId, userId, ct)) return Forbid();

        var restores = await _context.VaultRestores
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => new
            {
                id = r.Id,
                backupId = r.BackupId,
                preRestoreBackupId = r.PreRestoreBackupId,
                status = r.Status.ToString(),
                error = r.ErrorMessage,
                startedAt = r.StartedAt,
                completedAt = r.CompletedAt,
            })
            .ToListAsync(ct);

        return Ok(restores);
    }

    /// <summary>
    /// Yedek alır.
    ///
    /// <b>Editor yetkisi yetiyor</b> (<see cref="OrgAccess.CanEditAsync"/>):
    /// yedek almak veriyi DEĞİŞTİRMEYEN, kaybı önleyen bir işlem — onu dar bir
    /// role kilitlemek, riski azaltmak yerine artırırdı.
    /// </summary>
    [HttpPost("{projectId}/backups")]
    public async Task<IActionResult> Create(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _context.CanEditAsync(projectId, userId, ct)) return Forbid();

        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFound();

        var result = await _vault.BackupAsync(project, userId, VaultBackupKind.Manual, ct);
        return result.Ok
            ? Ok(new { backupId = result.BackupId })
            : BadRequest(new { error = result.Error });
    }

    /// <summary>
    /// Şifreli yedeği indirir.
    ///
    /// <b>Dosya ŞİFRELİ iniyor</b> ve bu bilinçli: içerik veritabanının tamamı,
    /// çözülmüş hâlini tarayıcıya göndermek onu tarayıcı önbelleğine ve indirilenler
    /// klasörüne düz metin olarak bırakırdı. Çözmek yalnızca geri yükleme yolunda.
    /// </summary>
    [HttpGet("{projectId}/backups/{backupId}/download")]
    public async Task<IActionResult> Download(string projectId, string backupId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        // Yedek indirmek verinin TAMAMINI almaktir — okuma degil, yonetim yetkisi.
        if (!await _context.CanManageMembersAsync(projectId, userId, ct)) return Forbid();

        var backup = await FindBackupAsync(projectId, backupId, ct);
        if (backup is null) return NotFound();

        var stream = await _vault.OpenEncryptedAsync(backup, ct);
        if (stream is null) return NotFound(new { error = "The backup file is missing from storage." });

        return File(stream, "application/octet-stream", $"{backup.DatabaseName}-{backup.CreatedAt:yyyyMMdd-HHmmss}.nvlt");
    }

    /// <summary>
    /// Geri yükler. <b>Hedefteki veriyi SİLER.</b>
    ///
    /// Owner şartı: bir Admin bir tabloyu Gateway'e açabiliyor, ama veritabanının
    /// tamamının üzerine yazmak faturalama/org silme ile aynı ağırlıkta.
    /// </summary>
    [HttpPost("{projectId}/backups/{backupId}/restore")]
    public async Task<IActionResult> Restore(
        string projectId, string backupId, [FromBody] RestoreBackupRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (await _context.GetRoleAsync(projectId, userId, ct) != OrgRole.Owner) return Forbid();

        var backup = await FindBackupAsync(projectId, backupId, ct);
        if (backup is null) return NotFound();

        if (!string.Equals(request?.ConfirmDatabaseName?.Trim(), backup.DatabaseName, StringComparison.Ordinal))
            return BadRequest(new { error = $"Type the database name exactly ({backup.DatabaseName}) to confirm." });

        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFound();

        var result = await _vault.RestoreAsync(project, backup, userId, ct);
        return result.Ok
            ? Ok(new { restored = true, preRestoreBackupId = result.BackupId })
            : BadRequest(new { error = result.Error });
    }

    /// <summary>Yedeği siler — kaydı ve dosyayı birlikte.</summary>
    [HttpDelete("{projectId}/backups/{backupId}")]
    public async Task<IActionResult> Delete(string projectId, string backupId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _context.CanManageMembersAsync(projectId, userId, ct)) return Forbid();

        var backup = await FindBackupAsync(projectId, backupId, ct);
        if (backup is null) return NotFound();

        await _vault.DeleteAsync(backup, ct);
        return NoContent();
    }

    /// <summary>
    /// Yedeği hem kimliğiyle hem PROJESİYLE arar.
    ///
    /// Yalnızca kimlikle aramak, yetkisi olan bir projenin kimliğiyle BAŞKA bir
    /// projenin yedeğine ulaşmayı mümkün kılardı.
    /// </summary>
    private Task<VaultBackup?> FindBackupAsync(string projectId, string backupId, CancellationToken ct) =>
        _context.VaultBackups.FirstOrDefaultAsync(b => b.Id == backupId && b.ProjectId == projectId, ct);
}
