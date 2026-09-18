using System;
using System.Collections.Generic;
using Namines.Core.Analysis;

namespace Namines.Infrastructure.AI;

/// <summary>
/// Groq'taki model kimlikleri ve sınırları.
///
/// <b>Bu tablonun geçmişi, neden tek yerde durması gerektiğini bire bir
/// anlatıyor — üç kez üst üste yanıldı:</b>
/// <list type="number">
/// <item><c>qwen/qwen3.6-27b</c> bir gün 404 (model_not_found) vermeye başladı
/// ve şema üretimi tamamen durdu.</item>
/// <item>Yerine AKLA YATKIN görünen bir ad yazıldı
/// (<c>llama-3.3-70b-versatile</c>). O da bu hesapta yoktu: ölü model ölü
/// modelle değiştirildi, bütün birim testler yeşil kaldı, hata ancak CANLI
/// istekte ortaya çıktı.</item>
/// <item>Sağlayıcının kataloğu sorulunca doğru ad bulundu (<c>qwen3.8-27b</c> —
/// model kaldırılmamış, sürümü artmış). Ama canlı istek yine başarısız oldu: o
/// modelin bu hesapta dakika başına ÇIKTI TOKENI sınırı 1.000, yani kabaca iki
/// tablo. Bir şema üretemiyor.</item>
/// </list>
/// Sonuç: <c>gpt-oss</c> ikilisi aynı hesapta 8.000 TPM ile sorunsuz çalışıyor.
/// Standard'ın Flash ile aynı modele düşmesi bilinçli bir taviz — katmanları
/// ayrı tutmak uğruna VARSAYILAN katmanı hiç şema üretemez bırakmak, ayrımı
/// anlamlı değil işlevsiz yapardı. Pro (120b) farkını koruyor.
///
/// <b>Kural:</b> burayı değiştirmeden önce hem modelin VAR olduğunu
/// (<c>GET /openai/v1/models</c>) hem de kotasının iş için yettiğini (küçük bir
/// istekle <c>x-ratelimit-*</c> başlıkları) doğrula. Tahmin etme.
/// </summary>
public sealed class GroqModelCatalog : IModelCatalog
{
    private static readonly Dictionary<NaiModel, (string Id, int MaxCompletionTokens)> Models = new()
    {
        [NaiModel.Flash] = ("openai/gpt-oss-20b", 65_536),
        [NaiModel.Standard] = ("openai/gpt-oss-20b", 65_536),
        [NaiModel.Pro] = ("openai/gpt-oss-120b", 65_536),
    };

    public string UpstreamModel(NaiModel model) => Models[model].Id;

    public int MaxCompletionTokens(NaiModel model) => Models[model].MaxCompletionTokens;

    public int ClampToModelLimit(string? upstreamModel, int requestedMaxTokens)
    {
        if (string.IsNullOrWhiteSpace(upstreamModel)) return requestedMaxTokens;

        foreach (var entry in Models.Values)
        {
            if (string.Equals(entry.Id, upstreamModel, StringComparison.OrdinalIgnoreCase))
                return Math.Min(requestedMaxTokens, entry.MaxCompletionTokens);
        }

        return requestedMaxTokens;
    }
}
