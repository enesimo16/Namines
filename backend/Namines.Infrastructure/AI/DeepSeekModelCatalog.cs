using System;
using System.Collections.Generic;
using Namines.Core.Analysis;

namespace Namines.Infrastructure.AI;

/// <summary>
/// DeepSeek'teki model kimlikleri ve sınırları.
///
/// <b>DOĞRULANMADI.</b> Bu tablo sağlayıcının belgelerine göre yazıldı; bu kod
/// tabanında DeepSeek'e karşı tek bir canlı istek yapılmadı (hesapta anahtar
/// yok). Groq tarafında TAM OLARAK bu durum üç kez yanılttı: birim testler
/// yeşilken model canlıda yoktu, sonra vardı ama kotası yetmiyordu, sonra
/// <c>max_tokens</c> modelin sınırını aşınca istek komple reddedildi. Hiçbirini
/// test paketi yakalamadı.
///
/// <b>İlk canlı istekten önce üçünü de doğrula:</b> model kimlikleri var mı,
/// dakikalık kota iş için yetiyor mu, ve tek çağrı tavanı gerçekten 8.192 mi.
///
/// Kimlikler <c>DeepSeek:Models:Flash|Standard|Pro</c> ile override edilebilir —
/// sağlayıcı bir modeli kaldırdığında yeni sürüm beklemeden geçilebilsin.
/// </summary>
public sealed class DeepSeekModelCatalog : IModelCatalog
{
    private readonly Dictionary<NaiModel, (string Id, int MaxCompletionTokens)> _models;

    public DeepSeekModelCatalog(
        string? flash = null, string? standard = null, string? pro = null)
    {
        _models = new Dictionary<NaiModel, (string, int)>
        {
            [NaiModel.Flash] = (Or(flash, "deepseek-chat"), 8_192),
            [NaiModel.Standard] = (Or(standard, "deepseek-chat"), 8_192),
            [NaiModel.Pro] = (Or(pro, "deepseek-reasoner"), 8_192),
        };
    }

    private static string Or(string? configured, string fallback)
        => string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();

    public string UpstreamModel(NaiModel model) => _models[model].Id;

    public int MaxCompletionTokens(NaiModel model) => _models[model].MaxCompletionTokens;

    public int ClampToModelLimit(string? upstreamModel, int requestedMaxTokens)
    {
        if (string.IsNullOrWhiteSpace(upstreamModel)) return requestedMaxTokens;

        foreach (var entry in _models.Values)
        {
            if (string.Equals(entry.Id, upstreamModel, StringComparison.OrdinalIgnoreCase))
                return Math.Min(requestedMaxTokens, entry.MaxCompletionTokens);
        }

        return requestedMaxTokens;
    }
}
