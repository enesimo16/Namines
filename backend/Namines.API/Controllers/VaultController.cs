using Namines.API.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Namines.BuildingBlocks.Security;
using Namines.Core.Models.Auth;
using Namines.Vault.Abstractions;
using Namines.Vault.Application;
using Namines.Vault.Domain;

namespace Namines.API.Controllers;

/// <param name="ConfirmDatabaseName">
/// Kullanıcının ELİYLE yazdığı proje adı. Geri yükleme mevcut veriyi siler;
/// tek tıkla tetiklenebilmesi, yanlış satıra basan biri için geri dönüşü
/// olmayan bir kayıp demekti.
/// </param>
/// <param name="Tables">
/// KISMİ geri yükleme (B-43): yalnızca bu tabloları geri yükle. Boş/null ise
/// tam geri yükleme. Yalnızca PostgreSQL yedeklerinde desteklenir — özel
/// biçim (custom-format) dump gerektirir.
/// </param>
public sealed record RestoreBackupRequest(string ConfirmDatabaseName, IReadOnlyList<string>? Tables = null);

/// <param name="Cadence">"Daily" veya "Weekly".</param>
/// <param name="DayOfWeek">Haftalıkta 0 (Pazar) – 6. Günlükte yok sayılır.</param>
/// <param name="RetainCount">Saklanacak otomatik yedek sayısı.</param>
public sealed record SaveScheduleRequest(
    bool Enabled, string Cadence, int HourUtc, int? DayOfWeek, int RetainCount);

/// <summary>
/// Namines Vault — yedekleme ve geri yükleme uçları.
///
/// <b>Modül sınırı:</b> Bu denetleyici <c>AuthDbContext</c>'e DOĞRUDAN DOKUNMAZ;
/// yalnızca <see cref="ITenantProjectAuthority"/> (tenant yetki kapısı) ve
/// <see cref="IVaultModule"/> (Vault modülü cephesi) üzerinden konuşur.
/// </summary>
[Authorize]
[ApiController]
[Route("api/vault")]
[FeatureGate("vault")]
public class VaultController : ControllerBase
{
    private readonly ITenantProjectAuthority _authority;
    private readonly IVaultModule _vaultModule;

    public VaultController(ITenantProjectAuthority authority, IVaultModule vaultModule)
    {
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _vaultModule = vaultModule ?? throw new ArgumentNullException(nameof(vaultModule));
    }

