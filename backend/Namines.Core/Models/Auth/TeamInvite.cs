using System;
using System.ComponentModel.DataAnnotations;

namespace Namines.Core.Models.Auth;

/// <summary>
/// Team planındaki tek kullanımlık davet bağlantısı.
///
/// <b>Neden ayrı bir tablo, doğrudan üye eklemek değil:</b> ProjectMemberController
/// üyeyi e-posta ile DOĞRUDAN ekliyor ve bu, karşı tarafın kayıtlı olmasını
/// zorunlu kılıyordu. Davet bağlantısı bu sırayı tersine çeviriyor — kişi
/// bağlantıyı alıp kaydolabilir, sonra katılabilir.
///
/// <b>Bağlantı TEK KULLANIMLIK.</b> Çok kullanımlı olsaydı bir kişiye gönderilen
/// bağlantı, o kişi tarafından paylaşıldığında koltuk sınırını sessizce delerdi:
/// 3 koltuklu bir ekip, tek bir bağlantı dolaşıma girdiğinde sınırsız olurdu.
/// Katılım anında <see cref="AcceptedByUserId"/> doluyor ve bağlantı ölüyor.
/// </summary>
public class TeamInvite
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Bağlantıdaki gizli değerin SHA-256 özeti — ham token saklanmıyor.
    ///
    /// Ham saklansaydı, veritabanına okuma erişimi olan herkes (yedek dosyası
    /// dahil) her ekibe katılabilirdi. Aynı gerekçe Gateway anahtarlarında da
    /// geçerli ve orada da özet saklanıyor.
    /// </summary>
    public string TokenHash { get; set; } = null!;

    public string OrganizationId { get; set; } = null!;
    public Organization Organization { get; set; } = null!;

    /// <summary>Daveti oluşturan (Admin/Owner).</summary>
    public string CreatedByUserId { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Son kullanma. Süresiz davet, ekipten ayrılan birinin elindeki eski
    /// bağlantıyla aylar sonra geri dönebilmesi demek olurdu.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Katılan kullanıcı; null ise bağlantı hâlâ geçerli.</summary>
    public string? AcceptedByUserId { get; set; }
    public DateTime? AcceptedAt { get; set; }

    /// <summary>Davet edilenin alacağı rol. Owner ASLA davetle verilmez.</summary>
    public OrgRole Role { get; set; } = OrgRole.Editor;

    /// <summary>Oluşturan tarafından iptal edildiyse dolu.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Bağlantı şu an kullanılabilir mi.
    ///
    /// Üç koşul da ayrı ayrı kontrol ediliyor: kullanılmış, iptal edilmiş ve
    /// süresi dolmuş davetler farklı sebeplerle geçersiz ve kullanıcıya hangisi
    /// olduğunu söylemek gerekiyor — "geçersiz bağlantı" tek başına, kişinin
    /// yeni bağlantı mı isteyeceğini yoksa zaten katılmış mı olduğunu anlamasını
    /// engellerdi.
    /// </summary>
    public bool IsUsable(DateTime nowUtc) =>
        AcceptedByUserId is null && RevokedAt is null && ExpiresAt > nowUtc;
}
