using System.Collections.Concurrent;

namespace Namines.API.Models;

public class DockerJobResult
{
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = "Starting";
    // Thread-safe: arka plan işi Enqueue ederken SSE thread'i eşzamanlı enumerate ediyor.
    public ConcurrentQueue<string> ProgressLog { get; set; } = new();
    public string? DownloadUrl { get; set; }
}
