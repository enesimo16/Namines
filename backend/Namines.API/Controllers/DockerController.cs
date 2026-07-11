using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Namines.API.Services;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;

namespace Namines.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DockerController : ControllerBase
{
    private readonly IDockerService _dockerService;
    private readonly DockerJobManager _jobManager;
    private readonly IDdlGeneratorFactory _ddlFactory;

    public DockerController(IDockerService dockerService, DockerJobManager jobManager, IDdlGeneratorFactory ddlFactory)
    {
        _dockerService = dockerService;
        _jobManager = jobManager;
        _ddlFactory = ddlFactory;
    }

    // Hibrit güvenlik: container spawn pahalı/DoS riski → login + rate-limit.
    // NOT: stream/download uçları açık bırakıldı — EventSource bearer header gönderemez;
    // jobId sunucu üretimi GUID olduğundan capability olarak korur.
    [Authorize]
    [EnableRateLimiting("sensitive")]
    [HttpPost("run")]
    public IActionResult RunDockerSandbox([FromBody] CompileRequest request)
    {
        var jobId = Guid.NewGuid().ToString();
        _jobManager.CreateJob(jobId);

        var generator = _ddlFactory.GetGenerator(request.DbType);
        var sql = generator.Generate(request.Schema);

        // Run in background
        Task.Run(async () =>
        {
            try
            {
                await _dockerService.RunSandboxAndBackupAsync(jobId, sql, request.DbType, log =>
                {
                    _jobManager.AddLog(jobId, log);
                });

                var downloadUrl = $"/api/docker/download/{jobId}";
                _jobManager.CompleteJob(jobId, downloadUrl);
            }
            catch (Exception ex)
            {
                _jobManager.CompleteJob(jobId, "", ex.Message);
            }
        });

        return Ok(new { jobId });
    }

    [HttpGet("stream/{jobId}")]
    public async Task StreamLogs(string jobId)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var job = _jobManager.GetJob(jobId);
        if (job == null)
        {
            Response.StatusCode = 404;
            return;
        }

        var tcs = new TaskCompletionSource();

        void OnProgress(string jId, string log)
        {
            if (jId == jobId)
            {
                var data = $"data: {log}\n\n";
                var bytes = System.Text.Encoding.UTF8.GetBytes(data);
                Response.Body.WriteAsync(bytes, 0, bytes.Length).Wait();
                Response.Body.FlushAsync().Wait();
            }
        }

        void OnCompleted(string jId)
        {
            if (jId == jobId)
            {
                tcs.TrySetResult();
            }
        }

        _jobManager.OnProgressUpdated += OnProgress;
        _jobManager.OnJobCompleted += OnCompleted;

        // Send existing logs
        foreach (var log in job.ProgressLog)
        {
            await Response.WriteAsync($"data: {log}\n\n");
        }
        await Response.Body.FlushAsync();

        if (job.Status == "Done" || job.Status == "Error")
        {
            tcs.TrySetResult();
        }

        // Wait until job is completed or client disconnects
        await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, HttpContext.RequestAborted));

        _jobManager.OnProgressUpdated -= OnProgress;
        _jobManager.OnJobCompleted -= OnCompleted;
        
        // Final event to signal client
        if (job.Status == "Done")
            await Response.WriteAsync($"data: DOWNLOAD_URL|{job.DownloadUrl}\n\n");
            
        await Response.Body.FlushAsync();
    }

    [HttpGet("download/{jobId}")]
    public IActionResult DownloadBackup(string jobId)
    {
        var outputsDir = Path.Combine(Directory.GetCurrentDirectory(), "Outputs");
        var bakPath = Path.Combine(outputsDir, $"{jobId}.bak");
        var sqlPath = Path.Combine(outputsDir, $"{jobId}.sql");
        var tarPath = Path.Combine(outputsDir, $"{jobId}.tar");

        if (System.IO.File.Exists(bakPath))
        {
            var stream = new FileStream(bakPath, FileMode.Open, FileAccess.Read);
            return File(stream, "application/octet-stream", $"backup_{jobId}.bak");
        }
        else if (System.IO.File.Exists(sqlPath))
        {
            var stream = new FileStream(sqlPath, FileMode.Open, FileAccess.Read);
            return File(stream, "application/sql", $"backup_{jobId}.sql");
        }
        else if (System.IO.File.Exists(tarPath))
        {
            var stream = new FileStream(tarPath, FileMode.Open, FileAccess.Read);
            return File(stream, "application/x-tar", $"backup_{jobId}.tar");
        }

        return NotFound("Backup file not found.");
    }
}
