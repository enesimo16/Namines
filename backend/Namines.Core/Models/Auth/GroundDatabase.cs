using System;
using System.ComponentModel.DataAnnotations;

namespace Namines.Core.Models.Auth;

/// <summary>Yönetilen bir veritabanının yaşam döngüsündeki durumu.</summary>
public enum GroundStatus
{
    /// <summary>
    /// Açılıyor. Kayıt sağlayıcıya gidilmeden ÖNCE bu durumda yazılır —
    /// yarıda kesilen bir provizyon iz bırakmasa, kimsenin bilmediği ama
    /// var olan bir kaynak kalırdı.
    /// </summary>
    Provisioning,

    Active,

    /// <summary>
    /// Silme istendi, bekleme penceresi işliyor. <b>Geri alınabilir.</b>
    /// </summary>
    PendingDelete,

    Deleted,

    Failed,
}

/// <summary>
/// Namines'in bir proje için açtığı yönetilen veritabanının kaydı.
///
/// <b>Bağlantı dizesi burada DEĞİL:</b> Desk/Vault ile aynı yere,
/// <see cref="CloudProject.EncryptedConnectionString"/>'e şifreli yazılır.
/// İkinci bir kopya tutmak, sızdırılabilecek yüzeyi ikiye katlamak olurdu.
/// </summary>
public class GroundDatabase
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Proje kimliği — üzerinde <b>benzersiz indeks</b> var.
    ///
    /// <b>İdempotansın dayanağı ve sonradan eklenecek bir iyileştirme değil:</b>
    /// kullanıcı "Barındır" düğmesine iki kez basarsa iki veritabanı açılır,
    /// ikincisinin kaydı birincinin üzerine yazılır ve birincisi sonsuza kadar
    /// faturalanan, kimsenin bilmediği bir kaynak olarak kalırdı. İkinci istek
    /// veritabanı seviyesinde reddediliyor.
    /// </summary>
    public string ProjectId { get; set; } = null!;

    public string? OrganizationId { get; set; }

    /// <summary>Hangi sağlayıcı ("LocalPostgres", "Neon").</summary>
    public string Provider { get; set; } = null!;

    /// <summary>Sağlayıcı tarafındaki üst kaynak kimliği.</summary>
    public string? ProviderProjectId { get; set; }

    /// <summary>Varsa dal/alt kaynak kimliği (yerel sağlayıcıda rol adı).</summary>
    public string? ProviderBranchId { get; set; }

    public string? Region { get; set; }

    public GroundStatus Status { get; set; } = GroundStatus.Provisioning;

    public string CreatedByUserId { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Silmenin İSTENDİĞİ an. Bekleme penceresi buradan sayılır; kalıcı silme
    /// bu an + <c>Ground:DeleteGraceDays</c> geçtikten sonra yapılır.
    /// </summary>
    public DateTime? DeleteRequestedAt { get; set; }

    /// <summary>Kaynağın sağlayıcıdan KALICI olarak silindiği an.</summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>Başarısızsa nedeni — kısaltılmış, bağlantı ayrıntısı içermez.</summary>
    public string? Error { get; set; }
}
