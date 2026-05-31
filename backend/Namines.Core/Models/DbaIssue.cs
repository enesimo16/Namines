using Namines.Core.Enums;

namespace Namines.Core.Models;

public class DbaIssue
{
    public string RuleId { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string? ColumnName { get; set; }
    public DbaIssueSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Suggestion { get; set; }
    public string Source { get; set; } = "Local";
    public string Category { get; set; } = "Performance";
}
