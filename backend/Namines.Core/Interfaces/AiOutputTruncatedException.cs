using System;

namespace Namines.Core.Interfaces;

/// <summary>
/// AI sağlayıcısının yanıtı, model token tavanına (<c>max_tokens</c>) ulaştığı
/// için yarım kaldı (<c>finish_reason == "length"</c>).
///
/// <b>Ayrı bir tip olmasının sebebi doğru tedavi.</b> Bu, bozuk/eksik bir JSON
/// üretimi (model saçmaladı) ile aynı şey değil — üretilen içerik zaten
/// kesildiği için JSON olarak ayrıştırılamıyor, ama kök sebep farklı. Genel bir
/// <see cref="System.Text.Json.JsonException"/> olarak fırlatılırsa çağıran
/// bunu "model kötü JSON üretti" sanıp sıcaklığı artırarak YENİDEN deniyordu —
/// bu da modeli daha fazla gevezeliğe (daha erken kesilmeye) itiyordu. Oysa
/// kesilmenin doğru çaresi ne sıcaklık ne de aynı isteği tekrarlamak: ya
/// istenen kapsamı daraltmak (daha az tablo) ya da token tavanını yükseltmek.
/// Bu tip, çağıranın (ve nihayetinde kullanıcı arayüzünün) "model saçmaladı"
/// ile "çıktı sığmadı" durumlarını birbirinden ayırt edebilmesi için var.
/// </summary>
public sealed class AiOutputTruncatedException : Exception
{
    public AiOutputTruncatedException(int? maxTokens)
        : base($"AI response was truncated at the {maxTokens} max_tokens ceiling (finish_reason=length). " +
               "Narrow the request scope or raise the token ceiling — retrying at a higher temperature will not help.")
    {
        MaxTokens = maxTokens;
    }

    /// <summary>Bu istekte kullanılan gerçek <c>max_tokens</c> değeri.</summary>
    public int? MaxTokens { get; }
}
