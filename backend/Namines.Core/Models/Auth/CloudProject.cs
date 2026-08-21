using System;
using System.ComponentModel.DataAnnotations;

namespace Namines.Core.Models.Auth
{
    public class CloudProject
    {
        [Key]
        public string Id { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string DbType { get; set; } = null!;
        public string SchemaJson { get; set; } = null!;       // Serialized DatabaseSchema JSON
        public string NodePositionsJson { get; set; } = null!;  // Serialized positions JSON
        
        /// <summary>Projeyi OLUŞTURAN kullanıcı. Yetki sınırı artık burası DEĞİL —
        /// <see cref="OrganizationId"/> üzerinden üyelik bakılır (bkz. 05 §6).
        /// Geriye uyumluluk ve "oluşturan kim" bilgisi için korunuyor.</summary>
        public string UserId { get; set; } = null!;
        public ApplicationUser User { get; set; } = null!;

        /// <summary>
        /// Yetki sınırı. Migration'da mevcut projeler sahiplerinin kişisel org'una
        /// taşındığı için pratikte her zaman dolu; eski satırların taşınmadan önceki
        /// hâline tolerans olsun diye tipte nullable bırakıldı.
        /// </summary>
        public string? OrganizationId { get; set; }
        public Organization? Organization { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Null ise proje özel, dolu ise bu token aracılığıyla herkese salt-okunur paylaşılmış.
        /// </summary>
        public string? ShareToken { get; set; }

        /// <summary>
        /// G16 — new-phase/29-DATABASE-CHANGE-REVIEW.md §3: "Safe | Otomatik onaylanabilir
        /// (opt-in ayar)". Varsayılan false — kullanıcı bilerek açmadıkça her değişiklik
        /// (Safe dahil) insan onayından geçer.
        /// </summary>
        public bool AutoApproveSafeChanges { get; set; } = false;
    }
}
