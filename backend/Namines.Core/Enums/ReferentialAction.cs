namespace Namines.Core.Enums;

/// <summary>
/// Yabancı anahtarın, işaret ettiği satır silindiğinde/güncellendiğinde ne yapacağı.
///
/// VARSAYILAN <see cref="NoAction"/>'DIR — bilinçli bir karar.
/// Önceden tüm FK'lara koşulsuz CASCADE yazılıyordu; bunun iki sonucu vardı:
///   1) SQL Server, aynı tabloya birden fazla cascade yolu olan şemaları reddediyordu
///      (Msg 1785: "may cause cycles or multiple cascade paths") — yani üretilen DDL
///      sıradan bir e-ticaret modelinde bile çalışmıyordu.
///   2) Reddedilmediği motorlarda sessiz veri kaybı riski üretiyordu: bir kullanıcı
///      silindiğinde siparişleri de siliniyordu.
///
/// Silme davranışı bir tasarım kararıdır ve kullanıcı tarafından açıkça seçilmelidir.
/// </summary>
public enum ReferentialAction
{
    /// <summary>
    /// Varsayılan. Bağlı satır varsa silme/güncelleme reddedilir.
    /// SQL standardında kısıt kontrolü işlem sonuna ertelenebilir.
    /// </summary>
    NoAction = 0,

    /// <summary>
    /// NoAction gibi, ancak kontrol hemen yapılır (ertelenemez).
    /// MSSQL ve Oracle bunu desteklemez → NO ACTION'a düşer.
    /// </summary>
    Restrict = 1,

    /// <summary>
    /// Bağlı satırlar da silinir/güncellenir. Veri kaybettirebilir —
    /// yalnızca kullanıcı açıkça seçerse üretilir.
    /// </summary>
    Cascade = 2,

    /// <summary>
    /// Bağlı satırların FK kolonu NULL yapılır. Kolon nullable olmalıdır.
    /// </summary>
    SetNull = 3,

    /// <summary>
    /// Bağlı satırların FK kolonu DEFAULT değerine çekilir. Kolonun default'u olmalıdır.
    /// Oracle desteklemez; MySQL/InnoDB ayrıştırır ama uygulamaz.
    /// </summary>
    SetDefault = 4
}
