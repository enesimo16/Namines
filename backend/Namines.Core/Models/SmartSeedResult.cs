using System.Collections.Generic;

namespace Namines.Core.Models;

public class SmartSeedResult
{
    public string SqlScript { get; set; } = string.Empty;
    public string DetectedDomain { get; set; } = string.Empty;
    public Dictionary<string, int> TableRowCounts { get; set; } = new();
    public long EstimatedSizeBytes { get; set; }
}
