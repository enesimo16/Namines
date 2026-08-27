using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Namines.Core.Models;

namespace Namines.Core.Analysis;

/// <summary>
/// second-phase/11-KODDAN-SEMA.md kademe 1 — <c>schema.prisma</c> dosyasından
/// <see cref="DatabaseSchema"/> çıkarır.
///
/// <b>AI YOK, ayrıştırıcı var.</b> Doc'un açık kuralı: Prisma şeması
/// yapılandırılmış bir dosya; onu modele okutmak hem pahalı hem güvenilmez.
/// Bu sınıf tamamen deterministik ve ağa hiç dokunmuyor.
///
/// <b>Bu, <see cref="Namines.Infrastructure.Generators.PrismaGenerator"/>'ın
/// TERSİ ama simetriği DEĞİL.</b> Üretici kanonik şemadan Prisma yazar; burada
/// insan eliyle yazılmış Prisma okunuyor ve o dosya üreticinin çıktısından
/// çok daha çeşitli olabilir (bilinmeyen nitelikler, özel adlar, henüz
/// desteklemediğimiz özellikler). Bu yüzden ayrıştırıcı tanımadığını
/// SESSİZCE ATLAMAZ, <see cref="SkippedItem"/> olarak bildirir.
/// </summary>
public static class PrismaSchemaParser
{
    /// <summary>Prisma skaler tipleri — bunlar kolon olur. Başka her tip bir ilişki adayıdır.</summary>
    private static readonly Dictionary<string, string> ScalarToSql = new(StringComparer.Ordinal)
    {
        ["String"] = "VARCHAR",
        ["Boolean"] = "BOOLEAN",
        ["Int"] = "INT",
        ["BigInt"] = "BIGINT",
        ["Float"] = "FLOAT",
        ["Decimal"] = "DECIMAL",
        ["DateTime"] = "TIMESTAMP",
        ["Json"] = "JSON",
        ["Bytes"] = "BLOB",
    };

    private static readonly Regex BlockHeader = new(@"^\s*(model|enum)\s+(\w+)\s*\{", RegexOptions.Compiled);
    private static readonly Regex FieldLine = new(@"^\s*(\w+)\s+(\w+)(\[\])?(\?)?\s*(.*)$", RegexOptions.Compiled);
    private static readonly Regex MapAttr = new(@"@map\(\s*""([^""]+)""\s*\)", RegexOptions.Compiled);
    private static readonly Regex BlockMapAttr = new(@"@@map\(\s*""([^""]+)""\s*\)", RegexOptions.Compiled);
    private static readonly Regex NativeSized = new(@"@db\.\w+\(\s*(\d+)\s*\)", RegexOptions.Compiled);
    private static readonly Regex DefaultAttr = new(@"@default\(\s*(.+?)\s*\)\s*(?:@|$)", RegexOptions.Compiled);
    private static readonly Regex RelationFields = new(@"@relation\([^)]*fields:\s*\[([^\]]+)\]", RegexOptions.Compiled);
    private static readonly Regex RelationReferences = new(@"@relation\([^)]*references:\s*\[([^\]]+)\]", RegexOptions.Compiled);
    private static readonly Regex BlockIdAttr = new(@"@@id\(\s*\[([^\]]+)\]", RegexOptions.Compiled);

