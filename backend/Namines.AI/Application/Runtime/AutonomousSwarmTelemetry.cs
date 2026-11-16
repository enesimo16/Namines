using System;
using System.Collections.Generic;
using System.Linq;
using Namines.AI.Domain;
using Namines.Core.Analysis;
using Namines.Core.Models;

namespace Namines.AI.Application;

public sealed record SubAgentDescriptor(
    string Kind,
    string Name,
    string Role,
    bool ZeroTokenCapable,
    string OrchestratorParent);

public sealed record WorkspaceToolDescriptor(
    string Name,
    string Description);

public sealed record PlanQuotaDescriptor(
    string Plan,
    int DailyTokens,
    int MaxAutonomyWaves,
    int MaxOutputTokens);

public sealed record AutonomousRunRecord(
    string RunId,
    DateTimeOffset Timestamp,
    string SourceKind,
    string Engine,
    string PromptPreview,
    bool Converged,
    double QualityScore,
    int TotalIterations,
    int TablesBuilt,
    int RelationsBuilt,
    int LockedContractsCount,
    int SpawnedSubAgents,
    int RetiredSubAgents,
    IReadOnlyList<string> PlusGains,
    int MinusBlockerCount,
    int MinusGapCount,
    int MinusDbaCount,
    IReadOnlyList<string> RemainingFindings,
    IReadOnlyList<string> PortabilityNotes,
    IReadOnlyList<string> Timeline);

public sealed record AutonomousEngineSnapshot(
    string EngineVersion,
    string ContextArchitecture,
    int MaxOutputTokensCeiling,
    int TotalRuns,
    int ConvergedRuns,
    double AverageQualityScore,
    int TotalSubAgentsSpawned,
    int TotalSubAgentsRetired,
    int TotalTablesSynthesized,
    int TotalLockedContracts,
    IReadOnlyList<SubAgentDescriptor> SubAgents,
    IReadOnlyList<WorkspaceToolDescriptor> WorkspaceTools,
    IReadOnlyList<PlanQuotaDescriptor> Quotas,
    IReadOnlyList<AutonomousRunRecord> RecentRuns);

/// <summary>
/// Sınırlı özerkliğe sahip Namines.AI v2 sürüsünün canlı orkestrasyon kararlarını,
/// dinamik sub-agent istatistiklerini ve mimari envanterini yönetim paneline sunan telemetri sözleşmesi.
/// </summary>
public interface IAutonomousSwarmTelemetry
{
    void RecordRun(OrchestrationTaskInput input, MasterVerdict verdict);
    AutonomousEngineSnapshot GetSnapshot();
}

/// <summary>
/// İş parçacığı güvenli halka arabellek (son 25 çalıştırma) + kümülatif KPI sayacı.
/// </summary>
public sealed class AutonomousSwarmTelemetry : IAutonomousSwarmTelemetry
{
    private const int MaxRecentRuns = 25;

