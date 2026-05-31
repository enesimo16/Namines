using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Namines.API.Models;

namespace Namines.API.Services;

public class DockerJobManager
{
    private readonly ConcurrentDictionary<string, DockerJobResult> _jobs = new();
    
    // To notify listeners (SSE) when a job progress is updated
    public event Action<string, string>? OnProgressUpdated;
    public event Action<string>? OnJobCompleted;

    public void CreateJob(string jobId)
    {
        _jobs[jobId] = new DockerJobResult { JobId = jobId };
    }

    public void AddLog(string jobId, string message)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.ProgressLog.Add(message);
            OnProgressUpdated?.Invoke(jobId, message);
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
                job.ProgressLog.Add($"ERROR: {error}");
                OnProgressUpdated?.Invoke(jobId, $"ERROR: {error}");
            }
            
            // Signal that the job is done so SSE stream can finalize
            OnJobCompleted?.Invoke(jobId);
        }
    }

    public DockerJobResult? GetJob(string jobId)
    {
        _jobs.TryGetValue(jobId, out var job);
        return job;
    }
}
