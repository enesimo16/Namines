using System.Collections.Generic;

namespace Namines.Core.Interfaces;

/// <summary>
/// Çalışmakta olan sandbox işlerinin kaydı.
/// <para>
/// Arka plan süpürücüsü (sweeper) eski container'ları temizler ama bir container'ın
/// "zombi" mi yoksa "hâlâ çalışan uzun bir iş" mi olduğunu yalnızca yaşına bakarak
/// ayırt edemez. Bu arayüz o ayrımı yapar: kaydı tutan katman (API) ile temizliği
/// yapan katman (Infrastructure) arasındaki bağımlılığı ters çevirmeden bilgi taşır.
/// </para>
/// </summary>
public interface ISandboxJobRegistry
{
    /// <summary>Hâlâ süren işlerin kimlikleri. Bunlara ait container'lar silinmemeli.</summary>
    IReadOnlyCollection<string> GetActiveJobIds();

    /// <summary>
    /// Saklama süresi dolmuş işleri bellekten düşürür ve kimliklerini döner.
    ///
    /// <b>Bulunan açık (docs/last-review M-6):</b> tahliye metodu vardı ama
    /// <b>hiçbir yerden çağrılmıyordu</b>. İki sonucu birden vardı: iş kayıtları
    /// bellekte sınırsız birikiyordu ve <c>Outputs/</c> altındaki yedek zip'leri
    /// <b>asla silinmiyordu</b> — oysa indirme ucu kullanıcıya "saklama penceresi
    /// (2 saat) geçmiş olabilir" diyordu. Yani ürün, tutmadığı bir sözü
    /// veriyordu; dosyalar diskte kalıcıydı ve kullanıcının tam şemasını taşıyorlar.
    ///
    /// Kimlik dönüyor, iş nesnesi değil: iş modeli API katmanında yaşıyor ve onu
    /// Core'a taşımak, bu arayüzün var olma sebebini (katman bağımlılığını ters
    /// çevirmemek) bozardı.
    /// </summary>
    IReadOnlyCollection<string> EvictExpiredJobs();
}
