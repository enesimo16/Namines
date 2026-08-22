using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Nsl;

/// <summary>
/// Kanonik JSON ara temsili (04 §3).
///
/// <b>NSL metninin makine tarafı.</b> Aynı bilgiyi taşır ama insan için değil,
/// başka araçlar için: CI'da doğrulanabilir, diff'lenebilir, başka dillerde
/// okunabilir. <see cref="NslWriter"/> okunabilirliği, bu ise kararlılığı
/// önceliklendirir.
///
/// <b>Sürüm alanı (<c>nsl</c>) ilk alan ve zorunlu.</b> Sürümsüz bir biçim, ileride
/// değiştiğinde eski dosyaları sessizce yanlış okur — ve bunu ancak veriler
/// bozulunca fark edersin.
///
/// <b>Modelin taşımadığı alanlar burada YOK.</b> Doküman <c>@ui</c>, <c>@tag</c>,
/// view ve RLS'ten de söz ediyor; onları boş yer tutucu olarak yazmak, dosyayı
/// okuyan bir araca "bu bilgi var ama boş" dedirtirdi. Yokluk, "henüz
/// desteklenmiyor"un dürüst hâli.
/// </summary>
public static class CanonicalIr
{
    public const string Version = "1.0";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string Write(DatabaseSchema schema, DatabaseType engine = DatabaseType.PostgreSQL)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var root = new JsonObject
        {
            ["nsl"] = Version,
            ["project"] = new JsonObject
            {
                ["name"] = schema.Name ?? string.Empty,
                ["engine"] = engine.ToString().ToLowerInvariant(),
            },
            ["enums"] = new JsonArray(schema.Enums.Select(EnumNode).ToArray()),
            ["tables"] = new JsonArray(schema.Tables.Select(t => TableNode(schema, t)).ToArray()),
            ["relations"] = new JsonArray(schema.Relations.Select(r => RelationNode(schema, r)).ToArray()),
        };

