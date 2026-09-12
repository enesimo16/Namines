using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Namines.Ground.Supabase;

/// <summary>
/// Supabase Management API istemcisi (F-10 / B-45).
///
/// <b>Uçlar belgeden alındı</b> (api.supabase.com/v1): proje oluşturma
/// <c>POST /v1/projects</c>, silme <c>DELETE /v1/projects/{ref}</c>, liste
/// <c>GET /v1/projects</c>.
///
/// <b>CANLI DENENMEDİ</b> — bu oturumda Supabase erişim jetonu yoktu.
/// Bu yüzden <see cref="SupabaseProvider"/> kendini
/// <c>IsLiveVerified: false</c> olarak bildiriyor ve arayüz bunu gösteriyor.
/// Deponun kuralı bu: yazılıp canlı doğrulanmayan bir sağlayıcı
/// "destekleniyor" sayılmaz (<c>00-GENEL-BAKIS.md</c> §9).
/// </summary>
public sealed class SupabaseClient : ISupabaseClient
{
    private const string BaseAddress = "https://api.supabase.com/";

    /// <summary>
    /// Bölge verilmediğinde kullanılan varsayılan.
    ///
    /// Supabase "smart region" seçimi de destekliyor ama açık bir bölge
    /// vermek KASITLI: verinin nerede durduğu, bir yönetişim aracının
    /// tahmine bırakamayacağı bir bilgi.
    /// </summary>
    private const string DefaultRegion = "eu-central-1";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;

