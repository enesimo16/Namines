using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Namines.API.Services;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using System.Linq;

namespace Namines.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CoderAIController : ControllerBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DockerJobManager _jobManager;
    private readonly ILogger<CoderAIController> _logger;

    public CoderAIController(
        IServiceScopeFactory scopeFactory,
        DockerJobManager jobManager,
        ILogger<CoderAIController> logger)
    {
        _scopeFactory = scopeFactory;
        _jobManager = jobManager;
        _logger = logger;
    }

    [HttpPost("generate")]
    public IActionResult GenerateSandbox([FromBody] CoderAIRequest request)
    {
        if (request?.Schema == null)
        {
            _logger.LogWarning("CoderAI: Geçersiz istek - Schema null");
            return BadRequest(new { error = "Schema boş olamaz" });
        }

        _logger.LogInformation("CoderAI: Generate isteği alındı. Şema: {Name}, DbType: {DbType}", request.Schema.Name, request.DbType);

        var jobId = Guid.NewGuid().ToString();
        _jobManager.CreateJob(jobId);

        // Run background packaging task (no docker container startup)
        Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;

            var aiFactory = sp.GetRequiredService<IAIFactory>();
            var packager = sp.GetRequiredService<ICoderAIPackager>();
            var ddlFactory = sp.GetRequiredService<Namines.Infrastructure.Generators.DdlGenerator.IDdlGeneratorFactory>();

            try
            {
                _jobManager.AddLog(jobId, "CoderAI: DDL Script oluşturuluyor...");
                var generator = ddlFactory.GetGenerator(request.DbType);
                var sqlContent = generator.Generate(request.Schema);
                _logger.LogInformation("CoderAI [{JobId}]: DDL oluşturuldu ({Len} karakter)", jobId, sqlContent.Length);

                string appPyContent;
                try
                {
                    _jobManager.AddLog(jobId, "CoderAI: Groq AI ile Streamlit uygulaması kodlanıyor...");
                    var aiService = aiFactory.GetService("Groq");
                    appPyContent = await aiService.GenerateStreamlitAppAsync(request.Schema, request.DbType);
                }
                catch (Exception aiEx)
                {
                    _logger.LogWarning(aiEx, "CoderAI [{JobId}]: AI Streamlit kod üretimi başarısız oldu. Programatik şablon motoruna geçiliyor...", jobId);
                    _jobManager.AddLog(jobId, "⚠️ AI kotası aşıldı! Geliştirici paketiniz için C# şablon motoruyla Streamlit uygulaması oluşturuluyor...");
                    appPyContent = GenerateStreamlitAppProgrammatically(request.Schema, request.DbType.ToString());
                }
                _logger.LogInformation("CoderAI [{JobId}]: app.py üretildi ({Len} karakter)", jobId, appPyContent.Length);

                _jobManager.AddLog(jobId, "CoderAI: Proje paketi (.zip) oluşturuluyor...");
                var tempZipPath = await packager.PackageAsZipAsync(appPyContent, sqlContent, request.DbType, request.Schema.Name);

                // Copy generated ZIP to outputs directory under target controller's jobId
                var targetPath = Path.Combine(Directory.GetCurrentDirectory(), "Outputs", $"coderai_{jobId}.zip");
                if (System.IO.File.Exists(targetPath))
                {
                    System.IO.File.Delete(targetPath);
                }
                System.IO.File.Move(tempZipPath, targetPath);

                _jobManager.CompleteJob(jobId, $"/api/coderai/download/{jobId}");
                _logger.LogInformation("CoderAI [{JobId}]: Tamamlandı.", jobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CoderAI [{JobId}]: HATA oluştu", jobId);
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
        Response.Headers.Append("X-Accel-Buffering", "no");

        var job = _jobManager.GetJob(jobId);
        if (job == null)
        {
            Response.StatusCode = 404;
            return;
        }

        var ct = HttpContext.RequestAborted;
        var tcs = new TaskCompletionSource();

        async Task WriteLineAsync(string msg)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                await Response.WriteAsync($"data: {msg}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
            catch { /* client disconnected */ }
        }

        void OnProgress(string jId, string log)
        {
            if (jId != jobId) return;
            WriteLineAsync(log).GetAwaiter().GetResult();
        }

        void OnCompleted(string jId)
        {
            if (jId == jobId) tcs.TrySetResult();
        }

        _jobManager.OnProgressUpdated += OnProgress;
        _jobManager.OnJobCompleted += OnCompleted;

        try
        {
            // Replay logs that arrived before SSE connection
            foreach (var log in job.ProgressLog.ToList())
            {
                await WriteLineAsync(log);
            }

            // If already done, signal immediately
            if (job.Status == "Done" || job.Status == "Error")
            {
                tcs.TrySetResult();
            }

            // Wait for completion signal or client disconnect
            await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, ct));

            // Finalize
            if (job.Status == "Done" && !string.IsNullOrEmpty(job.DownloadUrl))
            {
                await WriteLineAsync($"DOWNLOAD_URL|{job.DownloadUrl}");
            }
            await WriteLineAsync("DONE");
        }
        finally
        {
            _jobManager.OnProgressUpdated -= OnProgress;
            _jobManager.OnJobCompleted -= OnCompleted;
        }
    }

    [HttpGet("download/{jobId}")]
    public IActionResult DownloadZip(string jobId)
    {
        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "Outputs", $"coderai_{jobId}.zip");
        if (!System.IO.File.Exists(filePath))
            return NotFound("Zip dosyası bulunamadı.");

        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        return File(stream, "application/zip", $"admin_panel_{jobId}.zip");
    }

    private string GenerateStreamlitAppProgrammatically(DatabaseSchema schema, string dbType)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("import streamlit as st");
        sb.AppendLine("import pandas as pd");
        sb.AppendLine("import time");
        sb.AppendLine();
        sb.AppendLine("st.set_page_config(page_title='Namines Admin Panel', page_icon='⚡', layout='wide')");
        sb.AppendLine();
        sb.AppendLine("st.markdown('# ⚡ Celestial Admin Dashboard')");
        sb.AppendLine("st.markdown('---')");
        sb.AppendLine();
        sb.AppendLine("st.sidebar.title('⚡ Namines AI Navigation')");
        sb.AppendLine("tables = [");
        foreach (var t in schema.Tables)
        {
            sb.AppendLine($"    '{t.Name}',");
        }
        sb.AppendLine("]");
        sb.AppendLine("selected_table = st.sidebar.selectbox('Tablo Seçin', tables)");
        sb.AppendLine();
        sb.AppendLine("st.subheader(f'📋 {selected_table} Verileri')");
        sb.AppendLine();
        foreach (var t in schema.Tables)
        {
            sb.AppendLine($"if selected_table == '{t.Name}':");
            sb.AppendLine("    cols = [");
            foreach (var col in t.Columns)
            {
                sb.AppendLine($"        '{col.Name}',");
            }
            sb.AppendLine("    ]");
            sb.AppendLine("    data = []");
            sb.AppendLine("    for i in range(1, 11):");
            sb.AppendLine("        row = {}");
            foreach (var col in t.Columns)
            {
                if (col.IsPK)
                {
                    sb.AppendLine($"        row['{col.Name}'] = i");
                }
                else if (col.Type.Contains("INT") || col.Type.Contains("DECIMAL") || col.Type.Contains("FLOAT"))
                {
                    sb.AppendLine($"        row['{col.Name}'] = i * 10");
                }
                else if (col.Type.Contains("DATE") || col.Type.Contains("TIME"))
                {
                    sb.AppendLine($"        row['{col.Name}'] = '2026-05-31'");
                }
                else
                {
                    sb.AppendLine($"        row['{col.Name}'] = f'{t.Name}_{col.Name}_{{i}}'");
                }
            }
            sb.AppendLine("        data.append(row)");
            sb.AppendLine("    df = pd.DataFrame(data)");
            sb.AppendLine("    st.dataframe(df, use_container_width=True)");
            sb.AppendLine();
            sb.AppendLine("    st.markdown('### ➕ Yeni Kayıt Ekle')");
            sb.AppendLine("    with st.form(key=f'add_form_{selected_table}'):");
            foreach (var col in t.Columns)
            {
                if (col.IsPK) continue;
                sb.AppendLine($"        val_{col.Name} = st.text_input('{col.Name}')");
            }
            sb.AppendLine("        submitted = st.form_submit_button('Kayıt Ekle')");
            sb.AppendLine("        if submitted:");
            sb.AppendLine("            st.success('Kayıt başarıyla eklendi!')");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
