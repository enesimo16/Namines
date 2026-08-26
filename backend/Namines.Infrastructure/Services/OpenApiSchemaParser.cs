using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Namines.Core.Analysis;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Bir OpenAPI 3.x (<c>components.schemas</c>) ya da Swagger 2.0
/// (<c>definitions</c>) dokümanından varlık adayları çıkarır —
/// second-phase/06-VERI-KAYNAKLARI.md kademe 2.
///
/// <b>Saf fonksiyon, ağa hiç dokunmuyor</b> — bkz. <see cref="GraphQlSchemaParser"/>
/// ile aynı gerekçe: ağ çağrısı <see cref="ApiSpecExtractor"/>'da, burası yalnızca
/// JSON işliyor ve gerçek bir istek olmadan tam test edilebiliyor.
/// </summary>
public static class OpenApiSchemaParser
{
    private const int MaxSchemas = 40;

    /// <summary>
    /// Verilen kök JSON'un bir OpenAPI/Swagger dokümanı olup olmadığını
    /// söyler — <see cref="ApiSpecExtractor"/> kademeler arasında geçiş
    /// yaparken bunu kullanıyor.
    /// </summary>
    public static bool LooksLikeOpenApiDocument(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("openapi", out _) ||
                   doc.RootElement.TryGetProperty("swagger", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <returns>
    /// Boş liste dönebilir — çağıran bunu "bu kaynak işe yaramadı" olarak
    /// yorumlayıp bir alttaki kademeye düşmeli.
    /// </returns>
    public static IReadOnlyList<PlannedTable> Parse(string specJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(specJson);
            var root = doc.RootElement;

            JsonElement schemas;
            if (root.TryGetProperty("components", out var components) &&
                components.TryGetProperty("schemas", out var v3Schemas))
                schemas = v3Schemas;
            else if (root.TryGetProperty("definitions", out var v2Schemas))
                schemas = v2Schemas;
            else
                return Array.Empty<PlannedTable>();

            if (schemas.ValueKind != JsonValueKind.Object) return Array.Empty<PlannedTable>();

            var schemaNames = schemas.EnumerateObject().Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var results = new List<PlannedTable>();

            foreach (var entry in schemas.EnumerateObject())
            {
                if (results.Count >= MaxSchemas) break;

                var schema = entry.Value;
                if (!schema.TryGetProperty("properties", out var properties) ||
                    properties.ValueKind != JsonValueKind.Object)
                    continue; // Alanı olmayan (enum, salt tip takma adı vb.) bir tablo adayı değil.

                var fieldCount = 0;
                var relationCount = 0;

                foreach (var prop in properties.EnumerateObject())
                {
                    fieldCount++;
                    var referenced = FindReferencedSchemaName(prop.Value);
                    if (referenced is not null && schemaNames.Contains(referenced))
                        relationCount++;
                }

                if (fieldCount == 0) continue;

                var reason = relationCount > 0
                    ? $"OpenAPI şeması — {fieldCount} alan, {relationCount} ilişki adayı (tahmin)"
                    : $"OpenAPI şeması — {fieldCount} alan (tahmin)";

                results.Add(new PlannedTable(entry.Name, reason));
            }

            return results;
        }
        catch (JsonException)
        {
            return Array.Empty<PlannedTable>();
        }
    }

    /// <summary>
    /// Bir alanın doğrudan <c>$ref</c> ya da dizi öğesi <c>items.$ref</c>
    /// üzerinden başka bir şemaya işaret edip etmediğini bulur; işaret
    /// ediyorsa referans edilen şema adını döndürür.
    /// </summary>
    private static string? FindReferencedSchemaName(JsonElement property)
    {
        if (TryGetRefName(property, out var direct)) return direct;

        if (property.TryGetProperty("type", out var type) &&
            type.ValueKind == JsonValueKind.String && type.GetString() == "array" &&
            property.TryGetProperty("items", out var items) &&
            TryGetRefName(items, out var arrayItem))
            return arrayItem;

        return null;
    }

    private static bool TryGetRefName(JsonElement element, out string? name)
    {
        name = null;
        if (!element.TryGetProperty("$ref", out var refProp) || refProp.ValueKind != JsonValueKind.String)
            return false;

        var refValue = refProp.GetString();
        if (string.IsNullOrEmpty(refValue)) return false;

        // "#/components/schemas/Product" ya da "#/definitions/Product" -> "Product".
        name = refValue.Split('/').LastOrDefault();
        return !string.IsNullOrEmpty(name);
    }
}
