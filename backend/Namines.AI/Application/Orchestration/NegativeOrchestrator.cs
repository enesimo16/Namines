using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Namines.AI.Domain;
using Namines.Core.Models;

namespace Namines.AI.Application;

/// <summary>
/// [-] Eleştirel Alt-Orkestratör.
/// Doğrulama, çoklu motor DDL derleme, anlamsal plan kapsamı ve DBA (Performans/FinOps/Güvenlik)
/// eleştirmenlerini paralel/sıralı olarak çalıştırıp Baş Orkestratöre eyleme dönük Eksiler Raporu sunar.
/// </summary>
public sealed class NegativeOrchestrator : INegativeOrchestrator
{
    private readonly IAiWorkspaceToolRegistry _tools;
    private readonly AdversarialDbaSubAgent _dbaCritic;
    private readonly SemanticAndPortabilityCriticSubAgent _semanticPortabilityCritic;
    private readonly NormalizationCriticSubAgent _normalizationCritic;
    private readonly MultiTenancyAndRlsCriticSubAgent _multiTenancyCritic;
    private readonly IndexAndConstraintCriticSubAgent _indexCritic;
    private readonly TriggerAndProcedureCriticSubAgent _programmabilityCritic;

    public NegativeOrchestrator(
        IAiWorkspaceToolRegistry tools,
        AdversarialDbaSubAgent dbaCritic,
        SemanticAndPortabilityCriticSubAgent? semanticPortabilityCritic = null,
        NormalizationCriticSubAgent? normalizationCritic = null,
        MultiTenancyAndRlsCriticSubAgent? multiTenancyCritic = null,
        IndexAndConstraintCriticSubAgent? indexCritic = null,
        TriggerAndProcedureCriticSubAgent? programmabilityCritic = null)
    {
        _tools = tools;
        _dbaCritic = dbaCritic;
        _semanticPortabilityCritic = semanticPortabilityCritic ?? new SemanticAndPortabilityCriticSubAgent();
        _normalizationCritic = normalizationCritic ?? new NormalizationCriticSubAgent();
        _multiTenancyCritic = multiTenancyCritic ?? new MultiTenancyAndRlsCriticSubAgent();
        _indexCritic = indexCritic ?? new IndexAndConstraintCriticSubAgent();
        _programmabilityCritic = programmabilityCritic ?? new TriggerAndProcedureCriticSubAgent();
    }

