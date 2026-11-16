using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Namines.AI.Domain;
using Namines.Core.Models;

namespace Namines.AI.Application;

/// <summary>
/// [+] Yapıcı Alt-Orkestratör.
/// Verilen dalgayı veya Canvas evrimini üretmek için dinamik uzman alt-ajanlar doğurur,
/// NSL çıktısını birleştirir, PK/FK imzalarını kilitler ve Baş Orkestratöre Artılar Raporu sunar.
/// </summary>
public sealed class PositiveOrchestrator : IPositiveOrchestrator
{
    private readonly ISchemaContextEngine _contextEngine;
    private readonly NslDomainArchitectSubAgent _domainArchitect;
    private readonly CanvasEvolutionSubAgent _canvasEvolution;
    private readonly DeterministicRepairSubAgent _deterministicRepair;
    private readonly RelationWeaverSubAgent _relationWeaver;
    private readonly AuditAndTemporalWeaverSubAgent _temporalWeaver;
    private readonly ProgrammabilityArchitectSubAgent? _programmability;

    public PositiveOrchestrator(
        ISchemaContextEngine contextEngine,
        NslDomainArchitectSubAgent domainArchitect,
        CanvasEvolutionSubAgent canvasEvolution,
        DeterministicRepairSubAgent deterministicRepair,
        RelationWeaverSubAgent? relationWeaver = null,
        AuditAndTemporalWeaverSubAgent? temporalWeaver = null,
        ProgrammabilityArchitectSubAgent? programmability = null)
    {
        _contextEngine = contextEngine;
        _domainArchitect = domainArchitect;
        _canvasEvolution = canvasEvolution;
        _deterministicRepair = deterministicRepair;
        _relationWeaver = relationWeaver ?? new RelationWeaverSubAgent();
        _temporalWeaver = temporalWeaver ?? new AuditAndTemporalWeaverSubAgent();
        _programmability = programmability;
    }

    public async Task<PlusReport> ExecuteAsync(
        OrchestrationWorkspace workspace,
        PositiveDirective directive,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(directive);

        var gains = new List<PlusGainItem>();
        var beforeTables = workspace.CurrentSchema.Tables.Count;
        var beforeRels = workspace.CurrentSchema.Relations.Count;

        if (directive.IsCanvasEvolution)
        {
            await RunCanvasEvolutionAsync(workspace, directive, beforeTables, gains, ct).ConfigureAwait(false);
        }
        else if (directive.TargetWave is { } wave)
        {
            await RunWaveBuildAsync(workspace, wave, beforeTables, beforeRels, gains, ct).ConfigureAwait(false);
        }

        // Dalga sonrası 0-token yapıcı zincir: FK örme + zaman damgası hizalama + yapısal onarım
        await RunZeroTokenWeaversAsync(workspace, directive, gains, ct).ConfigureAwait(false);

        // Trigger ve Stored Procedure talebi varsa (Canvas evriminde veya son dalgada) sentezle.
        var isFinalWave = workspace.Plan is null ||
                          directive.TargetWave is null ||
                          directive.TargetWave.WaveIndex >= workspace.Plan.Waves.Count - 1;

        if (_programmability is not null &&
            (directive.IsCanvasEvolution || isFinalWave) &&
            ProgrammabilityArchitectSubAgent.IsProgrammabilityRequested(workspace.TaskInput.Prompt))
        {
            await RunProgrammabilityAsync(workspace, directive, gains, ct).ConfigureAwait(false);
        }

        var contracts = _contextEngine.ExtractLockedContracts(workspace.CurrentSchema);
        workspace.UpsertContracts(contracts);
        workspace.Skeleton = _contextEngine.BuildSkeleton(workspace.CurrentSchema, workspace.TaskInput.Engine);

        foreach (var g in gains)
            workspace.AddGain(g);

        var plannedTotal = Math.Max(1, workspace.Plan?.TotalPlannedTables ?? workspace.CurrentSchema.Tables.Count);
        var coverage = Math.Min(1.0, (double)workspace.CurrentSchema.Tables.Count / plannedTotal);

        var report = new PlusReport(
            WaveOrIteration: directive.WaveOrIteration,
            Gains: workspace.CumulativePlusGains.ToList(),
            LockedContracts: workspace.LockedContracts,
            CompletedDomains: workspace.CompletedDomains.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            TotalTablesBuilt: workspace.CurrentSchema.Tables.Count,
            TotalRelationsBuilt: workspace.CurrentSchema.Relations.Count,
            CoverageRatio: Math.Round(coverage, 3));

        workspace.Swarm.SendMessage(
            workspace.PositiveAgentId,
            workspace.MasterAgentId,
            "plus.report",
            $"[+] İterasyon {directive.WaveOrIteration}: {report.TotalTablesBuilt} tablo, {report.TotalRelationsBuilt} ilişki, {workspace.CurrentSchema.Triggers.Count} trigger, {workspace.CurrentSchema.StoredProcedures.Count} prosedür.");

        workspace.RecordStep(AgentStep.Positive(
            $"Yapıcı dalga {directive.WaveOrIteration} tamamlandı: {report.TotalTablesBuilt} tablo, {report.TotalRelationsBuilt} ilişki, {report.LockedContracts.Count} sözleşme kilitlendi."));

        return report;
    }

