using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Namines.AI.Domain;

namespace Namines.AI.Application;

/// <summary>
/// [-] Eleştirel Orkestratörün tespit ettiği, 0-token ile çözülemeyen anlamsal eksikleri
/// veya derleme hatalarını yalnızca ilgili 1-4 tabloluk L1 alt grafik üzerinde cerrahi olarak
/// düzelten LLM mutasyon alt-ajanı.
/// </summary>
public sealed class SurgicalMutationSubAgent
    : ISubAgent<(OrchestrationWorkspace Workspace, IReadOnlyList<MinusFindingItem> Directives), string>
{
    private readonly ILlmCompletionEngine _llm;
    private readonly ISchemaContextEngine _contextEngine;

    public SurgicalMutationSubAgent(
        ILlmCompletionEngine llm,
        ISchemaContextEngine contextEngine)
    {
        _llm = llm;
        _contextEngine = contextEngine;
    }

    public AgentKind Kind => AgentKind.SchemaMutator;

    public async Task<string> ExecuteAsync(
        AgentInstance self,
        (OrchestrationWorkspace Workspace, IReadOnlyList<MinusFindingItem> Directives) task,
        CancellationToken ct = default)
    {
        var workspace = task.Workspace;
        var directives = task.Directives;
        var input = workspace.TaskInput;

        if (directives.Count == 0 || !_llm.IsConfiguredFor(input.ProviderHint))
            return string.Empty;

        var focusTables = directives
            .Select(d => d.TableName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        var subgraph = _contextEngine.ExtractSubgraph(workspace.CurrentSchema, focusTables, input.Engine);

        var sb = new StringBuilder();
        sb.AppendLine($"Target Engine: {input.Engine.ToString().ToLowerInvariant()}");
        sb.AppendLine($"Original User Goal: {input.Prompt}");
        sb.AppendLine();
        sb.AppendLine("NEGATIVE CRITIC DIRECTIVES TO FIX:");
        foreach (var d in directives.Take(15))
            sb.AppendLine($"- [{d.Code} / {d.Severity}] Table={d.TableName ?? "*"} Col={d.ColumnName ?? "*"}: {d.ActionableDirective}");

        sb.AppendLine();
        sb.AppendLine("AFFECTED L1 NSL SUBGRAPH (preserve existing @uuid annotations!):");
        sb.AppendLine(subgraph.FocusNslText);

        if (subgraph.NeighborContracts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("LOCKED NEIGHBOR PK CONTRACTS:");
            foreach (var c in subgraph.NeighborContracts)
                sb.AppendLine($"- {c.TableName}({c.PkColumnName}: {c.PkCanonicalType})");
        }

        sb.AppendLine();
        sb.AppendLine("Return ONLY valid NSL 1.0 definitions for the repaired/added tables.");

        var system =
            "You are Namines Surgical Schema Mutator.\n" +
            "Apply ONLY the requested negative critic fixes cleanly in valid NSL 1.0 syntax.\n" +
            "Keep @uuid(\"...\") lines intact on edited tables, align FK types to locked contracts, and output strict NSL text only.";

        var response = await _llm.CompleteAsync(
            new LlmRequest(
                SystemPrompt: system,
                UserPrompt: sb.ToString(),
                Temperature: 0.05,
                ModelHint: input.ModelHint,
                ProviderHint: input.ProviderHint,
                FeatureKey: "schema_repair"),
            ct).ConfigureAwait(false);

        workspace.Swarm.SendMessage(
            self.AgentId,
            workspace.MasterAgentId,
            "mutation.surgical",
            $"{directives.Count} kritik bulgu için {focusTables.Count} tablo üzerinde cerrahi NSL yaması uygulandı.");

        return response.Content;
    }
}
