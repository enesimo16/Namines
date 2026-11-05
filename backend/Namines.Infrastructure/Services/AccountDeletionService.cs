using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;

namespace Namines.Infrastructure.Services;

/// <summary>Stripe aboneliğini iptal eder. API katmanı gerçek Stripe SDK ile uygular; burada soyut ki servis test edilebilsin.</summary>
public interface ISubscriptionCanceller
{
    /// <summary>İptal edildiyse (ya da zaten iptalse) true; yapılandırma yok/çağrı başarısızsa false (elle iptal gerekir).</summary>
    Task<bool> CancelAsync(string subscriptionId, CancellationToken ct);
}

public sealed record BlockingOrganization(string Id, string Name);

public sealed record AccountDeletionResult(
    bool Ok, int StatusCode, string? Code = null, string? Error = null,
    IReadOnlyList<BlockingOrganization>? Organizations = null)
{
    public static AccountDeletionResult Success() => new(true, 200);
}

/// <summary>
/// Hesap silme (KVKK/GDPR silme hakkı) — DB-08.
///
/// <b>Neden elle sıra gerekiyor:</b> <c>CloudProjects.UserId</c> FK'si CASCADE. Kullanıcıyı doğrudan
/// silmek, o kişinin oluşturduğu EKİP projelerini de (yedek/Ground yetimi bırakarak, diğer üyelerin
/// verisiyle birlikte) silerdi. Bu yüzden: ekip projeleri önce bir org Owner'ına devredilir, yalnızca
/// kişinin kendine ait projeler <see cref="ProjectDeletionService"/> ile silinir.
///
/// <b>Kurallar:</b>
/// <list type="bullet">
/// <item>Başka üyeleri olan bir org'un TEK Owner'ı ise silme reddedilir (409) — önce sahiplik devredilmeli.</item>
/// <item>Tek üyesi kendisi olan org'lar (kişisel org dahil) projeleriyle birlikte silinir.</item>
/// <item>Diğer org'larda üyelik düşer, kişinin oluşturduğu projeler bir Owner'a devredilir.</item>
/// <item>Stripe aboneliği iptal edilir; olmazsa denetim kaydına "elle iptal gerekli" yazılır, işlem BLOKLANMAZ.</item>
/// <item>Kişinin oluşturduğu Gateway API anahtarları iptal edilir; kalan satırlarda kişisel veri anonimleştirilir.</item>
/// <item>Denetim kaydı kullanıcı kimliği dışında kişisel veri taşımaz.</item>
/// </list>
///
/// Yeniden-doğrulama (parola/2FA/taze oturum) ÇAĞIRANIN işi.
/// </summary>
public class AccountDeletionService
{
    private readonly AuthDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ProjectDeletionService _projects;
    private readonly IHubApplicationService _hub;
    private readonly ICrossModuleTransaction _transaction;
    private readonly ISubscriptionCanceller? _stripe;
    private readonly ILogger<AccountDeletionService> _logger;

    public AccountDeletionService(
        AuthDbContext context,
        UserManager<ApplicationUser> userManager,
        ProjectDeletionService projects,
        IHubApplicationService hub,
        ICrossModuleTransaction transaction,
        ILogger<AccountDeletionService> logger,
        ISubscriptionCanceller? stripe = null)
    {
        _context = context;
        _userManager = userManager;
        _projects = projects;
        _hub = hub;
        _transaction = transaction;
        _logger = logger;
        _stripe = stripe;
    }

