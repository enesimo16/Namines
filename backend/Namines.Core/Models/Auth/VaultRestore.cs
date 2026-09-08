using System;
using System.ComponentModel.DataAnnotations;

namespace Namines.Core.Models.Auth;

/// <summary>
/// Bir geri yükleme girişiminin kaydı.
///
/// <b>Neden <see cref="VaultBackup"/>'tan ayrı bir tablo:</b> geri yükleme
/// veriyi SİLEN tek işlem ve aynı yedek birden çok kez geri yüklenebilir.
/// Yedek kaydına yazmak, bir önceki geri yüklemenin izini ezerdi — oysa
/// "bu veritabanına en son kim, ne zaman, hangi yedeği yazdı" sorusu tam da
/// bir olay sonrası sorulan sorudur.
/// </summary>
public class VaultRestore
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string ProjectId { get; set; } = null!;

    public string? OrganizationId { get; set; }

    /// <summary>Geri yüklenen yedek.</summary>
    public string BackupId { get; set; } = null!;

    /// <summary>
    /// Geri yükleme ÖNCESİ otomatik alınan yedek — "yanlış düğmeye bastım"ın
    /// geri dönüşü. Ön yedek alınamadıysa geri yükleme hiç başlamaz, dolayısıyla
    /// tamamlanmış bir kayıtta bu alan her zaman dolu.
    /// </summary>
    public string? PreRestoreBackupId { get; set; }

    public string PerformedByUserId { get; set; } = null!;

    public VaultBackupStatus Status { get; set; } = VaultBackupStatus.Running;

    public string? ErrorMessage { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }
}
