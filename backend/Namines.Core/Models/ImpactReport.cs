using System.Collections.Generic;
using Namines.Core.Enums;

namespace Namines.Core.Models;

/// <summary>Bir tablo/kolonun iki şema sürümü arasında geçirdiği durum değişikliği.</summary>
public enum ChangeKind
{
    Added,
    Removed,
    Modified,

    /// <summary><see cref="SchemaTable.StableUuid"/>/<see cref="SchemaColumn.StableUuid"/> aynı,
    /// ama <c>Name</c> değişmiş — gerçek bir rename, add+remove değil.</summary>
    RenamedFrom
}

/// <summary>
/// API/istemci sözleşmesini kıran veya DDL motor tarafından reddedilen değişiklik türü.
/// </summary>
public enum BreakingChangeKind
{
    ColumnRemoved,
    ColumnRenamed,
    TableRemoved,
    TableRenamed,

    /// <summary>Kolon tipi/uzunluğu daraltıldı — mevcut veri kesilebilir veya tip dönüşümü başarısız olabilir.</summary>
    TypeNarrowed,

    /// <summary>Mevcut satırlar varken DEFAULT'suz NOT NULL kolon eklendi — migration satır varsa başarısız olur.</summary>
    NotNullWithoutDefault,

    /// <summary><see cref="Analysis.CascadeIssueKind.MultipleCascadePaths"/> — SQL Server DDL'i reddeder (Msg 1785).</summary>
    MultipleCascadePaths,

    /// <summary><see cref="Analysis.CascadeIssueKind.CascadeCycle"/> — cascade döngüsü, öngörülemez zincirleme silme.</summary>
    CascadeCycle,

    /// <summary>ON DELETE SET NULL hedefi NOT NULL kolon — çalışma zamanında ihlal üretir.</summary>
    InvalidSetNullTarget,

    /// <summary>ON DELETE SET DEFAULT hedefinin DEFAULT değeri tanımlı değil.</summary>
    InvalidSetDefaultTarget
}

public sealed record AffectedTable(
    string TableName,
    ChangeKind Kind,
    IReadOnlyList<string> ChangedColumns,
    string? PreviousName = null);

public sealed record AffectedRelation(
    string? RelationId,
    ChangeKind Kind,
    string FromTable,
    string ToTable);

public sealed record AffectedIndex(
    string TableName,
    string? IndexName,
    ChangeKind Kind);

public sealed record BreakingChange(
    string Description,
    BreakingChangeKind Kind,
    string? TableName = null,
    string? ColumnName = null,
    string? SuggestedMitigation = null);

public sealed record DataLossRisk(
    string TableName,
    string? ColumnName,
    string Reason);

public sealed record MigrationLockRisk(
    string Operation,
    LockSeverity Severity,
    string? TableName = null,
    string? SaferAlternative = null);

public sealed record MissingIndexSuggestion(
    string TableName,
    string ColumnName,
    string Reason);

public sealed record RollbackAssessment(bool IsReversible, string? Reason);

/// <summary>
/// İki <see cref="DatabaseSchema"/> sürümü arasındaki yapısal fark ve bunun sonuçları.
/// <see cref="Analysis.SchemaImpactAnalyzer"/> tarafından üretilir — DETERMİNİSTİKTİR.
/// AI bu yapıyı sonradan insan diline çevirir, kendisi bulgu üretmez (bkz.
/// new-phase/28-IMPACT-ANALYSIS-ENGINE.md §1).
/// </summary>
public sealed record ImpactReport(
    IReadOnlyList<AffectedTable> AffectedTables,
    IReadOnlyList<AffectedRelation> AffectedRelations,
    IReadOnlyList<AffectedIndex> AffectedIndexes,
    IReadOnlyList<BreakingChange> BreakingChanges,
    IReadOnlyList<DataLossRisk> DataLossRisks,
    IReadOnlyList<MigrationLockRisk> LockRisks,
    IReadOnlyList<MissingIndexSuggestion> IndexSuggestions,
    RollbackAssessment Rollback,
    RiskLevel OverallRisk);
