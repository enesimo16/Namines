using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;

namespace Namines.Infrastructure.Data;

/// <summary>
/// Proje erişim/yetki kontrolünün TEK kopyası (05 §6 RBAC).
///
/// Önceden her endpoint `p.UserId == userId` yazıyordu — 6 controller × N endpoint.
/// Bu hem 2-kişi onay kuralını imkânsız kılıyordu (projeye ikinci kullanıcı
/// eklenemiyordu) hem de aynı mantığın kopyalanması demekti; SSRF regex'i ve branch
/// find-or-create'te tam olarak bu kopyalama bize hata olarak geri dönmüştü.
/// </summary>
public static class OrgAccess
{
    /// <summary>
    /// Kullanıcının projedeki rolünü döndürür; erişimi yoksa null.
    ///
    /// Geriye uyumluluk: <see cref="CloudProject.OrganizationId"/> boş olan (henüz
    /// org'a taşınmamış) eski satırlarda `UserId` sahipliğine düşülür ve Owner sayılır.
    /// Migration bunları taşıdığı için bu yol pratikte ölü, ama veri kaybı riskine
    /// karşı sessizce erişimi kesmiyoruz.
    /// </summary>
    public static async Task<OrgRole?> GetRoleAsync(
        this AuthDbContext context, string projectId, string userId, CancellationToken ct = default)
    {
        var project = await context.CloudProjects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.Id, p.UserId, p.OrganizationId })
            .FirstOrDefaultAsync(ct);

        if (project is null) return null;

        if (string.IsNullOrEmpty(project.OrganizationId))
            return project.UserId == userId ? OrgRole.Owner : null;

        var membership = await context.OrganizationMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.OrganizationId == project.OrganizationId && m.UserId == userId, ct);

        return membership?.Role;
    }

    /// <summary>Okuma yetkisi — Viewer dahil her üye.</summary>
    public static async Task<bool> CanViewAsync(
        this AuthDbContext context, string projectId, string userId, CancellationToken ct = default)
        => await context.GetRoleAsync(projectId, userId, ct) is not null;

    /// <summary>
    /// Yazma/oylama yetkisi — Editor ve üstü.
    /// 05 §6: `editor` şema düzenler ve "PR açabilir"; `viewer` yalnızca okur/yorumlar.
    /// `billing` sıralı bir seviye DEĞİL (yalnızca faturalama), bu yüzden açıkça dışlanır.
    /// </summary>
    public static async Task<bool> CanEditAsync(
        this AuthDbContext context, string projectId, string userId, CancellationToken ct = default)
    {
        var role = await context.GetRoleAsync(projectId, userId, ct);
        return role is OrgRole.Editor or OrgRole.Admin or OrgRole.Owner;
    }

    /// <summary>
    /// Projede OY VEREBİLECEK üye sayısı (Editor/Admin/Owner). Onay kuralının ekip
    /// büyüklüğüne uyarlanması için gerekli — bkz. ChangeRequestApprovalPolicy
    /// .EffectiveRequiredApprovals. Viewer ve Billing sayılmaz: oy veremezler,
    /// onları saymak "2 kişilik ekip" sanılmasına ve kuralın yanlış gevşemesine yol açardı.
    /// </summary>
    public static async Task<int> CountVotingMembersAsync(
        this AuthDbContext context, string projectId, CancellationToken ct = default)
    {
        var orgId = await context.CloudProjects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => p.OrganizationId)
            .FirstOrDefaultAsync(ct);

        // Org'a taşınmamış eski satır — yalnızca oluşturan var sayılır.
        if (string.IsNullOrEmpty(orgId)) return 1;

        return await context.OrganizationMembers
            .CountAsync(m => m.OrganizationId == orgId &&
                             (m.Role == OrgRole.Editor || m.Role == OrgRole.Admin || m.Role == OrgRole.Owner), ct);
    }

    /// <summary>Üye yönetimi — Admin ve üstü (05 §6 "Üye yönet" sütunu).</summary>
    public static async Task<bool> CanManageMembersAsync(
        this AuthDbContext context, string projectId, string userId, CancellationToken ct = default)
    {
        var role = await context.GetRoleAsync(projectId, userId, ct);
        return role is OrgRole.Admin or OrgRole.Owner;
    }

    /// <summary>
    /// Kullanıcının kişisel organizasyonunu bul-yoksa-oluştur. Kayıt sırasında ve
    /// eski kullanıcılar için tembel (lazy) çağrılır.
    /// BranchProvisioning ile aynı yarış-güvenli desen.
    /// </summary>
    public static async Task<Organization> GetOrCreatePersonalOrgAsync(
        this AuthDbContext context, string userId, string displayName, CancellationToken ct = default)
    {
        var existing = await context.Organizations
            .FirstOrDefaultAsync(o => o.IsPersonal && o.CreatedByUserId == userId, ct);
        if (existing is not null) return existing;

        var org = new Organization
        {
            Name = string.IsNullOrWhiteSpace(displayName) ? "Personal" : $"{displayName}'s workspace",
            IsPersonal = true,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        await context.Organizations.AddAsync(org, ct);
        await context.OrganizationMembers.AddAsync(new OrganizationMember
        {
            OrganizationId = org.Id,
            UserId = userId,
            Role = OrgRole.Owner
        }, ct);

        try
        {
            await context.SaveChangesAsync(ct);
            return org;
        }
        catch (DbUpdateException)
        {
            context.Entry(org).State = EntityState.Detached;
            var winner = await context.Organizations
                .FirstOrDefaultAsync(o => o.IsPersonal && o.CreatedByUserId == userId, ct);
            if (winner is null) throw;
            return winner;
        }
    }
}
