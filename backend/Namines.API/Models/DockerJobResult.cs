using System.Collections.Generic;

namespace Namines.API.Models;

public class DockerJobResult
{
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = "Starting";
    public List<string> ProgressLog { get; set; } = new();
    public string? DownloadUrl { get; set; }
}
