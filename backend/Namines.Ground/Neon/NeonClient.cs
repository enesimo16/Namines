using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Namines.Ground.Neon;

/// <summary>
/// <see cref="INeonClient"/>'ın gerçek HTTP uygulaması (Neon API v2).
///
/// <b>API anahtarı YALNIZCA sunucuda</b> (<c>00-GENEL-BAKIS.md</c> §5 madde 4):
/// hiçbir istemci, hiçbir koşulda görmez. Ground bir vekil olarak davranır.
///
/// <b>Canlı Neon'a karşı doğrulandı</b> (2026-09-09): gerçek proje açıldı,
/// bağlantı adresi doğru biçimde döndü, kimlikle silindi. Yol boyunca
/// <c>org_id</c> zorunluluğu ortaya çıktı — bkz. <see cref="OrgId"/>.
/// </summary>
public sealed class NeonClient : INeonClient
{
    private const string BaseAddress = "https://console.neon.tech/api/v2/";

    /// <summary>
    /// Neon'un varsayılan bölgesi. Yapılandırmadan değiştirilebilir; v1'de
    /// kullanıcıya seçtirilmiyor (<c>02-V1-KARARLARI.md</c> §5).
    /// </summary>
    private const string DefaultRegion = "aws-eu-central-1";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;

    public NeonClient(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _configuration = configuration;

        _http.BaseAddress = new Uri(BaseAddress);

        if (ApiKey is { } key)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
    }

    private string? ApiKey => _configuration["Ground:Neon:ApiKey"] is { Length: > 0 } key ? key : null;

    private string Region => _configuration["Ground:Neon:Region"] ?? DefaultRegion;

    /// <summary>
    /// Neon organizasyon kimliği.
    ///
    /// <b>Canlı denemede öğrenildi:</b> Neon iki tür API anahtarı veriyor —
    /// kişisel hesap anahtarı ve ORGANİZASYON anahtarı. Organizasyon
    /// anahtarıyla proje LİSTELEMEK ve OLUŞTURMAK <c>org_id</c> olmadan
    /// <c>400 "org_id is required"</c> ile reddediliyor; kişisel anahtarda
    /// bu alan yok ve gerekmiyor. Tek istemcinin ikisini de desteklemesi için
    /// alan opsiyonel: tanımlıysa gönderilir, değilse hiç eklenmez.
    ///
    /// Tek bir projeyi kimliğiyle OKUMAK/SİLMEK bu alana ihtiyaç duymuyor —
    /// yalnızca "hangi organizasyonun projelerine bakıyorum" belirsizliği
    /// olan uçlarda gerekiyor. Bu da canlı denenerek doğrulandı.
    /// </summary>
    private string? OrgId => _configuration["Ground:Neon:OrgId"] is { Length: > 0 } id ? id : null;

    public bool IsConfigured => ApiKey is not null;

    public Task<string?> ProbeAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return Task.FromResult<string?>(
                "Ground:Neon:ApiKey is not configured, so no database can be created on " +
                "Neon. Create an API key in your neon.tech account and set this value.");