    public async Task<AccountDeletionResult> DeleteAsync(ApplicationUser user, CancellationToken ct)
    {
        var userId = user.Id;

        // ── 1. Plan (hiçbir şeye dokunmadan) ─────────────────────────────────
        var memberships = await _context.OrganizationMembers
            .Include(m => m.Organization)
            .Where(m => m.UserId == userId)
            .ToListAsync(ct);

        var blocked = new List<BlockingOrganization>();
        var orgsToDelete = new HashSet<string>();
        var leftOrgs = 0;
        // Org -> projelerin devredileceği kişi (kullanıcı HARİÇ; Owner öncelikli).
        var heirByOrg = new Dictionary<string, string?>();

        foreach (var m in memberships)
        {
            var others = await _context.OrganizationMembers
                .Where(x => x.OrganizationId == m.OrganizationId && x.UserId != userId)
                .OrderBy(x => x.JoinedAt)
                .Select(x => new { x.UserId, x.Role })
                .ToListAsync(ct);

            if (others.Count == 0)
            {
                orgsToDelete.Add(m.OrganizationId);
                continue;
            }

            var heir = others.FirstOrDefault(x => x.Role == OrgRole.Owner)
                       ?? others.FirstOrDefault(x => x.Role == OrgRole.Admin)
                       ?? others[0];

            if (m.Role == OrgRole.Owner && !others.Any(x => x.Role == OrgRole.Owner))
            {
                blocked.Add(new BlockingOrganization(m.OrganizationId, m.Organization.Name));
                continue;
            }

            heirByOrg[m.OrganizationId] = heir.UserId;
            leftOrgs++;
        }

        if (blocked.Count > 0)
        {
            return new AccountDeletionResult(false, 409, "TRANSFER_OWNERSHIP_REQUIRED",
                "You are the only owner of an organization that has other members. Transfer ownership (or remove the other members) first.",
                blocked);
        }

        // Kullanıcının oluşturduğu ama ÜYESİ olmadığı org'lardaki projeler için de varis lazım
        // (üyelikten çıkarılmış kurucu): CASCADE onları sessizce silerdi.
        var created = await _context.CloudProjects
            .Where(p => p.UserId == userId)
            .Select(p => new { p.Id, p.OrganizationId })
            .ToListAsync(ct);

        var deleteIds = new HashSet<string>();
        var reassign = new List<(string ProjectId, string NewOwner)>();

        foreach (var p in created)
        {
            if (p.OrganizationId is null || orgsToDelete.Contains(p.OrganizationId))
            {
                deleteIds.Add(p.Id);
                continue;
            }

            if (!heirByOrg.TryGetValue(p.OrganizationId, out var heirId))
            {
                heirId = await _context.OrganizationMembers
                    .Where(x => x.OrganizationId == p.OrganizationId && x.UserId != userId)
                    .OrderByDescending(x => x.Role == OrgRole.Owner)
                    .ThenBy(x => x.JoinedAt)
                    .Select(x => x.UserId)
                    .FirstOrDefaultAsync(ct);
                heirByOrg[p.OrganizationId] = heirId;
            }

            if (heirId is null) deleteIds.Add(p.Id); // sahipsiz org: devredilecek kimse yok
            else reassign.Add((p.Id, heirId));
        }

        // Silinecek org'ların TÜM projeleri (başkası oluşturmuş olsa da): org silinince cascade ile
        // yedek/Ground temizliği atlanmasın.
        if (orgsToDelete.Count > 0)
        {
            var orgProjectIds = await _context.CloudProjects
                .Where(p => p.OrganizationId != null && orgsToDelete.Contains(p.OrganizationId))
                .Select(p => p.Id)
                .ToListAsync(ct);
            foreach (var id in orgProjectIds) deleteIds.Add(id);
        }

        // Kullanıcının KURDUĞU ve kalan org'larda "kurucu" kaydı bir Owner'a devredilir: ekip planı
        // kurucunun hesabından okunuyor (TeamController.OrgTierAsync); FK SET NULL ile boşalsaydı
        // org sessizce Free'ye düşerdi.
        var orgHeirs = new List<(string OrgId, string NewCreator)>();
        var createdOrgIds = await _context.Organizations
            .Where(o => o.CreatedByUserId == userId && !orgsToDelete.Contains(o.Id))
            .Select(o => o.Id)
            .ToListAsync(ct);
        foreach (var orgId in createdOrgIds)
        {
            if (!heirByOrg.TryGetValue(orgId, out var heirId))
            {
                heirId = await _context.OrganizationMembers
                    .Where(x => x.OrganizationId == orgId && x.UserId != userId)
                    .OrderByDescending(x => x.Role == OrgRole.Owner)
                    .ThenBy(x => x.JoinedAt)
                    .Select(x => x.UserId)
                    .FirstOrDefaultAsync(ct);
            }
            if (heirId is not null) orgHeirs.Add((orgId, heirId));
        }

        // ── 2. Projeler (dış kaynaklar dahil) ────────────────────────────────
        foreach (var projectId in deleteIds)
        {
            var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
            if (project is null) continue;

            // Denetim izinde ad/aktör YOK: silinen kişiye geri götürmesin.
            var result = await _projects.DeleteAsync(project, actorUserId: null, ct, recordName: false);
            if (!result.Ok)
            {
                _logger.LogWarning("Hesap silme durdu: proje {ProjectId} silinemedi ({Code}).", projectId, result.Code);
                return new AccountDeletionResult(false, result.StatusCode, result.Code,
                    "Your account was not deleted because one of your projects could not be removed: " + result.Error);
            }
        }

        // ── 3. Kalan DB işleri + kullanıcı + denetim: TEK işlem ──────────────
        // Stripe çağrısı bu işlemden SONRA: önce iptal edilseydi işlem düştüğünde hesap yerinde ama aboneliği
        // iptal edilmiş kalırdı. İptal isteği ise işlemin İÇİNDE outbox satırı olarak yazılır: süreç commit
        // ile Stripe arasında ölürse işçi tamamlar. Projeler bilinçli olarak önce: kullanıcı satırı silinirse
        // FK cascade blob/sağlayıcı temizliğini atlardı. İşlem düşerse yeniden deneme kaldığı yerden sürer.
        var hasSubscription = !string.IsNullOrWhiteSpace(user.StripeSubscriptionId);
        var subscriptionId = user.StripeSubscriptionId;
        string? auditId = null;
        string? outboxId = null;
        var revokedKeys = 0;
        // Hub yazımları (devir, yıldızlar) kullanıcı silmeyle aynı işlemde: biri geri alınırsa hepsi.
        AccountDeletionResult? failure = null;
        try
        {
            await _transaction.ExecuteAsync(async _ =>
            {

            foreach (var (projectId, newOwner) in reassign)
            {
                await _context.CloudProjects.Where(p => p.Id == projectId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.UserId, newOwner)
                        .SetProperty(p => p.UpdatedAt, DateTime.UtcNow), ct);
                var renamedSlug = await _hub.ReassignOwnerAsync(projectId, newOwner, ct);
                if (renamedSlug is not null)
                    _logger.LogWarning("Hesap silme: {ProjectId} devrinde slug çakıştı, yeni slug {Slug}.", projectId, renamedSlug);
            }

            // Kullanıcı silinince FK yıldızları cascade siler ama sayaçlar düşmezdi.
            await _hub.RemoveStarsByUserAsync(userId, ct);

            foreach (var (orgId, newCreator) in orgHeirs)
            {
                await _context.Organizations.Where(o => o.Id == orgId)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.CreatedByUserId, newCreator), ct);
            }

            if (orgsToDelete.Count > 0)
                await _context.Organizations.Where(o => orgsToDelete.Contains(o.Id)).ExecuteDeleteAsync(ct);

            var now = DateTime.UtcNow;
            revokedKeys = await _context.GatewayApiKeys
                .Where(k => k.CreatedByUserId == userId && k.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.RevokedAt, (DateTime?)now), ct);

