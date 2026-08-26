using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Namines.Core.Analysis;

namespace Namines.Infrastructure.Services;

/// <summary>
/// GraphQL introspection yanıtından (`{ __schema { types { ... } } }`) varlık
/// adayları çıkarır — second-phase/06-VERI-KAYNAKLARI.md kademe 1.
///
/// <b>Saf fonksiyon, ağa hiç dokunmuyor.</b> İstek atmayı <see cref="ApiSpecExtractor"/>
/// yapıyor; burası yalnızca gelen JSON'u işliyor — bu yüzden gerçek bir ağ
/// çağrısı olmadan tam test edilebiliyor.
///
/// <b>Çıkarılan şey "tahmin" — GERÇEK bir tablo listesi değil.</b> GraphQL
/// şeması API'nin dışa açtığı görünüm; iç tablolar, hesaplanan alanlar,
/// birleştirilmiş tipler burada görünmez (bkz. 06 §"Bugünkü durum").
/// </summary>
public static class GraphQlSchemaParser
{
    /// <summary>GraphQL introspection sorgusu — yalnızca ihtiyaç duyduğumuz alanlar.</summary>
    public const string IntrospectionQuery = """
        {
          "query": "{ __schema { types { name kind fields { name type { name kind ofType { name kind ofType { name kind } } } } } } }"
        }
        """;

    /// <summary>Kök tipler ve dahili giriş adları — varlık adayı sayılmaz.</summary>
    private static readonly HashSet<string> IgnoredNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Query", "Mutation", "Subscription", "PageInfo",
    };

    private const int MaxTypes = 40;

    /// <summary>
    /// Introspection yanıtından varlık adaylarını çıkarır.
    /// </summary>
    /// <returns>
    /// Boş liste dönebilir (ör. yanıt introspection şekline uymuyorsa) —
    /// çağıran bunu "bu kaynak işe yaramadı" olarak yorumlayıp bir alttaki
    /// kademeye düşmeli, hata fırlatmamalı.
    /// </returns>
    public static IReadOnlyList<PlannedTable> Parse(string introspectionJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(introspectionJson);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return Array.Empty<PlannedTable>();
            if (!data.TryGetProperty("__schema", out var schema)) return Array.Empty<PlannedTable>();
            if (!schema.TryGetProperty("types", out var types) || types.ValueKind != JsonValueKind.Array)
                return Array.Empty<PlannedTable>();

            var objectTypeNames = types.EnumerateArray()
                .Where(t => GetString(t, "kind") == "OBJECT")
                .Select(t => GetString(t, "name"))
                .Where(n => n is not null)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

            var results = new List<PlannedTable>();

            foreach (var type in types.EnumerateArray())
            {
                if (results.Count >= MaxTypes) break;

                var name = GetString(type, "name");
                if (name is null || name.StartsWith("__") || IgnoredNames.Contains(name)) continue;
                if (GetString(type, "kind") != "OBJECT") continue;

                var fieldCount = 0;
                var relationCount = 0;

                if (type.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
                {
                    foreach (var field in fields.EnumerateArray())
                    {
                        fieldCount++;
                        var referenced = UnwrapObjectTypeName(field);
                        // Kendi kendine referans (ör. "parent") ilişki sayılıyor ama
                        // ayrı bir tablo doğurmaz; yine de sayaç anlamlı bir sinyal.
                        if (referenced is not null && objectTypeNames.Contains(referenced))
                            relationCount++;
                    }
                }

                if (fieldCount == 0) continue; // Alanı olmayan tip anlamsız bir varlık adayı.

                var reason = relationCount > 0
                    ? $"GraphQL tipi — {fieldCount} alan, {relationCount} ilişki adayı (tahmin)"
                    : $"GraphQL tipi — {fieldCount} alan (tahmin)";

                results.Add(new PlannedTable(name, reason));
            }

            return results;
        }
        catch (JsonException)
        {
            // Bozuk/beklenmeyen JSON — çağıran bir alttaki kademeye düşsün.
            return Array.Empty<PlannedTable>();
        }
    }

    /// <summary>
    /// Bir alanın tipini NON_NULL/LIST sarmalayıcılarından soyup altındaki
    /// OBJECT tipinin adını bulur. En fazla 3 seviye soyuyor — GraphQL'de
    /// pratikte bundan derin sarmalama olmuyor (ör. <c>[User!]!</c> iki seviye).
    /// </summary>
    private static string? UnwrapObjectTypeName(JsonElement field)
    {
        if (!field.TryGetProperty("type", out var t)) return null;

        for (var i = 0; i < 3 && t.ValueKind == JsonValueKind.Object; i++)
        {
            if (GetString(t, "kind") == "OBJECT")
                return GetString(t, "name");

            if (!t.TryGetProperty("ofType", out var next) || next.ValueKind != JsonValueKind.Object)
                return null;

            t = next;
        }

        return null;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
