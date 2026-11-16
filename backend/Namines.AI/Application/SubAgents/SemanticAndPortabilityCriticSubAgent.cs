using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Namines.AI.Domain;
using Namines.Core.Analysis;
using Namines.Core.Enums;

namespace Namines.AI.Application;

public sealed record SemanticAndPortabilityResult(
    IReadOnlyList<MinusFindingItem> Findings,
    IReadOnlyList<string> PortabilityNotes);

/// <summary>
/// Planlanan tablolar ile üretilen şemanın anlamsal kapsamını (<c>SEM-COVERAGE</c>, <c>SEM-ISOLATED</c>)
/// ve 6 SQL motoru arası özellik taşınabilirliğini (<c>EngineConversionAnalyzer</c>) 0-token ile denetleyen eleştirmen.
/// </summary>
public sealed class SemanticAndPortabilityCriticSubAgent
    : ISubAgent<(OrchestrationWorkspace Workspace, bool RunSemanticCoverage), SemanticAndPortabilityResult>
{
    public AgentKind Kind => AgentKind.SemanticCoverageCritic;

    public Task<SemanticAndPortabilityResult> ExecuteAsync(
        AgentInstance self,
        (OrchestrationWorkspace Workspace, bool RunSemanticCoverage) task,
        CancellationToken ct = default)
    {
        var workspace = task.Workspace;
        var findings = new List<MinusFindingItem>();
        var portabilityNotes = new List<string>();
        var schema = workspace.CurrentSchema;
        var engine = workspace.TaskInput.Engine;

        // 1. Anlamsal Plan Kapsam ve İzolasyon Denetimi
        if (task.RunSemanticCoverage && workspace.Plan is not null)
        {
            var existingNames = schema.Tables
                .Select(t => t.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missing = workspace.Plan.Waves
                .SelectMany(w => w.TargetTables)
                .Where(t => !existingNames.Contains(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (missing.Count > 0)
            {
                var preview = string.Join(", ", missing.Take(8));
                findings.Add(new MinusFindingItem(
                    Id: $"sem-missing-{missing.Count}",
                    Code: "SEM-COVERAGE",
                    Severity: MinusSeverity.SemanticGap,
                    TableName: missing[0],
                    ColumnName: null,
                    ProblemDescription: $"Planda olup henüz üretilmeyen {missing.Count} tablo var: {preview}.",
                    ActionableDirective: $"Eksik tabloları NSL olarak ekle ve mevcut sözleşmelere bağla: {preview}.",
                    SourceSubAgent: nameof(SemanticAndPortabilityCriticSubAgent)));
            }

            if (schema.Tables.Count >= 4 && schema.Relations.Count == 0)
            {
                findings.Add(new MinusFindingItem(
                    Id: "sem-norel",
                    Code: "SEM-ISOLATED",
                    Severity: MinusSeverity.Structural,
                    TableName: schema.Tables[0].Name,
                    ColumnName: null,
                    ProblemDescription: "4+ tabloluk şemada hiçbir Foreign Key ilişkisi tanımlı değil.",
                    ActionableDirective: "Tablolar arasında mantıksal fk (...) -> target(id) ilişkileri kur.",
                    SourceSubAgent: nameof(SemanticAndPortabilityCriticSubAgent)));
            }
        }

        // 2. Motor Taşınabilirlik Analizi (Array, Collation, Generated PK vb.)
        var convReport = EngineConversionAnalyzer.Analyze(schema, DatabaseType.PostgreSQL, engine);
        if (convReport.HasFindings)
        {
            foreach (var f in convReport.Findings)
            {
                findings.Add(new MinusFindingItem(
                    Id: $"port-{f.Id}",
                    Code: "PORT-ENGINE-LOSS",
                    Severity: MinusSeverity.Structural,
                    TableName: f.TableName,
                    ColumnName: f.ColumnName,
                    ProblemDescription: f.Description,
                    ActionableDirective: f.Options.FirstOrDefault()?.Label ?? $"{engine} uyumlu türe uyarla.",
                    SourceSubAgent: nameof(SemanticAndPortabilityCriticSubAgent)));
            }
            portabilityNotes.Add($"EngineConversion({engine}): {convReport.Findings.Count} uyumluluk uyarısı");
        }

        if (findings.Count > 0)
        {
            workspace.Swarm.SendMessage(
                self.AgentId,
                workspace.NegativeAgentId,
                "critic.semantic_portability",
                $"{findings.Count} kapsam/taşınabilirlik bulgusu raporlandı.");
        }

        return Task.FromResult(new SemanticAndPortabilityResult(findings, portabilityNotes));
    }
}