            // Geri bildirim metni kalır (ürün geri bildirimi), kişiyi tanımlayan alanlar gider.
            await _context.Feedbacks.Where(f => f.UserId == userId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(f => f.Email, (string?)null)
                    .SetProperty(f => f.UserId, (string?)null), ct);

            // ExecuteUpdate/Delete izlenen varlıkları güncellemez: eski bir CloudProject örneği hâlâ
            // takipteyse kullanıcı silinirken EF onu (cascade) yeniden silmeye çalışıp devredilmiş
            // satırda eşzamanlılık hatası alırdı. Kullanıcıyı silmeden önce izleyiciyi temizle.
            _context.ChangeTracker.Clear();

            // Oturumlar: kullanıcı silinince SecurityStamp doğrulaması kullanıcıyı bulamaz → tüm jetonlar düşer.
            var deleted = await _userManager.DeleteAsync(user);
            if (!deleted.Succeeded)
            {
                _logger.LogError("Kullanici silinemedi: {Errors}", string.Join(", ", deleted.Errors.Select(e => e.Code)));
                failure = new AccountDeletionResult(false, 500, "ACCOUNT_DELETE_FAILED", "The account could not be deleted. Try again.");
                // İstisna işlemi geri alır; işleme açık kalıp commit edilmesin diye dönüş yeterli değil.
                throw new AccountDeletionAbortedException();
            }

