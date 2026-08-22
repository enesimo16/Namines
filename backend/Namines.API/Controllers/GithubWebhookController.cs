using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Namines.Core.Github;

namespace Namines.API.Controllers;

/// <summary>
/// Namines Bot webhook ucu (new-phase/11-MIGRATIONS-BRANCHING.md §7).
///
/// <b>Bu uç bugün OLAYLARI KABUL EDİYOR ama GitHub'a geri YAZMIYOR.</b> Yorum
/// göndermek ve status check oluşturmak, kullanıcının hesabında kayıtlı bir GitHub
/// App'in kimlik bilgilerini gerektiriyor (bkz. CHECKLIST "Kodun beklediği
/// kararlar"). Sahte bir istemciyle "yazıyormuş gibi" yapmak, çalıştığı sanılan
/// ama hiçbir şey yapmayan bir özellik bırakırdı. Doğrulama, komut ayrıştırma ve
/// yorum metni üretimi burada ve test edilmiş durumda; App geldiğinde tek eksik
/// HTTP çağrısı eklenir.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting("sensitive")]
[ApiController]
[Route("api/github")]
public class GithubWebhookController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GithubWebhookController> _logger;

    public GithubWebhookController(IConfiguration configuration, ILogger<GithubWebhookController> logger)
    {
        _configuration = configuration;
        _logger = logger;
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

        return Accepted(new
        {
            @event = eventName,
            note = "Accepted. Posting reviews back to GitHub needs the Namines GitHub App credentials.",
        });
    }
}
