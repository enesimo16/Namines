using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.Eject;

/// <summary>
/// <c>contract.jsonschema</c> — 12 §P2. JSON Schema draft 2020-12.
///
/// <see cref="Namines.Core.Analysis.GatewayOpenApiGenerator"/> ile karışmasın:
/// orası Gateway'in HTTP sözleşmesini üretiyor (yollar, güvenlik şeması, izinli
/// tablolar). Bu hedef yalnızca VERİ ŞEKLİNİ üretiyor ve izinlerden bağımsız —
/// kullanıcının kendi şemasının tamamı, doğrulama aracına verilmek üzere.
/// </summary>
public sealed class JsonSchemaGenerator : IEjectGenerator
{
    public string Target => "contract.jsonschema";
    public string DisplayName => "JSON Schema";

    public EjectResult Generate(DatabaseSchema schema, DatabaseType engine)
    {
        var warnings = new List<string>();
        EjectNaming.CollectUnsupported(schema, warnings, supportsIndexes: false, supportsUniques: false);

        var definitions = new Dictionary<string, object?>();

        foreach (var table in schema.Tables)
        {
            var properties = new Dictionary<string, object?>();
            var required = new List<string>();

            foreach (var column in table.Columns)
            {
                properties[column.Name] = JsonType(column);
                // Varsayılanı olan kolon zorunlu değildir; zorunlu göstermek
                // doğrulamayı gereksiz yere katılaştırırdı.
                if (!column.IsNullable && string.IsNullOrWhiteSpace(column.DefaultValue))
                    required.Add(column.Name);
            }

            var definition = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = properties,
                // additionalProperties:false bilinçli DEĞİL: şema değişip yeni bir
                // kolon eklendiğinde eski doğrulayıcılar tüm kayıtları reddederdi.
                ["additionalProperties"] = true,
            };
            if (required.Count > 0) definition["required"] = required;

            definitions[table.Name] = definition;
        }

        var document = new Dictionary<string, object?>
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = string.IsNullOrWhiteSpace(schema.Name) ? "Namines schema" : schema.Name,
            ["$defs"] = definitions,
        };

        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });

        return new EjectResult(
            new Dictionary<string, string> { ["schema.json"] = json },
            warnings);
    }

    private static Dictionary<string, object?> JsonType(SchemaColumn column)
    {
        var kind = CanonicalType.Classify(column.Type);

        var node = new Dictionary<string, object?>();
        switch (kind)
        {
            // Tamsayı ve ondalık ayrı: ikisini de "number" saymak, para alanlarını
            // kayan noktaya düşürür.
            case TypeKind.Integer or TypeKind.Long: node["type"] = "integer"; break;
            case TypeKind.Decimal or TypeKind.Double: node["type"] = "number"; break;
            case TypeKind.Boolean: node["type"] = "boolean"; break;
            case TypeKind.Uuid: node["type"] = "string"; node["format"] = "uuid"; break;
            case TypeKind.Date: node["type"] = "string"; node["format"] = "date"; break;
            case TypeKind.Time: node["type"] = "string"; node["format"] = "time"; break;
            case TypeKind.DateTime: node["type"] = "string"; node["format"] = "date-time"; break;
            case TypeKind.Binary: node["type"] = "string"; node["contentEncoding"] = "base64"; break;
            case TypeKind.Json: break; // herhangi bir tip
            default:
                node["type"] = "string";
                if (column.Length is > 0) node["maxLength"] = column.Length;
                break;
        }

        if (column.IsNullable && node.TryGetValue("type", out var type) && type is string single)
            node["type"] = new[] { single, "null" };

        return node;
    }
}

/// <summary>
/// <c>contract.protobuf</c> — 12 §P3. Protocol Buffers tanımları.
///
/// Alan numaraları kolon SIRASINDAN türetiliyor ve bu bir risk: protobuf'ta alan
/// numarası sözleşmenin kendisidir, değişirse eski istemciler veriyi yanlış okur.
/// Şemaya ortadan bir kolon eklendiğinde sonraki tüm numaralar kayar. Bu yüzden
/// dosyanın başına uyarı yazılıyor — üretilen .proto bir BAŞLANGIÇ noktasıdır,
/// sürümler arası kararlılık elle yönetilmelidir.
/// </summary>
public sealed class ProtobufGenerator : IEjectGenerator
{
    public string Target => "contract.protobuf";
    public string DisplayName => "Protocol Buffers";

    public EjectResult Generate(DatabaseSchema schema, DatabaseType engine)
    {
        var warnings = new List<string>
        {
            "Field numbers are derived from column order. Inserting a column later shifts " +
            "every following number and breaks wire compatibility with existing clients — " +
            "pin the numbers by hand once this contract is published.",
        };
        EjectNaming.CollectUnsupported(schema, warnings, supportsIndexes: false, supportsUniques: false);

        var sb = new StringBuilder();
        sb.AppendLine("// Generated by Namines. Do not edit by hand.");
        sb.AppendLine("// WARNING: field numbers follow column order; see the export warnings.");
        sb.AppendLine("syntax = \"proto3\";");
        sb.AppendLine();

        var needsTimestamp = schema.Tables.SelectMany(t => t.Columns)
            .Any(c => CanonicalType.Classify(c.Type) is TypeKind.Date or TypeKind.Time or TypeKind.DateTime);
        if (needsTimestamp)
        {
            sb.AppendLine("import \"google/protobuf/timestamp.proto\";");
            sb.AppendLine();
        }

        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"message {EjectNaming.Pascal(table.Name)} {{");
            var number = 1;
            foreach (var column in table.Columns)
                sb.AppendLine($"  {ProtoType(column)} {EjectNaming.Snake(column.Name)} = {number++};");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        return new EjectResult(
            new Dictionary<string, string> { ["schema.proto"] = sb.ToString() },
            warnings);
    }

    private static string ProtoType(SchemaColumn column) => CanonicalType.Classify(column.Type) switch
    {
        TypeKind.Integer => "int32",
        TypeKind.Long => "int64",
        TypeKind.Decimal or TypeKind.Double => "double",
        TypeKind.Boolean => "bool",
        TypeKind.Date or TypeKind.Time or TypeKind.DateTime => "google.protobuf.Timestamp",
        TypeKind.Binary => "bytes",
        _ => "string",
    };
}
