using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Namines.AI.Domain;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.AI.Application;

/// <summary>
/// Yetim kalmış <c>*_id</c> / <c>*Id</c> kolonlarını mevcut tablo birincil anahtarlarıyla
/// 0-token deterministik olarak eşleyip <see cref="SchemaRelation"/> + FK indeksi ören yapıcı alt-ajan.
/// </summary>
public sealed class RelationWeaverSubAgent : ISubAgent<OrchestrationWorkspace, IReadOnlyList<string>>
{
    public AgentKind Kind => AgentKind.RelationWeaver;

    public Task<IReadOnlyList<string>> ExecuteAsync(
        AgentInstance self,
        OrchestrationWorkspace workspace,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var schema = workspace.CurrentSchema;
        var notes = new List<string>();
        if (schema.Tables.Count < 2)
            return Task.FromResult<IReadOnlyList<string>>(notes);

        var targetsByKey = BuildTargetLookup(schema.Tables);
        var existingEdges = BuildExistingRelationKeys(schema.Relations);

        foreach (var srcTable in schema.Tables)
        {
            foreach (var col in srcTable.Columns)
            {
                if (col.IsPK) continue;
                if (!TryExtractTargetStem(col.Name, out var stem)) continue;
                if (!TryResolveTarget(targetsByKey, stem, srcTable.Id, out var targetTable, out var targetPk)) continue;

                var edgeKey = $"{srcTable.Id}.{col.Id}->{targetTable.Id}.{targetPk.Id}";
                if (existingEdges.Contains(edgeKey)) continue;

                col.IsFK = true;
                if (!string.Equals(col.Type, targetPk.Type, StringComparison.OrdinalIgnoreCase) ||
                    col.Length != targetPk.Length ||
                    col.Scale != targetPk.Scale)
                {
                    col.Type = targetPk.Type;
                    col.Length = targetPk.Length;
                    col.Scale = targetPk.Scale;
                }

                schema.Relations.Add(new SchemaRelation
                {
                    Id = $"rel_{SchemaIdConvention.Normalize(srcTable.Name)}_{SchemaIdConvention.Normalize(col.Name)}",
                    Type = "OneToMany",
                    SourceTableId = srcTable.Id,
                    SourceColumnId = col.Id,
                    TargetTableId = targetTable.Id,
                    TargetColumnId = targetPk.Id,
                    OnDelete = ReferentialAction.NoAction,
                    OnUpdate = ReferentialAction.NoAction,
                });
                existingEdges.Add(edgeKey);

                EnsureFkIndex(srcTable, col);
                notes.Add($"{srcTable.Name}.{col.Name} -> {targetTable.Name}.{targetPk.Name} otomatik FK olarak örüldü.");
            }
        }

        if (notes.Count > 0)
        {
            workspace.Swarm.SendMessage(
                self.AgentId,
                workspace.PositiveAgentId,
                "relations.woven",
                $"{notes.Count} yetim FK kolonu 0-token ile hedef tablolara bağlandı.");
        }

        return Task.FromResult<IReadOnlyList<string>>(notes);
    }

    private static Dictionary<string, (SchemaTable Table, SchemaColumn Pk)> BuildTargetLookup(IEnumerable<SchemaTable> tables)
    {
        var map = new Dictionary<string, (SchemaTable, SchemaColumn)>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            var pk = table.Columns.FirstOrDefault(c => c.IsPK);
            if (pk is null) continue;

            var norm = NormalizeKey(table.Name);
            map.TryAdd(norm, (table, pk));

            var singular = Singularize(norm);
            map.TryAdd(singular, (table, pk));
        }
        return map;
    }

    private static HashSet<string> BuildExistingRelationKeys(IEnumerable<SchemaRelation> relations)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in relations)
            set.Add($"{r.SourceTableId}.{r.SourceColumnId}->{r.TargetTableId}.{r.TargetColumnId}");
        return set;
    }

    private static bool TryExtractTargetStem(string columnName, out string stem)
    {
        stem = string.Empty;
        if (string.IsNullOrWhiteSpace(columnName) || string.Equals(columnName, "id", StringComparison.OrdinalIgnoreCase))
            return false;

        if (columnName.EndsWith("_id", StringComparison.OrdinalIgnoreCase) && columnName.Length > 3)
        {
            stem = NormalizeKey(columnName[..^3]);
            return stem.Length > 0;
        }

        if (columnName.EndsWith("Id", StringComparison.Ordinal) && columnName.Length > 2)
        {
            stem = NormalizeKey(columnName[..^2]);
            return stem.Length > 0;
        }

        return false;
    }

    private static bool TryResolveTarget(
        IReadOnlyDictionary<string, (SchemaTable Table, SchemaColumn Pk)> lookup,
        string stem,
        string sourceTableId,
        out SchemaTable targetTable,
        out SchemaColumn targetPk)
    {
        if (lookup.TryGetValue(stem, out var found) &&
            !string.Equals(found.Table.Id, sourceTableId, StringComparison.OrdinalIgnoreCase))
        {
            targetTable = found.Table;
            targetPk = found.Pk;
            return true;
        }

        var singular = Singularize(stem);
        if (lookup.TryGetValue(singular, out var singularMatch) &&
            !string.Equals(singularMatch.Table.Id, sourceTableId, StringComparison.OrdinalIgnoreCase))
        {
            targetTable = singularMatch.Table;
            targetPk = singularMatch.Pk;
            return true;
        }

        targetTable = null!;
        targetPk = null!;
        return false;
    }

    private static void EnsureFkIndex(SchemaTable table, SchemaColumn col)
    {
        var alreadyIndexed = table.Indexes.Any(ix =>
            ix.Columns.Count > 0 &&
            string.Equals(ix.Columns[0].ColumnId, col.Id, StringComparison.OrdinalIgnoreCase));
        if (alreadyIndexed) return;

        table.Indexes.Add(new SchemaIndex
        {
            Id = $"ix_{SchemaIdConvention.Normalize(table.Name)}_{SchemaIdConvention.Normalize(col.Name)}",
            Name = $"idx_{SchemaIdConvention.Normalize(table.Name)}_{SchemaIdConvention.Normalize(col.Name)}",
            IsUnique = false,
            Columns = new List<SchemaIndexColumn> { new() { ColumnId = col.Id, Descending = false } },
        });
    }

    private static string NormalizeKey(string value)
        => SchemaIdConvention.Normalize(value).Replace("_", string.Empty, StringComparison.Ordinal);

    private static string Singularize(string normalized)
    {
        if (normalized.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && normalized.Length > 4)
            return normalized[..^3] + "y";
        if (normalized.EndsWith("ses", StringComparison.OrdinalIgnoreCase) && normalized.Length > 4)
            return normalized[..^2];
        if (normalized.EndsWith("s", StringComparison.OrdinalIgnoreCase) && !normalized.EndsWith("ss", StringComparison.OrdinalIgnoreCase) && normalized.Length > 2)
            return normalized[..^1];
        return normalized;
    }
}