    public SupabaseClient(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _configuration = configuration;

        _http.BaseAddress = new Uri(BaseAddress);

        if (AccessToken is { } token)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private string? AccessToken =>
        _configuration["Ground:Supabase:AccessToken"] is { Length: > 0 } t ? t : null;

    /// <summary>
    /// Organizasyon kimliği/slug'ı. Supabase proje oluştururken ZORUNLU:
    /// bir kişisel erişim jetonu birden fazla organizasyona erişebilir ve
    /// hangisinde açılacağı tahmin edilemez.
    /// </summary>
    private string? OrganizationId =>
        _configuration["Ground:Supabase:OrganizationId"] is { Length: > 0 } id ? id : null;

    private string Region => _configuration["Ground:Supabase:Region"] ?? DefaultRegion;

    public bool IsConfigured => AccessToken is not null && OrganizationId is not null;

    public Task<string?> ProbeAsync(CancellationToken ct)
    {
        if (AccessToken is null)
            return Task.FromResult<string?>(
                "Ground:Supabase:AccessToken is not configured, so no database can be created " +
                "on Supabase. Create a personal access token at supabase.com/dashboard/account/tokens " +
                "and set this value.");

        if (OrganizationId is null)
            return Task.FromResult<string?>(
                "Ground:Supabase:OrganizationId is not configured. A token can reach more than one " +
                "organization, so the target cannot be guessed — set the organization id explicitly.");

        return PingAsync(ct);
    }

    private async Task<string?> PingAsync(CancellationToken ct)
    {
        try
        {
            // Proje listesi en ucuz kimlik kontrolü: yan etkisi yok ve jetonun
            // geçerli olup olmadığını kesin söyler.
            using var response = await _http.GetAsync("v1/projects", ct);
            return response.IsSuccessStatusCode
                ? null
                : $"The Supabase API could not be reached ({(int)response.StatusCode}).";
        }
        catch (Exception ex)
        {
            return $"The Supabase API could not be reached: {ex.Message}";
        }
    }

    public async Task<SupabaseProjectInfo> CreateProjectAsync(
        string name, string? region, CancellationToken ct)
    {
        if (OrganizationId is null)
            throw new InvalidOperationException(
                "Ground:Supabase:OrganizationId is not configured, so a project cannot be created.");

        // Parola BURADA üretiliyor ve hiçbir yere yazılmıyor: yalnızca
        // bağlantı dizesinin içine giriyor, o da çağıran tarafından
        // şifrelenip saklanıyor. Supabase parolayı sonradan OKUTMUYOR —
        // kaybolursa sıfırlamak gerekir, o yüzden dönüş değerinde taşımak
        // zorunlu.
        var password = GeneratePassword();
        var effectiveRegion = region ?? Region;

        var payload = new
        {
            name,
            organization_id = OrganizationId,
            db_pass = password,
            region = effectiveRegion,
        };

        using var response = await _http.PostAsJsonAsync("v1/projects", payload, Json, ct);
        await EnsureSuccessAsync(response, "Could not create the Supabase project", ct);

        var body = await response.Content.ReadFromJsonAsync<CreateProjectResponse>(Json, ct)
            ?? throw new InvalidOperationException("Supabase returned an empty response.");

        // `ref` ve `id` alanlarının ikisi de görülüyor; hangisi gelirse o
        // kullanılıyor. Referans olmadan bağlantı adresi kurulamaz, yani
        // sessizce "başarılı" saymak kullanılamaz bir kaynağı Active
        // işaretlemek olurdu.
        var projectRef = body.Ref ?? body.Id
            ?? throw new InvalidOperationException(
                "Supabase created the project but returned no project reference.");

        return new SupabaseProjectInfo(
            ProjectRef: projectRef,
            Region: body.Region ?? effectiveRegion,
            ConnectionString: BuildConnectionString(projectRef, password));
    }

    public async Task DeleteProjectAsync(string projectRef, CancellationToken ct)
    {
        using var response = await _http.DeleteAsync($"v1/projects/{Uri.EscapeDataString(projectRef)}", ct);

        // ZATEN YOK = istenen son durum. Silme yeniden denenebilir bir işten
        // çağrılıyor; 404'ü hata saymak, işin sonsuza kadar yeniden
        // denenmesine yol açardı.
        if (response.StatusCode == HttpStatusCode.NotFound) return;

        await EnsureSuccessAsync(response, "Could not delete the Supabase project", ct);
    }

    /// <summary>
    /// Supabase'in yayınladığı bağlantı biçimi:
    /// <c>db.&lt;ref&gt;.supabase.co:5432</c>, veritabanı ve kullanıcı
    /// <c>postgres</c>.
    ///
    /// Anahtar=değer biçimi ZORUNLU: Namines'in geri kalanı (Desk, Vault,
    /// Gateway ve SSRF kontrolü) bağlantıyı böyle ayrıştırıyor. URI olarak
    /// saklamak, o yolların host'u çıkaramaması — yani SSRF kontrolünün
    /// sessizce atlanması — demekti.
    /// </summary>
    internal static string BuildConnectionString(string projectRef, string password)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = $"db.{projectRef}.supabase.co",
            Port = 5432,
            Database = "postgres",
            Username = "postgres",
            Password = password,
            // Supabase TLS zorunlu tutuyor; düşürmek bağlantıyı ağda açık
            // hâle getirirdi.
            SslMode = SslMode.Require,
        };

        return builder.ConnectionString;
    }

    /// <summary>
    /// Kriptografik rastgele parola.
    ///
    /// <c>Random</c> DEĞİL: veritabanı parolası tahmin edilebilir olmamalı ve
    /// <c>Random</c> tohumu zamana bağlı. URL/bağlantı dizesinde sorun
    /// çıkarmayan alfabe kullanılıyor.
    /// </summary>
    internal static string GeneratePassword(int length = 32)
    {
        const string alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = new char[length];

        for (var i = 0; i < length; i++)
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

        return new string(chars);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response, string what, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct);

        // Gövde KISALTILARAK ekleniyor: Supabase'in hata metni operatörün
        // sorunu çözmesi için gereken tek bilgi olabilir ("organization not
        // found", "free project limit reached"), ama tamamını taşımak logu
        // doldurur.
        throw new InvalidOperationException(
            $"{what} ({(int)response.StatusCode}). {Truncate(body)}");
    }

    private static string Truncate(string value) =>
        value.Length <= 300 ? value : value[..300] + "…";

    private sealed record CreateProjectResponse(string? Id, string? Ref, string? Region);
}
