using System.Collections.Generic;
using Namines.Core.Models;

namespace Namines.AI.Domain;

/// <summary>
/// Bir tablonun kesinleşmiş ve alt dalgalara aktarılan arayüz sözleşmesi.
/// Kilitlenen tabloların PK ve temel benzersiz kolon tipleri sonraki turlarda bozulmaz.
/// </summary>
public sealed record TableSignatureContract(
    string TableName,
    string DomainName,
    string PkColumnName,
    string PkCanonicalType,
    IReadOnlyList<string> UniqueColumns,
    IReadOnlyList<string> ForeignKeysSummary,
    bool IsLocked);

/// <summary>
/// [+] Yapıcı Orkestratörün şemada doğruladığı veya eklediği somut bir kazanım.
/// </summary>
public sealed record PlusGainItem(
    string Id,
    string Category,
    string TargetTableOrDomain,
    string Summary,
    bool LocksStructure);

/// <summary>
/// [+] Yapıcı Orkestratörün Baş Orkestratöre sunduğu Artılar Raporu.
/// </summary>
public sealed record PlusReport(
    int WaveOrIteration,
    IReadOnlyList<PlusGainItem> Gains,
    IReadOnlyList<TableSignatureContract> LockedContracts,
    IReadOnlyList<string> CompletedDomains,
    int TotalTablesBuilt,
    int TotalRelationsBuilt,
    double CoverageRatio);

/// <summary>
/// [-] Eleştirel Orkestratörün sınıflandırdığı bulgu şiddeti.
/// </summary>
public enum MinusSeverity
{
    Blocker = 0,
    Structural = 1,
    SemanticGap = 2,
    DbaOptimization = 3,
}

/// <summary>
/// [-] Eleştirel Orkestratörün tespit ettiği ve eyleme dönük direktif içeren bulgu.
/// </summary>
public sealed record MinusFindingItem(
    string Id,
    string Code,
    MinusSeverity Severity,
    string? TableName,
    string? ColumnName,
    string ProblemDescription,
    string ActionableDirective,
    string SourceSubAgent,
    bool IsDeterministicAutoFixable = false);

/// <summary>
/// [-] Eleştirel Orkestratörün Baş Orkestratöre sunduğu Eksiler / Riskler Raporu.
/// </summary>
public sealed record MinusReport(
    int WaveOrIteration,
    IReadOnlyList<MinusFindingItem> Findings,
    IReadOnlyList<string> PortabilityNotes,
    bool IsCleanOfBlockers,
    int DeterministicErrorCount,
    int SemanticGapCount,
    int DbaWarningCount);

/// <summary>
/// Ana Baş Orkestratörün diyalektik döngü sonunda verdiği nihai karar.
/// </summary>
public sealed record MasterVerdict(
    bool Converged,
    double QualityScore,
    string Summary,
    DatabaseSchema FinalSchema,
    PlusReport CumulativePlus,
    MinusReport RemainingMinus,
    int TotalIterations,
    int TotalSubAgentsSpawned,
    int TotalSubAgentsRetired,
    IReadOnlyList<string> ExecutionTimeline,
    IReadOnlyList<string> MergeNotes);