    public async Task<MinusReport> AuditAsync(
        OrchestrationWorkspace workspace,
        NegativeDirective directive,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(directive);

        var findings = new List<MinusFindingItem>();
        var portabilityNotes = new List<string>();

        // 1. Deterministik NSL + FK Cascade + 6-Motor DDL Derleme Eleştirmeni
        if (directive.RunCrossEngineCompile)
        {
            await RunCriticAsync(
                workspace,
                AgentKind.DeterministicCritic,
                $"det-critic-iter{directive.WaveOrIteration}",
                "NSL kuralları, FkCascadeAnalyzer ve 6-motor DDL derleme denetimi",
                async _ =>
                {
                    var toolRes = await _tools
                        .InvokeAsync("deterministic_validate_compile", workspace, ct: ct)
                        .ConfigureAwait(false);
                    if (toolRes.Findings is not null)
                        findings.AddRange(toolRes.Findings);
                    if (toolRes.Metadata is not null)
                    {
                        foreach (var kv in toolRes.Metadata.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                            portabilityNotes.Add($"{kv.Key}={kv.Value}");
                    }
                }).ConfigureAwait(false);
        }

        // 2. Anlamsal Plan Kapsam + Motor Taşınabilirlik Eleştirmeni
        await RunCriticAsync(
            workspace,
            AgentKind.SemanticCoverageCritic,
            $"sem-port-critic-iter{directive.WaveOrIteration}",
            "Plan kapsamı, izole tablo tespiti ve çoklu motor uyumluluk denetimi",
            async agent =>
            {
                var res = await _semanticPortabilityCritic
                    .ExecuteAsync(agent, (workspace, directive.RunSemanticCoverage), ct)
                    .ConfigureAwait(false);
                findings.AddRange(res.Findings);
                portabilityNotes.AddRange(res.PortabilityNotes);
            }).ConfigureAwait(false);

        // 3. Normalizasyon (1NF/3NF) Eleştirmeni
        await RunCriticAsync(
            workspace,
            AgentKind.SemanticCoverageCritic,
            $"norm-critic-iter{directive.WaveOrIteration}",
            "1NF tekrarlayan sütunlar ve 3NF geçişli kopya kolon denetimi",
            async agent => findings.AddRange(await _normalizationCritic.ExecuteAsync(agent, workspace, ct).ConfigureAwait(false))).ConfigureAwait(false);

        // 4. Çok Kiracılık (Multi-Tenancy) ve RLS İzolasyon Eleştirmeni
        await RunCriticAsync(
            workspace,
            AgentKind.DbaFinOpsSecurityCritic,
            $"mt-critic-iter{directive.WaveOrIteration}",
            "Multi-tenant tenant_id izolasyonu ve RLS indeks denetimi",
            async agent => findings.AddRange(await _multiTenancyCritic.ExecuteAsync(agent, workspace, ct).ConfigureAwait(false))).ConfigureAwait(false);

        // 5. Bileşik İndeks & CHECK Kısıtı Eleştirmeni
        await RunCriticAsync(
            workspace,
            AgentKind.DbaFinOpsSecurityCritic,
            $"idx-critic-iter{directive.WaveOrIteration}",
            "Köprü tablo tekil kısıt, polimorfik indeks ve tarih aralığı CHECK denetimi",
            async agent => findings.AddRange(await _indexCritic.ExecuteAsync(agent, workspace, ct).ConfigureAwait(false))).ConfigureAwait(false);

        // 6. Trigger & Stored Procedure Bütünlük Eleştirmeni
        await RunCriticAsync(
            workspace,
            AgentKind.TriggerAndProcedureCritic,
            $"prog-critic-iter{directive.WaveOrIteration}",
            "Trigger TableId bütünlüğü ve Stored Procedure güvenlik/gövde denetimi",
            async agent => findings.AddRange(await _programmabilityCritic.ExecuteAsync(agent, workspace, ct).ConfigureAwait(false))).ConfigureAwait(false);

        // 7. DBA (Performans / FinOps / Güvenlik) Eleştirmeni
        await RunCriticAsync(
            workspace,
            AgentKind.DbaFinOpsSecurityCritic,
            $"dba-critic-iter{directive.WaveOrIteration}",
            "DBA Performans, FinOps ve Güvenlik/PII denetimi",
            async agent =>
            {
                var useAiDba = directive.RunDbaAudit && workspace.TaskInput.DeepDbaAudit;
                var dbaOut = await _dbaCritic.ExecuteAsync(
                    agent,
                    new DbaAuditInput(
                        workspace.CurrentSchema,
                        workspace.TaskInput.Engine,
                        useAiDba,
                        workspace.TaskInput.ModelHint,
                        workspace.TaskInput.ProviderHint),
                    ct).ConfigureAwait(false);
                findings.AddRange(dbaOut.MinusFindings);
            }).ConfigureAwait(false);

        // Aynı kural + tablo + kolon üzerindeki tekrarlayan bulguları tekilleştir.
        var deduped = findings
            .GroupBy(f => $"{f.Code}|{f.TableName}|{f.ColumnName}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(f => f.Severity)
            .ToList();

        var blockerCount = deduped.Count(f => f.Severity == MinusSeverity.Blocker);
        var semCount = deduped.Count(f => f.Severity == MinusSeverity.SemanticGap);
        var dbaCount = deduped.Count(f => f.Severity is MinusSeverity.DbaOptimization or MinusSeverity.Structural);

        var report = new MinusReport(
            WaveOrIteration: directive.WaveOrIteration,
            Findings: deduped,
            PortabilityNotes: portabilityNotes,
            IsCleanOfBlockers: blockerCount == 0,
            DeterministicErrorCount: blockerCount,
            SemanticGapCount: semCount,
            DbaWarningCount: dbaCount);

        workspace.LastMinusReport = report;

        workspace.Swarm.SendMessage(
            workspace.NegativeAgentId,
            workspace.MasterAgentId,
            "minus.report",
            $"[-] İterasyon {directive.WaveOrIteration}: {blockerCount} blocker, {semCount} kapsam boşluğu, {dbaCount} yapısal/DBA bulgusu.");

        workspace.RecordStep(AgentStep.Negative(
            $"Eleştirel denetim {directive.WaveOrIteration}: {blockerCount} blocker, {semCount} kapsam boşluğu, {dbaCount} DBA/yapısal öneri bulundu."));

        return report;
    }

    private static async Task RunCriticAsync(
        OrchestrationWorkspace workspace,
        AgentKind kind,
        string name,
        string purpose,
        Func<AgentInstance, Task> action)
    {
        var agent = workspace.Swarm.SpawnAgent(kind, name, purpose);
        try
        {
            await action(agent).ConfigureAwait(false);
        }
        finally
        {
            workspace.Swarm.RetireAgent(agent.AgentId);
        }
    }
}
