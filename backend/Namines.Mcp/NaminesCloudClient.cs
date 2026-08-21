using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Namines.Mcp;

/// <summary>
/// Barındırılan Namines API'sine giden TEK yol. Faz 1'in üç aracı tamamen yereldi;
/// <c>open_change_request</c> ise doğası gereği sunucuya yazar (33 §5 Faz 2).
///
/// Kimlik: <c>NAMINES_API_TOKEN</c> ortam değişkeni. MCP sunucusu kullanıcı adı/parola
/// İSTEMEZ ve saklamaz — kullanıcı giriş yapıp kendi token'ını verir. Token yoksa araç
/// sessizce başarısız olmaz; nasıl alınacağını söyleyen açık bir hata döndürür.
/// </summary>
public sealed class NaminesCloudClient
{
    private readonly HttpClient _http;

    public NaminesCloudClient(HttpClient http) => _http = http;

    public static string? Token => Environment.GetEnvironmentVariable("NAMINES_API_TOKEN");

    public static string BaseUrl =>
        Environment.GetEnvironmentVariable("NAMINES_API_URL")?.TrimEnd('/')
        ?? "http://localhost:5000";

    /// <summary>
    /// Yapılandırma eksikse çağrı yapılmadan önce patlar. Ağ hatasıyla yapılandırma
    /// hatasını karıştırmak kullanıcıyı yanlış yere baktırır.
    /// </summary>
    private static void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(Token))
            throw new InvalidOperationException(
                "NAMINES_API_TOKEN is not set, so this tool cannot reach the Namines server. " +
                "Sign in to Namines, copy your API token, and set NAMINES_API_TOKEN in the MCP " +
                "server config. Set NAMINES_API_URL too if you are not using " +
                $"{BaseUrl}. The other Namines tools work fully offline and need neither.");
    }

    public async Task<JsonElement> OpenChangeRequestAsync(
        string projectId, string schemaJson, string? title, string? message, CancellationToken ct)
    {
        EnsureConfigured();

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/changerequest/quick");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        req.Content = JsonContent.Create(new
        {
            projectId,
            schemaJson,
            title,
            message,
        });

        using var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Namines server rejected the change request ({(int)res.StatusCode} {res.StatusCode}): {body}");

        // Sunucunun gövdesi olduğu gibi geri verilir — araç katmanı onu yeniden
        // yorumlamaz. Sunucu otoritedir; burada "düzeltmek" iki gerçeklik yaratır.
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        return doc.RootElement.Clone();
    }
}
