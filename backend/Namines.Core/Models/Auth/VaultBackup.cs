using System;
using System.ComponentModel.DataAnnotations;

namespace Namines.Core.Models.Auth;

/// <summary>Bir yedeğin yaşam döngüsündeki durumu.</summary>
public enum VaultBackupStatus
{
    /// <summary>Alınıyor. Kayıt İŞ BAŞLAMADAN yazılır ki yarıda kesilen bir
    /// yedek görünmez kalmasın — aksi hâlde çöken bir işlem hiçbir iz bırakmazdı.</summary>
    Running,
    Succeeded,
    Failed,
}

/// <summary>Yedeğin neden alındığı.</summary>
public enum VaultBackupKind
{
    /// <summary>Kullanıcı istedi.</summary>
    Manual,

    /// <summary>
    /// Geri yükleme öncesi ZORUNLU otomatik yedek.
    ///
    /// Geri yükleme hedefteki veriyi siler; o an var olan hâli almadan bunu
    /// yapmak, yanlış yedeği seçen bir kullanıcı için geri dönüşü olmayan
    /// bir işlem olurdu. Bu tür ayrı tutuluyor ki listede karışmasın.
    /// </summary>
    PreRestore,

    /// <summary>
    /// Zamanlamanın otomatik aldığı yedek.
    ///
    /// <b>Saklama politikası YALNIZCA bu türü siler.</b> Elle alınan ve geri
    /// yükleme öncesi yedekler kullanıcının bilinçli kararlarıdır; onları
    /// otomatik bir temizliğe kurban etmek kabul edilemez.
    ///
    /// Enum'un SONUNA eklendi: mevcut kayıtların sayısal değerleri kaymasın.
    /// </summary>
    Scheduled,
}

/// <summary>
/// Alınmış bir yedeğin KAYDI. Yedeğin İÇERİĞİ burada DEĞİL —
/// nesne/dosya deposunda durur ve buradaki <see cref="StorageKey"/> ile bulunur.
///
/// <b>Neden ayrı:</b> bir dump veritabanının tamamının kopyasıdır; onu
/// control-plane veritabanına yazmak, o veritabanını bütün müşterilerin
/// verisinin toplandığı tek bir hedef hâline getirirdi.
/// </summary>
public class VaultBackup
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string ProjectId { get; set; } = null!;

    /// <summary>Yetki sınırı — okuma/silme yetkisi org üyeliğinden gelir.</summary>
    public string? OrganizationId { get; set; }

    /// <summary>Yedeği başlatan kullanıcı. Kullanıcı silinse bile kayıt kalsın diye düz metin.</summary>
    public string CreatedByUserId { get; set; } = null!;

    /// <summary>Yedeklenen veritabanının adı — yalnızca gösterim için.</summary>
    public string DatabaseName { get; set; } = null!;

    /// <summary>Hangi motor ("PostgreSQL"). Geri yüklerken doğru sağlayıcıyı seçmek için.</summary>
    public string Engine { get; set; } = null!;

    /// <summary>Depodaki anahtar. Depo değişse de (dosya → S3) anahtar aynı kalır.</summary>
    public string StorageKey { get; set; } = null!;

    /// <summary>Yedeğin nerede durduğu — kullanıcıya "bu dosya nerede" sorusunu cevaplamak için.</summary>
    public string StoreDescription { get; set; } = null!;

    /// <summary>ŞİFRELENMİŞ boyut. Düz boyut değil: kullanıcının diskte gördüğü sayı bu.</summary>
    public long SizeBytes { get; set; }

    public VaultBackupStatus Status { get; set; } = VaultBackupStatus.Running;

    public VaultBackupKind Kind { get; set; } = VaultBackupKind.Manual;

    /// <summary>Başarısızsa nedeni — kısaltılmış, bağlantı ayrıntısı içermez.</summary>
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Yedeğin GERÇEKTEN geri yüklenebildiğinin kanıtlandığı an.
    ///
    /// <b>Neden ayrı bir alan:</b> "yedek alındı" ile "yedek işe yarar" aynı şey
    /// değil. Bir dump bozuk, eksik ya da çözülemez olabilir ve bu yalnızca geri
    /// yüklemeye çalışıldığında anlaşılır — yani felaket anında. Doğrulama, o
    /// denemeyi felaketten ÖNCE, kullanıcının veritabanına dokunmadan yapıyor.
    ///
    /// null: henüz doğrulanmadı. Doğrulanmamış bir yedek geçersiz değil,
    /// yalnızca kanıtlanmamış — arayüz ikisini ayırt ediyor.
    /// </summary>
    public DateTime? VerifiedAt { get; set; }

    /// <summary>Doğrulama denendiyse ve BAŞARISIZ olduysa nedeni.</summary>
    public string? VerifyError { get; set; }
}
