using System;
using System.ComponentModel.DataAnnotations;

namespace Namines.Core.Models.Auth;

/// <summary>Yedeğin ne sıklıkla alınacağı.</summary>
public enum VaultCadence
{
    Daily,
    Weekly,
}

/// <summary>
/// Bir projenin otomatik yedek ayarı.
///
/// <b>Cron ifadesi DEĞİL, iki seçenek</b> (<c>01-INSA-PLANI.md</c> V4):
/// kullanıcıya cron yazdırmak, yanlış yazılmış bir ifadeyle yedeğin sessizce
/// hiç çalışmaması demek — ve bunun fark edildiği an, yedeğin gerçekten
/// gerektiği andır. İki sabit seçenek bu hata sınıfını tamamen kaldırıyor.
/// </summary>
public class VaultSchedule
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Proje başına EN FAZLA bir ayar (benzersiz indeks).</summary>
    public string ProjectId { get; set; } = null!;

    public string? OrganizationId { get; set; }

    public bool Enabled { get; set; }

    public VaultCadence Cadence { get; set; } = VaultCadence.Daily;

    /// <summary>
    /// Yedeğin alınacağı saat, <b>UTC</b>.
    ///
    /// Yerel saat değil: sunucunun saat dilimi değiştiğinde ya da farklı bölgede
    /// bir instance çalıştığında zamanlamanın kayması, "yedek gece 3'te alınıyor
    /// sanıyordum" sınıfı bir sürprize dönerdi.
    /// </summary>
    public int HourUtc { get; set; }

    /// <summary>Haftalıkta hangi gün (0 = Pazar). Günlükte anlamsız, null.</summary>
    public int? DayOfWeek { get; set; }

    /// <summary>
    /// Kaç yedek saklanacağı. Bu sayıyı aşan EN ESKİ <b>otomatik</b> yedekler
    /// silinir.
    ///
    /// <b>Elle alınan ve geri yükleme öncesi yedeklere dokunulmaz:</b> kullanıcının
    /// bilerek aldığı ya da bir geri yüklemenin geri dönüşü olan bir yedeği,
    /// otomatik bir temizliğin sessizce silmesi kabul edilemez.
    /// </summary>
    public int RetainCount { get; set; } = 7;

    /// <summary>
    /// Zamanlanmış işin en son ne zaman ÜSTLENİLDİĞİ.
    ///
    /// <b>Çoklu instance güvenliğinin dayanağı:</b> iki API instance'ı aynı anda
    /// uyandığında ikisi de yedek almasın diye, çalışmadan önce bu alan koşullu
    /// (gördüğü değere eşitse) güncelleniyor. Güncellemeyi kazanan tek instance
    /// işi yapıyor.
    /// </summary>
    public DateTime? LastRunAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// <paramref name="now"/> anında bu zamanlamanın çalışması gerekip
    /// gerekmediği.
    ///
    /// <b>"Kaçırılan çalıştırma" bilerek telafi edilmiyor:</b> sunucu iki gün
    /// kapalı kaldıysa açılışta iki yedek almak, dolu bir diski daha da doldurmaktan
    /// başka bir işe yaramaz — kullanıcının istediği güncel bir kopya, geçmişin
    /// tamamlanması değil. Kural tek cümle: <b>bu pencerede henüz alınmadıysa al.</b>
    /// </summary>
    public bool IsDue(DateTime now)
    {
        if (!Enabled) return false;
        if (now.Hour < HourUtc) return false;

        if (Cadence == VaultCadence.Weekly)
        {
            if (DayOfWeek is null || (int)now.DayOfWeek != DayOfWeek.Value) return false;
            // Bu haftaki gün geldi; aynı gün içinde ikinci kez çalışmasın.
            return LastRunAt is null || LastRunAt.Value.Date < now.Date;
        }

        return LastRunAt is null || LastRunAt.Value.Date < now.Date;
    }
}
