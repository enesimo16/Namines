using System.Collections.Generic;

namespace Namines.Core.Models;

public class DbaAnalysisResult
{
    public List<DbaIssue> Issues { get; set; } = new();
    public int TotalScore { get; set; } = 100;
    public string OverallAssessment { get; set; } = string.Empty;
}
