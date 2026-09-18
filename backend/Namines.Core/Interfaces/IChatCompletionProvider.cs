using System;
using System.Net.Http;
using Namines.Core.Analysis;

namespace Namines.Core.Interfaces;

/// <summary>
/// Bir sohbet-tamamlama sağlayıcısına özgü olan HER ŞEY.
///
/// <b>Neden tüketiciler değil de burası soyutlandı:</b> <c>GroqAIService</c>'i
/// somut tip olarak alan altı yer var (<c>GatewayController</c>,
/// <c>AIDbaService</c>, <c>AutomationExecutor</c>, <c>GroqSchemaDraftSource</c>,
/// <c>MigrationService</c>, <c>SmartSeedService</c>) ve hepsi
/// <see cref="IAIService"/>'te bulunmayan metotlara ihtiyaç duyuyor. Altısını da
/// bir arayüze çevirmek, o arayüzü yirmi metoda çıkarır ve ikinci sağlayıcının
/// yalnızca şema üretiminde çalışmasıyla sonuçlanırdı. Sağlayıcıyı ALTTAN
/// değiştirince altı tüketici hiç değişmeden ikinci sağlayıcıyı da kullanıyor.
/// </summary>
public interface IChatCompletionProvider
{
    /// <summary>Yapılandırmada geçen ad: <c>groq</c>, <c>deepseek</c>.</summary>
    string Name { get; }

    /// <summary>Sağlayıcının OpenAI uyumlu kök adresi, sonunda eğik çizgiyle.</summary>
    Uri BaseAddress { get; }

    /// <summary>API anahtarı var mı. Yoksa ağa hiç çıkılmaz.</summary>
    bool IsConfigured { get; }

    /// <summary>Bu sağlayıcıdaki model kimlikleri ve sınırları.</summary>
    IModelCatalog Models { get; }

    /// <summary>
    /// İsteğe kimlik başlığını yazar.
    ///
    /// <b>İstek başına, <c>DefaultRequestHeaders</c>'a DEĞİL:</b> paylaşılan
    /// <c>HttpClient</c>'ta başlık değiştirmek, paralel isteklerde yarış koşulu
    /// ve log sızıntısı riski demek.
    /// </summary>
    void Authenticate(HttpRequestMessage request);
}
