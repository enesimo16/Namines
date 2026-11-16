using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Namines.AI.Domain;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Core.Nsl;

namespace Namines.AI.Application;

/// <summary>
/// L0 (topolojik iskelet), L1 (odaklı NSL alt grafiği) ve dalga tabanlı NSL->DatabaseSchema
/// birleştirmesini yöneten sıfır/düşük-token bağlam motoru.
/// </summary>
public sealed class SchemaContextEngine : ISchemaContextEngine
{
    private static readonly Regex CodeFenceRegex = new(
        @"^```[a-zA-Z0-9_-]*\s*|\s*```$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex TableDeclRegex = new(
        @"^\s*table\s+(?:[a-zA-Z0-9_""`]+\.)?(?<name>[a-zA-Z0-9_""`]+)\s*\{",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private static readonly Regex EnumDeclRegex = new(
        @"^\s*enum\s+(?<name>[a-zA-Z0-9_""`]+)\s*\{",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private static readonly Regex FkRefLineRegex = new(
        @"fk\s*\([^)]+\)\s*->\s*(?<target>[a-zA-Z0-9_""`]+)\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ColonColumnRegex = new(
        @"^(?<indent>\s*)(?<col>[a-zA-Z0-9_""`]+)\s*:\s+(?<rest>.+)$",
        RegexOptions.Compiled);

    public SchemaTopologicalSkeleton BuildSkeleton(
        DatabaseSchema schema,
        DatabaseType engine = DatabaseType.PostgreSQL)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var tableById = schema.Tables.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        var outgoing = schema.Tables.ToDictionary(
            t => t.Name,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var incoming = schema.Tables.ToDictionary(
            t => t.Name,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        foreach (var rel in schema.Relations)
        {
            if (tableById.TryGetValue(rel.SourceTableId, out var src) &&
                tableById.TryGetValue(rel.TargetTableId, out var dst) &&
                !string.Equals(src.Name, dst.Name, StringComparison.OrdinalIgnoreCase))
            {
                outgoing[src.Name].Add(dst.Name);
                incoming[dst.Name].Add(src.Name);
            }
        }

        var nodes = new List<TableTopologyNode>(schema.Tables.Count);
        var pkTypes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var snakeCount = 0;
        var pascalCount = 0;

        foreach (var table in schema.Tables)
        {
            if (table.Name.Contains('_', StringComparison.Ordinal))
                snakeCount++;
            else if (table.Name.Length > 0 && char.IsUpper(table.Name[0]))
                pascalCount++;

            var pkCols = table.Columns.Where(c => c.IsPK).ToList();
            var pkSummary = pkCols.Count > 0
                ? string.Join(", ", pkCols.Select(c => $"{c.Name}:{c.Type}"))
                : "NO_PK";

            foreach (var pk in pkCols)
            {
                var key = pk.Type.Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(key))
                    pkTypes[key] = pkTypes.TryGetValue(key, out var cnt) ? cnt + 1 : 1;
            }

            var outList = outgoing[table.Name].OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            var inList = incoming[table.Name].OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

            var tierRole = (outList.Count, inList.Count) switch
            {
                (0, 0) => "Standalone",
                (0, > 0) => "Root",
                ( >= 2, 0) when pkCols.Count >= 2 => "Bridge",
                (_, >= 3) => "Hub",
                (> 0, _) => "Leaf",
                _ => "Standalone",
            };

            nodes.Add(new TableTopologyNode(
                table.Name,
                table.Id,
                pkSummary,
                outList,
                inList,
                tierRole));
        }

        var convention = snakeCount >= pascalCount ? "snake_case" : "PascalCase";
        var dominantPk = pkTypes.Count > 0
            ? pkTypes.OrderByDescending(kv => kv.Value).First().Key
            : "uuid";

        var schemaTitle = string.IsNullOrWhiteSpace(schema.Name) ? "unnamed" : schema.Name;
        var sb = new StringBuilder();
        sb.AppendLine($"SCHEMA {schemaTitle} ({engine}) tables={schema.Tables.Count} rels={schema.Relations.Count} triggers={schema.Triggers.Count} procs={schema.StoredProcedures.Count} pk={dominantPk} style={convention}");
        foreach (var node in nodes)
        {
            sb.Append($"- [{node.TierRole}] {node.TableName}({node.PkSummary})");
            if (node.OutgoingFkTargets.Count > 0)
                sb.Append($" -> {string.Join(",", node.OutgoingFkTargets)}");
            if (node.IncomingFkSources.Count > 0)
                sb.Append($" <- {string.Join(",", node.IncomingFkSources)}");
            sb.AppendLine();
        }
        if (schema.Triggers.Count > 0)
            sb.AppendLine($"TRIGGERS: {string.Join(", ", schema.Triggers.Take(30).Select(t => $"{t.Id}({t.TableId}:{t.Timing} {t.Event})"))}");
        if (schema.StoredProcedures.Count > 0)
            sb.AppendLine($"PROCEDURES: {string.Join(", ", schema.StoredProcedures.Take(30).Select(p => $"{p.Name}({string.Join(",", p.Parameters.Select(x => $"{x.Name}:{x.Type}"))})"))}");

        return new SchemaTopologicalSkeleton(
            schemaTitle,
            engine,
            schema.Tables.Count,
            schema.Relations.Count,
            convention,
            dominantPk,
            nodes,
            sb.ToString().TrimEnd());
    }

    public ScopedNslSubgraph ExtractSubgraph(
        DatabaseSchema schema,
        IReadOnlyList<string> focusTables,
        DatabaseType engine = DatabaseType.PostgreSQL)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var focusSet = new HashSet<string>(
            focusTables ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        if (focusSet.Count == 0 && schema.Tables.Count <= 25)
        {
            foreach (var t in schema.Tables)
                focusSet.Add(t.Name);
        }

        var selectedTables = schema.Tables
            .Where(t => focusSet.Contains(t.Name))
            .ToList();

        var selectedIds = selectedTables
            .Select(t => t.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var neighborIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var focusRelations = new List<SchemaRelation>();

        foreach (var rel in schema.Relations)
        {
            var srcIn = selectedIds.Contains(rel.SourceTableId);
            var dstIn = selectedIds.Contains(rel.TargetTableId);
            if (srcIn && dstIn)
            {
                focusRelations.Add(rel);
            }
            else if (srcIn)
            {
                neighborIds.Add(rel.TargetTableId);
            }
            else if (dstIn)
            {
                neighborIds.Add(rel.SourceTableId);
            }
        }

        var neighborTables = schema.Tables
            .Where(t => neighborIds.Contains(t.Id) && !selectedIds.Contains(t.Id))
            .ToList();

        var neighborSet = neighborTables
            .Select(t => t.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var contracts = neighborTables.Count > 0
            ? ExtractLockedContracts(schema, neighborSet)
                .Where(c => neighborSet.Contains(c.TableName))
                .ToList()
            : new List<TableSignatureContract>();

        var subSchema = new DatabaseSchema
        {
            SchemaId = schema.SchemaId,
            Name = schema.Name,
            Enums = schema.Enums,
            Tables = selectedTables,
            Relations = focusRelations,
        };

        var nslText = selectedTables.Count > 0
            ? NslWriter.Write(subSchema, engine)
            : $"nsl {NslWriter.FormatVersion}\n\nproject \"{schema.Name}\" {{\n  engine {engine.ToString().ToLowerInvariant()}\n}}\n";

        var estimatedTokens = Math.Max(1, nslText.Length / 4);

        return new ScopedNslSubgraph(
            selectedTables.Select(t => t.Name).ToList(),
            contracts,
            nslText,
            estimatedTokens);
    }

    public IReadOnlyList<TableSignatureContract> ExtractLockedContracts(
        DatabaseSchema schema,
        IReadOnlySet<string>? explicitLocks = null)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var tableById = schema.Tables.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        var fksBySourceId = schema.Relations
            .GroupBy(r => r.SourceTableId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var result = new List<TableSignatureContract>(schema.Tables.Count);
        foreach (var table in schema.Tables)
        {
            var pk = table.Columns.FirstOrDefault(c => c.IsPK);
            var pkName = pk?.Name ?? "id";
            var pkType = pk is null ? "uuid" : FormatColumnType(pk);

            var uniqueCols = table.Columns
                .Where(c => table.Uniques.Any(u => u.ColumnIds.Contains(c.Id, StringComparer.OrdinalIgnoreCase)))
                .Select(c => c.Name)
                .ToList();

            var fkSummaries = new List<string>();
            if (fksBySourceId.TryGetValue(table.Id, out var rels))
            {
                foreach (var rel in rels)
                {
                    var srcCol = table.Columns.FirstOrDefault(c => c.Id == rel.SourceColumnId)?.Name ?? rel.SourceColumnId;
                    var dstTable = tableById.TryGetValue(rel.TargetTableId, out var dst) ? dst : null;
                    var dstCol = dstTable?.Columns.FirstOrDefault(c => c.Id == rel.TargetColumnId)?.Name ?? rel.TargetColumnId;
                    var dstName = dstTable?.Name ?? rel.TargetTableId;
                    fkSummaries.Add($"{srcCol}->{dstName}({dstCol})");
                }
            }

            var isLocked = explicitLocks is null || explicitLocks.Contains(table.Name);
            var domain = string.IsNullOrWhiteSpace(schema.Name) ? "core" : schema.Name;

            result.Add(new TableSignatureContract(
                table.Name,
                domain,
                pkName,
                pkType,
                uniqueCols,
                fkSummaries,
                isLocked));
        }

        return result;
    }

    public (DatabaseSchema Merged, IReadOnlyList<string> Notes) MergeNslIntoSchema(
        DatabaseSchema current,
        string rawNslOrJsonFragment,
        DatabaseType engine = DatabaseType.PostgreSQL,
        bool preserveExistingUuids = true)
    {
        ArgumentNullException.ThrowIfNull(current);

        var notes = new List<string>();
        if (string.IsNullOrWhiteSpace(rawNslOrJsonFragment))
        {
            notes.Add("Boş parça geldi; mevcut şema korundu.");
            return (current, notes);
        }

        var cleaned = StripFences(rawNslOrJsonFragment).Trim();

        // JSON fallback: model NSL yerine JSON döndüyse yine veri kaybı olmadan birleştirilir.
        if (cleaned.StartsWith('{'))
        {
            return MergeJsonFragment(current, cleaned, preserveExistingUuids, notes);
        }

        cleaned = NormalizeNslFragment(cleaned);
        var declaredEnums = ExtractDeclaredNames(EnumDeclRegex, cleaned);
        var declaredTables = ExtractDeclaredNames(TableDeclRegex, cleaned);

        // NslParser yalnızca tek metin içindeki tablo ve enum'ları çözdüğü için
        // önceki dalgalardan gelen ve henüz yeniden yazılmamış tabloların hafif NSL
        // tanımlarını NslWriter ile üreterek başa ekliyoruz.
        var stubSchema = new DatabaseSchema
        {
            SchemaId = current.SchemaId,
            Name = current.Name,
            Enums = current.Enums.Where(e => !declaredEnums.Contains(e.Name)).ToList(),
            Tables = current.Tables.Where(t => !declaredTables.Contains(t.Name)).ToList(),
        };

        var stubNsl = NslWriter.Write(stubSchema, engine);
        var fragmentBody = StripHeaderStatements(cleaned);
        var combinedNsl = stubNsl + Environment.NewLine + fragmentBody;

        DatabaseSchema parsed;
        try
        {
            parsed = NslParser.Parse(combinedNsl);
        }
        catch (Exception ex)
        {
            notes.Add($"İlk NSL derlemesinde hata ({ex.Message}); çözülemeyen ileri FK satırları ayrılıp yeniden deneniyor.");
            var knownTables = new HashSet<string>(declaredTables, StringComparer.OrdinalIgnoreCase);
            foreach (var t in stubSchema.Tables)
                knownTables.Add(t.Name);

            var safeFragment = StripUnresolvableForeignKeys(fragmentBody, knownTables, notes);
            var retryNsl = stubNsl + Environment.NewLine + safeFragment;
            try
            {
                parsed = NslParser.Parse(retryNsl);
            }
            catch (Exception ex2)
            {
                notes.Add($"NSL ayrıştırma başarısız oldu: {ex2.Message}");
                return (current, notes);
            }
        }

        return CombineSchemas(current, parsed, declaredTables, declaredEnums, preserveExistingUuids, notes);
    }

    private static (DatabaseSchema Merged, IReadOnlyList<string> Notes) MergeJsonFragment(
        DatabaseSchema current,
        string json,
        bool preserveExistingUuids,
        List<string> notes)
    {
        try
        {
            var sanitized = JsonSanitizerPreprocessor.Sanitize(json);
            var parsed = JsonSerializer.Deserialize<DatabaseSchema>(sanitized, SchemaJsonOptions.Default);
            if (parsed is null)
            {
                notes.Add("JSON şema parçası null döndü.");
                return (current, notes);
            }

            var updatedTables = parsed.Tables.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var updatedEnums = parsed.Enums.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            notes.Add($"JSON fallback üzerinden {parsed.Tables.Count} tablo birleştirildi.");
            return CombineSchemas(current, parsed, updatedTables, updatedEnums, preserveExistingUuids, notes);
        }
        catch (Exception ex)
        {
            notes.Add($"JSON şema ayrıştırma hatası: {ex.Message}");
            return (current, notes);
        }
    }

    private static (DatabaseSchema Merged, IReadOnlyList<string> Notes) CombineSchemas(
        DatabaseSchema current,
        DatabaseSchema parsedFragment,
        HashSet<string> authoredTableNames,
        HashSet<string> authoredEnumNames,
        bool preserveExistingUuids,
        List<string> notes)
    {
        var mergedTables = new Dictionary<string, SchemaTable>(StringComparer.OrdinalIgnoreCase);
        var existingByName = current.Tables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var existing in current.Tables)
            mergedTables[existing.Name] = existing;

        foreach (var incoming in parsedFragment.Tables)
        {
            if (authoredTableNames.Count > 0 &&
                !authoredTableNames.Contains(incoming.Name) &&
                existingByName.ContainsKey(incoming.Name))
            {
                continue;
            }

            var alignedId = SchemaIdConvention.TableId(incoming.Name);
            SchemaTable? prev = null;
            if (existingByName.TryGetValue(incoming.Name, out var foundPrev))
                prev = foundPrev;

            var tableId = preserveExistingUuids && prev is not null ? prev.Id : alignedId;
            var stableUuid = preserveExistingUuids && prev is not null && !string.IsNullOrWhiteSpace(prev.StableUuid)
                ? prev.StableUuid
                : incoming.StableUuid;

            var prevColByName = prev?.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            var colIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var normalizedCols = new List<SchemaColumn>(incoming.Columns.Count);
            foreach (var col in incoming.Columns)
            {
                var colId = preserveExistingUuids &&
                            prevColByName is not null &&
                            prevColByName.TryGetValue(col.Name, out var prevCol)
                    ? prevCol.Id
                    : $"{tableId}_{SchemaIdConvention.Normalize(col.Name)}";

                var colStableUuid = preserveExistingUuids &&
                                    prevColByName is not null &&
                                    prevColByName.TryGetValue(col.Name, out var prevColUuid) &&
                                    !string.IsNullOrWhiteSpace(prevColUuid.StableUuid)
                    ? prevColUuid.StableUuid
                    : col.StableUuid;

                colIdMap[col.Id] = colId;
                normalizedCols.Add(new SchemaColumn
                {
                    Id = colId,
                    Name = col.Name,
                    StableUuid = colStableUuid,
                    Type = col.Type,
                    Length = col.Length,
                    Scale = col.Scale,
                    IsPK = col.IsPK,
                    IsFK = col.IsFK,
                    IsNullable = col.IsNullable,
                    DefaultValue = col.DefaultValue,
                    Identity = col.Identity,
                    EnumRef = col.EnumRef,
                    Generated = col.Generated,
                    Collation = col.Collation,
                    IsArray = col.IsArray,
                });
            }

            string MapColId(string oldColId) => colIdMap.TryGetValue(oldColId, out var mapped) ? mapped : oldColId;

            var mappedIndexes = incoming.Indexes.Select(idx => new SchemaIndex
            {
                Id = idx.Id,
                Name = idx.Name,
                IsUnique = idx.IsUnique,
                Where = idx.Where,
                Columns = idx.Columns.Select(c => new SchemaIndexColumn
                {
                    ColumnId = MapColId(c.ColumnId),
                    Descending = c.Descending,
                }).ToList(),
            }).ToList();

            var mappedUniques = incoming.Uniques.Select(u => new SchemaUnique
            {
                Id = u.Id,
                Name = u.Name,
                ColumnIds = u.ColumnIds.Select(MapColId).ToList(),
            }).ToList();

            mergedTables[incoming.Name] = new SchemaTable
            {
                Id = tableId,
                Name = incoming.Name,
                StableUuid = string.IsNullOrWhiteSpace(stableUuid) ? SchemaIdentity.ForTable(incoming.Name) : stableUuid,
                Columns = normalizedCols,
                Indexes = mappedIndexes,
                Uniques = mappedUniques,
                Checks = incoming.Checks.ToList(),
                EnableRls = incoming.EnableRls || (prev?.EnableRls ?? false),
                RlsPolicies = incoming.RlsPolicies.Count > 0 ? incoming.RlsPolicies.ToList() : (prev?.RlsPolicies.ToList() ?? new()),
            };
        }

        var mergedEnums = new Dictionary<string, SchemaEnum>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in current.Enums)
            mergedEnums[e.Name] = e;
        foreach (var e in parsedFragment.Enums)
        {
            if (authoredEnumNames.Count == 0 || authoredEnumNames.Contains(e.Name))
                mergedEnums[e.Name] = e;
        }

        var relKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mergedRelations = new List<SchemaRelation>();

        void TryAddRelation(SchemaRelation rel, DatabaseSchema owner)
        {
            var origSrcTable = owner.Tables.FirstOrDefault(t => string.Equals(t.Id, rel.SourceTableId, StringComparison.OrdinalIgnoreCase));
            var origDstTable = owner.Tables.FirstOrDefault(t => string.Equals(t.Id, rel.TargetTableId, StringComparison.OrdinalIgnoreCase));
            if (origSrcTable is null || origDstTable is null) return;

            var origSrcColName = origSrcTable.Columns.FirstOrDefault(c => c.Id == rel.SourceColumnId)?.Name;
            var origDstColName = origDstTable.Columns.FirstOrDefault(c => c.Id == rel.TargetColumnId)?.Name;
            if (origSrcColName is null || origDstColName is null) return;

            if (!mergedTables.TryGetValue(origSrcTable.Name, out var targetSrcTable) ||
                !mergedTables.TryGetValue(origDstTable.Name, out var targetDstTable))
                return;

            var targetSrcCol = targetSrcTable.Columns.FirstOrDefault(c => string.Equals(c.Name, origSrcColName, StringComparison.OrdinalIgnoreCase));
            var targetDstCol = targetDstTable.Columns.FirstOrDefault(c => string.Equals(c.Name, origDstColName, StringComparison.OrdinalIgnoreCase));
            if (targetSrcCol is null || targetDstCol is null) return;

            targetSrcCol.IsFK = true;

            var key = $"{targetSrcTable.Name}.{targetSrcCol.Name}->{targetDstTable.Name}.{targetDstCol.Name}";
            if (!relKeys.Add(key)) return;

            mergedRelations.Add(new SchemaRelation
            {
                Id = string.IsNullOrWhiteSpace(rel.Id)
                    ? $"{targetSrcTable.Name}_{targetSrcCol.Name}_fk"
                    : rel.Id,
                Type = string.IsNullOrWhiteSpace(rel.Type) ? "ManyToOne" : rel.Type,
                SourceTableId = targetSrcTable.Id,
                SourceColumnId = targetSrcCol.Id,
                TargetTableId = targetDstTable.Id,
                TargetColumnId = targetDstCol.Id,
                OnDelete = rel.OnDelete,
                OnUpdate = rel.OnUpdate,
            });
        }

        foreach (var rel in parsedFragment.Relations)
            TryAddRelation(rel, parsedFragment);
        foreach (var rel in current.Relations)
            TryAddRelation(rel, current);

        var mergedTriggers = new Dictionary<string, SchemaTrigger>(StringComparer.OrdinalIgnoreCase);
        foreach (var trg in current.Triggers)
            mergedTriggers[!string.IsNullOrWhiteSpace(trg.Id) ? trg.Id : trg.StableUuid] = trg;
        foreach (var trg in parsedFragment.Triggers)
            mergedTriggers[!string.IsNullOrWhiteSpace(trg.Id) ? trg.Id : trg.StableUuid] = trg;

        var mergedProcedures = new Dictionary<string, SchemaStoredProcedure>(StringComparer.OrdinalIgnoreCase);
        foreach (var sp in current.StoredProcedures)
            mergedProcedures[!string.IsNullOrWhiteSpace(sp.Name) ? $"{sp.Name}:{sp.TargetEngine}" : sp.Id] = sp;
        foreach (var sp in parsedFragment.StoredProcedures)
            mergedProcedures[!string.IsNullOrWhiteSpace(sp.Name) ? $"{sp.Name}:{sp.TargetEngine}" : sp.Id] = sp;

        var merged = new DatabaseSchema
        {
            SchemaId = string.IsNullOrWhiteSpace(current.SchemaId) ? Guid.NewGuid().ToString() : current.SchemaId,
            Name = !string.IsNullOrWhiteSpace(current.Name) ? current.Name : parsedFragment.Name,
            CloudProvider = current.CloudProvider ?? parsedFragment.CloudProvider,
            IncludeBiModule = current.IncludeBiModule || parsedFragment.IncludeBiModule,
            Tables = mergedTables.Values.ToList(),
            Relations = mergedRelations,
            Enums = mergedEnums.Values.ToList(),
            Triggers = mergedTriggers.Values.ToList(),
            StoredProcedures = mergedProcedures.Values.ToList(),
        };

        return (merged, notes);
    }

    private static string FormatColumnType(SchemaColumn col)
    {
        if (!string.IsNullOrWhiteSpace(col.EnumRef))
            return col.EnumRef;
        if (col.Length is > 0 && col.Scale is > 0)
            return $"{col.Type}({col.Length},{col.Scale})";
        if (col.Length is > 0)
            return $"{col.Type}({col.Length})";
        return col.Type;
    }

    private static string StripFences(string raw)
        => CodeFenceRegex.Replace(raw, string.Empty).Trim();

    private static string NormalizeNslFragment(string text)
    {
        var sb = new StringBuilder();
        var started = false;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart();

            if (!started)
            {
                if (trimmed.StartsWith("nsl ", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("project ", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("enum ", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("table ", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    started = true;
                }
                else
                {
                    continue;
                }
            }

            // Modeller ara sıra "col: type" şeklinde iki nokta koyabiliyor; NslParser boşluklu biçim bekler.
            var colonMatch = ColonColumnRegex.Match(line);
            if (colonMatch.Success &&
                !trimmed.StartsWith("table ", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("enum ", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("project ", StringComparison.OrdinalIgnoreCase))
            {
                line = $"{colonMatch.Groups["indent"].Value}{colonMatch.Groups["col"].Value} {colonMatch.Groups["rest"].Value}";
            }

            sb.AppendLine(line.TrimEnd(','));
        }

        return sb.Length > 0 ? sb.ToString() : text;
    }

    private static string StripHeaderStatements(string nsl)
    {
        var lines = nsl.Split('\n');
        var sb = new StringBuilder();
        var insideProject = false;
        foreach (var raw in lines)
        {
            var trimmed = raw.Trim();
            if (trimmed.StartsWith("nsl ", StringComparison.OrdinalIgnoreCase))
                continue;
            if (trimmed.StartsWith("project ", StringComparison.OrdinalIgnoreCase))
            {
                insideProject = trimmed.Contains('{');
                continue;
            }
            if (insideProject)
            {
                if (trimmed == "}") insideProject = false;
                continue;
            }
            sb.AppendLine(raw.TrimEnd('\r'));
        }
        return sb.ToString();
    }

    private static HashSet<string> ExtractDeclaredNames(Regex regex, string nsl)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in regex.Matches(nsl))
        {
            var name = m.Groups["name"].Value.Trim('"', '`');
            if (!string.IsNullOrWhiteSpace(name))
                set.Add(name);
        }
        return set;
    }

    private static string StripUnresolvableForeignKeys(
        string fragment,
        HashSet<string> knownTables,
        List<string> notes)
    {
        var sb = new StringBuilder();
        foreach (var rawLine in fragment.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var m = FkRefLineRegex.Match(line);
            if (m.Success)
            {
                var target = m.Groups["target"].Value.Trim('"', '`');
                if (!knownTables.Contains(target))
                {
                    notes.Add($"İleri dalga FK hedefi '{target}' henüz tanımlı olmadığı için ayrıldı.");
                    continue;
                }
            }
            sb.AppendLine(line);
        }
        return sb.ToString();
    }
}
