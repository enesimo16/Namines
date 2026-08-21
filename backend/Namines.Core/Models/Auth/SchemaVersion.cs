using System;
using System.ComponentModel.DataAnnotations;

namespace Namines.Core.Models.Auth
{
    /// <summary>
    /// Bir branch üzerinde alınan, sıra numaralı şema anlık görüntüsü —
    /// new-phase/18-CONTROL-PLANE-DDL.md'deki <c>schema_versions</c>'ın Faz 0 karşılığı.
    ///
    /// Fark: 18'deki tasarım <c>nsl_ref</c> (S3 anahtarı) + <c>nsl_inline</c> (küçük
    /// şemalar için JSONB) ikilisini kullanıyor — NSL (bkz. 04-NSL-SCHEMA-IR.md) henüz
    /// yok, S3/blob depolama altyapısı da yok. Faz 0'da tek biçim var: mevcut
    /// <see cref="Models.Auth.CloudProject.SchemaJson"/> ile AYNI serileştirme
    /// formatında inline JSON. NSL geldiğinde bu alan onun üstüne oturacak
    /// (28-IMPACT-ANALYSIS-ENGINE.md §2'de zaten öngörülen geçiş).
    /// </summary>
    public class SchemaVersion
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string ProjectId { get; set; } = null!;
        public CloudProject Project { get; set; } = null!;

        public string BranchId { get; set; } = null!;
        public Branch Branch { get; set; } = null!;

        /// <summary>Branch içinde 1'den başlayan sıra numarası (branch, version) birlikte benzersiz.</summary>
        public int Version { get; set; }

        /// <summary>SchemaJson'ın SHA-256 özeti — sunucu tarafında hesaplanır, istemciden gelen değere güvenilmez.</summary>
        public string Checksum { get; set; } = null!;

        /// <summary><see cref="CloudProject.SchemaJson"/> ile aynı formatta serileştirilmiş anlık görüntü.</summary>
        public string SchemaJson { get; set; } = null!;

        /// <summary>Kullanıcının bıraktığı commit-benzeri açıklama. Opsiyonel.</summary>
        public string? Message { get; set; }

        public short TableCount { get; set; }

        /// <summary>Null olabilir — yazar hesabı silinmişse geçmiş versiyon kaybolmaz (ON DELETE SET NULL).</summary>
        public string? AuthorUserId { get; set; }
        public ApplicationUser? AuthorUser { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
