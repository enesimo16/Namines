using System.Collections.Generic;

namespace Namines.Core.Models;

public class MigrationResult
{
    public string UpCode { get; set; } = string.Empty;
    public string DownCode { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
}
