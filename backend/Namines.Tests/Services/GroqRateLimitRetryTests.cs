using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Namines.Core.Interfaces;
using Namines.Infrastructure.AI;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// Hız sınırına takıldığında bekleyip yeniden deneme davranışı.
///
/// <b>Neden bu testler var:</b> <c>AiRateLimitException</c> zaten sağlayıcının
/// verdiği bekleme süresini TAŞIYORDU ama hiç kullanılmıyordu — kullanıcı yedi
/// saniye beklemek yerine hata alıyordu. Büyük şema üretiminde bu, işin hiç
/// bitmemesi demekti.
/// </summary>
public class GroqRateLimitRetryTests
{
    private static (GroqAIService Service, List<TimeSpan> Waits) Build(
        RecordingHttpMessageHandler handler,
        int maxTotalWaitSeconds = 600,
        int maxSingleWaitSeconds = 60)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Groq:ApiKey"] = "test-key",
                ["Groq:Model"] = "openai/gpt-oss-20b",
                ["Ai:RateLimitRetry:MaxTotalWaitSeconds"] = maxTotalWaitSeconds.ToString(),
                ["Ai:RateLimitRetry:MaxSingleWaitSeconds"] = maxSingleWaitSeconds.ToString(),
            })
            .Build();

        var waits = new List<TimeSpan>();

        var service = new GroqAIService(
            new HttpClient(handler),
            config,
            new HttpContextAccessor(),
            new MemoryCache(new MemoryCacheOptions()),
            logger: null,
            delay: (d, _) => { waits.Add(d); return Task.CompletedTask; });

        return (service, waits);
    }

    private static readonly IReadOnlyList<AgentChatMessage> Messages =
        new[] { new AgentChatMessage("user", "merhaba") };

    [Fact]
    public async Task Waits_the_advertised_interval_then_succeeds()
    {
        var handler = new RecordingHttpMessageHandler()
            .EnqueueRateLimited("7")
            .EnqueueChatCompletion("tamam");

        var (service, waits) = Build(handler);

        var response = await service.CompleteAsync(
            Messages, Array.Empty<AgentToolDefinition>(), 0.2);

        Assert.Equal("tamam", response.Content);
        Assert.Equal(2, handler.CallCount);
        Assert.Equal(new[] { TimeSpan.FromSeconds(7) }, waits);
    }

    [Fact]
    public async Task Gives_up_with_the_rate_limit_exception_once_the_budget_is_spent()
    {
        // Bütçe 10 saniye; sağlayıcı her seferinde 6 saniye istiyor.
        // İki bekleme sığar, üçüncüsü sığmaz — o noktada bugünkü davranışa
        // dönülmeli, yani kullanıcı susturulmuş bir hata değil AiRateLimit almalı.
        var handler = new RecordingHttpMessageHandler()
            .EnqueueRateLimited("6")
            .EnqueueRateLimited("6")
            .EnqueueRateLimited("6");

        var (service, waits) = Build(handler, maxTotalWaitSeconds: 10);

        await Assert.ThrowsAsync<AiRateLimitException>(() => service.CompleteAsync(
            Messages, Array.Empty<AgentToolDefinition>(), 0.2));

        Assert.Equal(3, handler.CallCount);
        Assert.Equal(new[] { TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(4) }, waits);
    }

    [Fact]
    public async Task Does_not_retry_a_non_rate_limit_failure()
    {
        // 400'ü yeniden denemek yalnızca aynı 400'ü tekrar almaktır.
        var handler = new RecordingHttpMessageHandler()
            .EnqueueStatus(HttpStatusCode.BadRequest, "{\"error\":{\"code\":\"invalid_request\"}}");

        var (service, waits) = Build(handler);

        await Assert.ThrowsAnyAsync<Exception>(() => service.CompleteAsync(
            Messages, Array.Empty<AgentToolDefinition>(), 0.2));

        Assert.Equal(1, handler.CallCount);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task Cancellation_interrupts_the_wait_instead_of_sitting_it_out()
    {
        var handler = new RecordingHttpMessageHandler().EnqueueRateLimited("30");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Groq:ApiKey"] = "test-key",
                ["Ai:RateLimitRetry:MaxTotalWaitSeconds"] = "600",
            })
            .Build();

        using var cts = new CancellationTokenSource();

        var service = new GroqAIService(
            new HttpClient(handler),
            config,
            new HttpContextAccessor(),
            new MemoryCache(new MemoryCacheOptions()),
            logger: null,
            // Beklemeye girildiği anda kullanıcı iptal etmiş gibi davran.
            delay: (_, ct) => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CompleteAsync(
            Messages, Array.Empty<AgentToolDefinition>(), 0.2, cts.Token));

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Retries_even_when_the_provider_gives_no_interval()
    {
        // Süre bildirilmediğinde beklemeden vazgeçmek, yeniden denemeyi
        // sağlayıcının nezaketine bağlamak olurdu. Varsayılan tek seferlik
        // tavan kullanılır.
        var handler = new RecordingHttpMessageHandler()
            .EnqueueRateLimited(null)
            .EnqueueChatCompletion("oldu");

        var (service, waits) = Build(handler, maxSingleWaitSeconds: 60);

        var response = await service.CompleteAsync(
            Messages, Array.Empty<AgentToolDefinition>(), 0.2);

        Assert.Equal("oldu", response.Content);
        Assert.Equal(new[] { TimeSpan.FromSeconds(60) }, waits);
    }

    [Theory]
    [InlineData("Please try again in 12.5s", 12.5)]
    [InlineData("rate limit reached, try again in 3s", 3)]
    public void ParseRetryAfter_reads_the_interval_from_the_error_body(string body, double expected)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        Assert.True(GroqAIService.ParseRetryAfter(response, body, out var wait));
        Assert.Equal(TimeSpan.FromSeconds(expected), wait);
    }

    [Fact]
    public void ParseRetryAfter_prefers_the_Retry_After_header_over_the_body()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter =
            new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(9));

        Assert.True(GroqAIService.ParseRetryAfter(response, "try again in 99s", out var wait));
        Assert.Equal(TimeSpan.FromSeconds(9), wait);
    }

    [Fact]
    public void ParseRetryAfter_reports_failure_when_nothing_says_how_long()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        Assert.False(GroqAIService.ParseRetryAfter(response, "rate_limit_exceeded", out _));
    }
}