    public static CodeExtractionResult Parse(string prismaText)
    {
        var schema = new DatabaseSchema { Name = "from-prisma" };
        var parsed = new List<string>();
        var skipped = new List<SkippedItem>();

        // Model adı → tablo (ilişkileri ikinci geçişte çözmek için; bir model
        // kendisinden SONRA tanımlanan bir modele referans verebilir).
        var tablesByModel = new Dictionary<string, SchemaTable>(StringComparer.Ordinal);
        // Bekleyen ilişkiler: (kaynak model, FK alan adları, hedef model, hedef alan adları)
        var pendingRelations = new List<(string SourceModel, string[] FkFields, string TargetModel, string[] RefFields)>();

        var lines = prismaText.Replace("\r\n", "\n").Split('\n');
        var i = 0;

        while (i < lines.Length)
        {
            var header = BlockHeader.Match(lines[i]);
            if (!header.Success) { i++; continue; }

            var kind = header.Groups[1].Value;
            var name = header.Groups[2].Value;
            var body = new List<string>();
            i++;
            while (i < lines.Length && !lines[i].TrimStart().StartsWith("}"))
            {
                body.Add(lines[i]);
                i++;
            }
            i++; // kapanış "}"

            if (kind == "enum")
            {
                var values = body
                    .Select(StripComment)
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0 && !l.StartsWith("@"))
                    .ToList();

                if (values.Count == 0)
                {
                    skipped.Add(new SkippedItem(name, "enum has no values"));
                    continue;
                }

                schema.Enums.Add(new SchemaEnum { Id = name, Name = name, Values = values });
                parsed.Add($"enum {name}");
                continue;
            }

            var table = ParseModel(name, body, skipped, pendingRelations);
            if (table.Columns.Count == 0)
            {
                skipped.Add(new SkippedItem(name, "model has no scalar fields — nothing to build a table from"));
                continue;
            }

            tablesByModel[name] = table;
            schema.Tables.Add(table);
            parsed.Add(name);
        }

        ResolveRelations(schema, tablesByModel, pendingRelations, skipped);

