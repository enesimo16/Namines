using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Namines.AI.Domain;
using Namines.Core.Models;

namespace Namines.AI.Application;

/// <summary>
/// Sınırlı özerkliğe sahip Ana Baş Orkestratör.
/// [+] Yapıcı ve [-] Eleştirel orkestratörleri sürekli diyalektik döngüde yönetir,
/// dinamik sub-agent sürüsü oluşturup emekliye ayırır, bağlamı NSL-first taşır ve nihai kararı verir.
/// </summary>
public sealed class MasterOrchestrator : IMasterOrchestrator
{
    private readonly ISchemaContextEngine _contextEngine;
    private readonly CartographerSubAgent _cartographer;
    private readonly TopologicalPlannerSubAgent _planner;
    private readonly IPositiveOrchestrator _positive;
    private readonly INegativeOrchestrator _negative;
    private readonly DeterministicRepairSubAgent _deterministicRepair;
    private readonly SurgicalMutationSubAgent _surgicalMutation;
    private readonly LifecycleTaskSubAgent _lifecycle;
    private readonly IAutonomousSwarmTelemetry? _telemetry;

    public MasterOrchestrator(
        ISchemaContextEngine contextEngine,
        CartographerSubAgent cartographer,
        TopologicalPlannerSubAgent planner,
        IPositiveOrchestrator positive,
        INegativeOrchestrator negative,
        DeterministicRepairSubAgent deterministicRepair,
        SurgicalMutationSubAgent surgicalMutation,
        LifecycleTaskSubAgent lifecycle,
        IAutonomousSwarmTelemetry? telemetry = null)
    {
        _contextEngine = contextEngine;
        _cartographer = cartographer;
        _planner = planner;
        _positive = positive;
        _negative = negative;
        _deterministicRepair = deterministicRepair;
        _surgicalMutation = surgicalMutation;
        _lifecycle = lifecycle;
        _telemetry = telemetry;
    }

    public async Task<MasterVerdict> OrchestrateAsync(
        OrchestrationTaskInput input,
        IProgress<AgentStep>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var swarm = new AgentSwarmRuntime();
        var masterAgent = swarm.SpawnAgent(AgentKind.MasterOrchestrator, "master-orchestrator", "Diyalektik [+] / [-] orkestrasyon ve nihai karar lideri");
        var posAgent = swarm.SpawnAgent(AgentKind.PositiveOrchestrator, "positive-orchestrator", "Artıları derleyen ve şema dalgalarını inşa eden orkestratör");
        var negAgent = swarm.SpawnAgent(AgentKind.NegativeOrchestrator, "negative-orchestrator", "Eksileri, riskleri ve DBA bulgularını derleyen orkestratör");

        var initialSchema = input.ExistingSchema is not null
            ? CloneSchemaShell(input.ExistingSchema)
            : new DatabaseSchema { Name = "namines_schema" };

        var workspace = new OrchestrationWorkspace(input, swarm, initialSchema, progress)
        {
            MasterAgentId = masterAgent.AgentId,
            PositiveAgentId = posAgent.AgentId,
            NegativeAgentId = negAgent.AgentId,
        };

        workspace.RecordStep(AgentStep.Master(
            $"Orkestrasyon başlatıldı (Kaynak={input.SourceKind}, Motor={input.Engine}, MevcutTablo={initialSchema.Tables.Count})"));

        await IngestVisionIfPresentAsync(workspace, ct).ConfigureAwait(false);

        // Faz 1: Haritacı + Topolojik Planlayıcı
        await RunSubAgentAsync(swarm, AgentKind.Cartographer, "cartographer", "Çoklu kaynak taraması ve L0 iskelet",
            inst => _cartographer.ExecuteAsync(inst, workspace, ct)).ConfigureAwait(false);

        var plan = await RunSubAgentAsync(swarm, AgentKind.TopologicalPlanner, "topological-planner", "Tier 0..N bağımlılık dalga planı",
            inst => _planner.ExecuteAsync(inst, workspace, ct)).ConfigureAwait(false);

        // Faz 2: [+] Yapıcı İnşa & İlk [-] Eleştirel Denetim
        var (iteration, latestPlus) = await RunPositivePhaseAsync(workspace, plan, ct).ConfigureAwait(false);
        var latestMinus = await _negative.AuditAsync(
            workspace,
            new NegativeDirective(iteration, RunDbaAudit: true, RunCrossEngineCompile: true, RunSemanticCoverage: true),
            ct).ConfigureAwait(false);

        // Faz 3: Sınırlı Özerklik İyileştirme Döngüsü
        if (input.EnableAutonomyRepair)
        {
            (iteration, latestPlus, latestMinus) = await RunAutonomyRepairLoopAsync(
                workspace, swarm, iteration, latestPlus, latestMinus, ct).ConfigureAwait(false);
        }

        latestPlus ??= BuildPlusSnapshot(workspace, iteration);

        var quality = ComputeQualityScore(latestPlus, latestMinus);
        var converged = latestMinus.IsCleanOfBlockers && workspace.CurrentSchema.Tables.Count > 0;

        swarm.RetireAgent(posAgent.AgentId);
        swarm.RetireAgent(negAgent.AgentId);
        swarm.RetireAgent(masterAgent.AgentId);

        var (spawned, retired, _) = swarm.GetSwarmStats();
        var summary =
            $"Master karar: {(converged ? "ONAYLANDI" : "UYARILARLA TAMAMLANDI")} | " +
            $"{workspace.CurrentSchema.Tables.Count} tablo, {workspace.CurrentSchema.Relations.Count} ilişki, " +
            $"kalite={quality:P0}, blocker={latestMinus.DeterministicErrorCount}, sürü={retired}/{spawned} ajan.";

        workspace.RecordStep(AgentStep.Master(summary));

        var verdict = new MasterVerdict(
            Converged: converged,
            QualityScore: quality,
            Summary: summary,
            FinalSchema: workspace.CurrentSchema,
            CumulativePlus: latestPlus,
            RemainingMinus: latestMinus,
            TotalIterations: iteration,
            TotalSubAgentsSpawned: spawned,
            TotalSubAgentsRetired: retired,
            ExecutionTimeline: workspace.Timeline.ToList(),
            MergeNotes: workspace.MergeNotes.ToList());

        _telemetry?.RecordRun(input, verdict);
        return verdict;
    }

