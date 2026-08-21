using System;
using System.ComponentModel.DataAnnotations;

namespace Namines.Core.Models.Auth
{
    /// <summary>
    /// Sunucu-otoriteli branch kaydı — new-phase/30-SERVER-SIDE-BRANCHING.md §3 Adım 1.
    ///
    /// Bilinçli kapsam: bu Adım 1'dir. Şemanın kendisi henüz burada değil — bir CRDT
    /// dokümanına veya ephemeral branch DB'sine bağlı değil (Adım 2/3, ayrı işler:
    /// CHECKLIST.md G17 "CanvasHub'ı branch_id'ye bağla"). Burada sadece "bu projenin
    /// X adında bir branch'i var, sunucu bunu biliyor" kavramı kayıt altına alınıyor —
    /// önceden bu bilgi yalnızca istemcide (cihaz başına) vardı.
    ///
    /// new-phase/18-CONTROL-PLANE-DDL.md'deki tam Faz 2 planından tek fark: orada
    /// <c>projects</c>/<c>users</c> Faz 2'nin ayrı ULID tabanlı çok-kiracılı şemasına
    /// referans veriyor. Faz 0'da bu tablolar yok — bunun yerine mevcut
    /// <see cref="CloudProject"/>/<see cref="ApplicationUser"/> modeline bağlanıyoruz
    /// (CLAUDE.md: "sıfırdan rewrite etme" prensibi — mevcut modelin yanına eklenir,
    /// onu değiştirmez).
    /// </summary>
    public class Branch
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string ProjectId { get; set; } = null!;
        public CloudProject Project { get; set; } = null!;

        public string Name { get; set; } = null!;

        /// <summary>Fork edildiği branch — null ise kök branch (proje oluşturulduğunda açılan "main").</summary>
        public string? ParentBranchId { get; set; }
        public Branch? ParentBranch { get; set; }

        /// <summary>Fork anında <see cref="ParentBranch"/>'in hangi <see cref="SchemaVersion.Version"/>'ından ayrıldığı.</summary>
        public int? ForkedFromVersion { get; set; }

        /// <summary>Projenin ana branch'i. Her projede en fazla bir tane olabilir (kısmi unique index).</summary>
        public bool IsDefault { get; set; }

        public string CreatedByUserId { get; set; } = null!;
        public ApplicationUser CreatedByUser { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Null değilse branch kapatılmış (merge edilmiş veya terk edilmiş) — silinmez, geçmiş korunur.</summary>
        public DateTime? ClosedAt { get; set; }
    }
}
