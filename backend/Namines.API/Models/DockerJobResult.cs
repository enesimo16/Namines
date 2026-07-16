using System;
using System.Collections.Concurrent;

namespace Namines.API.Models;

public class DockerJobResult
{
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = "Starting";
    // Thread-safe: arka plan işi Enqueue ederken SSE thread'i eşzamanlı enumerate ediyor.
    public ConcurrentQueue<string> ProgressLog { get; set; } = new();
    public string? DownloadUrl { get; set; }

    /// <summary>
    /// İşi başlatan kullanıcının kimliği. Backup çıktısı kullanıcının tam şemasını
    /// içerir; indirme ucunda sahiplik kontrolü buna dayanır (IDOR koruması).
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>Oluşturulma zamanı — TTL tabanlı tahliye ve retention süpürmesi için.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>İşin hâlâ sürüp sürmediği. Sweeper canlı job'ların container'ını silmemeli.</summary>
    public bool IsActive => Status is "Starting" or "Running";
}