        return root.ToJsonString(WriteOptions);
    }

    public static DatabaseSchema Read(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var root = JsonNode.Parse(json)?.AsObject()
                   ?? throw new NslParseException(0, "The canonical IR must be a JSON object.");

        var version = root["nsl"]?.GetValue<string>();

        // Sürüm kontrolü sessizce atlanamaz: farklı sürümdeki bir dosyayı bu
        // sürümün kurallarıyla okumak, alanları yanlış yorumlamak demektir.
        if (version != Version)
            throw new NslParseException(0,
                $"This file declares nsl \"{version}\", but this build reads \"{Version}\".");

        var schema = new DatabaseSchema
        {
            Name = root["project"]?["name"]?.GetValue<string>() ?? string.Empty,
        };

        foreach (var node in root["enums"]?.AsArray() ?? new JsonArray())
        {
            var e = node!.AsObject();
            schema.Enums.Add(new SchemaEnum
            {
                Id = Guid.NewGuid().ToString(),
                Name = e["name"]!.GetValue<string>(),
                StableUuid = e["uuid"]?.GetValue<string>() ?? SchemaIdentity.ForTable("enum:" + e["name"]!.GetValue<string>()),
                Values = (e["values"]?.AsArray() ?? new JsonArray()).Select(v => v!.GetValue<string>()).ToList(),
            });
        }

        foreach (var node in root["tables"]?.AsArray() ?? new JsonArray())
            schema.Tables.Add(ReadTable(node!.AsObject()));

        foreach (var node in root["relations"]?.AsArray() ?? new JsonArray())
            ReadRelation(schema, node!.AsObject());

        return schema;
    }

    // ── Yazma ────────────────────────────────────────────────────────────────

    private static JsonObject EnumNode(SchemaEnum definition) => new()
    {
        ["uuid"] = definition.StableUuid,
        ["name"] = definition.Name,
        ["values"] = new JsonArray(definition.Values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray()),
    };

    private static JsonObject TableNode(DatabaseSchema schema, SchemaTable table)
    {
        var pkCount = table.Columns.Count(c => c.IsPK);

        return new JsonObject
        {
            ["uuid"] = table.StableUuid,
            ["name"] = table.Name,
            ["columns"] = new JsonArray(table.Columns.Select(c => ColumnNode(c, pkCount)).ToArray()),
            ["primaryKey"] = new JsonArray(
                table.Columns.Where(c => c.IsPK).Select(c => (JsonNode)JsonValue.Create(c.Name)!).ToArray()),
            ["uniques"] = new JsonArray(table.Uniques.Select(u => (JsonNode)new JsonObject
            {
                ["uuid"] = u.StableUuid,
                ["name"] = u.Name,
                ["columns"] = new JsonArray(Names(table, u.ColumnIds).ToArray()),
            }).ToArray()),
            ["indexes"] = new JsonArray(table.Indexes.Select(i => (JsonNode)new JsonObject
            {
                ["uuid"] = i.StableUuid,
                ["name"] = i.Name,
                ["unique"] = i.IsUnique,
                ["columns"] = new JsonArray(i.Columns
                    .Select(c => (JsonNode)new JsonObject
                    {
                        ["column"] = table.Columns.FirstOrDefault(x => x.Id == c.ColumnId)?.Name,
                        ["order"] = c.Descending ? "desc" : "asc",
                    }).ToArray()),
            }).ToArray()),
            ["checks"] = new JsonArray(table.Checks.Select(c => (JsonNode)new JsonObject
            {
                ["name"] = c.Name,
                ["expression"] = c.Expression,
            }).ToArray()),
        };
    }

    private static JsonObject ColumnNode(SchemaColumn column, int primaryKeyCount)
    {
        var type = new JsonObject
        {
            ["base"] = string.IsNullOrWhiteSpace(column.Type) ? "text" : column.Type.ToLowerInvariant(),
            ["length"] = column.Length,
            ["array"] = column.IsArray,
            ["enumRef"] = column.EnumRef,
        };

        return new JsonObject
        {
            ["uuid"] = column.StableUuid,
            ["name"] = column.Name,
            ["type"] = type,
            ["nullable"] = column.IsNullable,
            ["primaryKey"] = column.IsPK,
            // Çıkarımın SONUCU da yazılıyor, yalnızca kullanıcının söylediği değil:
            // bu dosyayı okuyan bir araç "bu anahtarı kim atıyor?" sorusunu
            // çıkarım kurallarını yeniden uygulamadan cevaplayabilmeli.
            ["identity"] = column.Identity,
            ["identityEffective"] = IdentityPolicy.IsGenerated(column, primaryKeyCount),
            ["default"] = column.DefaultValue,
            ["generated"] = column.Generated,
            ["collation"] = column.Collation,
        };
    }

    private static JsonObject RelationNode(DatabaseSchema schema, SchemaRelation relation)
    {
        var (sourceTable, sourceColumn) = Locate(schema, relation.SourceTableId, relation.SourceColumnId);
        var (targetTable, targetColumn) = Locate(schema, relation.TargetTableId, relation.TargetColumnId);

        return new JsonObject
        {
            ["from"] = new JsonObject { ["table"] = sourceTable, ["column"] = sourceColumn },
            ["to"] = new JsonObject { ["table"] = targetTable, ["column"] = targetColumn },
            ["onDelete"] = relation.OnDelete.ToString().ToLowerInvariant(),
            ["onUpdate"] = relation.OnUpdate.ToString().ToLowerInvariant(),
        };
    }

    // ── Okuma ────────────────────────────────────────────────────────────────

    private static SchemaTable ReadTable(JsonObject node)
    {
        var name = node["name"]!.GetValue<string>();
        var table = new SchemaTable
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            StableUuid = node["uuid"]?.GetValue<string>() ?? SchemaIdentity.ForTable(name),
        };

        foreach (var columnNode in node["columns"]?.AsArray() ?? new JsonArray())
        {
            var c = columnNode!.AsObject();
            var type = c["type"]?.AsObject() ?? new JsonObject();
            var columnName = c["name"]!.GetValue<string>();

            table.Columns.Add(new SchemaColumn
            {
                Id = Guid.NewGuid().ToString(),
                Name = columnName,
                StableUuid = c["uuid"]?.GetValue<string>() ?? SchemaIdentity.ForColumn(name, columnName),
                Type = (type["base"]?.GetValue<string>() ?? "text").ToUpperInvariant(),
                Length = type["length"]?.GetValue<int?>(),
                IsArray = type["array"]?.GetValue<bool>() ?? false,
                EnumRef = type["enumRef"]?.GetValue<string>(),
                IsNullable = c["nullable"]?.GetValue<bool>() ?? false,
                IsPK = c["primaryKey"]?.GetValue<bool>() ?? false,
                // "identityEffective" BİLEREK okunmuyor: o, çıkarımın sonucu.
                // Geri yazarken kullanıcının söylediğini değil hesaplananı
                // saklamak, "söylenmedi" ile "evet dendi" arasındaki farkı yok eder.
                Identity = c["identity"]?.GetValue<bool?>(),
                DefaultValue = c["default"]?.GetValue<string>(),
                Generated = c["generated"]?.GetValue<string>(),
                Collation = c["collation"]?.GetValue<string>(),
            });
        }

        foreach (var checkNode in node["checks"]?.AsArray() ?? new JsonArray())
        {
            var c = checkNode!.AsObject();
            table.Checks.Add(new SchemaCheck
            {
                Id = Guid.NewGuid().ToString(),
                Name = c["name"]?.GetValue<string>(),
                Expression = c["expression"]?.GetValue<string>() ?? string.Empty,
            });
        }

        foreach (var uniqueNode in node["uniques"]?.AsArray() ?? new JsonArray())
        {
            var u = uniqueNode!.AsObject();
            table.Uniques.Add(new SchemaUnique
            {
                Id = Guid.NewGuid().ToString(),
                Name = u["name"]?.GetValue<string>(),
                ColumnIds = ResolveIds(table, u["columns"]),
            });
        }

        foreach (var indexNode in node["indexes"]?.AsArray() ?? new JsonArray())
        {
            var i = indexNode!.AsObject();
            var index = new SchemaIndex
            {
                Id = Guid.NewGuid().ToString(),
                Name = i["name"]?.GetValue<string>(),
                IsUnique = i["unique"]?.GetValue<bool>() ?? false,
            };

            foreach (var columnNode in i["columns"]?.AsArray() ?? new JsonArray())
            {
                var c = columnNode!.AsObject();
                var target = table.Columns.FirstOrDefault(x => x.Name == c["column"]?.GetValue<string>());
                if (target is null) continue;

                index.Columns.Add(new SchemaIndexColumn
                {
                    ColumnId = target.Id,
                    Descending = c["order"]?.GetValue<string>() == "desc",
                });
            }

            table.Indexes.Add(index);
        }

        return table;
    }

    private static void ReadRelation(DatabaseSchema schema, JsonObject node)
    {
        var fromTable = schema.Tables.FirstOrDefault(t => t.Name == node["from"]?["table"]?.GetValue<string>());
        var toTable = schema.Tables.FirstOrDefault(t => t.Name == node["to"]?["table"]?.GetValue<string>());
        if (fromTable is null || toTable is null) return;

        var fromColumn = fromTable.Columns.FirstOrDefault(c => c.Name == node["from"]?["column"]?.GetValue<string>());
        var toColumn = toTable.Columns.FirstOrDefault(c => c.Name == node["to"]?["column"]?.GetValue<string>());
        if (fromColumn is null || toColumn is null) return;

        schema.Relations.Add(new SchemaRelation
        {
            Id = Guid.NewGuid().ToString(),
            Type = "many-to-one",
            SourceTableId = fromTable.Id,
            SourceColumnId = fromColumn.Id,
            TargetTableId = toTable.Id,
            TargetColumnId = toColumn.Id,
            OnDelete = ParseAction(node["onDelete"]?.GetValue<string>()),
            OnUpdate = ParseAction(node["onUpdate"]?.GetValue<string>()),
        });
    }

    /// <summary>
    /// Bilinmeyen bir referans fiili <see cref="ReferentialAction.NoAction"/>'a
    /// düşer — <see cref="Namines.Core.Models.SchemaRelation.OnDelete"/>'teki
    /// ilkeyle aynı: varsayılan asla veri kaybına doğru düşmemeli.
    /// </summary>
    private static ReferentialAction ParseAction(string? value) =>
        Enum.TryParse<ReferentialAction>(value?.Replace(" ", string.Empty), ignoreCase: true, out var parsed)
            ? parsed
            : ReferentialAction.NoAction;

    // ── Yardımcılar ──────────────────────────────────────────────────────────

    private static IEnumerable<JsonNode> Names(SchemaTable table, IEnumerable<string> columnIds) =>
        columnIds
            .Select(id => table.Columns.FirstOrDefault(c => c.Id == id)?.Name)
            .Where(n => n is not null)
            .Select(n => (JsonNode)JsonValue.Create(n)!);

    private static List<string> ResolveIds(SchemaTable table, JsonNode? names) =>
        (names?.AsArray() ?? new JsonArray())
            .Select(n => table.Columns.FirstOrDefault(c => c.Name == n!.GetValue<string>())?.Id)
            .Where(id => id is not null)
            .Select(id => id!)
            .ToList();

    private static (string? Table, string? Column) Locate(DatabaseSchema schema, string tableId, string columnId)
    {
        var table = schema.Tables.FirstOrDefault(t => t.Id == tableId);
        var column = table?.Columns.FirstOrDefault(c => c.Id == columnId);
        return (table?.Name, column?.Name);
    }
}
