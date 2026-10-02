using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Namines.Core.Enums;

namespace Namines.Core.Models.Auth
{
    /// <summary>
    /// "Database PR" — new-phase/29-DATABASE-CHANGE-REVIEW.md. İki <see cref="SchemaVersion"/>
    /// arasındaki <see cref="Analysis.SchemaImpactAnalyzer"/> raporunu, insan onayı gerektiren
    /// kalıcı bir kayda dönüştürür. G10'un (Branch/SchemaVersion) üzerine oturur.
    ///
    /// Kapsam notu: doc'taki tam yaşam döngüsünün (draft/analyzing/applying/applied/failed)
    /// yalnızca review kısmı burada — apply pipeline'ı yok (bkz. ChangeRequestStatus).
    /// </summary>
    public class ChangeRequest
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string ProjectId { get; set; } = null!;
        public CloudProject Project { get; set; } = null!;

        public string BranchId { get; set; } = null!;
        public Branch Branch { get; set; } = null!;

        /// <summary>Null ise "boş şemadan" karşılaştırılıyor demektir (branch'in ilk versiyonu).</summary>
        public string? BaseVersionId { get; set; }
        public SchemaVersion? BaseVersion { get; set; }

        public string HeadVersionId { get; set; } = null!;
        public SchemaVersion HeadVersion { get; set; } = null!;

        public string? Title { get; set; }

        public ChangeRequestStatus Status { get; set; } = ChangeRequestStatus.PendingReview;

        /// <summary><see cref="Models.ImpactReport.OverallRisk"/> ile aynı değer — onay
        /// gereksinimini hesaplamak için ayrı bir sorgu/deserileştirme gerektirmesin diye kopyalandı.</summary>
        public RiskLevel RiskLevel { get; set; }

        /// <summary>Oluşturma anında hesaplanmış <see cref="Models.ImpactReport"/>'un JSON hâli —
        /// deterministik olduğu için tekrar hesaplamaya gerek yok, ama drift'e karşı saklanır.</summary>
        public string ImpactReportJson { get; set; } = null!;

        public string CreatedByUserId { get; set; } = null!;
        public ApplicationUser CreatedByUser { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ResolvedAt { get; set; }

        /// <summary>
        /// CR AÇILDIĞI ANDA oy verebilen üye sayısı (Editor/Admin/Owner).
        ///
        /// <b>Bulunan açık (docs/last-review M-1):</b> gereken onay sayısı her oyda
        /// CANLI ekip büyüklüğünden yeniden hesaplanıyordu
        /// (<c>CountVotingMembersAsync</c>). Ekip üyesi çıkarma yetkisi olan bir Admin,
        /// kendi Destructive/Breaking CR'ını açtıktan sonra ekibi 1 kişiye
        /// küçültüp — <c>EffectiveRequiredApprovals</c> o durumda 1 onaya ve
        /// <c>RequiresDistinctFromAuthor</c> <c>false</c>'a düştüğü için — kendi
        /// yıkıcı değişikliğini TEK BAŞINA onaylayabiliyordu. İki kişilik onay kuralı,
        /// onu uygulayacak kişiler silinerek atlatılabiliyordu.
        ///
        /// <b>Değer 0:</b> bu alandan ÖNCE açılmış CR'lar (eski satırlar). O durumda
        /// canlı sayıya düşülüyor — eski davranış. Geriye dönük bir sayı UYDURMAK,
        /// olmayan bir bilgiyi varmış gibi göstermek olurdu.
        /// </summary>
        public int VotingMembersAtCreation { get; set; }

        /// <summary>
        /// AI'ın ürettiği etki açıklaması — BİR KEZ hesaplanıp saklanıyor.
        ///
        /// <b>Bulunan açık (docs/last-review M-4):</b> <c>GET /api/changerequest/{id}</c>
        /// her görüntülemede İKİ LLM çağrısı yapıyordu (etki açıklayıcı + migration
        /// kod üreticisi; ikincisi de Groq'a gidiyor, kod yorumunun "deterministik EF
        /// Core üreticisi" dediğinin aksine). Uç Viewer'a açık, hız sınırı yok ve
        /// çağrılar AI kotasına HİÇ yazılmıyordu: bir sekmeyi yenilemeye devam eden
        /// biri, kimsenin bütçesinden düşmeyen sınırsız model çağrısı üretebiliyordu.
        ///
        /// Girdi DEĞİŞMEZ (CR'ın base/head şemaları sabit), yani sonucu yeniden
        /// hesaplamanın hiçbir faydası yok — saklamak hem maliyeti hem açığı kapatıyor.
        /// <c>null</c> = henüz hesaplanmadı ya da hesaplanamadı.
        /// </summary>
        public string? AiExplanation { get; set; }

        /// <summary>
        /// Üretilen migration sonucu (JSON). Aynı gerekçe: <see cref="AiExplanation"/>.
        /// </summary>
        public string? MigrationResultJson { get; set; }

        // ── G12 — "Run Tests" sonucu (new-phase/29 §4) ──────────────────────
        // Impact Analysis bir TAHMİN; bu alanlar üretilen DDL'in gerçek, ephemeral bir
        // motor container'ında ÇALIŞTIRILDIĞININ kanıtıdır. Null = hiç çalıştırılmadı.
        public bool? TestRunSuccess { get; set; }
        public bool? TestRunSupported { get; set; }
        public string? TestRunMessage { get; set; }
        public string? TestRunFailedStatement { get; set; }
        public long? TestRunDurationMs { get; set; }
        public DateTime? TestRunAt { get; set; }

        public List<ChangeRequestApproval> Approvals { get; set; } = new();
    }

    /// <summary>
    /// Bir kullanıcının bir <see cref="ChangeRequest"/> üzerindeki tek kararı.
    /// new-phase/29-DATABASE-CHANGE-REVIEW.md §3: risk seviyesine göre 1 veya 2 farklı
    /// kişinin onayı gerekir; Destructive/Breaking'de onaylayan, değişikliği yapan
    /// kişiyle aynı olamaz (uygulama katmanında <c>ChangeRequestController</c>'da denetlenir).
    /// </summary>
    public class ChangeRequestApproval
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string ChangeRequestId { get; set; } = null!;
        public ChangeRequest ChangeRequest { get; set; } = null!;

        public string UserId { get; set; } = null!;
        public ApplicationUser User { get; set; } = null!;

        public ApprovalDecision Decision { get; set; }
        public string? Comment { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
