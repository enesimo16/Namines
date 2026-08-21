using System;
using System.ComponentModel.DataAnnotations;

namespace Namines.Core.Models.Auth
{
    /// <summary>
    /// G16 — new-phase/29-DATABASE-CHANGE-REVIEW.md §3, new-phase/18-CONTROL-PLANE-DDL.md'nin
    /// `audit_log` fikri. Bilinçli kapsam sadeleştirmesi: doc'taki `audit_log` çok-kiracılı
    /// (org_id), genel amaçlı bir tablo — bu projede henüz bir Organization kavramı yok
    /// (CloudProject doğrudan UserId'ye bağlı). Onun yerine ChangeRequest'e ÖZEL, append-only
    /// bir denetim izi: CR'ın yaşam döngüsü boyunca durum geçişlerini kim/ne zaman/neden
    /// yaptığını kaydeder. İnsan onayları zaten <see cref="ChangeRequestApproval"/>'da —
    /// bu tablo ONUN yerine değil, sistem-güdümlü olaylar (otomatik onay gibi insan
    /// onayı olmayan olaylar) için de aynı zaman çizelgesinde görünür olsun diye var.
    /// </summary>
    public enum ChangeRequestAuditAction
    {
        Created,
        AutoApproved,
        Approved,
        Rejected
    }

    public class ChangeRequestAuditLog
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string ChangeRequestId { get; set; } = null!;
        public ChangeRequest ChangeRequest { get; set; } = null!;

        public ChangeRequestAuditAction Action { get; set; }

        /// <summary>Sistem-güdümlü olaylarda (ör. AutoApproved) null — insan aktörü yok.</summary>
        public string? ActorUserId { get; set; }
        public ApplicationUser? ActorUser { get; set; }

        public string? Details { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
