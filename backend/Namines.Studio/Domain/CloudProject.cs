using System;
using System.ComponentModel.DataAnnotations;

namespace Namines.Studio.Domain
{
    /// <summary>
    /// <c>studio.projects</c>: yalnızca şema tasarımının çekirdeği. Canlı bağlantı
    /// <see cref="ProjectConnection"/>'a, paylaşım <see cref="ProjectShare"/>'e, Hub vitrini
    /// <see cref="ProjectRepository"/> ve <see cref="ProjectTag"/>'e taşındı (Faz 14.2B).
    /// </summary>
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

        /// <summary>
        /// Yetki sınırı. Migration'da mevcut projeler sahiplerinin kişisel org'una
        /// taşındığı için pratikte her zaman dolu; eski satırların taşınmadan önceki
        /// hâline tolerans olsun diye tipte nullable bırakıldı.
        /// </summary>
        public string? OrganizationId { get; set; }

        /// <summary>
        /// Optimistic concurrency belirteci (REL-003a / B-32).
        ///
        /// <b>Çözdüğü sorun:</b> İki kullanıcı aynı projeyi aynı anda
        /// düzenlediğinde son yazan kazanıyor ve ilk kullanıcının çalışması
        /// SESSİZCE kayboluyordu. Ürün ekip çalışması iddiasında
        /// (Organization, roller, change request) olduğu için bu gerçekçi.
        ///
        /// <b>Neden yalnızca kolon eklemek YETMEZ:</b> EF'in `[Timestamp]`
        /// kontrolü, güncellenen varlığın YÜKLENDİĞİ andaki değeri kullanır.
        /// Sunucu satırı isteğin başında okuduğu için o değer her zaman
        /// güncel olur ve `WHERE RowVersion = …` koşulu HER ZAMAN eşleşir —
        /// yani hiçbir çakışma yakalanmaz. Koruma, İSTEMCİNİN en son gördüğü
        /// sürümü GERİ GÖNDERMESİYLE oluşur (bkz. <c>SyncProjectDto.RowVersion</c>).
        ///
        /// PostgreSQL'de <c>xmin</c> sistem kolonuna eşleniyor: ayrı bir kolon
        /// eklemiyor, mevcut satırlar için de anında çalışıyor.
        /// </summary>
        [Timestamp]
        public uint RowVersion { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// G16 — new-phase/29-DATABASE-CHANGE-REVIEW.md §3: "Safe | Otomatik onaylanabilir
        /// (opt-in ayar)". Varsayılan false — kullanıcı bilerek açmadıkça her değişiklik
        /// (Safe dahil) insan onayından geçer.
        /// </summary>
        public bool AutoApproveSafeChanges { get; set; } = false;
    }
}
