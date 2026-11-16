using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Namines.AI.Domain;
using Namines.Core.Models;

namespace Namines.AI.Application;

/// <summary>
/// Çoka-çok köprü (junction) tablolarında eksik bileşik tekil kısıtı, polimorfik <c>(_type, _id)</c>
/// çiftlerinde eksik bileşik indeksi ve tarih aralığı <c>CHECK</c> eksikliği gibi performans/bütünlük
/// kusurlarını 0-token ile tespit eden alt-ajan.
/// </summary>
public sealed class IndexAndConstraintCriticSubAgent
    : ISubAgent<OrchestrationWorkspace, IReadOnlyList<MinusFindingItem>>
{
    public AgentKind Kind => AgentKind.DbaFinOpsSecurityCritic;

    public Task<IReadOnlyList<MinusFindingItem>> ExecuteAsync(
        AgentInstance self,
        OrchestrationWorkspace workspace,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var schema = workspace.CurrentSchema;
        var findings = new List<MinusFindingItem>();

        foreach (var table in schema.Tables)
        {
            CheckJunctionTableUniqueness(table, findings);
            CheckPolymorphicIndex(table, findings);
            CheckTemporalRangeConstraint(table, findings);
        }

        if (findings.Count > 0)
        {
            workspace.Swarm.SendMessage(
                self.AgentId,
                workspace.NegativeAgentId,
                "critic.indexes",
                $"{findings.Count} bileşik indeks/CHECK kısıtı bulgusu tespit edildi.");
        }

        return Task.FromResult<IReadOnlyList<MinusFindingItem>>(findings);
    }

    private static void CheckJunctionTableUniqueness(SchemaTable table, List<MinusFindingItem> findings)
    {
        var fkCols = table.Columns.Where(c => c.IsFK).ToList();
        var pkCols = table.Columns.Where(c => c.IsPK).ToList();
        if (fkCols.Count < 2 || table.Columns.Count > 5 || pkCols.Count >= 2)
            return;

        var fkIdSet = fkCols.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var coveredByUnique = table.Uniques.Any(u => fkIdSet.All(id => u.ColumnIds.Contains(id, StringComparer.OrdinalIgnoreCase))) ||
                              table.Indexes.Any(ix => ix.IsUnique && fkIdSet.All(id => ix.Columns.Any(c => string.Equals(c.ColumnId, id, StringComparison.OrdinalIgnoreCase))));

        if (!coveredByUnique)
        {
            findings.Add(new MinusFindingItem(
                Id: $"idx-junction-{table.Name}",
                Code: "IDX-JUNCTION-UNIQUE",
                Severity: MinusSeverity.Structural,
                TableName: table.Name,
                ColumnName: fkCols[0].Name,
                ProblemDescription: $"Köprü tablo '{table.Name}' ({string.Join(", ", fkCols.Select(c => c.Name))}) üzerinde tekrarlı eşleşmeyi engelleyen UNIQUE bileşik indeks yok.",
                ActionableDirective: $"({string.Join(", ", fkCols.Select(c => c.Name))}) için UNIQUE kısıt/indeks ekle.",
                SourceSubAgent: nameof(IndexAndConstraintCriticSubAgent),
                IsDeterministicAutoFixable: true));
        }
    }

    private static void CheckPolymorphicIndex(SchemaTable table, List<MinusFindingItem> findings)
    {
        foreach (var typeCol in table.Columns.Where(c => c.Name.EndsWith("_type", StringComparison.OrdinalIgnoreCase)))
        {
            var prefix = typeCol.Name[..^5];
            var idCol = table.Columns.FirstOrDefault(c =>
                string.Equals(c.Name, $"{prefix}_id", StringComparison.OrdinalIgnoreCase));
            if (idCol is null) continue;

            var hasComposite = table.Indexes.Any(ix =>
                ix.Columns.Count >= 2 &&
                string.Equals(ix.Columns[0].ColumnId, typeCol.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ix.Columns[1].ColumnId, idCol.Id, StringComparison.OrdinalIgnoreCase));

            if (!hasComposite)
            {
                findings.Add(new MinusFindingItem(
                    Id: $"idx-poly-{table.Name}-{prefix}",
                    Code: "IDX-POLYMORPHIC",
                    Severity: MinusSeverity.DbaOptimization,
                    TableName: table.Name,
                    ColumnName: typeCol.Name,
                    ProblemDescription: $"Polimorfik '{table.Name}.({typeCol.Name}, {idCol.Name})' çifti için bileşik indeks yok.",
                    ActionableDirective: $"({typeCol.Name}, {idCol.Name}) bileşik indeksi ekle.",
                    SourceSubAgent: nameof(IndexAndConstraintCriticSubAgent),
                    IsDeterministicAutoFixable: true));
            }
        }
    }

    private static void CheckTemporalRangeConstraint(SchemaTable table, List<MinusFindingItem> findings)
    {
        var startCol = table.Columns.FirstOrDefault(c =>
            c.Name.Equals("start_date", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Equals("starts_at", StringComparison.OrdinalIgnoreCase));
        var endCol = table.Columns.FirstOrDefault(c =>
            c.Name.Equals("end_date", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Equals("ends_at", StringComparison.OrdinalIgnoreCase));

        if (startCol is null || endCol is null) return;

        var hasCheck = table.Checks.Any(ch =>
            ch.Expression.Contains(startCol.Name, StringComparison.OrdinalIgnoreCase) &&
            ch.Expression.Contains(endCol.Name, StringComparison.OrdinalIgnoreCase));

        if (!hasCheck)
        {
            findings.Add(new MinusFindingItem(
                Id: $"chk-temporal-{table.Name}",
                Code: "CHK-TEMPORAL-ORDER",
                Severity: MinusSeverity.DbaOptimization,
                TableName: table.Name,
                ColumnName: endCol.Name,
                ProblemDescription: $"{table.Name} tablosunda {endCol.Name} >= {startCol.Name} tarih sırası CHECK kısıtı eksik.",
                ActionableDirective: $"CHECK ({endCol.Name} >= {startCol.Name}) kuralını ekle.",
                SourceSubAgent: nameof(IndexAndConstraintCriticSubAgent),
                IsDeterministicAutoFixable: true));
        }
    }
}
