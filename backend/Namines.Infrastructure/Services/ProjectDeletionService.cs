using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.Hub.Application;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;
using Namines.Ground.Application;
using Namines.Infrastructure.Data;
using Namines.Vault.Application;

namespace Namines.Infrastructure.Services;

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
public class ProjectDeletionService : IProjectDeletion
{
    private readonly AuthDbContext _context;
    private readonly IVaultApplicationService _vault;
    private readonly IGroundApplicationService _ground;
    private readonly IHubApplicationService _hub;
    private readonly IStudioProjects _projects;
    private readonly IStudioBranches _branches;
    private readonly IStudioRelations _relations;
    private readonly Namines.Gateway.Application.IGatewayMaintenance _gateway;
    private readonly ICrossModuleTransaction _transaction;
    private readonly ILogger<ProjectDeletionService> _logger;

    public ProjectDeletionService(
        AuthDbContext context,
        IVaultApplicationService vault,
        IGroundApplicationService ground,
        ILogger<ProjectDeletionService> logger,
        IHubApplicationService hub,
        ICrossModuleTransaction transaction,
        IStudioProjects projects,
        IStudioBranches branches,
        IStudioRelations relations,
        Namines.Gateway.Application.IGatewayMaintenance gateway)
    {
        _context = context;
        _vault = vault;
        _ground = ground;
        _logger = logger;
        _hub = hub;
        _transaction = transaction;
        _projects = projects;
        _branches = branches;
        _relations = relations;
        _gateway = gateway;
    }

    public async Task<ProjectDeletionResult> DeleteAsync(
        string projectId, string? actorUserId, CancellationToken ct, bool recordName = true)
    {
        var project = await _projects.GetHeaderAsync(projectId, ct);
        // Satır yoksa silinecek bir şey yok: yeniden deneme de başarılı sayılır.
        if (project is null) return ProjectDeletionResult.Success();

        var counts = new
        {
            backups = await _vault.CountProjectBackupsAsync(projectId, ct),
            groundDatabases = await _ground.CountActiveProjectDatabasesAsync(projectId, ct),
            branches = await _branches.CountBranchesAsync(projectId, ct),
            schemaVersions = await _branches.CountVersionsAsync(projectId, ct),
            changeRequests = await _context.ChangeRequests.CountAsync(c => c.ProjectId == projectId, ct),
            crossDatabaseRelations = await _relations.CountForProjectAsync(projectId, ct),
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

        // (d) Ground (sağlayıcı kaynaklarını siler ve Restrict FK'li ground_databases kayıtlarını kaldırır)
        var groundResult = await _ground.DeprovisionAndRemoveProjectRecordsAsync(projectId, ct);
        if (!groundResult.Ok)
        {
            await WriteAuditAsync(ChangeRequestAuditAction.ProjectDeletionFailed, project, actorUserId, counts, "ground", recordName, ct);
            var busy = groundResult.Error!.Contains("right now", StringComparison.Ordinal)
                       || groundResult.Error.Contains("changed while", StringComparison.Ordinal);
            return new ProjectDeletionResult(false, busy ? 409 : 503,
                busy ? "GROUND_BUSY" : "GROUND_DEPROVISION_FAILED", groundResult.Error);
        }

        // (e) Satırlar: ilişkiler Restrict olduğu için önce; her modül kendi satırını sözleşmesiyle siler.
        // DB'deki şemalar arası FK'ler güvence olarak duruyor: bir modül unutsa da yetim satır oluşamaz.
        // "Silindi" denetim satırı silmeyle AYNI işlemde: biri olmadan diğeri olmaz.
        AddAudit(ChangeRequestAuditAction.ProjectDeleted, project, actorUserId, counts, null, recordName);
        await _transaction.ExecuteAsync(async c =>
        {
            await _relations.RemoveForProjectAsync(projectId, c);
            await _hub.DetachFromForkSourceAsync(projectId, c);
            await _hub.PurgeProjectAsync(projectId, c);
            await _context.ChangeRequests.Where(r => r.ProjectId == projectId).ExecuteDeleteAsync(c);
            await _gateway.PurgeProjectAsync(projectId, c);
            await _projects.DeleteAsync(projectId, c);
            await _context.SaveChangesAsync(acceptAllChangesOnSuccess: false, c);
        }, ct);
        _context.ChangeTracker.AcceptAllChanges();

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
        ChangeRequestAuditAction action, StudioProjectHeader project, string? actorUserId, object counts, string? failedStep, bool recordName)
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
        ChangeRequestAuditAction action, StudioProjectHeader project, string? actorUserId, object counts,
        string? failedStep, bool recordName, CancellationToken ct)
    {
        AddAudit(action, project, actorUserId, counts, failedStep, recordName);
        // Silme başarısız olursa denetim satırı yine de kalmalı: ayrı kaydediliyor.
        await _context.SaveChangesAsync(ct);
    }
}
