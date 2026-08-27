using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Namines.Core.Models;

namespace Namines.Core.Analysis;

/// <summary>
/// second-phase/11-KODDAN-SEMA.md kademe 2 — C# entity sınıflarından
/// <see cref="DatabaseSchema"/> çıkarır.
///
/// <b>Metin taraması, AST değil</b> — <see cref="AffectedCodeScanner"/> ile aynı
/// gerekçe: bir Roslyn bağımlılığı ve tam derleme, "bir depoya bakıp ne var
/// gör" işinin bedelini kat kat aşardı. Bunun bedeli, tanımadığı yapıyı
/// atlamak; o yüzden atlananlar <see cref="SkippedItem"/> olarak SAYILIYOR ve
/// kullanıcıya gösteriliyor.
///
/// <b>DbContext varsa o otorite.</b> Bir dosyada <c>DbSet&lt;X&gt;</c> görülürse
/// yalnızca o sınıflar tablo sayılır — bir depodaki her POCO tablo değildir
/// (DTO'lar, view model'ler, yapılandırma sınıfları). DbContext yoksa
/// buluşsal yönteme düşülür ve bu, sonuçta açıkça belirtilir.
/// </summary>
public static class EfCoreEntityParser
{
    private static readonly Dictionary<string, string> CSharpToSql = new(StringComparer.Ordinal)
    {
        ["int"] = "INT", ["Int32"] = "INT",
        ["long"] = "BIGINT", ["Int64"] = "BIGINT",
        ["short"] = "SMALLINT", ["Int16"] = "SMALLINT",
        ["byte"] = "TINYINT",
        ["bool"] = "BOOLEAN", ["Boolean"] = "BOOLEAN",
        ["string"] = "VARCHAR", ["String"] = "VARCHAR",
        ["decimal"] = "DECIMAL", ["Decimal"] = "DECIMAL",
        ["double"] = "FLOAT", ["Double"] = "FLOAT",
        ["float"] = "FLOAT", ["Single"] = "FLOAT",
        ["DateTime"] = "TIMESTAMP",
        ["DateTimeOffset"] = "TIMESTAMP",
        ["DateOnly"] = "DATE",
        ["TimeOnly"] = "TIME",
        ["Guid"] = "UUID",
    };

    private static readonly Regex DbSetDecl = new(@"DbSet<(\w+)>", RegexOptions.Compiled);
    private static readonly Regex ClassDecl = new(@"^\s*(?:public|internal)\s+(?:sealed\s+|partial\s+|abstract\s+)*class\s+(\w+)", RegexOptions.Compiled);
    private static readonly Regex PropDecl = new(@"^\s*public\s+(?:virtual\s+|required\s+)*([\w<>\[\],\.]+?)(\?)?\s+(\w+)\s*\{\s*get;\s*(?:set;|init;)", RegexOptions.Compiled);
    private static readonly Regex CollectionType = new(@"^(?:List|ICollection|IEnumerable|HashSet|IList)<(\w+)>$", RegexOptions.Compiled);
    private static readonly Regex TableAttr = new(@"\[Table\(\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex ColumnAttr = new(@"\[Column\(\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex MaxLengthAttr = new(@"\[(?:MaxLength|StringLength)\(\s*(\d+)\s*\)", RegexOptions.Compiled);

    /// <param name="files">Dosya adı → içerik. Ağdan değil, çağırandan gelir.</param>
    public static CodeExtractionResult Parse(IReadOnlyDictionary<string, string> files)
    {
        var schema = new DatabaseSchema { Name = "from-efcore" };
        var parsed = new List<string>();
        var skipped = new List<SkippedItem>();

        // 1. DbContext'ten entity kümesini bul (varsa).
        var dbSetTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var content in files.Values)
            foreach (Match m in DbSetDecl.Matches(content))
                dbSetTypes.Add(m.Groups[1].Value);

        var hasDbContext = dbSetTypes.Count > 0;

        // 2. Her dosyadaki sınıfları çıkar.
        var classes = new Dictionary<string, ParsedClass>(StringComparer.Ordinal);
        foreach (var (fileName, content) in files)
            foreach (var cls in ExtractClasses(fileName, content))
                classes[cls.Name] = cls;

        // 3. Hangileri tablo? DbContext varsa o söyler; yoksa buluşsal.
        var entityNames = hasDbContext
            ? dbSetTypes.Where(classes.ContainsKey).ToList()
            : classes.Values.Where(c => c.Properties.Count > 0).Select(c => c.Name).ToList();

        if (hasDbContext)
        {
            foreach (var missing in dbSetTypes.Where(t => !classes.ContainsKey(t)))
                skipped.Add(new SkippedItem(missing, "declared as a DbSet but its class definition was not among the given files"));
        }

        var entitySet = new HashSet<string>(entityNames, StringComparer.Ordinal);
        var tablesByClass = new Dictionary<string, SchemaTable>(StringComparer.Ordinal);
        var pendingRelations = new List<(string SourceClass, string FkProp, string TargetClass)>();

        foreach (var name in entityNames)
        {
            var cls = classes[name];
            var table = BuildTable(cls, entitySet, skipped, pendingRelations);

            if (table.Columns.Count == 0)
            {
                skipped.Add(new SkippedItem(name, "no mappable scalar properties — treated as not a table"));
                continue;
            }

            tablesByClass[name] = table;
            schema.Tables.Add(table);
            parsed.Add(name);
        }

        ResolveRelations(schema, tablesByClass, pendingRelations, skipped);

        return new CodeExtractionResult(schema, "efcore", parsed, skipped);
    }

    private sealed record ParsedClass(string Name, string? TableNameOverride, List<ParsedProperty> Properties);
    private sealed record ParsedProperty(string Name, string Type, bool IsNullable, string? ColumnNameOverride, int? MaxLength, bool HasKeyAttribute);

    private static IEnumerable<ParsedClass> ExtractClasses(string fileName, string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        ParsedClass? current = null;
        string? pendingTableName = null;
        var pendingAttrs = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("["))
            {
                pendingAttrs.Add(trimmed);
                var t = TableAttr.Match(trimmed);
                if (t.Success) pendingTableName = t.Groups[1].Value;
                continue;
            }

            var cls = ClassDecl.Match(line);
            if (cls.Success)
            {
                if (current is not null) yield return current;
                current = new ParsedClass(cls.Groups[1].Value, pendingTableName, new List<ParsedProperty>());
                pendingTableName = null;
                pendingAttrs.Clear();
                continue;
            }

            var prop = PropDecl.Match(line);
            if (prop.Success && current is not null)
            {
                var attrText = string.Join(" ", pendingAttrs);
                var colOverride = ColumnAttr.Match(attrText);
                var maxLen = MaxLengthAttr.Match(attrText);

                current.Properties.Add(new ParsedProperty(
                    Name: prop.Groups[3].Value,
                    Type: prop.Groups[1].Value,
                    IsNullable: prop.Groups[2].Success,
                    ColumnNameOverride: colOverride.Success ? colOverride.Groups[1].Value : null,
                    MaxLength: maxLen.Success && int.TryParse(maxLen.Groups[1].Value, out var ml) ? ml : null,
                    HasKeyAttribute: attrText.Contains("[Key]")));
            }

            if (!trimmed.StartsWith("[")) pendingAttrs.Clear();
        }

        if (current is not null) yield return current;
    }

