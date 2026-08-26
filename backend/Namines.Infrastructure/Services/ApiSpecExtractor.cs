using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Analysis;
using Namines.Core.Security;

namespace Namines.Infrastructure.Services;

/// <param name="Success">Herhangi bir kaynak işe yaradıysa true.</param>
/// <param name="SourceKind">"graphql" | "openapi" | "none" — hangi kademenin işe yaradığı.</param>
/// <param name="SourceUrl">Gerçekten veri döndüren URL — kullanıcının verdiğinden farklı olabilir (well-known yol denemesi).</param>
/// <param name="Tables">Çıkarılan varlık adayları. Başarısızsa boş.</param>
/// <param name="FailureReason">
/// Başarısızsa neden — kullanıcıya gösterilecek. <b>"Sayfa metnine düş"
/// YOK</b>: bu tip, o eski davranışın yerine geçen dürüst boşluk.
/// </param>
public sealed record ApiSpecExtractionResult(
    bool Success,
    string SourceKind,
    string? SourceUrl,
    IReadOnlyList<PlannedTable> Tables,
    string? FailureReason);

/// <summary>
/// Kullanıcının verdiği bir URL'den, kademeli bir zincirle veri modeli
/// çıkarmayı dener — second-phase/06-VERI-KAYNAKLARI.md.
///
/// <b>Bu, eski `ReferenceUrl` (sayfa metni kazıma) özelliğinin YERİNE
/// geçiyor.</b> Eski özellik üç yerden kırıktı: uzunluk sınırı yoktu, sayfa
/// metni şema hakkında neredeyse hiçbir şey söylemiyordu, JS ile render olan
/// sitelerde boş kabuk dönüyordu. Bu sınıf onun yerine <b>yapılandırılmış</b>
/// kaynaklara bakıyor — GraphQL introspection ve OpenAPI/Swagger dokümanı —
/// ve hiçbiri yoksa DÜRÜSTÇE boş dönüyor, sayfa metnine düşmüyor.
///
/// <b>"Kademe 3: gözlemlenen JSON trafiği" burada YOK.</b> O kademe
/// kullanıcının kendi tarayıcı oturumunu gözlemlemeyi gerektiriyor — bir
/// sunucu ucu bunu yapamaz, extension'ın işi (bkz. 06 §"Kademeli çıkarım
/// zinciri"). Bu sınıf yalnızca kademe 1 ve 2'yi kapsıyor.
/// </summary>
public static class ApiSpecExtractor
{
    /// <summary>
    /// Kullanıcı düz bir site adresi verdiğinde denenecek, yaygın kabul
    /// görmüş API doküman yolları. Bunlar rastgele bir tarama DEĞİL —
    /// sektörde standart, statik, sabit yollar; her biri tek bir istek.
    /// </summary>
    private static readonly string[] WellKnownSuffixes =
    {
        "", // kullanıcının verdiği URL'in kendisi
        "/graphql",
        "/openapi.json",
        "/swagger.json",
        "/v1/openapi.json",
        "/api-docs",
        "/swagger/v1/swagger.json",
    };

    private const int MaxResponseBytes = 2 * 1024 * 1024; // 2 MB — büyük bir doküman bile bunun altında.

    public static async Task<ApiSpecExtractionResult> ExtractAsync(string userUrl, CancellationToken ct)
    {
        if (!SsrfGuard.IsUrlSafe(userUrl))
            return Fail("The given URL is not allowed.");

        if (!Uri.TryCreate(userUrl, UriKind.Absolute, out var baseUri))
            return Fail("The given URL is not valid.");

        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        foreach (var suffix in WellKnownSuffixes)
        {
            ct.ThrowIfCancellationRequested();

            var candidateUrl = suffix.Length == 0
                ? userUrl
                : $"{baseUri.Scheme}://{baseUri.Authority}{suffix}";

            // İlk deneme kullanıcının verdiği URL; sonrakiler aynı origin'e
            // türetiliyor — SsrfGuard'ı tekrar tekrar aynı origin için
            // çalıştırmaya gerek yok, ama farklı bir origin'e sapmadığından
            // emin olmak için yine de kontrol ediliyor (defans katmanı).
            if (!SsrfGuard.IsUrlSafe(candidateUrl)) continue;

            var graphQlResult = await TryGraphQlAsync(client, candidateUrl, ct);
            if (graphQlResult.Count > 0)
                return new ApiSpecExtractionResult(true, "graphql", candidateUrl, graphQlResult, null);

            var openApiResult = await TryOpenApiAsync(client, candidateUrl, ct);
            if (openApiResult.Count > 0)
                return new ApiSpecExtractionResult(true, "openapi", candidateUrl, openApiResult, null);
        }

        return Fail(
            "Could not find a GraphQL or OpenAPI/Swagger schema at this address. " +
            "Try giving the direct API documentation URL (e.g. /openapi.json).");
    }

    private static async Task<IReadOnlyList<PlannedTable>> TryGraphQlAsync(HttpClient client, string url, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(GraphQlSchemaParser.IntrospectionQuery, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(url, content, ct);
            if (!response.IsSuccessStatusCode) return Array.Empty<PlannedTable>();

            var body = await ReadBoundedAsync(response, ct);
            return body is null ? Array.Empty<PlannedTable>() : GraphQlSchemaParser.Parse(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Array.Empty<PlannedTable>();
        }
    }

    private static async Task<IReadOnlyList<PlannedTable>> TryOpenApiAsync(HttpClient client, string url, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return Array.Empty<PlannedTable>();

            var body = await ReadBoundedAsync(response, ct);
            if (body is null || !OpenApiSchemaParser.LooksLikeOpenApiDocument(body))
                return Array.Empty<PlannedTable>();

            return OpenApiSchemaParser.Parse(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Array.Empty<PlannedTable>();
        }
    }

    /// <summary>
    /// Yanıt gövdesini <see cref="MaxResponseBytes"/> ile sınırlı okur.
    ///
    /// Eski `ReferenceUrl` özelliğinin kırıldığı üç noktadan biri buydu:
    /// uzunluk sınırı yoktu, büyük bir sayfa on binlerce token'ı prompt'a
    /// basıyordu. Burada limit AĞ SEVİYESİNDE — sınırı aşan bir gövde
    /// tamamen reddediliyor, kırpılıp kullanılmıyor (kırpılmış bir JSON
    /// zaten parse edilemez).
    /// </summary>
    private static async Task<string?> ReadBoundedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is > MaxResponseBytes) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var bounded = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        var total = 0;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > MaxResponseBytes) return null;
            bounded.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(bounded.ToArray());
    }

    private static ApiSpecExtractionResult Fail(string reason) =>
        new(false, "none", null, Array.Empty<PlannedTable>(), reason);
}