    private async Task IngestVisionIfPresentAsync(OrchestrationWorkspace workspace, CancellationToken ct)
    {
        var input = workspace.TaskInput;
        if (input.ImageBytes is not { Length: > 0 } || workspace.CurrentSchema.Tables.Count > 0)
            return;

        var visionSchema = await _lifecycle
            .AnalyzeImageAsync(input.ImageBytes, input.ImageMimeType ?? "image/png", ct)
            .ConfigureAwait(false);
        workspace.CurrentSchema = WorkspaceToolRegistry.MergeExtractedIntoWorkspace(
            _contextEngine, workspace.CurrentSchema, visionSchema, input.Engine);
        workspace.RecordStep(AgentStep.Tool("Görsel ERD çıkarıldı", $"{workspace.CurrentSchema.Tables.Count} tablo okundu."));
    }

    private async Task<(int Iteration, PlusReport? Plus)> RunPositivePhaseAsync(
        OrchestrationWorkspace workspace,
        TopologicalArchitecturePlan plan,
        CancellationToken ct)
    {
        var input = workspace.TaskInput;
        var iteration = 0;
        PlusReport? latestPlus = null;

        if (input.SourceKind == SchemaInputSourceKind.ExistingCanvasSchema && workspace.CurrentSchema.Tables.Count > 0)
        {
            iteration++;
            latestPlus = await _positive.ExecuteAsync(
                workspace, new PositiveDirective(iteration, IsCanvasEvolution: true), ct).ConfigureAwait(false);
            return (iteration, latestPlus);
        }

        var maxWaves = Math.Max(1, input.MaxAutonomyWaves);
        foreach (var wave in plan.Waves.Take(maxWaves))
        {
            iteration++;
            latestPlus = await _positive.ExecuteAsync(
                workspace, new PositiveDirective(iteration, TargetWave: wave), ct).ConfigureAwait(false);
        }

        return (iteration, latestPlus);
    }

