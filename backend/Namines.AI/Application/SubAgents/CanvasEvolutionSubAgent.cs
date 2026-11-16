using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Namines.AI.Domain;

namespace Namines.AI.Application;

/// <summary>
/// Mevcut Canvas şemasını L0 topolojik iskelet + L1 odaklı NSL alt grafiği +
/// kilitli komşu PK/FK sözleşmeleri ile StableUuid koruyarak evrimleştiren alt-ajan.
/// </summary>
public sealed class CanvasEvolutionSubAgent
    : ISubAgent<(OrchestrationWorkspace Workspace, PositiveDirective Directive), string>
{
    private readonly ILlmCompletionEngine _llm;
    private readonly ISchemaContextEngine _contextEngine;

    public CanvasEvolutionSubAgent(
        ILlmCompletionEngine llm,
        ISchemaContextEngine contextEngine)
    {
        _llm = llm;
        _contextEngine = contextEngine;
    }

    public AgentKind Kind => AgentKind.SchemaMutator;

    public async Task<string> ExecuteAsync(
        AgentInstance self,
        (OrchestrationWorkspace Workspace, PositiveDirective Directive) task,
        CancellationToken ct = default)
    {
        var workspace = task.Workspace;
        var input = workspace.TaskInput;

        if (!_llm.IsConfiguredFor(input.ProviderHint))
            return string.Empty;

        var focusTables = DetermineFocusTables(workspace);
        var skeleton = workspace.Skeleton ?? _contextEngine.BuildSkeleton(workspace.CurrentSchema, input.Engine);
        var subgraph = _contextEngine.ExtractSubgraph(workspace.CurrentSchema, focusTables, input.Engine);

        var sb = new StringBuilder();
        sb.AppendLine($"Engine: {input.Engine.ToString().ToLowerInvariant()}");
        sb.AppendLine($"User Canvas Revision Request: {input.Prompt}");
        sb.AppendLine();
        sb.AppendLine("L0 FULL SCHEMA TOPOLOGY (read-only context — preserve untouched tables):");
        sb.AppendLine(skeleton.CompactTextMap);
        sb.AppendLine();
        sb.AppendLine("L1 FOCUS NSL SUBGRAPH (preserve @uuid(\"...\") lines on modified existing tables!):");
        sb.AppendLine(subgraph.FocusNslText);

        if (subgraph.NeighborContracts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("1-HOP LOCKED NEIGHBOR CONTRACTS (FK targets available):");
            foreach (var c in subgraph.NeighborContracts)
                sb.AppendLine($"- {c.TableName}({c.PkColumnName}: {c.PkCanonicalType})");
        }

        sb.AppendLine();
        sb.AppendLine("Emit ONLY the added or updated tables/enums in valid NSL 1.0 syntax. Preserve @uuid annotations on edited tables.");

        var systemPrompt =
            "You are Namines Canvas Schema Evolution Architect.\n" +
            "Output ONLY valid NSL 1.0 syntax containing the NEW or MODIFIED tables/enums required by the revision request.\n" +
            "Rules:\n" +
            "1. Keep existing @uuid(\"...\") lines intact on tables you revise so frontend canvas layout identity never breaks.\n" +
            "2. Match FK column types to referenced locked neighbor PK types.\n" +
            "3. Never emit tables that did not need changes — untouched tables remain preserved automatically.\n" +
            "4. Output strict NSL text only.";

        var response = await _llm.CompleteAsync(
            new LlmRequest(
                SystemPrompt: systemPrompt,
                UserPrompt: sb.ToString(),
                Temperature: 0.1,
                ModelHint: input.ModelHint,
                ProviderHint: input.ProviderHint,
                FeatureKey: "schema_revise"),
            ct).ConfigureAwait(false);

        workspace.Swarm.SendMessage(
            self.AgentId,
            workspace.PositiveAgentId,
            "canvas.evolution.authored",
            $"Odak={focusTables.Count} tablo, alt grafik={subgraph.EstimatedTokens} token; evrim yaması üretildi.");

        return response.Content;
    }

    private static IReadOnlyList<string> DetermineFocusTables(OrchestrationWorkspace workspace)
    {
        var input = workspace.TaskInput;
        if (input.SelectedCanvasTables is { Count: > 0 })
            return input.SelectedCanvasTables;

        // Seçim verilmediyse prompt'ta geçen tablo adlarını yakala.
        var promptLower = (input.Prompt ?? string.Empty).ToLowerInvariant();
        var matched = workspace.CurrentSchema.Tables
            .Where(t => promptLower.Contains(t.Name.ToLowerInvariant(), StringComparison.Ordinal))
            .Select(t => t.Name)
            .ToList();

        if (matched.Count > 0)
            return matched;

        return workspace.CurrentSchema.Tables.Take(12).Select(t => t.Name).ToList();
    }
}
