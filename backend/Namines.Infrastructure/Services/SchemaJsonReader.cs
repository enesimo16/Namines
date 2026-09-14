using System.Text.Json;
using Namines.Core.Models;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Modelin düz metin cevabından şema JSON'unu okur.
///
/// <b>Neden ayrı bir sınıf:</b> aynı kırpma/temizleme adımları GroqAIService'te
/// iki kez yazılmıştı; araç döngüsü üçüncü bir kopya olacaktı.
///
/// <b>Neden istisna değil <c>null</c>:</b> okunamayan bir cevap hata değil, bir
/// DURUM — çağıran eski (araçsız) yola düşebilsin diye. İstisna fırlatmak,
/// modelin biçimi tutturamadığı her turda tüm üretimi düşürürdü.
/// </summary>
public static class SchemaJsonReader
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static DatabaseSchema? TryRead(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var text = raw.Trim();
        var first = text.IndexOf('{');
        var last = text.LastIndexOf('}');
        if (first == -1 || last <= first) return null;

        text = JsonSanitizerPreprocessor.Sanitize(text.Substring(first, last - first + 1));

        try
        {
            var schema = JsonSerializer.Deserialize<DatabaseSchema>(text, Options);

            // Tablosuz bir şema "okundu" sayılmamalı: model açıklama metni
            // döndürdüyse JSON ayrıştırılabilir ama içi boş olur ve sessizce
            // kullanıcının tüm tablolarını silerdi.
            return schema is { Tables.Count: > 0 } ? schema : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
