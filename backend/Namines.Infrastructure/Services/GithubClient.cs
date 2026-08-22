using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Namines.Core.Github;

namespace Namines.Infrastructure.Services;

/// <summary>
/// <see cref="IGithubClient"/>'ın gerçek uygulaması (11 §7).
///
/// <b>Kimlik akışı iki adımlı ve bu bilinçli:</b> App'in özel anahtarıyla kısa
/// ömürlü bir JWT üretilir, o JWT ile kuruluma özel bir token alınır, depoya
/// yalnızca o token'la dokunulur. Özel anahtarı doğrudan API çağrılarında
/// kullanmanın yolu zaten yok — ve olsaydı bile, tek bir sızıntı bütün
/// kurulumları açardı; kurulum token'ı bir saatte ölür ve tek kuruluma bakar.
/// </summary>
public sealed class GithubClient : IGithubClient
{
    private const string ApiBase = "https://api.github.com";

    private readonly HttpClient _http;
    private readonly ILogger<GithubClient> _logger;
    private readonly string? _appId;
    private readonly string? _privateKey;

    // Kurulum token'ı bir saat yaşar; her istekte yenisini istemek hem gereksiz
    // gecikme hem de GitHub'ın kendi hız sınırını boşa harcamak olurdu.
    private readonly ConcurrentDictionary<long, (string Token, DateTimeOffset ExpiresAt)> _tokens = new();

    public GithubClient(HttpClient http, IConfiguration configuration, ILogger<GithubClient> logger)
    {
        _http = http;
        _logger = logger;

        _appId = configuration["Github:AppId"] ?? Environment.GetEnvironmentVariable("GITHUB_APP_ID");
        _privateKey = configuration["Github:PrivateKey"] ?? Environment.GetEnvironmentVariable("GITHUB_APP_PRIVATE_KEY");

        // GitHub, User-Agent taşımayan isteği 403 ile reddeder — sebebi mesajdan
        // anlaşılmadığı için burada bir kez, merkezî olarak ayarlanıyor.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("namines-bot");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_appId) && !string.IsNullOrWhiteSpace(_privateKey);

    public async Task PostCommentAsync(
        GithubRepository repository, long installationId, int issueNumber, string body,
        CancellationToken cancellationToken = default)
    {
        var token = await InstallationTokenAsync(installationId, cancellationToken);

        using var request = Request(HttpMethod.Post,
            $"/repos/{repository.Owner}/{repository.Name}/issues/{issueNumber}/comments", token);
        request.Content = JsonContent.Create(new { body });

        await SendAsync(request, cancellationToken);
    }

    public async Task CreateCheckRunAsync(
        GithubRepository repository, long installationId, string headSha,
        string name, string conclusion, string title, string summary, string body,
        CancellationToken cancellationToken = default)
    {
        var token = await InstallationTokenAsync(installationId, cancellationToken);

        using var request = Request(HttpMethod.Post,
            $"/repos/{repository.Owner}/{repository.Name}/check-runs", token);

        request.Content = JsonContent.Create(new
        {
            name,
            head_sha = headSha,
            status = "completed",
            conclusion,
            completed_at = DateTimeOffset.UtcNow.ToString("o"),
            output = new { title, summary, text = body },
        });

        await SendAsync(request, cancellationToken);
    }

    public async Task<string?> GetFileContentAsync(
        GithubRepository repository, long installationId, string path, string reference,
        CancellationToken cancellationToken = default)
    {
        var token = await InstallationTokenAsync(installationId, cancellationToken);

        using var request = Request(HttpMethod.Get,
            $"/repos/{repository.Owner}/{repository.Name}/contents/{Uri.EscapeDataString(path)}" +
            $"?ref={Uri.EscapeDataString(reference)}", token);

        using var response = await _http.SendAsync(request, cancellationToken);

        // 404, "dosya bu ref'te yok" demek ve bu NORMAL bir durum: şemayı ilk kez
        // ekleyen bir PR'da taban ref'te dosya bulunmaz. Hata saymak, o PR'ı hiç
        // inceleyemememiz demek olurdu.
        if (response.StatusCode == HttpStatusCode.NotFound) return null;

        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        if (!json.TryGetProperty("content", out var content)) return null;

        // GitHub içeriği satır sonlarıyla bölünmüş base64 olarak veriyor;
        // temizlenmezse Convert.FromBase64String patlar.
        var encoded = content.GetString()?.Replace("\n", string.Empty).Replace("\r", string.Empty);
        if (string.IsNullOrEmpty(encoded)) return null;

        return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
    }

    private HttpRequestMessage Request(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, ApiBase + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    /// <summary>
    /// Başarısız yanıtın GÖVDESİ log'a alınıyor.
    ///
    /// GitHub reddin sebebini gövdede açıklıyor ("resource not accessible by
    /// integration" gibi) ve bu, App'in izinleri eksik olduğunda tek ipucu.
    /// Yalnızca durum kodunu loglamak, kurulum hatalarını teşhis edilemez kılardı.
    /// </summary>
    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogWarning("GitHub API {Status}: {Body}", (int)response.StatusCode, body);

        throw new HttpRequestException($"GitHub API returned {(int)response.StatusCode}.");
    }

    private async Task<string> InstallationTokenAsync(long installationId, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "Github:AppId and Github:PrivateKey are not configured, so the bot cannot write to GitHub.");

        // Bir dakika erken yenileniyor: tam sona erme anında kullanılan bir token,
        // yolda geçen sürede geçersizleşip anlaşılmaz bir 401 üretir.
        if (_tokens.TryGetValue(installationId, out var cached) &&
            cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return cached.Token;
        }

        var jwt = GithubAppJwt.Create(_appId!, _privateKey!);

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{ApiBase}/app/installations/{installationId}/access_tokens");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var token = json.GetProperty("token").GetString()
                    ?? throw new InvalidOperationException("GitHub returned an installation token without a token field.");

        var expiresAt = json.TryGetProperty("expires_at", out var exp) && exp.ValueKind == JsonValueKind.String
            ? DateTimeOffset.Parse(exp.GetString()!)
            : DateTimeOffset.UtcNow.AddMinutes(50);

        _tokens[installationId] = (token, expiresAt);
        return token;
    }
}