        return PingAsync(ct);
    }

    private async Task<string?> PingAsync(CancellationToken ct)
    {
        try
        {
            // Proje listesi en ucuz kimlik doğrulama kontrolü: yan etkisi yok
            // ve anahtarın geçerli olup olmadığını kesin söyler.
            var query = OrgId is { } orgId ? $"projects?limit=1&org_id={Uri.EscapeDataString(orgId)}" : "projects?limit=1";
            using var response = await _http.GetAsync(query, ct);
            return response.IsSuccessStatusCode
                ? null
                : $"The Neon API could not be reached ({(int)response.StatusCode}).";
        }
        catch (Exception ex)
        {
            return $"The Neon API could not be reached: {ex.Message}";
        }
    }

    public async Task<NeonProject> CreateProjectAsync(string name, string? regionId, CancellationToken ct)
    {
        // org_id PROJE NESNESİNİN İÇİNDE gidiyor, sorgu dizesinde değil —
        // liste/oluşturma uçları arasındaki bu tutarsızlık canlı denemede
        // ölçüldü. Null ise Json seçenekleri (WhenWritingNull) onu hiç
        // yazmıyor, kişisel anahtarlı hesaplarda gövde değişmeden kalıyor.
        var payload = new
        {
            project = new
            {
                name,
                region_id = regionId ?? Region,
                org_id = OrgId,
            },
        };

        using var response = await _http.PostAsJsonAsync("projects", payload, Json, ct);
        await EnsureSuccessAsync(response, "Could not create the Neon project", ct);

        var body = await response.Content.ReadFromJsonAsync<CreateProjectResponse>(Json, ct)
            ?? throw new InvalidOperationException("Neon returned an empty response.");

        // Bağlantı adresi olmadan açılmış bir proje işe yaramaz; sessizce
        // "başarılı" saymak, kullanılamaz bir kaynağı Active işaretlemek olurdu.
        var uri = body.ConnectionUris?.FirstOrDefault()?.ConnectionUri
            ?? throw new InvalidOperationException(
                "Neon created the project but returned no connection URI.");

        return new NeonProject(
            ProjectId: body.Project?.Id ?? throw new InvalidOperationException("Neon returned no project id."),
            BranchId: body.Branch?.Id ?? string.Empty,
            RegionId: body.Project.RegionId ?? regionId ?? Region,
            ConnectionUri: uri);
    }

    public async Task DeleteProjectAsync(string projectId, CancellationToken ct)
    {
        using var response = await _http.DeleteAsync($"projects/{Uri.EscapeDataString(projectId)}", ct);

        // 404 = kaynak yok. Silmenin istenen son durumu bu, dolayısıyla başarı.
        if (response.StatusCode == HttpStatusCode.NotFound) return;

        await EnsureSuccessAsync(response, "Could not delete the Neon project", ct);
    }

    public async Task<NeonUsage> GetUsageAsync(string projectId, CancellationToken ct)
    {
        using var response = await _http.GetAsync($"projects/{Uri.EscapeDataString(projectId)}", ct);
        if (!response.IsSuccessStatusCode) return new NeonUsage(null);

        var body = await response.Content.ReadFromJsonAsync<GetProjectResponse>(Json, ct);
        return new NeonUsage(body?.Project?.SyntheticStorageSize);
    }

    /// <summary>
    /// Başarısız yanıtı anlamlı bir istisnaya çevirir.
    ///
    /// Gövde okunuyor çünkü Neon hatanın SEBEBİNİ orada söylüyor (kota aşımı,
    /// geçersiz bölge, ad çakışması); yalnızca durum kodu döndürmek kullanıcıyı
    /// "400" ile baş başa bırakırdı.
    /// </summary>
    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response, string context, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct);
        var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : $": {Truncate(body)}";

        throw new InvalidOperationException($"{context} ({(int)response.StatusCode}){detail}");
    }

    private static string Truncate(string text)
    {
        var clean = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return clean.Length <= 300 ? clean : clean[..300] + "…";
    }

    // ── Yanıt şekilleri ─────────────────────────────────────────────────────
    // Yalnızca KULLANILAN alanlar tanımlı: Neon'un yanıtının tamamını
    // modellemek, kullanılmayan her alanı bakım yüküne çevirirdi.

    private sealed record CreateProjectResponse(
        NeonProjectDto? Project,
        NeonBranchDto? Branch,
        ConnectionUriDto[]? ConnectionUris);

    private sealed record GetProjectResponse(NeonProjectDto? Project);

    private sealed record NeonProjectDto(string? Id, string? RegionId, long? SyntheticStorageSize);

    private sealed record NeonBranchDto(string? Id);

    private sealed record ConnectionUriDto(string? ConnectionUri);
}
