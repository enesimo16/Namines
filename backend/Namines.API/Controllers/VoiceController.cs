using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Namines.AI.Application;
using Namines.Core.Interfaces;

namespace Namines.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VoiceController : ControllerBase
{
    private const long MaxAudioBytes = 15 * 1024 * 1024; // 15 MB

    private readonly ISpeechTranscriber _transcriber;

    public VoiceController(ISpeechTranscriber transcriber)
    {
        _transcriber = transcriber;
    }

    [HttpPost("transcribe")]
    public async Task<IActionResult> TranscribeAudio([FromForm] IFormFile audio, CancellationToken ct)
    {
        if (audio == null || audio.Length == 0)
            return BadRequest("Audio file is required.");

        // DoS koruması: boyut ve içerik türü sınırı.
        if (audio.Length > MaxAudioBytes)
            return BadRequest("Audio file too large (max 15 MB).");
        if (string.IsNullOrEmpty(audio.ContentType) || !audio.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only audio uploads are allowed.");

        try
        {
            using var buffer = new MemoryStream();
            await audio.CopyToAsync(buffer, ct);
            buffer.Position = 0;

            var transcript = await _transcriber.TranscribeAsync(
                buffer, audio.FileName ?? "audio.webm", audio.ContentType, language: "tr", ct);

            return Ok(new { text = transcript });
        }
        catch (Exception ex) when (ex is not AiNotConfiguredException and not OperationCanceledException)
        {
            // İç/upstream detayını sızdırma. Yapılandırılmamış sağlayıcı ise ExceptionMiddleware'de 503 olur.
            return StatusCode(500, "Transcription failed.");
        }
    }
}
