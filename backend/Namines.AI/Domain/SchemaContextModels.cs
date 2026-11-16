using System.Collections.Generic;
using Namines.Core.Analysis;
using Namines.Core.Enums;

namespace Namines.AI.Domain;

/// <summary>
/// Büyük şemaların bağımlılık sırasıyla üretilmesi için topolojik dalga tanımı (Wave 0..N).
/// </summary>
public sealed record TopologicalWave(
    int WaveIndex,
    string Label,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> TargetTables,
    string Rationale);

/// <summary>
/// 1'den 100+ tabloya kadar ölçeklenen katmanlı üretim planı.
/// </summary>
public sealed record TopologicalArchitecturePlan(
    string SchemaName,
    ProjectArchetype Archetype,
    IReadOnlyList<TopologicalWave> Waves,
    int TotalPlannedTables);

/// <summary>
/// L0 İskelet Haritasındaki tek bir tablo düğümü.
/// </summary>
public sealed record TableTopologyNode(
    string TableName,
    string TableId,
    string PkSummary,
    IReadOnlyList<string> OutgoingFkTargets,
    IReadOnlyList<string> IncomingFkSources,
    string TierRole);

/// <summary>
/// L0 — Sıfır LLM token ile milisaniyeler içinde çıkarılan topolojik şema haritası.
/// 100+ tabloluk mevcut bir veritabanını tek bakışta (~1200 token) anlamayı sağlar.
/// </summary>
public sealed record SchemaTopologicalSkeleton(
    string SchemaName,
    DatabaseType Engine,
    int TableCount,
    int RelationCount,
    string NamingConvention,
    string DominantPkType,
    IReadOnlyList<TableTopologyNode> Nodes,
    string CompactTextMap);

/// <summary>
/// L1 — Odaklanılan tabloların kompakt NSL kodu + 1. derece komşu PK sözleşmeleri.
/// </summary>
public sealed record ScopedNslSubgraph(
    IReadOnlyList<string> FocusTables,
    IReadOnlyList<TableSignatureContract> NeighborContracts,
    string FocusNslText,
    int EstimatedTokens);