    private static SchemaTable BuildTable(
        ParsedClass cls,
        HashSet<string> entityNames,
        List<SkippedItem> skipped,
        List<(string, string, string)> pendingRelations)
    {
        var table = new SchemaTable
        {
            Id = cls.Name,
            Name = cls.TableNameOverride ?? cls.Name,
        };

        // Navigasyon özelliği adları — "FooId + Foo" çiftini tanımak için.
        var navigationTargets = cls.Properties
            .Where(p => entityNames.Contains(p.Type))
            .ToDictionary(p => p.Name, p => p.Type, StringComparer.Ordinal);

        foreach (var prop in cls.Properties)
        {
            // Koleksiyon (`List<Post> Posts`) — ters yön, FK karşı tarafta. Hata değil.
            if (CollectionType.IsMatch(prop.Type)) continue;

            // Tek navigasyon (`Profile Profile`) — kolon değil, ilişki ipucu.
            if (entityNames.Contains(prop.Type)) continue;

            if (!CSharpToSql.TryGetValue(prop.Type, out var sqlType))
            {
                skipped.Add(new SkippedItem($"{cls.Name}.{prop.Name}", $"unmapped type '{prop.Type}'"));
                continue;
            }

            var column = new SchemaColumn
            {
                Id = $"{cls.Name}.{prop.Name}",
                Name = prop.ColumnNameOverride ?? prop.Name,
                Type = sqlType,
                IsNullable = prop.IsNullable,
                Length = sqlType == "VARCHAR" ? (prop.MaxLength ?? 255) : prop.MaxLength,
            };

            // Birincil anahtar: [Key] açıkça söyler, yoksa EF Core'un kendi
            // adlandırma kuralı ("Id" ya da "{Sınıf}Id"). Bu bir tahmin değil,
            // EF'in belgelenmiş varsayılanı.
            if (prop.HasKeyAttribute ||
                prop.Name.Equals("Id", StringComparison.Ordinal) ||
                prop.Name.Equals($"{cls.Name}Id", StringComparison.Ordinal))
            {
                column.IsPK = true;
                column.IsNullable = false;
            }

            // Yabancı anahtar kuralı: "{Nav}Id" adlı bir skaler ve aynı sınıfta
            // "{Nav}" adlı bir navigasyon özelliği varsa.
            if (prop.Name.EndsWith("Id", StringComparison.Ordinal) && !column.IsPK)
            {
                var navName = prop.Name[..^2];
                if (navigationTargets.TryGetValue(navName, out var targetClass))
                    pendingRelations.Add((cls.Name, prop.Name, targetClass));
            }

            table.Columns.Add(column);
        }

        return table;
    }

    private static void ResolveRelations(
        DatabaseSchema schema,
        Dictionary<string, SchemaTable> tablesByClass,
        List<(string SourceClass, string FkProp, string TargetClass)> pending,
        List<SkippedItem> skipped)
    {
        foreach (var (sourceClass, fkProp, targetClass) in pending)
        {
            if (!tablesByClass.TryGetValue(sourceClass, out var sourceTable) ||
                !tablesByClass.TryGetValue(targetClass, out var targetTable))
            {
                skipped.Add(new SkippedItem($"{sourceClass}.{fkProp} → {targetClass}", "relation points at a class that was not parsed as a table"));
                continue;
            }

            var sourceCol = sourceTable.Columns.FirstOrDefault(c => c.Id == $"{sourceClass}.{fkProp}");
            var targetPk = targetTable.Columns.FirstOrDefault(c => c.IsPK);

            if (sourceCol is null || targetPk is null)
            {
                skipped.Add(new SkippedItem($"{sourceClass}.{fkProp} → {targetClass}", "the target table has no primary key to reference"));
                continue;
            }

            sourceCol.IsFK = true;
            schema.Relations.Add(new SchemaRelation
            {
                Id = $"{sourceClass}_{fkProp}_fk",
                Type = "ManyToOne",
                SourceTableId = sourceTable.Id,
                SourceColumnId = sourceCol.Id,
                TargetTableId = targetTable.Id,
                TargetColumnId = targetPk.Id,
            });
        }
    }
}
