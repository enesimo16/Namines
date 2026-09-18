namespace Namines.Core.Analysis;

/// <summary>
/// Ürün kademelerinin (<see cref="NaiModel"/>) bir SAĞLAYICIDAKİ karşılıkları.
///
/// <b>Neden <see cref="NaiCatalog"/>'dan ayrı:</b> "Standard ne kadar pahalı" ve
/// "Free planda Pro var mı" soruları ürünün kendi kararları, sağlayıcı değişince
/// değişmezler. "Standard hangi model" ve "o model en fazla kaç token yazabilir"
/// soruları ise tamamen sağlayıcının kararı. İkisi aynı tabloda durursa, ikinci
/// sağlayıcı eklendiğinde aynı sorunun iki doğru cevabı olur.
/// </summary>
public interface IModelCatalog
{
    /// <summary>Sağlayıcıdaki gerçek model kimliği. Kullanıcıya GÖSTERİLMEZ.</summary>
    string UpstreamModel(NaiModel model);

    /// <summary>Bu modelin tek çağrıda yazabileceği en fazla token.</summary>
    int MaxCompletionTokens(NaiModel model);

    /// <summary>
    /// İstenen <c>max_tokens</c>'ı modelin sağlayıcı tarafındaki sınırına çeker.
    ///
    /// <b>Neden şart:</b> plan tavanları ürünün bütçe kararı, modelin üst sınırı
    /// sağlayıcının kararı ve ikisi birbirinden habersiz. Sınırın üstünde bir
    /// değer gönderildiğinde sağlayıcı isteği kısmen değil KOMPLE reddediyor
    /// (400 invalid_request): kullanıcı küçük bir şema değil, hiçbir şey almıyor.
    /// Canlı testte tam olarak bu yaşandı ve hiçbir birim test yakalayamadı,
    /// çünkü sınır kod tabanında hiçbir yerde yazılı değildi.
    ///
    /// <b>Tanınmayan model olduğu gibi geçer</b> (fail-open): kimlik
    /// yapılandırmadan override edilebiliyor ve bilmediğimiz bir modele uydurma
    /// bir tavan dayatmak, sessizce çıktıyı kısmak olurdu. Sağlayıcı zaten
    /// reddedip sebebini söylüyor.
    /// </summary>
    int ClampToModelLimit(string? upstreamModel, int requestedMaxTokens);
}
