using System;
using System.Collections.Generic;
using System.Linq;
using Namines.Core.Models;

namespace Namines.Core.Analysis;

/// <summary>
/// Şemadan OpenAPI 3.1 belgesi üretir (new-phase/08-GATEWAY-API.md §2, "/openapi.json").
///
/// Deterministik: aynı şema + aynı izin listesi her zaman aynı belgeyi verir. Bir
/// dil modeline yazdırılmadı, çünkü OpenAPI belgesi istemci SDK'sı üretmek için
/// kullanılır — orada "çoğunlukla doğru" bir çıktı, sessizce yanlış tipli bir
/// istemci demektir.
///
/// <b>Yalnızca izin verilen tablolar belgelenir.</b> Erişilemeyen bir tabloyu
/// belgede göstermek, 08 §1'in "hiçbir tablo varsayılan olarak public değil"
/// kuralını belge üzerinden delerdi: şemanın tamamı okunabilir hâle gelirdi.
/// </summary>
public static class GatewayOpenApiGenerator
{
    /// <param name="allowedTables">
    /// Tablo adı → (okunabilir, yazılabilir). Listede olmayan tablo belgeye girmez.
    /// </param>
    public static Dictionary<string, object?> Generate(
        DatabaseSchema schema,
        IReadOnlyDictionary<string, (bool CanRead, bool CanWrite)> allowedTables,
        string baseUrl = "/api/gateway")
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(allowedTables);

        var paths = new Dictionary<string, object?>();
        var schemas = new Dictionary<string, object?>();

        foreach (var table in schema.Tables.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            if (!allowedTables.TryGetValue(table.Name, out var access)) continue;
            if (!access.CanRead && !access.CanWrite) continue;

            schemas[table.Name] = TableSchema(table);

            var operations = new Dictionary<string, object?>();

            if (access.CanRead)
            {
                operations["post"] = ListOperation(table);
                paths[$"{baseUrl}/list"] = operations;
                paths[$"{baseUrl}/detail"] = new Dictionary<string, object?>
                {
                    ["post"] = DetailOperation(table),
                };
            }

            if (access.CanWrite)
            {
                paths[$"{baseUrl}/create"] = new Dictionary<string, object?> { ["post"] = WriteOperation(table, "create") };
                paths[$"{baseUrl}/update"] = new Dictionary<string, object?> { ["post"] = WriteOperation(table, "update") };
                paths[$"{baseUrl}/delete"] = new Dictionary<string, object?> { ["post"] = WriteOperation(table, "delete") };
            }
        }

