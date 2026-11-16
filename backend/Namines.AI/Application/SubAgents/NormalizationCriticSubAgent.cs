using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Namines.AI.Domain;
using Namines.Core.Models;

namespace Namines.AI.Application;

/// <summary>
/// 1NF tekrar eden numaralı kolon gruplarını (<c>phone1/phone2</c>, <c>tag1/tag2</c>),
/// virgülle ayrılmış metin kolonlarını (<c>*_csv</c>, <c>*_ids</c>) ve FK bulunan çocuk tablolardaki
/// 3NF geçişli kopya kolonları 0-token ile tespit eden normalizasyon eleştirmeni.
/// </summary>
public sealed class NormalizationCriticSubAgent
    : ISubAgent<OrchestrationWorkspace, IReadOnlyList<MinusFindingItem>>
{
    private static readonly Regex RepeatingColumnRegex = new(
        @"^(?<stem>[a-zA-Z_]+?)_?(?<num>\d+)$",
        RegexOptions.Compiled);

    public AgentKind Kind => AgentKind.SemanticCoverageCritic;

    public Task<IReadOnlyList<MinusFindingItem>> ExecuteAsync(
        AgentInstance self,
        OrchestrationWorkspace workspace,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var schema = workspace.CurrentSchema;
        var findings = new List<MinusFindingItem>();
        var tableById = schema.Tables.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var table in schema.Tables)
        {
            DetectRepeatingGroups(table, findings);
            DetectDelimitedListColumns(table, findings);
        }

        DetectTransitiveFkCopies(schema, tableById, findings);

        if (findings.Count > 0)
        {
            workspace.Swarm.SendMessage(
                self.AgentId,
                workspace.NegativeAgentId,
                "critic.normalization",
                $"{findings.Count} normalizasyon (1NF/3NF) bulgusu raporlandı.");
        }

        return Task.FromResult<IReadOnlyList<MinusFindingItem>>(findings);
    }

    private static void DetectRepeatingGroups(SchemaTable table, List<MinusFindingItem> findings)
    {
        var stemCounts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in table.Columns)
        {
            var m = RepeatingColumnRegex.Match(col.Name);
            if (!m.Success) continue;

            var stem = m.Groups["stem"].Value.TrimEnd('_');
            if (stem.Length < 2) continue;
            if (!stemCounts.TryGetValue(stem, out var list))
            {
                list = new List<string>();
                stemCounts[stem] = list;
            }
            list.Add(col.Name);
        }

        foreach (var (stem, cols) in stemCounts.Where(kv => kv.Value.Count >= 2))
        {
            findings.Add(new MinusFindingItem(
                Id: $"norm-1nf-{table.Name}-{stem}",
                Code: "NORM-1NF-REPEAT",
                Severity: MinusSeverity.Structural,
                TableName: table.Name,
                ColumnName: cols[0],
                ProblemDescription: $"{table.Name} tablosunda 1NF ihlali: tekrar eden '{string.Join(", ", cols)}' kolon grubu var.",
                ActionableDirective: $"Tekrar eden '{stem}' değerlerini ayrı bir child tabloya ({table.Name}_{stem}s) taşı.",
                SourceSubAgent: nameof(NormalizationCriticSubAgent)));
        }
    }

    private static void DetectDelimitedListColumns(SchemaTable table, List<MinusFindingItem> findings)
    {
        foreach (var col in table.Columns)
        {
            if (col.IsArray) continue;
            var lower = col.Name.ToLowerInvariant();
            if (!(lower.EndsWith("_csv", StringComparison.Ordinal) ||
                  lower.EndsWith("_list", StringComparison.Ordinal) ||
                  lower.EndsWith("_ids", StringComparison.Ordinal)))
                continue;

            findings.Add(new MinusFindingItem(
                Id: $"norm-csv-{table.Name}-{col.Name}",
                Code: "NORM-1NF-CSV",
                Severity: MinusSeverity.DbaOptimization,
                TableName: table.Name,
                ColumnName: col.Name,
                ProblemDescription: $"{table.Name}.{col.Name} tek kolonda liste/CSV tutuyor (atomik 1NF ihlali).",
                ActionableDirective: "Bunu ilişkisel bir ara tabloya (junction table) veya dizi/JSONB yapısına dönüştür.",
                SourceSubAgent: nameof(NormalizationCriticSubAgent)));
        }
    }

    private static void DetectTransitiveFkCopies(
        DatabaseSchema schema,
        IReadOnlyDictionary<string, SchemaTable> tableById,
        List<MinusFindingItem> findings)
    {
        foreach (var rel in schema.Relations)
        {
            if (!tableById.TryGetValue(rel.SourceTableId, out var child) ||
                !tableById.TryGetValue(rel.TargetTableId, out var parent))
                continue;

            var parentPrefix = Singularize(parent.Name.ToLowerInvariant()) + "_";
            var parentColNames = parent.Columns
                .Select(c => c.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var col in child.Columns)
            {
                if (col.IsPK || col.IsFK) continue;
                var lower = col.Name.ToLowerInvariant();
                if (!lower.StartsWith(parentPrefix, StringComparison.Ordinal)) continue;

                var stripped = col.Name[parentPrefix.Length..];
                if (string.Equals(stripped, "id", StringComparison.OrdinalIgnoreCase) || !parentColNames.Contains(stripped))
                    continue;

                findings.Add(new MinusFindingItem(
                    Id: $"norm-3nf-{child.Name}-{col.Name}",
                    Code: "NORM-3NF-TRANSITIVE",
                    Severity: MinusSeverity.DbaOptimization,
                    TableName: child.Name,
                    ColumnName: col.Name,
                    ProblemDescription: $"{child.Name}.{col.Name}, zaten FK ile bağlı olduğu {parent.Name}.{stripped} bilgisini 3NF dışı kopyalıyor.",
                    ActionableDirective: $"Veri tutarlılığı için {col.Name} kopyasını kaldır veya kasıtlı snapshot ise belgeleyip tut.",
                    SourceSubAgent: nameof(NormalizationCriticSubAgent)));
            }
        }
    }

    private static string Singularize(string lowerName)
    {
        if (lowerName.EndsWith("ies", StringComparison.Ordinal) && lowerName.Length > 4)
            return lowerName[..^3] + "y";
        if (lowerName.EndsWith('s') && !lowerName.EndsWith("ss", StringComparison.Ordinal) && lowerName.Length > 2)
            return lowerName[..^1];
        return lowerName;
    }
}