    private async Task RunCanvasEvolutionAsync(
        OrchestrationWorkspace workspace,
        PositiveDirective directive,
        int beforeTables,
        List<PlusGainItem> gains,
        CancellationToken ct)
    {
        if (ProgrammabilityArchitectSubAgent.IsPureProgrammabilityRequest(workspace.TaskInput.Prompt, workspace.CurrentSchema))
        {
            workspace.CompletedDomains.Add("canvas_programmability");
            return;
        }

        var agent = workspace.Swarm.SpawnAgent(
            AgentKind.SchemaMutator,
            $"canvas-mutator-iter{directive.WaveOrIteration}",
            "Mevcut Canvas şemasını L0+L1 bağlamıyla evrimleştir");

        try
        {
            var nslPatch = await _canvasEvolution
                .ExecuteAsync(agent, (workspace, directive), ct)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(nslPatch))
            {
                var (merged, notes) = _contextEngine.MergeNslIntoSchema(
                    workspace.CurrentSchema,
                    nslPatch,
                    workspace.TaskInput.Engine,
                    preserveExistingUuids: true);
                workspace.CurrentSchema = merged;
                workspace.AddMergeNotes(notes);
            }

            workspace.CompletedDomains.Add("canvas_evolution");
            gains.Add(new PlusGainItem(
                Id: $"plus-evo-{directive.WaveOrIteration}",
                Category: "CanvasEvolution",
                TargetTableOrDomain: "canvas",
                Summary: $"Canvas evrimi uygulandı ({beforeTables} -> {workspace.CurrentSchema.Tables.Count} tablo).",
                LocksStructure: true));
        }
        finally
        {
            workspace.Swarm.RetireAgent(agent.AgentId);
        }
    }

    private async Task RunWaveBuildAsync(
        OrchestrationWorkspace workspace,
        TopologicalWave wave,
        int beforeTables,
        int beforeRels,
        List<PlusGainItem> gains,
        CancellationToken ct)
    {
        var agent = workspace.Swarm.SpawnAgent(
            AgentKind.DomainArchitect,
            $"architect-wave{wave.WaveIndex}",
            $"Dalga {wave.WaveIndex} ({wave.Label}) için NSL şema üretimi");

        try
        {
            var nslChunk = await _domainArchitect
                .ExecuteAsync(agent, (workspace, wave), ct)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(nslChunk))
            {
                var (merged, notes) = _contextEngine.MergeNslIntoSchema(
                    workspace.CurrentSchema,
                    nslChunk,
                    workspace.TaskInput.Engine,
                    preserveExistingUuids: true);
                workspace.CurrentSchema = merged;
                workspace.AddMergeNotes(notes);
            }

            foreach (var dom in wave.Domains)
                workspace.CompletedDomains.Add(dom);

            gains.Add(new PlusGainItem(
                Id: $"plus-wave-{wave.WaveIndex}",
                Category: "WaveGeneration",
                TargetTableOrDomain: wave.Label,
                Summary: $"Dalga {wave.WaveIndex}: +{Math.Max(0, workspace.CurrentSchema.Tables.Count - beforeTables)} tablo, +{Math.Max(0, workspace.CurrentSchema.Relations.Count - beforeRels)} ilişki.",
                LocksStructure: true));
        }
        finally
        {
            workspace.Swarm.RetireAgent(agent.AgentId);
        }
    }

    private async Task RunZeroTokenWeaversAsync(
        OrchestrationWorkspace workspace,
        PositiveDirective directive,
        List<PlusGainItem> gains,
        CancellationToken ct)
    {
        var relAgent = workspace.Swarm.SpawnAgent(
            AgentKind.RelationWeaver,
            $"rel-weaver-iter{directive.WaveOrIteration}",
            "0-token yetim FK sütunlarını ilişkilere örme");
        try
        {
            var woven = await _relationWeaver.ExecuteAsync(relAgent, workspace, ct).ConfigureAwait(false);
            if (woven.Count > 0)
            {
                gains.Add(new PlusGainItem(
                    Id: $"plus-relweave-{directive.WaveOrIteration}",
                    Category: "RelationWeaving",
                    TargetTableOrDomain: "relations",
                    Summary: $"{woven.Count} yetim FK kolonu otomatik ilişkilendirildi.",
                    LocksStructure: true));
            }
        }
        finally
        {
            workspace.Swarm.RetireAgent(relAgent.AgentId);
        }

        var temporalAgent = workspace.Swarm.SpawnAgent(
            AgentKind.SchemaMutator,
            $"audit-weaver-iter{directive.WaveOrIteration}",
            "0-token denetim zaman damgası ve soft-delete standardizasyonu");
        try
        {
            var temporalChanges = await _temporalWeaver.ExecuteAsync(temporalAgent, workspace, ct).ConfigureAwait(false);
            if (temporalChanges.Count > 0)
            {
                gains.Add(new PlusGainItem(
                    Id: $"plus-temporal-{directive.WaveOrIteration}",
                    Category: "AuditAndTemporal",
                    TargetTableOrDomain: "schema",
                    Summary: $"{temporalChanges.Count} zaman damgası/soft-delete iyileştirmesi uygulandı.",
                    LocksStructure: false));
            }
        }
        finally
        {
            workspace.Swarm.RetireAgent(temporalAgent.AgentId);
        }

        var repairAgent = workspace.Swarm.SpawnAgent(
            AgentKind.SchemaMutator,
            $"hygiene-iter{directive.WaveOrIteration}",
            "0-token yapısal temizlik, bileşik indeks ve FK hizalama");
        try
        {
            var fixes = await _deterministicRepair.ExecuteAsync(repairAgent, workspace, ct).ConfigureAwait(false);
            if (fixes.Count > 0)
            {
                gains.Add(new PlusGainItem(
                    Id: $"plus-fix-{directive.WaveOrIteration}",
                    Category: "DeterministicHygiene",
                    TargetTableOrDomain: "schema",
                    Summary: $"{fixes.Count} yapısal iyileştirme (0-token) uygulandı.",
                    LocksStructure: false));
            }
        }
        finally
        {
            workspace.Swarm.RetireAgent(repairAgent.AgentId);
        }
    }

    private async Task RunProgrammabilityAsync(
        OrchestrationWorkspace workspace,
        PositiveDirective directive,
        List<PlusGainItem> gains,
        CancellationToken ct)
    {
        var progAgent = workspace.Swarm.SpawnAgent(
            AgentKind.ProgrammabilityArchitect,
            $"programmability-arch-iter{directive.WaveOrIteration}",
            "Motor lehçesine özel Trigger ve Stored Procedure sentezi");
        try
        {
            var res = await _programmability!.ExecuteAsync(progAgent, workspace, ct).ConfigureAwait(false);
            if (res.SummaryNotes.Count > 0)
            {
                gains.Add(new PlusGainItem(
                    Id: $"plus-prog-{directive.WaveOrIteration}",
                    Category: "Programmability",
                    TargetTableOrDomain: workspace.TaskInput.Engine.ToString(),
                    Summary: string.Join(" ", res.SummaryNotes),
                    LocksStructure: true));
            }
        }
        finally
        {
            workspace.Swarm.RetireAgent(progAgent.AgentId);
        }
    }
}