    private static readonly IReadOnlyList<SubAgentDescriptor> CatalogSubAgents = new[]
    {
        new SubAgentDescriptor(nameof(AgentKind.Cartographer), nameof(CartographerSubAgent), "Çoklu kaynak tarama + L0 topolojik iskelet çıkarımı (0-token)", true, "MasterOrchestrator"),
        new SubAgentDescriptor(nameof(AgentKind.TopologicalPlanner), nameof(TopologicalPlannerSubAgent), "Tier 0..4 katmanlı 1-160 tablo bağımlılık dalgası planlama", true, "MasterOrchestrator"),
        new SubAgentDescriptor(nameof(AgentKind.DomainArchitect), nameof(NslDomainArchitectSubAgent), "Kilitli PK/FK sözleşmeleriyle dalga bazlı NSL 1.0 şema inşası", false, "PositiveOrchestrator [+]"),
        new SubAgentDescriptor(nameof(AgentKind.SchemaMutator), nameof(CanvasEvolutionSubAgent), "L0 + L1 alt grafikle StableUuid koruyan Canvas evrimi", false, "PositiveOrchestrator [+]"),
        new SubAgentDescriptor(nameof(AgentKind.RelationWeaver), nameof(RelationWeaverSubAgent), "0-token yetim *_id sütunlarını hedef PK'lara otomatik FK örme ve indeksleme", true, "PositiveOrchestrator [+]"),
        new SubAgentDescriptor(nameof(AgentKind.SchemaMutator), nameof(AuditAndTemporalWeaverSubAgent), "0-token created_at varsayılanı ve soft-delete kısmi indeks standardizasyonu", true, "PositiveOrchestrator [+]"),
        new SubAgentDescriptor(nameof(AgentKind.ProgrammabilityArchitect), nameof(ProgrammabilityArchitectSubAgent), "Hedef motor lehçesinde Trigger ve Stored Procedure sentezi", false, "PositiveOrchestrator [+]"),
        new SubAgentDescriptor(nameof(AgentKind.SchemaMutator), nameof(DeterministicRepairSubAgent), "0-token PK, DECIMAL, password_hash, köprü/polimorfik indeks ve Cascade onarımı", true, "Positive [+] / Master"),
        new SubAgentDescriptor(nameof(AgentKind.SchemaMutator), nameof(SurgicalMutationSubAgent), "Kalan blocker ve kapsam eksikleri için 1-8 tabloluk L1 cerrahi NSL", false, "MasterOrchestrator"),
        new SubAgentDescriptor(nameof(AgentKind.SemanticCoverageCritic), nameof(SemanticAndPortabilityCriticSubAgent), "0-token dalga kapsamı ve 6 SQL motoru arası özellik taşınabilirlik analizi", true, "NegativeOrchestrator [-]"),
        new SubAgentDescriptor(nameof(AgentKind.SemanticCoverageCritic), nameof(NormalizationCriticSubAgent), "0-token 1NF tekrarlayan kolon / CSV ve 3NF geçişli kopya kolon denetimi", true, "NegativeOrchestrator [-]"),
        new SubAgentDescriptor(nameof(AgentKind.DbaFinOpsSecurityCritic), nameof(MultiTenancyAndRlsCriticSubAgent), "0-token çok kiracılı tenant_id izolasyonu ve kiracı indeks açığı tespiti", true, "NegativeOrchestrator [-]"),
        new SubAgentDescriptor(nameof(AgentKind.DbaFinOpsSecurityCritic), nameof(IndexAndConstraintCriticSubAgent), "0-token köprü tablo tekil kısıt, polimorfik indeks ve tarih CHECK denetimi", true, "NegativeOrchestrator [-]"),
        new SubAgentDescriptor(nameof(AgentKind.TriggerAndProcedureCritic), nameof(TriggerAndProcedureCriticSubAgent), "0-token Trigger TableId, Timing/Event, SQL gövde ve yıkıcı DDL güvenlik denetimi", true, "NegativeOrchestrator [-]"),
        new SubAgentDescriptor(nameof(AgentKind.DbaFinOpsSecurityCritic), nameof(AdversarialDbaSubAgent), "0-token yerel + NSL-sıkıştırılmış derin Performans/FinOps/PII DBA denetimi", true, "NegativeOrchestrator [-]"),
        new SubAgentDescriptor(nameof(AgentKind.LifecycleSpecialist), nameof(LifecycleTaskSubAgent), "Migration Up/Down, SmartSeed, MockData, DbContext, Summary, NL->SQL, Vision", false, "UnifiedAiService"),
    };

    private static readonly IReadOnlyList<WorkspaceToolDescriptor> CatalogTools = new[]
    {
        new WorkspaceToolDescriptor("code_schema_extract", "Prisma (.prisma), EF Core (DbSet) ve ham .sql dosyalarından 0-token deterministik çıkarım"),
        new WorkspaceToolDescriptor("json_shape_infer", "Örnek JSON yanıtlarından değer saklamadan varlık, FK ve belirsiz (nullable) alan çıkarımı"),
        new WorkspaceToolDescriptor("openapi_extract", "OpenAPI/Swagger ve GraphQL introspection uçlarından varlık adayları çıkarma"),
        new WorkspaceToolDescriptor("github_extract", "SSRF-güvenli GitHub depo URL taraması ve şema birleştirme"),
        new WorkspaceToolDescriptor("deterministic_validate_compile", "NslValidator + FkCascadeAnalyzer + 6 motor (PG/MySQL/MSSQL/Oracle/SQLite/MariaDB) DDL derleme kapısı"),
        new WorkspaceToolDescriptor("subgraph_inspect", "L1 odaklı NSL alt grafiği + 1-hop kilitli komşu PK/FK sözleşme incelemesi"),
    };

