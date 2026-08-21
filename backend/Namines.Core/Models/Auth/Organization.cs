using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Namines.Core.Models.Auth
{
    /// <summary>
    /// new-phase/05-CONTROL-PLANE.md §6 RBAC tablosu.
    ///
    /// Kapsam notu: doc'ta `billing` rolü de var ama faturalama henüz yok
    /// (Stripe yapılandırılmamış) — rol enum'una eklendi, hiçbir yerde
    /// yetki vermiyor. Var olmayan bir yetkiyi taklit etmemek için.
    /// </summary>
    public enum OrgRole
    {
        Viewer = 0,   // salt-okunur
        Editor = 1,   // şema düzenler, change request açar/oylar
        Admin = 2,    // + üye yönetir
        Owner = 3,    // + faturalama, org silme
        Billing = 4   // yalnızca faturalama (yetki tablosunda ayrık — sıralamaya dahil DEĞİL)
    }

    /// <summary>
    /// Projelerin sahiplik ve yetki sınırı. Önceden `CloudProject.UserId` tek
    /// sahipti; bu yüzden new-phase/29 §3'teki "Destructive/Breaking → 2 FARKLI
    /// kişi onaylamalı" kuralı uygulanamıyordu (bkz. CHECKLIST G18) — projeye
    /// ikinci bir kullanıcı eklemenin hiçbir yolu yoktu.
    /// </summary>
    public class Organization
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = null!;

        /// <summary>Her kullanıcı kaydolduğunda kendi kişisel org'u açılır; bu bayrak
        /// onu işaretler (UI "organizasyonu sil" gibi seçenekleri gizleyebilsin diye).</summary>
        public bool IsPersonal { get; set; }

        public string CreatedByUserId { get; set; } = null!;
        public ApplicationUser CreatedByUser { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public List<OrganizationMember> Members { get; set; } = new();
    }

    /// <summary>18-CONTROL-PLANE-DDL.md'deki `org_members` — bileşik PK (org, user).</summary>
    public class OrganizationMember
    {
        public string OrganizationId { get; set; } = null!;
        public Organization Organization { get; set; } = null!;

        public string UserId { get; set; } = null!;
        public ApplicationUser User { get; set; } = null!;

        public OrgRole Role { get; set; } = OrgRole.Editor;

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    }
}
