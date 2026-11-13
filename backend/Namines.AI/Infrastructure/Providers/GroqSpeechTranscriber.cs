using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Namines.AI.Application;

namespace Namines.AI.Infrastructure.Providers;

/// <summary>
/// Groq Whisper ile sesten metne.
///
/// Önceden bu çağrı VoiceController'ın içindeydi ve iki sorunu vardı: (1) anahtar yoksa denetleyicinin KURUCUSU
/// <c>ArgumentNullException</c> atıyordu (her istek 500, nedeni belirsiz); (2) paylaşılan HttpClient'ın
/// <c>DefaultRequestHeaders</c>'ı her istekte yeniden yazılıyordu (paralel isteklerde yarış). Şimdi kimlik doğrulama
/// Groq sağlayıcısının kendisinden, İSTEK BAŞINA geliyor; anahtar yoksa <c>AiNotConfiguredException</c> → 503.
/// </summary>
public sealed class GroqSpeechTranscriber : ISpeechTranscriber
{
    private const string Model = "whisper-large-v3";

    private readonly HttpClient _http;
    private readonly GroqChatCompletionProvider _provider;

    public GroqSpeechTranscriber(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _provider = new GroqChatCompletionProvider(configuration);
        _http.BaseAddress = _provider.BaseAddress;
    }

    public async Task<string?> TranscribeAsync(Stream audio, string fileName, string contentType, string language, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        var audioContent = new StreamContent(audio);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(audioContent, "file", fileName);
        content.Add(new StringContent(Model), "model");
        content.Add(new StringContent(language), "language");

        using var request = new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions") { Content = content };
        _provider.Authenticate(request); // anahtar yoksa AiNotConfiguredException

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("text").GetString();
    }
}
