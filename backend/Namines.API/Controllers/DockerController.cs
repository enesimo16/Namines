using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private readonly DockerJobManager _jobManager;
    private readonly IDdlGeneratorFactory _ddlFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DockerController> _logger;

    public DockerController(
        DockerJobManager jobManager,
        IDdlGeneratorFactory ddlFactory,
        IServiceScopeFactory scopeFactory,
        ILogger<DockerController> logger)
    {
        _jobManager = jobManager;
        _ddlFactory = ddlFactory;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    private string? CurrentUserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// jobId yalnızca sunucu üretimi bir GUID olabilir. Doğrulanmazsa değer
    /// Path.Combine'a ham girer ve "..%5C..%5Cfoo" gibi bir yol Outputs/ klasöründen
    /// kaçarak diskteki rastgele dosyaların okunmasına yol açar (path traversal).
    /// </summary>
    private static bool IsValidJobId(string jobId) => Guid.TryParse(jobId, out _);

    // Hibrit güvenlik: container spawn pahalı/DoS riski → login + rate-limit.
    [Authorize]
    [EnableRateLimiting("sensitive")]
    [HttpPost("run")]
    public IActionResult RunDockerSandbox([FromBody] CompileRequest request)
    {
        if (request?.Schema == null)
            return BadRequest(new { message = "Şema bulunamadı. Lütfen önce bir şema oluşturun." });

        // DDL üretimi job kaydından ÖNCE yapılır: desteklenmeyen bir DbType burada
        // exception atarsa geriye "Starting" durumunda asılı kalan bir job kalmasın.
        string sql;
        try
        {
            var generator = _ddlFactory.GetGenerator(request.DbType);
            sql = generator.Generate(request.Schema);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Docker sandbox için DDL üretilemedi. DbType={DbType}", request.DbType);
            return BadRequest(new { message = $"'{request.DbType}' için DDL üretilemedi: {ex.Message}" });
        }

        var jobId = Guid.NewGuid().ToString();
        var userId = CurrentUserId;
        _jobManager.CreateJob(jobId, userId);

        // Arka planda çalıştır.
        // KRİTİK: IDockerService scoped'dur; request scope'u response yazılınca kapanır
        // ve DockerClient dispose edilir. Fire-and-forget iş bunu kullanamaz — bu yüzden
        // işin ömrü boyunca yaşayan KENDİ scope'unu açıyoruz.
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var dockerService = scope.ServiceProvider.GetRequiredService<IDockerService>();

            try
            {
                await dockerService.RunSandboxAndBackupAsync(jobId, sql, request.DbType, log =>
                {
                    _jobManager.AddLog(jobId, log);
                });

                _jobManager.CompleteJob(jobId, $"/api/docker/download/{jobId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Docker sandbox işi başarısız. JobId={JobId}", jobId);
                _jobManager.CompleteJob(jobId, "", ex.Message);
            }
        });

        return Ok(new { jobId });
    }

    /// <summary>
    /// İlerleme akışı (SSE).
    /// EventSource Authorization header gönderemediği için [Authorize] konulamaz;
    /// sunucu üretimi GUID jobId capability görevi görür. Akış yalnızca log yayar —
    /// asıl veri (backup) indirme ucunda sahiplik kontrolüyle korunur.
    /// </summary>
    [HttpGet("stream/{jobId}")]
    public async Task StreamLogs(string jobId)
    {
        if (!IsValidJobId(jobId))
        {
            Response.StatusCode = 400;
            return;
        }

        var job = _jobManager.GetJob(jobId);
        if (job == null)
        {
            Response.StatusCode = 404;
            return;
        }

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");
        // Reverse proxy (nginx) arkasında tamponlamayı kapat — yoksa loglar iş bitene
        // kadar istemciye ulaşmaz.
        Response.Headers.Append("X-Accel-Buffering", "no");

        var ct = HttpContext.RequestAborted;
        var tcs = new TaskCompletionSource();

        // Response.Body'ye eşzamanlı yazım ASP.NET Core'da desteklenmez: arka plan
        // thread'inden gelen OnProgress ile buradaki replay döngüsü çakışabilir.
        // Tüm yazımlar tek bir kilit üzerinden seri hale getirilir.
        var writeLock = new SemaphoreSlim(1, 1);

        async Task WriteEventAsync(string payload)
        {
            if (ct.IsCancellationRequested) return;

            await writeLock.WaitAsync(CancellationToken.None);
            try
            {
                if (ct.IsCancellationRequested) return;
                await Response.WriteAsync(FormatSseData(payload), CancellationToken.None);
                await Response.Body.FlushAsync(CancellationToken.None);
            }
            catch
            {
                // İstemci bağlantıyı kapatmış olabilir. Bu hata YUTULMALI — aksi halde
                // exception job thread'ine kadar yükselip çalışan sandbox'ı iptal eder.
            }
            finally
            {
                writeLock.Release();
            }
        }

        void OnProgress(string jId, string log)
        {
            if (jId != jobId) return;
            // Job thread'ini bloklama: yazımı ateşle-unut olarak sıraya al.
            // Sıralama writeLock ile korunur.
            _ = WriteEventAsync(log);
        }

        void OnCompleted(string jId)
        {
            if (jId == jobId) tcs.TrySetResult();
        }

        // Mevcut logları önce yayınla, ABONELİĞİ SONRA aç: ters sırada, replay
        // sürerken gelen bir log hem çift yazılır hem de eşzamanlı yazıma yol açar.
        foreach (var log in job.ProgressLog.ToArray())
            await WriteEventAsync(log);

        _jobManager.OnProgressUpdated += OnProgress;
        _jobManager.OnJobCompleted += OnCompleted;

        try
        {
            // Abonelik açılmadan önce iş bitmiş olabilir — durumu yeniden kontrol et,
            // aksi halde akış sonsuza dek asılı kalır.
            if (job.Status is "Done" or "Error")
                tcs.TrySetResult();

            await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, ct));

            if (job.Status == "Done" && !ct.IsCancellationRequested)
                await WriteEventAsync($"DOWNLOAD_URL|{job.DownloadUrl}");
        }
        finally
        {
            // try/finally ŞART: abone kaldırılmazsa singleton event ölü bir Response
            // tutmaya devam eder ve sonraki her job'da tetiklenir.
            _jobManager.OnProgressUpdated -= OnProgress;
            _jobManager.OnJobCompleted -= OnCompleted;
        }
    }

    /// <summary>
    /// SSE'de her satır kendi "data: " önekini taşımalı ve boş satır olayı bitirir.
    /// Çok satırlı DB hataları ham gönderilirse ilk satırdan sonrası düşer ve
    /// "ERROR:" öneki kaybolduğu için istemci hata durumuna hiç geçmez.
    /// </summary>
    private static string FormatSseData(string payload)
    {
        var lines = payload.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return string.Concat(lines.Select(l => $"data: {l}\n")) + "\n";
    }

    /// <summary>
    /// Backup indirme. Çıktı kullanıcının tam veritabanı şemasını içerdiğinden
    /// [Authorize] + sahiplik kontrolü uygulanır (IDOR koruması).
    /// </summary>
    [Authorize]
    [HttpGet("download/{jobId}")]
    public IActionResult DownloadBackup(string jobId)
    {
        if (!IsValidJobId(jobId))
            return BadRequest(new { message = "Geçersiz iş kimliği." });

        var job = _jobManager.GetJob(jobId);
        if (job == null)
            return NotFound(new { message = "Bu sandbox işi bulunamadı veya süresi doldu. Lütfen sandbox'ı yeniden çalıştırın." });

        if (job.UserId != null && job.UserId != CurrentUserId)
            return Forbid();

        var outputsDir = Path.Combine(Directory.GetCurrentDirectory(), "Outputs");

        // (uzantı, MIME) — üretim tarafıyla eşleşir: MSSQL .bak, diğerleri .sql
        var candidates = new[]
        {
            (ext: ".bak", mime: "application/octet-stream"),
            (ext: ".sql", mime: "text/plain"),
            (ext: ".tar", mime: "application/x-tar"),
        };

        foreach (var (ext, mime) in candidates)
        {
            var path = Path.Combine(outputsDir, $"{jobId}{ext}");
            if (!System.IO.File.Exists(path)) continue;

            var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
            return File(stream, mime, $"namines_backup_{jobId}{ext}");
        }

        return NotFound(new { message = "Yedek dosyası bulunamadı. Sandbox tamamlanmamış olabilir veya dosya saklama süresi (2 saat) dolmuş olabilir." });
    }
}
