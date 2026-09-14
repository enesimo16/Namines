using System.Text.Json;
using Namines.Infrastructure.AI;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Model çıktısından <c>DatabaseSchema</c> okuyan HER yerin paylaştığı tek
/// deserileştirme ayarı.
///
/// <b>Neden ayrı bir sınıf, her okuyucunun kendi seçeneği değil:</b>
/// <see cref="TolerantStringConverter"/> olmadan model "length": "255" ya da
/// "defaultValue": 0 gibi tip uyuşmazlıkları döndürdüğünde deserileştirme
/// İSTİSNA fırlatır — ve bu istisna sessizce "okunamadı" sayılıp modelin
/// (araç turları dahil) tüm turunun çöpe atılmasına yol açar. Bu ayrım daha
/// önce <c>GroqAIService</c>'in kendi alanında vardı; <c>SchemaJsonReader</c>
/// ve <c>AgentTools</c> ayrı birer kopya açtığında ikisi de converter'ı
/// unuttu — aynı hatayı iki kez üretmemek için tek kaynak burada.
/// </summary>
public static class SchemaJsonOptions
{
    public static readonly JsonSerializerOptions Default = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new TolerantStringConverter());
        return options;
    }
}