    private async Task<(int Iteration, PlusReport? Plus, MinusReport Minus)> RunAutonomyRepairLoopAsync(
        OrchestrationWorkspace workspace,
        IAgentSwarmRuntime swarm,
        int iteration,
        PlusReport? latestPlus,
        MinusReport latestMinus,
        CancellationToken ct)
    {
        var input = workspace.TaskInput;
        var repairBudget = Math.Clamp(input.MaxAutonomyWaves, 1, 3);

        for (var round = 0; round < repairBudget; round++)
        {
            var unresolved = latestMinus.Findings
                .Where(f => f.Severity is MinusSeverity.Blocker or MinusSeverity.Structural or MinusSeverity.SemanticGap)
                .ToList();

            if (unresolved.Count == 0 && latestMinus.IsCleanOfBlockers)
                break;

            iteration++;

            if (unresolved.Any(f => f.IsDeterministicAutoFixable))
            {
                await RunSubAgentAsync(
                    swarm, AgentKind.SchemaMutator, $"auto-repair-iter{iteration}",
                    "Oto-onarılabilir hataların 0-token giderimi",
                    inst => _deterministicRepair.ExecuteAsync(inst, workspace, ct)).ConfigureAwait(false);
            }

            var llmTargets = unresolved.Where(f => !f.IsDeterministicAutoFixable).Take(10).ToList();
            if (llmTargets.Count > 0)
            {
                var patch = await RunSubAgentAsync(
                    swarm, AgentKind.SchemaMutator, $"surgical-repair-iter{iteration}",
                    $"[-] eksikleri için L1 alt grafik cerrahisi ({llmTargets.Count} bulgu)",
                    inst => _surgicalMutation.ExecuteAsync(inst, (workspace, llmTargets), ct)).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(patch))
                {
                    var (merged, notes) = _contextEngine.MergeNslIntoSchema(
                        workspace.CurrentSchema, patch, input.Engine, preserveExistingUuids: true);
                    workspace.CurrentSchema = merged;
                    workspace.AddMergeNotes(notes);
                }
            }

            workspace.UpsertContracts(_contextEngine.ExtractLockedContracts(workspace.CurrentSchema));
            latestMinus = await _negative.AuditAsync(
                workspace,
                new NegativeDirective(iteration, RunDbaAudit: false, RunCrossEngineCompile: true, RunSemanticCoverage: true),
                ct).ConfigureAwait(false);

            latestPlus = BuildPlusSnapshot(workspace, iteration);
        }

        return (iteration, latestPlus, latestMinus);
    }

    private static async Task<T> RunSubAgentAsync<T>(
        IAgentSwarmRuntime swarm,
        AgentKind kind,
        string name,
        string purpose,
        Func<AgentInstance, Task<T>> work)
    {
        var inst = swarm.SpawnAgent(kind, name, purpose);
        try
        {
            return await work(inst).ConfigureAwait(false);
        }
        finally
        {
            swarm.RetireAgent(inst.AgentId);
        }
    }

    private static PlusReport BuildPlusSnapshot(OrchestrationWorkspace workspace, int iteration)
    {
        var plannedTotal = Math.Max(1, workspace.Plan?.TotalPlannedTables ?? workspace.CurrentSchema.Tables.Count);
        return new PlusReport(
            WaveOrIteration: iteration,
            Gains: workspace.CumulativePlusGains.ToList(),
            LockedContracts: workspace.LockedContracts,
            CompletedDomains: workspace.CompletedDomains.ToList(),
            TotalTablesBuilt: workspace.CurrentSchema.Tables.Count,
            TotalRelationsBuilt: workspace.CurrentSchema.Relations.Count,
            CoverageRatio: Math.Min(1.0, (double)workspace.CurrentSchema.Tables.Count / plannedTotal));
    }

    private static double ComputeQualityScore(PlusReport plus, MinusReport minus)
    {
        var baseScore = Math.Clamp(plus.CoverageRatio, 0.0, 1.0) * 0.4 + 0.6;
        var penalty = minus.DeterministicErrorCount * 0.25 +
                      minus.SemanticGapCount * 0.10 +
                      minus.DbaWarningCount * 0.02;
        return Math.Round(Math.Clamp(baseScore - penalty, 0.0, 1.0), 3);
    }

    private static DatabaseSchema CloneSchemaShell(DatabaseSchema source) => new()
    {
        SchemaId = string.IsNullOrWhiteSpace(source.SchemaId) ? Guid.NewGuid().ToString() : source.SchemaId,
        Name = source.Name,
        CloudProvider = source.CloudProvider,
        IncludeBiModule = source.IncludeBiModule,
        Tables = source.Tables.ToList(),
        Relations = source.Relations.ToList(),
        Enums = source.Enums.ToList(),
        Triggers = source.Triggers.ToList(),
        StoredProcedures = source.StoredProcedures.ToList(),
    };
}
