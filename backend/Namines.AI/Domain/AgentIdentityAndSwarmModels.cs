using System;
using Namines.Core.Analysis;

namespace Namines.AI.Domain;

/// <summary>
/// Otonom orkestrasyon sürüsündeki ajan rolleri.
/// </summary>
public enum AgentKind
{
    MasterOrchestrator = 0,
    PositiveOrchestrator = 1,
    NegativeOrchestrator = 2,
    Cartographer = 3,
    TopologicalPlanner = 4,
    DomainArchitect = 5,
    SchemaMutator = 6,
    RelationWeaver = 7,
    DeterministicCritic = 8,
    SemanticCoverageCritic = 9,
    DbaFinOpsSecurityCritic = 10,
    SourceIngestor = 11,
    LifecycleSpecialist = 12,
    ProgrammabilityArchitect = 13,
    TriggerAndProcedureCritic = 14,
}

/// <summary>
/// Dinamik olarak oluşturulan bir sub-agent'ın yaşam döngüsü durumu.
/// </summary>
public enum AgentLifecycleState
{
    Spawned = 0,
    Working = 1,
    Idle = 2,
    Retired = 3,
    Terminated = 4,
}

/// <summary>
/// Çalışma zamanında orkestratör tarafından oluşturulup görev bitiminde silinen/emekliye ayrılan ajan kaydı.
/// </summary>
public sealed record AgentInstance(
    string AgentId,
    string Name,
    AgentKind Kind,
    NaiModel ModelTier,
    AgentLifecycleState State,
    DateTimeOffset SpawnedAt,
    string Purpose,
    DateTimeOffset? RetiredAt = null);

/// <summary>
/// Orkestratörler ve sub-agent'lar arasındaki mesaj panosu kaydı.
/// </summary>
public sealed record AgentMessage(
    string MessageId,
    string FromAgentId,
    string ToAgentId,
    string Topic,
    string Content,
    DateTimeOffset SentAt);