        return new CodeExtractionResult(schema, "prisma", parsed, skipped);
    }

    private static SchemaTable ParseModel(
        string modelName,
        IReadOnlyList<string> body,
        List<SkippedItem> skipped,
        List<(string, string[], string, string[])> pendingRelations)
    {
        var table = new SchemaTable { Id = modelName, Name = modelName };
        var blockPkFields = new List<string>();

        foreach (var raw in body)
        {
            var line = StripComment(raw);
            if (line.Trim().Length == 0) continue;

            // Blok seviyesi nitelikler (@@id, @@map, @@index...) alan değildir.
            if (line.TrimStart().StartsWith("@@"))
            {
                var blockMap = BlockMapAttr.Match(line);
                if (blockMap.Success) table.Name = blockMap.Groups[1].Value;

                var blockId = BlockIdAttr.Match(line);
                if (blockId.Success)
                    blockPkFields.AddRange(blockId.Groups[1].Value.Split(',').Select(s => s.Trim()));

                continue;
            }

            var field = FieldLine.Match(line);
            if (!field.Success)
            {
                skipped.Add(new SkippedItem($"{modelName}.{line.Trim()}", "line could not be parsed as a field"));
                continue;
            }

            var fieldName = field.Groups[1].Value;
            var fieldType = field.Groups[2].Value;
            var isList = field.Groups[3].Success;
            var isOptional = field.Groups[4].Success;
            var attrs = field.Groups[5].Value;

            // Bir model tipine bakan alan KOLON DEĞİL, ilişki alanıdır. Gerçek
            // yabancı anahtar kolonu @relation(fields: [...]) içinde adı geçen
            // AYRI bir skaler alandır ve o zaten kendi satırında tanımlıdır.
            if (!ScalarToSql.ContainsKey(fieldType))
            {
                var fieldsMatch = RelationFields.Match(attrs);
                var refsMatch = RelationReferences.Match(attrs);
                if (fieldsMatch.Success && refsMatch.Success)
                {
                    pendingRelations.Add((
                        modelName,
                        fieldsMatch.Groups[1].Value.Split(',').Select(s => s.Trim()).ToArray(),
                        fieldType,
                        refsMatch.Groups[1].Value.Split(',').Select(s => s.Trim()).ToArray()));
                }
                // Ters yön (`posts Post[]`) ya da @relation'sız bir bağ: FK'yı
                // KARŞI taraf taşıyor, burada kaydedilecek bir şey yok. Bu bir
                // hata değil, o yüzden "atlandı" olarak da bildirilmiyor.
                continue;
            }

            var column = new SchemaColumn
            {
                Id = $"{modelName}.{fieldName}",
                Name = fieldName,
                Type = ScalarToSql[fieldType],
                IsNullable = isOptional,
            };

            if (isList)
            {
                // Prisma'da skaler liste (`String[]`) yalnızca PostgreSQL'de var ve
                // kanonik modelde IsArray olarak karşılığı var.
                column.IsArray = true;
            }

            var map = MapAttr.Match(attrs);
            if (map.Success) column.Name = map.Groups[1].Value;

            var sized = NativeSized.Match(attrs);
            if (sized.Success && int.TryParse(sized.Groups[1].Value, out var len)) column.Length = len;
            else if (column.Type == "VARCHAR") column.Length = 255;

            if (attrs.Contains("@id"))
            {
                column.IsPK = true;
                column.IsNullable = false;
            }

            var def = DefaultAttr.Match(attrs);
            if (def.Success)
            {
                var value = def.Groups[1].Value.Trim();
                if (value is "autoincrement()")
                    column.Identity = true;
                else if (value is not ("uuid()" or "cuid()"))
                    // now()/dbgenerated()/sabitler olduğu gibi taşınıyor; tırnaklar
                    // kanonik modelde de string sabitin parçası.
                    column.DefaultValue = value;
            }

            table.Columns.Add(column);
        }

        // @@id([a, b]) — bileşik anahtar. Alan seviyesi @id'den SONRA uygulanıyor
        // çünkü ikisi aynı modelde bir arada bulunmaz ve blok hâli daha kesindir.
        foreach (var pkField in blockPkFields)
        {
            var col = table.Columns.FirstOrDefault(c => c.Id == $"{table.Id}.{pkField}");
            if (col is null)
            {
                skipped.Add(new SkippedItem($"{modelName}.@@id({pkField})", "composite key references an unknown field"));
                continue;
            }
            col.IsPK = true;
            col.IsNullable = false;
        }

        return table;
    }

    private static void ResolveRelations(
        DatabaseSchema schema,
        Dictionary<string, SchemaTable> tablesByModel,
        List<(string SourceModel, string[] FkFields, string TargetModel, string[] RefFields)> pending,
        List<SkippedItem> skipped)
    {
        foreach (var (sourceModel, fkFields, targetModel, refFields) in pending)
        {
            if (!tablesByModel.TryGetValue(sourceModel, out var sourceTable) ||
                !tablesByModel.TryGetValue(targetModel, out var targetTable))
            {
                skipped.Add(new SkippedItem($"{sourceModel} → {targetModel}", "relation points at a model that was not parsed"));
                continue;
            }

            // Bileşik yabancı anahtarlar kanonik modelde tek kolon çifti olarak
            // ifade ediliyor; ilkini alıp gerisini bildirmek, sessizce yanlış
            // bir ilişki kurmaktan dürüst.
            if (fkFields.Length != 1 || refFields.Length != 1)
            {
                skipped.Add(new SkippedItem(
                    $"{sourceModel} → {targetModel}",
                    $"composite foreign key ({fkFields.Length} columns) — this model only supports single-column relations"));
                continue;
            }

            var sourceCol = sourceTable.Columns.FirstOrDefault(c => c.Id == $"{sourceModel}.{fkFields[0]}");
            var targetCol = targetTable.Columns.FirstOrDefault(c => c.Id == $"{targetModel}.{refFields[0]}");

            if (sourceCol is null || targetCol is null)
            {
                skipped.Add(new SkippedItem($"{sourceModel}.{fkFields[0]} → {targetModel}.{refFields[0]}", "relation references a field that was not parsed"));
                continue;
            }

            sourceCol.IsFK = true;
            schema.Relations.Add(new SchemaRelation
            {
                Id = $"{sourceModel}_{fkFields[0]}_fk",
                Type = "ManyToOne",
                SourceTableId = sourceTable.Id,
                SourceColumnId = sourceCol.Id,
                TargetTableId = targetTable.Id,
                TargetColumnId = targetCol.Id,
            });
        }
    }

    private static string StripComment(string line)
    {
        var idx = line.IndexOf("//", StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
    }
}