        return new Dictionary<string, object?>
        {
            ["openapi"] = "3.1.0",
            ["info"] = new Dictionary<string, object?>
            {
                ["title"] = string.IsNullOrWhiteSpace(schema.Name) ? "Namines Gateway" : $"{schema.Name} — Namines Gateway",
                ["version"] = "1.0.0",
                ["description"] =
                    "Auto-generated from the Namines schema. Only tables explicitly granted to API " +
                    "keys appear here; tables you cannot reach are not documented.",
            },
            ["components"] = new Dictionary<string, object?>
            {
                ["schemas"] = schemas,
                ["securitySchemes"] = new Dictionary<string, object?>
                {
                    ["ApiKey"] = new Dictionary<string, object?>
                    {
                        ["type"] = "apiKey",
                        ["in"] = "header",
                        ["name"] = "X-Namines-Key",
                    },
                },
            },
            ["security"] = new[] { new Dictionary<string, object?> { ["ApiKey"] = Array.Empty<string>() } },
            ["paths"] = paths,
        };
    }

    private static Dictionary<string, object?> TableSchema(SchemaTable table)
    {
        var properties = new Dictionary<string, object?>();
        var required = new List<string>();

        foreach (var column in table.Columns)
        {
            properties[column.Name] = JsonTypeOf(column);

            // Nullable olmayan ve varsayılanı bulunmayan kolonlar zorunludur.
            // Varsayılanı olanı zorunlu göstermek, üretilen istemcide gereksiz
            // yere doldurulması gereken alanlar yaratırdı.
            if (!column.IsNullable && string.IsNullOrWhiteSpace(column.DefaultValue) && !column.IsPK)
                required.Add(column.Name);
        }

        var result = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = properties,
        };
        if (required.Count > 0) result["required"] = required;
        return result;
    }

    /// <summary>
    /// Kanonik tipi JSON Schema tipine çevirir.
    ///
    /// Tamsayı ve ondalık ayrılır (<c>integer</c> / <c>number</c>): ikisini de
    /// "number" saymak, üretilen istemcilerde para alanlarını kayan noktaya
    /// düşürürdü.
    /// </summary>
    private static Dictionary<string, object?> JsonTypeOf(SchemaColumn column)
    {
        var t = (column.Type ?? string.Empty).Trim().ToUpperInvariant();

        var mapped = t switch
        {
            "INT" or "INTEGER" or "SMALLINT" or "TINYINT" or "BIGINT" => new Dictionary<string, object?> { ["type"] = "integer" },
            "BIT" or "BOOLEAN" or "BOOL" => new Dictionary<string, object?> { ["type"] = "boolean" },
            "DECIMAL" or "NUMERIC" or "MONEY" or "FLOAT" or "REAL" or "DOUBLE" =>
                new Dictionary<string, object?> { ["type"] = "number" },
            "DATE" => new Dictionary<string, object?> { ["type"] = "string", ["format"] = "date" },
            "DATETIME" or "DATETIME2" or "TIMESTAMP" =>
                new Dictionary<string, object?> { ["type"] = "string", ["format"] = "date-time" },
            "UUID" or "UNIQUEIDENTIFIER" =>
                new Dictionary<string, object?> { ["type"] = "string", ["format"] = "uuid" },
            "BINARY" or "VARBINARY" or "BLOB" or "IMAGE" =>
                new Dictionary<string, object?> { ["type"] = "string", ["format"] = "byte" },
            _ => new Dictionary<string, object?> { ["type"] = "string" },
        };

        if (column.Length is > 0 && mapped["type"] as string == "string" && !mapped.ContainsKey("format"))
            mapped["maxLength"] = column.Length;

        // Nullable kolonlar JSON Schema'da tip birleşimiyle ifade edilir; yoksa
        // üretilen istemci null geldiğinde doğrulama hatası verir.
        if (column.IsNullable)
            mapped["type"] = new[] { mapped["type"] as string ?? "string", "null" };

        return mapped;
    }

    private static Dictionary<string, object?> ListOperation(SchemaTable table) => new()
    {
        ["summary"] = $"List rows from {table.Name}",
        ["operationId"] = $"list{table.Name}",
        ["responses"] = new Dictionary<string, object?>
        {
            ["200"] = new Dictionary<string, object?>
            {
                ["description"] = "A page of rows.",
                ["content"] = new Dictionary<string, object?>
                {
                    ["application/json"] = new Dictionary<string, object?>
                    {
                        ["schema"] = new Dictionary<string, object?>
                        {
                            ["type"] = "object",
                            ["properties"] = new Dictionary<string, object?>
                            {
                                ["rows"] = new Dictionary<string, object?>
                                {
                                    ["type"] = "array",
                                    ["items"] = Ref(table.Name),
                                },
                                ["page"] = new Dictionary<string, object?> { ["type"] = "integer" },
                                ["pageSize"] = new Dictionary<string, object?> { ["type"] = "integer" },
                                ["totalCount"] = new Dictionary<string, object?> { ["type"] = "integer" },
                            },
                        },
                    },
                },
            },
            ["403"] = new Dictionary<string, object?> { ["description"] = "The API key may not read this table." },
        },
    };

    private static Dictionary<string, object?> DetailOperation(SchemaTable table) => new()
    {
        ["summary"] = $"Fetch a single {table.Name} row by key",
        ["operationId"] = $"get{table.Name}",
        ["responses"] = new Dictionary<string, object?>
        {
            ["200"] = new Dictionary<string, object?>
            {
                ["description"] = "The row.",
                ["content"] = new Dictionary<string, object?>
                {
                    ["application/json"] = new Dictionary<string, object?> { ["schema"] = Ref(table.Name) },
                },
            },
            ["404"] = new Dictionary<string, object?> { ["description"] = "No row for the given key." },
        },
    };

    private static Dictionary<string, object?> WriteOperation(SchemaTable table, string verb) => new()
    {
        ["summary"] = $"{char.ToUpperInvariant(verb[0])}{verb[1..]} a {table.Name} row",
        ["operationId"] = $"{verb}{table.Name}",
        ["responses"] = new Dictionary<string, object?>
        {
            ["200"] = new Dictionary<string, object?> { ["description"] = "Affected row count, and the row when the engine can return it." },
            ["403"] = new Dictionary<string, object?> { ["description"] = "The API key may not write to this table." },
            ["409"] = new Dictionary<string, object?>
            {
                ["description"] =
                    "The write would have affected more than one row and was rolled back: " +
                    "the key column is not unique for the given value.",
            },
        },
    };

    private static Dictionary<string, object?> Ref(string name) => new()
    {
        ["$ref"] = $"#/components/schemas/{name}",
    };
}
