using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;

namespace Namines.Infrastructure.Services;

/// <summary>Proje silmenin sonucu. <see cref="StatusCode"/> HTTP karşılığı (200 = silindi).</summary>
public sealed record ProjectDeletionResult(bool Ok, int StatusCode, string? Code = null, string? Error = null)
{
    public static ProjectDeletionResult Success() => new(true, 200);
}

/// <summary>
/// Bir projeyi, dış kaynaklarıyla birlikte tutarlı biçimde siler (DB-10, DB-01/02).
///
/// <b>Neden tek servis:</b> yedekler ve yönetilen veritabanları projeye <c>Restrict</c> FK ile
/// bağlı (cascade ile sessizce silinirse blob ve sağlayıcı kaynağı yetim kalırdı: bedava
/// saklanan şifreli dump + faturalanan veritabanı). Hub'daki silme ucu ve hesap silme akışı
/// aynı sırayı izlemeli; iki kopya zamanla ayrışırdı.
///
/// <b>Sıra bilinçli:</b> (1) kalıcı denetim kaydı — işlem yarıda kesilse bile "silinmek
/// istendi" izi kalsın; (2) Vault blob'ları; (3) Ground sağlayıcı kaynağı; (4) ancak
/// bunlar BAŞARILIYSA projenin kendisi (kalan her şey cascade). Dış adımlardan biri
/// başarısız olursa proje yerinde kalır ve işlem güvenle yeniden denenebilir (her adım idempotent).
///
/// Yetki denetimi ÇAĞIRANIN işi (Hub'da Owner, hesap silmede sahiplik).
/// </summary>
public class ProjectDeletionService
{
    private readonly AuthDbContext _context;
    private readonly VaultService _vault;
    private readonly GroundService _ground;
    private readonly ILogger<ProjectDeletionService> _logger;

    public ProjectDeletionService(
        AuthDbContext context, VaultService vault, GroundService ground, ILogger<ProjectDeletionService> logger)
    {
        _context = context;
        _vault = vault;
        _ground = ground;
        _logger = logger;
    }

    public async Task<ProjectDeletionResult> DeleteAsync(
        CloudProject project, string? actorUserId, CancellationToken ct, bool recordName = true)
    {
        var projectId = project.Id;

        var counts = new
        {
            backups = await _context.VaultBackups.CountAsync(b => b.ProjectId == projectId, ct),
            groundDatabases = await _context.GroundDatabases.CountAsync(g => g.ProjectId == projectId && g.Status != GroundStatus.Deleted, ct),
            branches = await _context.Branches.CountAsync(b => b.ProjectId == projectId, ct),
            schemaVersions = await _context.SchemaVersions.CountAsync(v => v.ProjectId == projectId, ct),
            changeRequests = await _context.ChangeRequests.CountAsync(c => c.ProjectId == projectId, ct),
            crossDatabaseRelations = await _context.CrossDatabaseRelations.CountAsync(
                r => r.SourceProjectId == projectId || r.TargetProjectId == projectId, ct),
        };

        // (b) Kalıcı denetim kaydı — dış kaynaklara dokunmadan ÖNCE.
        await WriteAuditAsync(ChangeRequestAuditAction.ProjectDeletionRequested, project, actorUserId, counts, null, recordName, ct);

        // (c) Vault
        var vault = await _vault.PurgeProjectBackupsAsync(projectId, ct);
        if (!vault.Ok)
        {
            await WriteAuditAsync(ChangeRequestAuditAction.ProjectDeletionFailed, project, actorUserId, counts, "vault", recordName, ct);
            // Çalışan iş = çakışma (409); depo hatası = geçici altyapı sorunu (503).
            return vault.Error!.Contains("still running", StringComparison.Ordinal)
                ? new ProjectDeletionResult(false, 409, "BACKUP_IN_PROGRESS", vault.Error)
                : new ProjectDeletionResult(false, 503, "BACKUP_STORAGE_FAILED", vault.Error);
        }

        // (d) Ground
        var ground = await _context.GroundDatabases.Where(g => g.ProjectId == projectId).ToListAsync(ct);
        foreach (var record in ground)
        {
            var result = await _ground.DeprovisionNowAsync(record, ct);
            if (!result.Ok)
            {
                await WriteAuditAsync(ChangeRequestAuditAction.ProjectDeletionFailed, project, actorUserId, counts, "ground", recordName, ct);
                var busy = result.Error!.Contains("right now", StringComparison.Ordinal)
                           || result.Error.Contains("changed while", StringComparison.Ordinal);
                return new ProjectDeletionResult(false, busy ? 409 : 503,
                    busy ? "GROUND_BUSY" : "GROUND_DEPROVISION_FAILED", result.Error);
            }
        }

        // (e) Satırlar: Restrict FK'li kayıtlar elle, geri kalanı cascade.
        _context.GroundDatabases.RemoveRange(ground);

        var crossRels = await _context.CrossDatabaseRelations
            .Where(r => r.SourceProjectId == projectId || r.TargetProjectId == projectId)
            .ToListAsync(ct);
        _context.CrossDatabaseRelations.RemoveRange(crossRels);

        // "Silindi" denetim satırı silmeyle AYNI işlemde: biri olmadan diğeri olmaz.
        _context.CloudProjects.Remove(project);
        AddAudit(ChangeRequestAuditAction.ProjectDeleted, project, actorUserId, counts, null, recordName);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Proje silindi: {ProjectId} (aktör {Actor}).", projectId, actorUserId);
        return ProjectDeletionResult.Success();
    }

    /// <summary>
    /// Denetim satırı. <c>ChangeRequestId</c> sütununa PROJE kimliği yazılıyor (bu tablo CR'a özel
    /// bir FK değil, tarihsel referans); <c>Details</c> yalnızca sayaç içerir.
    /// <paramref name="recordName"/> false ise (hesap silme) ad ve aktör de yazılmaz: denetim izi
    /// silinen kişiye geri götüren bilgi taşımamalı.
    /// </summary>
    private void AddAudit(
        ChangeRequestAuditAction action, CloudProject project, string? actorUserId, object counts, string? failedStep, bool recordName)
    {
        _context.ChangeRequestAuditLogs.Add(new ChangeRequestAuditLog
        {
            ChangeRequestId = project.Id,
            ProjectId = project.Id,
            ChangeRequestTitle = recordName ? project.Name : null,
            Action = action,
            ActorUserId = recordName ? actorUserId : null,
            Details = JsonSerializer.Serialize(new { counts, failedStep }),
        });
    }

    private async Task WriteAuditAsync(
        ChangeRequestAuditAction action, CloudProject project, string? actorUserId, object counts,
        string? failedStep, bool recordName, CancellationToken ct)
    {
        AddAudit(action, project, actorUserId, counts, failedStep, recordName);
        // Silme başarısız olursa denetim satırı yine de kalmalı: ayrı kaydediliyor.
        await _context.SaveChangesAsync(ct);
    }
}