            if (hasSubscription)
            {
                var outbox = new PendingSubscriptionCancellation { SubscriptionId = subscriptionId! };
                _context.PendingSubscriptionCancellations.Add(outbox);
                outboxId = outbox.Id;
            }

            var accountAudit = new ChangeRequestAuditLog
            {
                ChangeRequestId = userId,
                ProjectId = string.Empty,
                Action = ChangeRequestAuditAction.AccountDeleted,
                // "queued": iptal outbox'ta; doğruluk kaynağı PendingSubscriptionCancellations satırı.
                Details = AuditDetails(hasSubscription ? "queued" : "none"),
            };
            _context.ChangeRequestAuditLogs.Add(accountAudit);
            auditId = accountAudit.Id;
            await _context.SaveChangesAsync(ct);
            }, ct);
        }
        catch (AccountDeletionAbortedException)
        {
            return failure!;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // İşlem geri alındı, Stripe'a dokunulmadı; ama kişisel projeler bu adımdan önce silinmiş olabilir.
            // Kullanıcı bunu ve yeniden denemenin güvenli olduğunu bilmeli, genel bir 500 değil.
            _logger.LogError(ex, "Hesap silme islemi basarisiz (kullanici {UserId}); {Deleted} proje zaten silinmisti.", userId, deleteIds.Count);
            var detail = deleteIds.Count > 0
                ? "Your personal projects were already removed, but your account was not deleted. "
                : "Your account was not deleted. ";
            return new AccountDeletionResult(false, 503, "ACCOUNT_DELETE_INCOMPLETE",
                detail + "Nothing else changed and your subscription was not touched. It is safe to try again.");
        }

        // ── 4. Stripe: ilk deneme (DB dışı; başarısızlık silmeyi bloklamaz, işçi yeniden dener) ──
        if (outboxId is not null)
        {
            // CancellationToken.None: istemci kopsa da iptal ve kayıt tamamlansın. Buradaki hata yanıtı
            // bozmamalı, hesap artık yok; satır açık kaldığı için iş kaybolmaz.
            try
            {
                var processor = new SubscriptionCancellationProcessor(_context, _stripe, _logger);
                if (await processor.AttemptAsync(outboxId, DateTime.UtcNow))
                {
                    var details = AuditDetails("canceled");
                    await _context.ChangeRequestAuditLogs.Where(a => a.Id == auditId)
                        .ExecuteUpdateAsync(s => s.SetProperty(a => a.Details, details), CancellationToken.None);
                }
                else
                {
                    _logger.LogWarning("Hesap silindi, Stripe iptali kuyrukta yeniden denenecek (kayit {Id}).", outboxId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hesap silindi, Stripe ilk denemesi tamamlanamadi; isci yeniden deneyecek (kayit {Id}).", outboxId);
            }
        }

        return AccountDeletionResult.Success();

        string AuditDetails(string stripeOutcome) => JsonSerializer.Serialize(new
        {
            deletedProjects = deleteIds.Count,
            reassignedProjects = reassign.Count,
            deletedOrganizations = orgsToDelete.Count,
            leftOrganizations = leftOrgs,
            revokedApiKeys = revokedKeys,
            stripe = stripeOutcome,
        });
    }

    private sealed class AccountDeletionAbortedException : Exception
    {
    }
}
