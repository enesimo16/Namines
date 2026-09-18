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
/// <b>İki ayrı durdurucu var ve ikisi de şart.</b> İlk sürümde yalnızca süre
/// bütçesi vardı ve şu açık kalmıştı: sağlayıcı <c>Retry-After: 0</c> derse
/// bekleme sıfır oluyor, sıfır bekleme bütçeden hiçbir şey yemiyor ve döngü
/// hiç bitmiyordu — kullanıcıya dönmeyen, sağlayıcıyı ağ hızında döven bir
/// tur. Sıfır gerekmiyordu bile: sağlayıcı ondalıklı süre veriyor ve
/// tekrarlayan bir "0.017s", 600 saniyelik bütçede otuz beş bin istek demekti.
/// Bu yüzden <see cref="MaxAttempts"/> deneme sayısını da sınırlıyor; süre
/// bütçesi "ne kadar bekleyeceğiz"i, deneme sayısı "kaç kere deneyeceğiz"i
/// cevaplıyor ve biri diğerinin yerine geçemiyor.
///
/// <b>Örnek durumludur ve iş parçacığı güvenlidir:</b> tek bir istek içinde
/// paralel çalışan parçalar (bkz. <c>SchemaAgentPipeline.MaxConcurrentChunks</c>)
/// AYNI bütçeyi paylaşıyor — paylaşmasalar, parça başına ayrı bir bütçe
/// kullanıcının toplam bekleme süresini parça sayısıyla çarpardı.
/// </summary>
public sealed class AiRetryPolicy
{
    /// <summary>
    /// Süre bütçesi ne kadar geniş olursa olsun bu sayıdan fazla denenmez.
    ///
    /// Onu geçen bir senaryoda sorun geçici bir sıkışma değil, kotanın işe
    /// yetmemesidir; daha fazla denemek yalnızca sağlayıcıyı döver.
    /// </summary>
    public const int DefaultMaxAttempts = 10;

    private readonly object _gate = new();
    private readonly TimeSpan _maxTotal;
    private readonly TimeSpan _maxSingle;
    private readonly int _maxAttempts;
    private TimeSpan _spent;
    private int _attempts;

    public AiRetryPolicy(
        int maxTotalWaitSeconds,
        int maxSingleWaitSeconds,
        int maxAttempts = DefaultMaxAttempts)
    {
        _maxTotal = TimeSpan.FromSeconds(Math.Max(0, maxTotalWaitSeconds));
        _maxSingle = TimeSpan.FromSeconds(Math.Max(0, maxSingleWaitSeconds));
        _maxAttempts = Math.Max(0, maxAttempts);
    }

    /// <summary>Hiç beklemeyen politika — yeniden deneme kapalı demek.</summary>
    public static AiRetryPolicy Disabled => new(0, 0);

    /// <summary>Şu ana kadar beklenen toplam süre. Yalnızca loglama için.</summary>
    public TimeSpan Spent { get { lock (_gate) return _spent; } }

    /// <summary>Şu ana kadar verilen yeniden deneme izni sayısı.</summary>
    public int Attempts { get { lock (_gate) return _attempts; } }

    /// <summary>İzin verilen en fazla yeniden deneme sayısı.</summary>
    public int MaxAttempts => _maxAttempts;

    /// <summary>
    /// Sağlayıcının istediği <paramref name="requested"/> beklemeyi bütçeye
    /// göre değerlendirir.
    /// </summary>
    /// <returns>
    /// Beklenip yeniden denenecekse <c>true</c> ve <paramref name="granted"/>
    /// beklenecek süre; bütçe ya da deneme hakkı bittiyse <c>false</c>.
    /// </returns>
    public bool TryNextDelay(TimeSpan requested, out TimeSpan granted)
    {
        lock (_gate)
        {
            granted = TimeSpan.Zero;

            // Deneme sayısı ÖNCE kontrol ediliyor: sıfır süreli bir bekleme
            // bütçeden bir şey yemediği için, döngüyü bitiren tek şey bu.
            if (_attempts >= _maxAttempts) return false;
            if (_maxTotal <= TimeSpan.Zero) return false;

            var remaining = _maxTotal - _spent;
            if (remaining <= TimeSpan.Zero) return false;

            // Negatif ya da sıfır istek: hemen yeniden dene, bütçeden bir şey
            // yeme. Bu dalın güvenli olmasını sağlayan şey yukarıdaki deneme
            // sayacı; o olmadan burası sonsuz bir döngünün kapısıydı.
            if (requested <= TimeSpan.Zero)
            {
                _attempts++;
                granted = TimeSpan.Zero;
                return true;
            }

            // Önce tek seferlik tavan, sonra kalan bütçe. Sıra önemli:
            // sağlayıcının saçma bir değeri kalan bütçeyi tek kalemde yutmasın.
            var capped = requested > _maxSingle ? _maxSingle : requested;
            if (capped > remaining) capped = remaining;

            _spent += capped;
            _attempts++;
            granted = capped;
            return true;
        }
    }
}
