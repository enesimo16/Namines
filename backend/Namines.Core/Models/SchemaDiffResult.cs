using System.Collections.Generic;
using Namines.Core.Enums;

namespace Namines.Core.Models;

public class SchemaDiffResult
{
    public List<string> AddedTables { get; set; } = new();
    public List<string> RemovedTables { get; set; } = new();
    public List<TableRenameDetail> RenamedTables { get; set; } = new();
    public List<TableDiffDetail> ModifiedTables { get; set; } = new();
    public List<SchemaRelation> AddedRelations { get; set; } = new();
    public List<SchemaRelation> RemovedRelations { get; set; } = new();

    /// <summary>Geriye uyumluluk için korunuyor. G9'dan itibaren <c>Impact.BreakingChanges</c>'ten
    /// türetiliyor — artık ad-hoc bir tahmin değil, <see cref="Analysis.SchemaImpactAnalyzer"/>'ın
    /// kanıtladığı bulgulardan hesaplanıyor.</summary>
    public bool HasBreakingChanges { get; set; }

    /// <summary>Tek bakışta risk seviyesi — <c>Impact.OverallRisk</c> ile aynı değer.</summary>
    public RiskLevel OverallRisk { get; set; } = RiskLevel.Safe;

    /// <summary><see cref="Analysis.SchemaImpactAnalyzer"/>'ın tam yapılandırılmış çıktısı —
    /// kilit riski, veri kaybı riski, eksik index önerisi, geri alınabilirlik dahil.
    /// Bkz. new-phase/28-IMPACT-ANALYSIS-ENGINE.md.</summary>
    public ImpactReport? Impact { get; set; }
}

public class TableDiffDetail
{
    public string TableName { get; set; } = string.Empty;
    public List<string> AddedColumns { get; set; } = new();
    public List<string> RemovedColumns { get; set; } = new();
    public List<ColumnRenameDetail> RenamedColumns { get; set; } = new();
    public List<string> ModifiedColumns { get; set; } = new();
}

public class TableRenameDetail
{
    public string OldName { get; set; } = string.Empty;
    public string NewName { get; set; } = string.Empty;
}

public class ColumnRenameDetail
{
    public string OldName { get; set; } = string.Empty;
    public string NewName { get; set; } = string.Empty;
}
