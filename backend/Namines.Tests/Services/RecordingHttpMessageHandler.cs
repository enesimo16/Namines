using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Namines.Tests.Services;

/// <summary>
/// Sırayla verilen yanıtları döndüren ve gördüğü istekleri kaydeden sahte
/// aktarım katmanı.
///
/// <b>Neden gerçek ağ yerine bu:</b> sınanan şey sağlayıcının davranışı değil,
/// BİZİM 429'a verdiğimiz tepki. Gerçek bir 429 üretmek için gerçekten hız
/// sınırına çarpmak gerekirdi — yani test, hesabın o anki kotasına bağlı olurdu.
/// </summary>
public sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<string> RequestBodies { get; } = new();
    public List<Uri?> RequestUris { get; } = new();
    public List<string?> AuthorizationHeaders { get; } = new();
    public int CallCount => RequestBodies.Count;

    public RecordingHttpMessageHandler Enqueue(Func<HttpResponseMessage> response)
    {
        _responses.Enqueue(response);
        return this;
    }

    public RecordingHttpMessageHandler EnqueueRateLimited(string? retryAfterSeconds)
    {
        return Enqueue(() =>
        {
            var body = retryAfterSeconds is null
                ? "{\"error\":{\"code\":\"rate_limit_exceeded\"}}"
                : $"{{\"error\":{{\"code\":\"rate_limit_exceeded\",\"message\":\"Please try again in {retryAfterSeconds}s\"}}}}";

            return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent(body),
            };
        });
    }

    public RecordingHttpMessageHandler EnqueueChatCompletion(string content)
    {
        var escaped = System.Text.Json.JsonSerializer.Serialize(content);
        return Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"{{\"choices\":[{{\"finish_reason\":\"stop\",\"message\":{{\"content\":{escaped}}}}}]}}"),
        });
    }

    public RecordingHttpMessageHandler EnqueueStatus(HttpStatusCode status, string body)
        => Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(body) });

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUris.Add(request.RequestUri);
        AuthorizationHeaders.Add(request.Headers.Authorization?.ToString());
        RequestBodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken));

        if (_responses.Count == 0)
            throw new InvalidOperationException("Sahte aktarıma beklenenden fazla istek geldi.");

        return _responses.Dequeue()();
    }
}