    private readonly object _sync = new();
    private readonly LinkedList<AutonomousRunRecord> _recentRuns = new();
    private int _totalRuns;
    private int _convergedRuns;
    private double _qualityScoreSum;
    private int _totalSpawned;
    private int _totalRetired;
    private int _totalTables;
    private int _totalLockedContracts;

    public void RecordRun(OrchestrationTaskInput input, MasterVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(verdict);

        var prompt = (input.Prompt ?? string.Empty).Trim();
        var preview = prompt.Length <= 140 ? prompt : prompt[..140] + "...";

        var record = new AutonomousRunRecord(
            RunId: Guid.NewGuid().ToString("N")[..12],
            Timestamp: DateTimeOffset.UtcNow,
            SourceKind: input.SourceKind.ToString(),
            Engine: input.Engine.ToString(),
            PromptPreview: preview,
            Converged: verdict.Converged,
            QualityScore: verdict.QualityScore,
            TotalIterations: verdict.TotalIterations,
            TablesBuilt: verdict.FinalSchema.Tables.Count,
            RelationsBuilt: verdict.FinalSchema.Relations.Count,
            LockedContractsCount: verdict.CumulativePlus.LockedContracts.Count,
            SpawnedSubAgents: verdict.TotalSubAgentsSpawned,
            RetiredSubAgents: verdict.TotalSubAgentsRetired,
            PlusGains: verdict.CumulativePlus.Gains.Select(g => $"[{g.Category}] {g.Summary}").Take(8).ToList(),
            MinusBlockerCount: verdict.RemainingMinus.DeterministicErrorCount,
            MinusGapCount: verdict.RemainingMinus.SemanticGapCount,
            MinusDbaCount: verdict.RemainingMinus.DbaWarningCount,
            RemainingFindings: verdict.RemainingMinus.Findings.Select(f => $"[{f.Code}/{f.Severity}] {f.ProblemDescription}").Take(8).ToList(),
            PortabilityNotes: verdict.RemainingMinus.PortabilityNotes.Take(6).ToList(),
            Timeline: verdict.ExecutionTimeline.Take(20).ToList());

        lock (_sync)
        {
            _totalRuns++;
            if (verdict.Converged) _convergedRuns++;
            _qualityScoreSum += verdict.QualityScore;
            _totalSpawned += verdict.TotalSubAgentsSpawned;
            _totalRetired += verdict.TotalSubAgentsRetired;
            _totalTables += verdict.FinalSchema.Tables.Count;
            _totalLockedContracts += verdict.CumulativePlus.LockedContracts.Count;

            _recentRuns.AddFirst(record);
            while (_recentRuns.Count > MaxRecentRuns)
                _recentRuns.RemoveLast();
        }
    }

    public AutonomousEngineSnapshot GetSnapshot()
    {
        var maxCeiling = AiAdvancedSettings.Default.MaxTokensValue;
        var quotas = new[]
        {
            PlanTier.Free,
            PlanTier.Pro,
            PlanTier.Team,
            PlanTier.Enterprise,
            PlanTier.Dev,
        }
        .Select(tier =>
        {
            var q = PlanQuotas.For(tier);
            return new PlanQuotaDescriptor(
                tier.ToString(),
                q.DailyAiTokens,
                q.MaxAgentRepairTurns,
                AiAdvancedSettings.Default.MaxTokensFor(tier));
        })
        .ToList();

        lock (_sync)
        {
            var avgQuality = _totalRuns > 0
                ? Math.Round(_qualityScoreSum / _totalRuns, 3)
                : 0.0;

            return new AutonomousEngineSnapshot(
                EngineVersion: "Namines.AI v2 (Dialectical Swarm)",
                ContextArchitecture: "NSL-First L0 Topological Skeleton + L1 Scoped Subgraph + L2 Locked Contracts",
                MaxOutputTokensCeiling: maxCeiling,
                TotalRuns: _totalRuns,
                ConvergedRuns: _convergedRuns,
                AverageQualityScore: avgQuality,
                TotalSubAgentsSpawned: _totalSpawned,
                TotalSubAgentsRetired: _totalRetired,
                TotalTablesSynthesized: _totalTables,
                TotalLockedContracts: _totalLockedContracts,
                SubAgents: CatalogSubAgents,
                WorkspaceTools: CatalogTools,
                Quotas: quotas,
                RecentRuns: _recentRuns.ToList());
        }
    }
}
