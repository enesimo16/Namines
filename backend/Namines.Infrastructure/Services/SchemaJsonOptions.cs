using System.Text.Json;
using System.Text.Json.Serialization;
using Namines.Infrastructure.AI;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Model çıktısından <c>DatabaseSchema</c> okuyan HER yerin paylaştığı tek
/// deserileştirme ayarı.
///
/// <b>Neden ayrı bir sınıf, her okuyucunun kendi seçeneği değil:</b>
/// <see cref="TolerantStringConverter"/> olmadan model bir METİN alanına sayı
/// ya da bool döndürdüğünde ("defaultValue": 0) deserileştirme
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

        // Enum'lar JSON'da STRING olarak duruyor: `SchemaRelation.OnDelete`/
        // `OnUpdate` (<see cref="Namines.Core.Models.ReferentialAction"/>)
        // "NoAction"/"Cascade" diye yazılıyor. Bu dönüştürücü olmadan İLİŞKİSİ
        // OLAN her şema JsonException fırlatıyordu.
        //
        // <b>Bu sınıfın tek kaynak olma iddiasını boşa çıkaran eksiklik buydu:</b>
        // BranchController ve ChangeRequestController converter'ı kendi yerel
        // seçeneklerine EKLEDİKLERİ için çalışıyordu; buradaki "ortak" ayarı
        // kullanan yerler ise sessizce kırılıyordu —
        //   • LaunchController.Download "stored schema is not valid" veriyordu,
        //   • AuthController'ın Namines Flow diff'i try/catch'e düşüp
        //     SESSİZCE atlanıyordu, yani ilişkisi olan bir projede sunucu
        //     tarafı otomasyon hiç tetiklenmiyordu.
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
