using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Namines.API.Models;
using Namines.Core.Interfaces;

namespace Namines.API.Services;

public class DockerJobManager : ISandboxJobRegistry
{
    /// <summary>Tamamlanan bir job'ın bellekte ve diskte tutulma süresi.</summary>
    public static readonly TimeSpan JobRetention = TimeSpan.FromHours(2);

    private readonly ConcurrentDictionary<string, DockerJobResult> _jobs = new();
    private readonly ILogger<DockerJobManager> _logger;

    public DockerJobManager(ILogger<DockerJobManager> logger)
    {
        _logger = logger;
    }

    // To notify listeners (SSE) when a job progress is updated
    public event Action<string, string>? OnProgressUpdated;
    public event Action<string>? OnJobCompleted;

    public void CreateJob(string jobId, string? userId = null)
    {
        _jobs[jobId] = new DockerJobResult { JobId = jobId, UserId = userId };
    }

    public void AddLog(string jobId, string message)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.ProgressLog.Enqueue(message);
            RaiseProgress(jobId, message);
        }
    }

    public void CompleteJob(string jobId, string downloadUrl, string? error = null)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = error == null ? "Done" : "Error";
            job.DownloadUrl = downloadUrl;

            if (error != null)
            {
                job.ProgressLog.Enqueue($"ERROR: {error}");
                RaiseProgress(jobId, $"ERROR: {error}");
            }

            // Signal that the job is done so SSE stream can finalize
            RaiseCompleted(jobId);
        }
    }

    public DockerJobResult? GetJob(string jobId)
    {
        _jobs.TryGetValue(jobId, out var job);
        return job;
    }

    /// <summary>Sweeper'ın canlı job'ların container'ını silmemesi için aktif job kimlikleri.</summary>
    public IReadOnlyCollection<string> GetActiveJobIds()
        => _jobs.Values.Where(j => j.IsActive).Select(j => j.JobId).ToArray();

    /// <summary>Retention süresi dolmuş job'ları bellekten düşürür ve kimliklerini döner.</summary>
    public IReadOnlyCollection<DockerJobResult> EvictExpiredJobs()
    {
        var cutoff = DateTimeOffset.UtcNow - JobRetention;
        var expired = _jobs.Values.Where(j => !j.IsActive && j.CreatedAt < cutoff).ToArray();

        foreach (var job in expired)
            _jobs.TryRemove(job.JobId, out _);

        return expired;
    }

    // ── Event yayını ────────────────────────────────────────────────────────
    // KRİTİK: bu event'ler arka plan JOB THREAD'inden tetiklenir. Aboneler
    // (SSE stream'leri) kopmuş bir sokete yazıp exception fırlatabilir. İzole
    // edilmezse o exception job'ın kendisini öldürür — yani bir tarayıcı
    // sekmesinin kapanması çalışan sandbox'ı iptal eder. Her abone ayrı ayrı
    // çağrılır ve hataları yutulur.
    private void RaiseProgress(string jobId, string message)
    {
        foreach (var handler in OnProgressUpdated?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            try { ((Action<string, string>)handler)(jobId, message); }
            catch (Exception ex) { _logger.LogDebug(ex, "SSE progress abonesi hata verdi (istemci kopmuş olabilir)."); }
        }
    }

    private void RaiseCompleted(string jobId)
    {
        foreach (var handler in OnJobCompleted?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            try { ((Action<string>)handler)(jobId); }
            catch (Exception ex) { _logger.LogDebug(ex, "SSE completion abonesi hata verdi (istemci kopmuş olabilir)."); }
        }
    }
}
