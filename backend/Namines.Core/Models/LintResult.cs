using System.Collections.Generic;

namespace Namines.Core.Models;

public enum LintSeverity
{
    Info,
    Warning,
    Error
}

public class LintMessage
{
    public LintSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? TableId { get; set; }
    public string? ColumnId { get; set; }
}

public class LintResult
{
    public List<LintMessage> Messages { get; set; } = new();
    public bool HasErrors => Messages.Exists(m => m.Severity == LintSeverity.Error);
}