    private IVaultApplicationService Vault => _vaultModule.Backups;
    private IVaultJobQueue Jobs => _vaultModule.JobQueue;

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet("health")]
    [AllowAnonymous]
    public async Task<IActionResult> Health(CancellationToken ct)
    {
        var problem = await Vault.ProbeAsync(ct);
        var authenticated = User?.Identity?.IsAuthenticated == true;

        if (!authenticated)
            return Ok(new { ok = problem is null });

        return Ok(new
        {
            ok = problem is null,
            engines = Vault.SupportedEngines,
            store = Vault.StoreDescription,
            problem,
        });
    }

    /// <summary>Projenin yedekleri, en yenisi üstte.</summary>
    [HttpGet("{projectId}/backups")]
    public async Task<IActionResult> List(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _authority.CanViewAsync(projectId, userId, ct)) return Forbid();

        var records = await Vault.ListBackupsAsync(projectId, ct);
        var backups = records.Select(b => new
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
            verifiedAt = b.VerifiedAt,
            verifyError = b.VerifyError,
        }).ToList();

        return Ok(new { store = Vault.StoreDescription, backups });
    }

    /// <summary>Geri yükleme geçmişi — "bu veritabanına en son kim ne yazdı".</summary>
    [HttpGet("{projectId}/restores")]
    public async Task<IActionResult> Restores(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _authority.CanViewAsync(projectId, userId, ct)) return Forbid();

        var records = await Vault.ListRestoresAsync(projectId, ct);
        var restores = records.Select(r => new
        {
            id = r.Id,
            backupId = r.BackupId,
            preRestoreBackupId = r.PreRestoreBackupId,
            status = r.Status.ToString(),
            error = r.ErrorMessage,
            startedAt = r.StartedAt,
            completedAt = r.CompletedAt,
        }).ToList();

        return Ok(restores);
    }

    [HttpPost("{projectId}/backups")]
    public async Task<IActionResult> Create(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _authority.CanEditAsync(projectId, userId, ct)) return Forbid();

        var snapshot = await _authority.GetProjectSnapshotAsync(projectId, ct);
        if (snapshot is null) return NotFound();

        var result = await Vault.QueueBackupAsync(projectId, userId, VaultBackupKind.Manual, ct);
        if (!result.Ok) return BadRequest(new { error = result.Error });

        if (!Jobs.TryEnqueue(new VaultBackupJob(result.BackupId!, projectId)))
        {
            await Vault.MarkQueueFullFailedAsync(result.BackupId!, ct);

            return StatusCode(503, new
            {
                error = "Too many backups are already queued. Try again in a few minutes.",
                queued = Jobs.PendingCount,
            });
        }

        return Accepted(
            Url.Action(nameof(GetBackup), new { projectId, backupId = result.BackupId }),
            new
            {
                backupId = result.BackupId,
                status = VaultBackupStatus.Running.ToString(),
                queued = Jobs.PendingCount,
            });
    }

    [HttpGet("{projectId}/backups/{backupId}")]
    public async Task<IActionResult> GetBackup(string projectId, string backupId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (await _authority.GetRoleAsync(projectId, userId, ct) is null) return NotFound();

        var backup = await Vault.FindBackupAsync(projectId, backupId, ct);
        if (backup is null) return NotFound();

        return Ok(new
        {
            backup.Id,
            status = backup.Status.ToString(),
            backup.SizeBytes,
            backup.ErrorMessage,
            backup.CreatedAt,
            backup.CompletedAt,
            done = backup.Status != VaultBackupStatus.Running,
        });
    }

    [HttpGet("{projectId}/backups/{backupId}/download")]
    public async Task<IActionResult> Download(string projectId, string backupId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _authority.CanManageMembersAsync(projectId, userId, ct)) return Forbid();

        var backup = await Vault.FindBackupAsync(projectId, backupId, ct);
        if (backup is null) return NotFound();

        var stream = await Vault.OpenEncryptedAsync(backup, ct);
        if (stream is null) return NotFound(new { error = "The backup file is missing from storage." });

        return File(stream, "application/octet-stream", $"{backup.DatabaseName}-{backup.CreatedAt:yyyyMMdd-HHmmss}.nvlt");
    }

    [HttpPost("{projectId}/backups/{backupId}/restore")]
    public async Task<IActionResult> Restore(
        string projectId, string backupId, [FromBody] RestoreBackupRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _authority.IsOwnerAsync(projectId, userId, ct)) return Forbid();

        var backup = await Vault.FindBackupAsync(projectId, backupId, ct);
        if (backup is null) return NotFound();

        if (!string.Equals(request?.ConfirmDatabaseName?.Trim(), backup.DatabaseName, StringComparison.Ordinal))
            return BadRequest(new { error = $"Type the database name exactly ({backup.DatabaseName}) to confirm." });

        var snapshot = await _authority.GetProjectSnapshotAsync(projectId, ct);
        if (snapshot is null) return NotFound();

        var result = await Vault.RestoreProjectAsync(projectId, backup, userId, ct, request?.Tables);
        return result.Ok
            ? Ok(new { restored = true, preRestoreBackupId = result.BackupId })
            : BadRequest(new { error = result.Error });
    }

    [HttpDelete("{projectId}/backups/{backupId}")]
    public async Task<IActionResult> Delete(string projectId, string backupId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _authority.CanManageMembersAsync(projectId, userId, ct)) return Forbid();

        var backup = await Vault.FindBackupAsync(projectId, backupId, ct);
        if (backup is null) return NotFound();

        await Vault.DeleteAsync(backup, ct);
        return NoContent();
    }

    [HttpPost("{projectId}/backups/{backupId}/verify")]
    public async Task<IActionResult> Verify(string projectId, string backupId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _authority.CanEditAsync(projectId, userId, ct)) return Forbid();

        var backup = await Vault.FindBackupAsync(projectId, backupId, ct);
        if (backup is null) return NotFound();

        var result = await Vault.VerifyAsync(backup, ct);
        return result.Ok
            ? Ok(new { verified = true, verifiedAt = backup.VerifiedAt })
            : BadRequest(new { error = result.Error });
    }

    [HttpGet("{projectId}/schedule")]
    public async Task<IActionResult> GetSchedule(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _authority.CanViewAsync(projectId, userId, ct)) return Forbid();

        var schedule = await Vault.GetScheduleAsync(projectId, ct);

        return Ok(new
        {
            enabled = schedule?.Enabled ?? false,
            cadence = (schedule?.Cadence ?? VaultCadence.Daily).ToString(),
            hourUtc = schedule?.HourUtc ?? 3,
            dayOfWeek = schedule?.DayOfWeek,
            retainCount = schedule?.RetainCount ?? 7,
            lastRunAt = schedule?.LastRunAt,
        });
    }

    [HttpPut("{projectId}/schedule")]
    public async Task<IActionResult> SaveSchedule(
        string projectId, [FromBody] SaveScheduleRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _authority.CanManageMembersAsync(projectId, userId, ct)) return Forbid();

        if (!Enum.TryParse<VaultCadence>(request.Cadence, ignoreCase: true, out var cadence))
            return BadRequest(new { error = "Cadence must be 'Daily' or 'Weekly'." });

        if (request.HourUtc is < 0 or > 23)
            return BadRequest(new { error = "HourUtc must be between 0 and 23." });

        if (cadence == VaultCadence.Weekly && request.DayOfWeek is not (>= 0 and <= 6))
            return BadRequest(new { error = "A weekly schedule needs DayOfWeek between 0 (Sunday) and 6." });

        if (request.RetainCount is < 1 or > 60)
            return BadRequest(new { error = "RetainCount must be between 1 and 60." });

        var snapshot = await _authority.GetProjectSnapshotAsync(projectId, ct);
        if (snapshot is null) return NotFound();

        var schedule = await Vault.SaveScheduleAsync(
            projectId,
            request.Enabled,
            cadence,
            request.HourUtc,
            request.DayOfWeek,
            request.RetainCount,
            ct);

        return Ok(new
        {
            enabled = schedule.Enabled,
            cadence = schedule.Cadence.ToString(),
            hourUtc = schedule.HourUtc,
            dayOfWeek = schedule.DayOfWeek,
            retainCount = schedule.RetainCount,
            lastRunAt = schedule.LastRunAt,
        });
    }
}
