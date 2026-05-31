using System.Collections.Generic;

namespace Namines.Core.Models;

public class SchemaDiffResult
{
    public List<string> AddedTables { get; set; } = new();
    public List<string> RemovedTables { get; set; } = new();
    public List<TableRenameDetail> RenamedTables { get; set; } = new();
    public List<TableDiffDetail> ModifiedTables { get; set; } = new();
    public bool HasBreakingChanges { get; set; }
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
