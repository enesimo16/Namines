using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Namines.Core.Github;
using Namines.Infrastructure.Services;

namespace Namines.API.Controllers;

/// <summary>
/// Namines Bot webhook ucu (new-phase/11-MIGRATIONS-BRANCHING.md §7).
///
/// Doğrulanan olay <see cref="IGithubBotService"/>'e devrediliyor; o da PR'daki
/// şema farkını analiz edip yorumu ve status check'i yazıyor.
///
/// <b>App kimlik bilgileri tanımlı değilse yazma DENENMEZ</b> — olay kabul edilir
/// ve yanıtta yazılmadığı söylenir. Sahte bir başarı raporlamak, çalıştığı sanılan
/// ama hiçbir şey yapmayan bir özellik bırakırdı.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting("sensitive")]
[ApiController]
[Route("api/github")]
public class GithubWebhookController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GithubWebhookController> _logger;
    private readonly IGithubBotService _bot;

    public GithubWebhookController(
        IConfiguration configuration, ILogger<GithubWebhookController> logger, IGithubBotService bot)
    {
        _configuration = configuration;
        _logger = logger;
        _bot = bot;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Receive()
    {
        // Gövde HAM hâliyle okunmalı: imza baytların üzerinden hesaplanıyor ve
        // ASP.NET'in model bağlaması JSON'u yeniden serileştirdiğinde tek bir
        // boşluk farkı bile imzayı geçersiz kılar.
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var payload = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        var secret = _configuration["Github:WebhookSecret"]
                     ?? Environment.GetEnvironmentVariable("GITHUB_WEBHOOK_SECRET");

        if (!GithubWebhook.IsSignatureValid(secret, payload, Request.Headers["X-Hub-Signature-256"]))
        {
            // Neden reddedildiği SÖYLENMİYOR: "sır yapılandırılmamış" ile "imza
            // yanlış" arasındaki farkı bildirmek, saldırgana kurulum hakkında
            // bilgi verir. Ayrıntı log'a gidiyor.
            _logger.LogWarning(
                "GitHub webhook rejected. Secret configured: {Configured}.",
                !string.IsNullOrWhiteSpace(secret));

            return Unauthorized(new { message = "Invalid signature." });
        }

        var eventName = Request.Headers["X-GitHub-Event"].ToString();

        // Ping, App kurulunca GitHub'ın gönderdiği ilk olay; 200 dönmezse kurulum
        // ekranı webhook'u "başarısız" gösterir.
        if (string.Equals(eventName, "ping", StringComparison.OrdinalIgnoreCase))
            return Ok(new { message = "pong" });

        _logger.LogInformation("GitHub webhook received: {Event}.", eventName);

        try
        {
            var result = await _bot.HandleAsync(eventName, payload, HttpContext.RequestAborted);
            return Accepted(new { @event = eventName, note = result });
        }
        catch (Exception ex)
        {
            // GitHub başarısız bir webhook'u YENİDEN DENER. Bir ayrıştırma hatası
            // ya da eksik izin yüzünden 500 dönmek, aynı hatayı saatlerce
            // tekrarlatır; olay kabul edilip sorun log'a yazılıyor.
            _logger.LogError(ex, "Namines Bot could not finish handling {Event}.", eventName);
            return Accepted(new { @event = eventName, note = "Accepted, but the bot could not finish. See server logs." });
        }
    }
}
