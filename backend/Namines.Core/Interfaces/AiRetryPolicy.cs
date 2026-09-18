using System;

namespace Namines.Core.Interfaces;

/// <summary>
/// Hız sınırına takılan bir isteğin ne kadar bekleyebileceğine karar verir.
///
/// <b>Neden ayrı ve saf bir sınıf:</b> karar HTTP'den bağımsız. Bütçe
/// tükendiğinde durmak, tek bir beklemeyi kısmak, kalan bütçeye sığdırmak —
/// hepsi gerçek zaman geçirmeden sınanabilmeli. Aynı mantık <c>PostAsync</c>'in
/// içine gömülseydi, "bütçe bitince duruyor mu" sorusunu ancak on dakika
/// bekleyen bir test sorabilirdi ve o test yazılmazdı.
///
/// <b>Örnek durumludur</b> (harcanan süreyi biriktirir): bir istek boyunca tek
/// bir örnek kullanılır, istekler arasında paylaşılmaz.
/// </summary>
public sealed class AiRetryPolicy
{
    private readonly TimeSpan _maxTotal;
    private readonly TimeSpan _maxSingle;
    private TimeSpan _spent;

    public AiRetryPolicy(int maxTotalWaitSeconds, int maxSingleWaitSeconds)
    {
        _maxTotal = TimeSpan.FromSeconds(Math.Max(0, maxTotalWaitSeconds));
        _maxSingle = TimeSpan.FromSeconds(Math.Max(0, maxSingleWaitSeconds));
    }

    /// <summary>Hiç beklemeyen politika — yeniden deneme kapalı demek.</summary>
    public static AiRetryPolicy Disabled => new(0, 0);

    /// <summary>Şu ana kadar beklenen toplam süre. Yalnızca loglama için.</summary>
    public TimeSpan Spent => _spent;

    /// <summary>
    /// Sağlayıcının istediği <paramref name="requested"/> beklemeyi bütçeye
    /// göre değerlendirir.
    /// </summary>
    /// <returns>
    /// Beklenip yeniden denenecekse <c>true</c> ve <paramref name="granted"/>
    /// beklenecek süre; bütçe bittiyse <c>false</c>.
    /// </returns>
    public bool TryNextDelay(TimeSpan requested, out TimeSpan granted)
    {
        granted = TimeSpan.Zero;

        if (_maxTotal <= TimeSpan.Zero) return false;

        var remaining = _maxTotal - _spent;
        if (remaining <= TimeSpan.Zero) return false;

        // Negatif ya da sıfır istek: hemen yeniden dene, bütçeden bir şey yeme.
        if (requested <= TimeSpan.Zero)
        {
            granted = TimeSpan.Zero;
            return true;
        }

        // Önce tek seferlik tavan, sonra kalan bütçe. Sıra önemli: sağlayıcının
        // saçma bir değeri kalan bütçeyi tek kalemde yutmasın.
        var capped = requested > _maxSingle ? _maxSingle : requested;
        if (capped > remaining) capped = remaining;

        _spent += capped;
        granted = capped;
        return true;
    }
}
