using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Namines.AI.Domain;
using Namines.Core.Analysis;
using Namines.Core.Nsl;

namespace Namines.AI.Application;

/// <summary>
/// Tek bir topolojik dalgayı kilitli PK/FK sözleşmelerine sadık kalarak kompakt NSL (.nsl)
/// sözdiziminde üreten alan mimarı alt-ajanı (~%80-85 token tasarrufu).
/// </summary>
public sealed class NslDomainArchitectSubAgent
    : ISubAgent<(OrchestrationWorkspace Workspace, TopologicalWave Wave), string>
{
    private readonly ILlmCompletionEngine _llm;

    public NslDomainArchitectSubAgent(ILlmCompletionEngine llm)
    {
        _llm = llm;
    }

    public AgentKind Kind => AgentKind.DomainArchitect;

    public async Task<string> ExecuteAsync(
        AgentInstance self,
        (OrchestrationWorkspace Workspace, TopologicalWave Wave) task,
        CancellationToken ct = default)
    {
        var workspace = task.Workspace;
        var wave = task.Wave;
        var input = workspace.TaskInput;

        var existingSet = new HashSet<string>(
            workspace.CurrentSchema.Tables.Select(t => t.Name),
            StringComparer.OrdinalIgnoreCase);

        var tablesToBuild = wave.TargetTables
            .Where(t => !existingSet.Contains(t) || wave.WaveIndex == 0 && workspace.CurrentSchema.Tables.Count == 0)
            .ToList();

        if (tablesToBuild.Count == 0)
            tablesToBuild = wave.TargetTables.ToList();

        var systemPrompt = BuildNslSystemPrompt();
        var userPrompt = BuildWaveUserPrompt(workspace, wave, tablesToBuild);

        var response = await _llm.CompleteAsync(
            new LlmRequest(
                SystemPrompt: systemPrompt,
                UserPrompt: userPrompt,
                Temperature: 0.1,
                ModelHint: input.ModelHint,
                ProviderHint: input.ProviderHint,
                FeatureKey: "schema_generate"),
            ct).ConfigureAwait(false);

        workspace.Swarm.SendMessage(
            self.AgentId,
            workspace.PositiveAgentId,
            "wave.nsl.authored",
            $"Dalga {wave.WaveIndex} ({wave.Label}) için {tablesToBuild.Count} tablo NSL çıktısı üretildi ({response.CompletionTokens} output token).");

        return response.Content;
    }

    private static string BuildNslSystemPrompt() =>
        """
        You are Namines NSL Schema Architect. Output STRICT NSL 1.0 text ONLY (no markdown prose, no JSON).
        Exact NSL syntax:
        nsl 1.0

        project "schema_name" {
          engine postgresql
        }

        enum status_type {
          "pending"
          "active"
        }

        table users {
          id uuid pk not null
          email varchar(255) not null unique
          role status_type not null default 'active'
          created_at timestamp not null default 'CURRENT_TIMESTAMP'
          index (email)
        }

        table orders {
          id uuid pk not null
          user_id uuid not null
          total_amount decimal(19,4) not null
          fk (user_id) -> users(id) on delete restrict on update no action
          index (user_id)
        }

        Rules:
        1. Every table MUST have a primary key (`pk`), explicit sizes on `varchar(N)` and `decimal(P,S)`, and indexes on every FK column.
        2. FK column types MUST EXACTLY match the referenced locked PK column type from prior waves.
        3. Use `on delete no action` or `on delete restrict` by default to prevent MSSQL Msg 1785 multiple cascade path failures.
        4. Never repeat tables already listed in LOCKED CONTRACTS unless instructed.
        """;

    private static string BuildWaveUserPrompt(
        OrchestrationWorkspace workspace,
        TopologicalWave wave,
        IReadOnlyList<string> targetTables)
    {
        var sb = new StringBuilder();
        var input = workspace.TaskInput;
        var engineName = input.Engine.ToString().ToLowerInvariant();
        var archetype = ArchetypeDetector.Detect(input.Prompt);

        sb.AppendLine($"Target Engine: {engineName}");
        sb.AppendLine($"Project Context: {input.Prompt}");
        sb.AppendLine($"Wave {wave.WaveIndex}: {wave.Label} — {wave.Rationale}");
        sb.AppendLine($"Required tables in this wave ({targetTables.Count}): {string.Join(", ", targetTables)}");

        if (!string.IsNullOrWhiteSpace(ArchetypeRoles.For(archetype)))
            sb.AppendLine($"Archetype Rules: {ArchetypeRoles.For(archetype)}");

        var contracts = workspace.LockedContracts;
        if (contracts.Count > 0)
        {
            sb.AppendLine("LOCKED UPSTREAM CONTRACTS (reference ONLY these PK names/types — DO NOT redefine these tables):");
            foreach (var c in contracts.Take(80))
                sb.AppendLine($"- {c.TableName}({c.PkColumnName}: {c.PkCanonicalType})");
        }

        if (workspace.SourceInsights.Count > 0)
        {
            sb.AppendLine("Observed source signals (JSON/Code/OpenAPI/GitHub):");
            foreach (var insight in workspace.SourceInsights.Take(12))
                sb.AppendLine($"- {insight}");
        }

        sb.AppendLine($"Emit ONLY NSL 1.0 definitions for this wave's {targetTables.Count} tables plus any needed enums.");
        return sb.ToString();
    }
}
