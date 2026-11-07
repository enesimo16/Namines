using System;
using System.ComponentModel.DataAnnotations;

using Namines.Core.Models.Auth;

namespace Namines.Governance.Domain
{
    public class ChangeRequestAuditLog
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Tarihsel referans — bilinçli olarak FK DEĞİL: CR silinse (proje/branch silme)
        /// denetim kaydı yaşamalı. Silinmiş CR için <see cref="ProjectId"/> ve
        /// <see cref="ChangeRequestTitle"/> anlık kopyaları okunur.
        /// </summary>
        public string ChangeRequestId { get; set; } = null!;

        /// <summary>Olay anındaki proje (anlık kopya; FK yok).</summary>
        public string ProjectId { get; set; } = string.Empty;

        /// <summary>Olay anındaki CR başlığı (anlık kopya).</summary>
        public string? ChangeRequestTitle { get; set; }

        public ChangeRequestAuditAction Action { get; set; }

        /// <summary>Sistem-güdümlü olaylarda (ör. AutoApproved) null — insan aktörü yok.</summary>
        public string? ActorUserId { get; set; }

        public string? Details { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
