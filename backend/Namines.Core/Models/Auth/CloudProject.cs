using System;
using System.Text.Json.Serialization;
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
        
        /// <summary>
        /// Projenin CANLI veritabanı bağlantısı — <b>şifreli</b>
        /// (bkz. <c>IConnectionSecretProtector</c>). Namines Desk için eklendi:
        /// barındırılan panel, bağlantıyı sunucuda çözer, tarayıcı hiç görmez.
        ///
        /// <b>Nullable ve varsayılan null:</b> mevcut projelerin hiçbirinde canlı
        /// bağlantı yok ve olması da gerekmiyor — tasarım düzlemi bağlantısız
        /// çalışmaya devam eder. Yalnızca kullanıcı açıkça bir veritabanı
        /// bağladığında dolar.
        ///
        /// <b>Buraya asla düz metin yazılmaz.</b> Yazan tek yer
        /// <c>GatewayKeyController.SetProjectConnection</c>.
        /// </summary>
        /// <remarks>
        /// <b>[JsonIgnore] — bu alan hiçbir API yanıtında yer almamalı.</b>
        /// Bugün her uç yanıtını elle projekte ediyor (anonim nesne), yani
        /// sızıntı yok. Ama bu, her yazan kişinin hatırlamasına bağlı bir
        /// güvence: tek bir <c>return Ok(project)</c> yeter. Öznitelik, kazayı
        /// dikkate bırakmak yerine serileştirici seviyesinde imkânsız kılıyor.
        /// </remarks>
        [JsonIgnore]
        public string? EncryptedConnectionString { get; set; }

        /// <summary>
        /// <see cref="EncryptedConnectionString"/> hangi motora ait.
        /// <see cref="DbType"/>'dan AYRI: o, şemanın DERLENDİĞİ hedef motor
        /// (kullanıcı canvas'ta seçer). Bu ise gerçekten BAĞLANILAN motor.
        /// İkisi farklı olabilir — kullanıcı PostgreSQL'e bağlıyken şemayı
        /// MSSQL için derliyor olabilir.
        /// </summary>
        public string? ConnectionDbType { get; set; }

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
        /// Null ise proje özel, dolu ise bu token aracılığıyla herkese salt-okunur paylaşılmış.
        /// </summary>
        public string? ShareToken { get; set; }

        /// <summary>
        /// G16 — new-phase/29-DATABASE-CHANGE-REVIEW.md §3: "Safe | Otomatik onaylanabilir
        /// (opt-in ayar)". Varsayılan false — kullanıcı bilerek açmadıkça her değişiklik
        /// (Safe dahil) insan onayından geçer.
        /// </summary>
        public bool AutoApproveSafeChanges { get; set; } = false;

        /// <summary>
        /// Namines Desk v2 §E4.2 — Desk'in salt-okunur SQL konsolu bu proje için açık mı.
        /// Varsayılan false: bir kişi normal <see cref="OrgRole.Owner"/> olsa bile SQL
        /// konsolu SESSİZCE açık olmaz — proje sahibinin AYRICA, açıkça bir kez daha
        /// onaylaması gerekir (<c>AutoApproveSafeChanges</c> ile aynı opt-in deseni).
        /// Yalnızca Owner rolündeki kullanıcı bu bayrağı değiştirebilir
        /// (bkz. GatewayKeyController.SetDeskSqlEnabled) ve yalnızca Owner rolündeki
        /// kullanıcı bu bayrak açıkken SQL çalıştırabilir — iki ayrı kontrol.
        /// </summary>
        public bool AllowDeskSql { get; set; } = false;
    }
}
