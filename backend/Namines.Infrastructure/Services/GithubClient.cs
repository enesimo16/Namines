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

    private readonly GithubInstallationTokenCache _tokens;

    public GithubClient(
        HttpClient http, IConfiguration configuration, ILogger<GithubClient> logger,
        GithubInstallationTokenCache tokens)
    {
        _http = http;
        _logger = logger;
        _tokens = tokens;

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
        GithubRepository repository, long? installationId, string path, string reference,
        CancellationToken cancellationToken = default)
    {
        // Yol bölümlerinin arasındaki '/' KORUNUYOR: bütün yolu tek parça
        // kodlamak "prisma%2Fschema.prisma" üretir ve GitHub onu bulamaz.
        var encodedPath = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

        using var request = await RequestAsync(HttpMethod.Get,
            $"/repos/{repository.Owner}/{repository.Name}/contents/{encodedPath}" +
            $"?ref={Uri.EscapeDataString(reference)}", installationId, cancellationToken);

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

    public async Task<string?> GetDefaultBranchAsync(
        GithubRepository repository, long? installationId, CancellationToken cancellationToken = default)
    {
        using var request = await RequestAsync(HttpMethod.Get,
            $"/repos/{repository.Owner}/{repository.Name}", installationId, cancellationToken);

        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureReadSuccessAsync(response, repository, cancellationToken);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return json.TryGetProperty("default_branch", out var branch) ? branch.GetString() : null;
    }

    public async Task<RepositoryTree> GetRepositoryTreeAsync(
        GithubRepository repository, string reference, long? installationId,
        CancellationToken cancellationToken = default)
    {
        // recursive=1: ağacın tamamı TEK çağrıda. Dizin dizin gezmek büyük bir
        // depoda yüzlerce istek ve anonim kotanın (saatte 60) anında tükenmesi
        // demek olurdu.
        using var request = await RequestAsync(HttpMethod.Get,
            $"/repos/{repository.Owner}/{repository.Name}/git/trees/{Uri.EscapeDataString(reference)}?recursive=1",
            installationId, cancellationToken);

        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureReadSuccessAsync(response, repository, cancellationToken);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        var paths = new List<string>();
        if (json.TryGetProperty("tree", out var tree) && tree.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in tree.EnumerateArray())
            {
                // Yalnızca blob: "tree" girdileri dizin, içerikleri okunamaz.
                if (entry.TryGetProperty("type", out var type) && type.GetString() == "blob" &&
                    entry.TryGetProperty("path", out var path) && path.GetString() is { } value)
                    paths.Add(value);
            }
        }

        var truncated = json.TryGetProperty("truncated", out var t) && t.ValueKind == JsonValueKind.True;
        return new RepositoryTree(paths, truncated);
    }

    /// <summary>
    /// İsteği hazırlar; <paramref name="installationId"/> <c>null</c> ise
    /// ANONİM çıkar.
    ///
    /// Public depo okuması App kimliği gerektirmiyor ve gerektirmemeli —
    /// F1'in GitHub App'i beklemeden çalışabilmesinin tek sebebi bu yol.
    /// Kimliğin olup olmadığı kararı tek bir yerde veriliyor; her okuma
    /// metodunda tekrarlansaydı biri unutulduğunda sessizce kimlik isteyen
    /// bir çağrı kalırdı.
    /// </summary>
    private async Task<HttpRequestMessage> RequestAsync(
        HttpMethod method, string path, long? installationId, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, ApiBase + path);

        if (installationId is { } id)
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", await InstallationTokenAsync(id, ct));

        return request;
    }

    /// <summary>
    /// Okuma yolunun hata ayrımı.
    ///
    /// Yazma yolundan ayrı, çünkü okumada iki durum kullanıcıya AYRI AYRI
    /// söylenmeli: 404 "depo yok ya da private", 403 + kalan kota 0 ise
    /// "saatlik sınır doldu". İkisini tek bir "GitHub hata verdi"ye indirgemek,
    /// kullanıcının ne yapacağını bilememesi demek — biri beklemekle, diğeri
    /// kurulum yapmakla çözülüyor.
    /// </summary>
    private async Task EnsureReadSuccessAsync(
        HttpResponseMessage response, GithubRepository repository, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new GithubRepositoryUnavailableException(
                $"{repository} could not be read. It does not exist, or it is private — " +
                "a private repository needs a GitHub App installation.");

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests &&
            response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) &&
            remaining.FirstOrDefault() == "0")
            throw new GithubRateLimitedException(
                "GitHub's hourly limit for anonymous requests is used up. " +
                "Try again later, or connect a GitHub App.");

        await EnsureSuccessAsync(response, ct);
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
        if (_tokens.TryGet(installationId, out var cached)) return cached!;

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

        _tokens.Set(installationId, token, expiresAt);
        return token;
    }
}

/// <summary>
/// Kurulum token'ları için paylaşılan depo.
///
/// <b>Ayrı bir sınıf, çünkü <see cref="GithubClient"/> transient.</b>
/// <c>AddHttpClient</c> her çözümlemede yeni bir istemci verir; önbellek onun
/// örnek alanı olsaydı her webhook yeniden token isterdi — yani önbellek hiç
/// çalışmazdı. Statik bir alan da olabilirdi ama o, testler arasında sızan ve
/// çalışma sırasına göre farklı davranan bir durum bırakırdı; DI ile
/// singleton vermek aynı ömrü, yalıtımı bozmadan sağlıyor.
/// </summary>
public sealed class GithubInstallationTokenCache
{
    private readonly ConcurrentDictionary<long, (string Token, DateTimeOffset ExpiresAt)> _entries = new();

    /// <summary>
    /// Geçerli token varsa true.
    ///
    /// Bir dakika erken "süresi doldu" sayılıyor: tam sona erme anında kullanılan
    /// bir token, yolda geçen sürede geçersizleşip anlaşılmaz bir 401 üretir.
    /// </summary>
    public bool TryGet(long installationId, out string? token)
    {
        token = null;

        if (!_entries.TryGetValue(installationId, out var entry)) return false;
        if (entry.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(1)) return false;

        token = entry.Token;
        return true;
    }

    public void Set(long installationId, string token, DateTimeOffset expiresAt) =>
        _entries[installationId] = (token, expiresAt);
}
