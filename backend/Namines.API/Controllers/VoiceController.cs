using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Namines.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VoiceController : ControllerBase
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public VoiceController(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["Groq:ApiKey"] ?? throw new ArgumentNullException("Groq:ApiKey is missing");
        
        _httpClient.BaseAddress = new Uri("https://api.groq.com/openai/v1/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    [HttpPost("transcribe")]
    public async Task<IActionResult> TranscribeAudio([FromForm] IFormFile audio)
    {
        if (audio == null || audio.Length == 0)
            return BadRequest("Audio file is required.");

        try
        {
            using var memoryStream = new MemoryStream();
            await audio.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            using var content = new MultipartFormDataContent();
            
            var audioContent = new StreamContent(memoryStream);
            audioContent.Headers.ContentType = new MediaTypeHeaderValue(audio.ContentType ?? "audio/webm");
            content.Add(audioContent, "file", audio.FileName ?? "audio.webm");
            
            content.Add(new StringContent("whisper-large-v3"), "model");
            content.Add(new StringContent("tr"), "language"); // Assuming Turkish user base
            
            var response = await _httpClient.PostAsync("audio/transcriptions", content);
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(responseString);
            var transcript = result.GetProperty("text").GetString();

            return Ok(new { text = transcript });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Transcription failed: {ex.Message}");
        }
    }
}
