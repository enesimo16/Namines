namespace Namines.Core.Interfaces;

/// <summary>
/// Bir istek boyunca sağlayıcıdan dönen GERÇEK token kullanımını biriktirir.
///
/// <b>Neden var:</b> kota, tur başına sabit bir tahminle (2.500) düşülüyordu ve
/// sağlayıcının yanıtındaki <c>usage</c> bloğu hiç okunmuyordu. Kullanıcı
/// gelişmiş ayarlardan <c>max_tokens</c>'ı 32.000'e çekince gerçek harcama
/// tahminin 10 katını aşabiliyor, ama kotadan yine 2.500 düşülüyordu — yani
/// sayaç ölçmüyordu, sadece sayıyordu.
///
/// <b>Neden arayüz değişikliği DEĞİL:</b> <see cref="IAIService"/>'in sekiz
/// metodu var ve hepsi farklı şey döndürüyor. Her birine "bir de kullanım
/// döndür" eklemek sekiz imza değişikliği ve sekiz çağıran demekti. Kapsam
/// (scoped) bir toplayıcı, sağlayıcı katmanının yazdığı ve denetleyicinin
/// okuduğu tek bir yer sağlıyor — üstelik yalnızca şema üretimi değil,
/// revizyon/doküman/mock veri gibi bugün hiç ölçülmeyen yollar da kendiliğinden
/// ölçülür hâle geliyor.
/// </summary>
public interface IAiUsageTracker
{
    /// <summary>Sağlayıcının bildirdiği toplam token. Bilinmiyorsa çağrılmaz.</summary>
    void Record(int totalTokens);

    /// <summary>Bu istek boyunca biriken toplam. Hiç kayıt yoksa 0.</summary>
    int TotalTokens { get; }

    /// <summary>Sağlayıcı en az bir kez gerçek kullanım bildirdi mi?</summary>
    bool HasMeasurement { get; }
}
