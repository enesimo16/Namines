using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Middleware;
using Namines.Core.Exceptions;
using Namines.Core.Github;
using Namines.Core.Interfaces;
using Namines.Core.Nsl;
using Namines.Infrastructure.Resilience;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;
using Xunit;

namespace Namines.Tests.Architecture;

/// <summary>
/// FAZ 3: Harici Servis Zırhı & Dayanıklılık (Polly v8 Resilience Pipelines,
/// Circuit Breakers, Concurrency Limiter ve RFC 7807 ProblemDetails Global Error Handling) testleri.
/// </summary>
public sealed class ResilienceAndProblemDetailsTests
{
    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Namines.API";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
    [Fact]
    public void ResilienceProvider_Tum_Boru_Hatlari_Basariyla_Cozumlenir()
    {
        var provider = new NaminesResilienceProvider(NullLoggerFactory.Instance);

        var aiPipeline = provider.GetPipeline(NaminesResiliencePipelineNames.AiPipeline);
        var cloudDbPipeline = provider.GetPipeline(NaminesResiliencePipelineNames.CloudDbPipeline);
        var dockerPipeline = provider.GetPipeline(NaminesResiliencePipelineNames.DockerSocketPipeline);
        var httpPipeline = provider.GetPipeline(NaminesResiliencePipelineNames.ExternalHttpPipeline);

        Assert.NotNull(aiPipeline);
        Assert.NotNull(cloudDbPipeline);
        Assert.NotNull(dockerPipeline);
        Assert.NotNull(httpPipeline);
    }

    [Fact]
    public async Task ResilienceProvider_Normal_Islem_Sonucunu_Dondurur()
    {
        var provider = new NaminesResilienceProvider(NullLoggerFactory.Instance);

        var result = await provider.ExecuteAsync(
            NaminesResiliencePipelineNames.ExternalHttpPipeline,
            ct => Task.FromResult(42));

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task CircuitBreaker_Hata_Limiti_Asildiginda_Devre_Acilir_Ve_CircuitBrokenException_Firlatir()
    {
        // AiPipeline: MinimumThroughput = 4, FailureRatio = 0.5
        var provider = new NaminesResilienceProvider(NullLoggerFactory.Instance);

        // 4 ardışık hata fırlatarak devreyi tetikleyelim
        for (int i = 0; i < 4; i++)
        {
            try
            {
                await provider.ExecuteAsync(
                    NaminesResiliencePipelineNames.AiPipeline,
                    ct => throw new HttpRequestException("Simulated upstream failure"));
            }
            catch
            {
                // Beklenen ara hatalar
            }
        }

        // Devre açık olmalı ve sonraki istek CircuitBrokenException fırlatmalı
        var ex = await Assert.ThrowsAsync<CircuitBrokenException>(async () =>
        {
            await provider.ExecuteAsync(
                NaminesResiliencePipelineNames.AiPipeline,
                ct => Task.FromResult("should not execute"));
        });

        Assert.Equal(NaminesResiliencePipelineNames.AiPipeline, ex.ServiceName);
        Assert.NotNull(ex.RetryAfter);
        Assert.True(ex.RetryAfter.Value.TotalSeconds > 0);
    }

    [Fact]
    public async Task DockerSocketPipeline_ConcurrencyLimiter_Eszamanli_Cagriları_Sinirlandirir()
    {
        var provider = new NaminesResilienceProvider(NullLoggerFactory.Instance);
        var pipeline = provider.GetPipeline(NaminesResiliencePipelineNames.DockerSocketPipeline);

        // DockerSocketPipeline PermitLimit = 3, QueueLimit = 10
        // Çok sayıda paralel iş başlatıp concurrency sınırını test edelim
        int executingCount = 0;
        int maxObservedExecuting = 0;
        var lockObj = new object();

        var tasks = new Task[5];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                await pipeline.ExecuteAsync(async ct =>
                {
                    lock (lockObj)
                    {
                        executingCount++;
                        if (executingCount > maxObservedExecuting)
                            maxObservedExecuting = executingCount;
                    }

                    await Task.Delay(50, ct);

                    lock (lockObj)
                    {
                        executingCount--;
                    }
                });
            });
        }

        await Task.WhenAll(tasks);

        // Concurrency limiter 3'ü aşmamalı
        Assert.True(maxObservedExecuting <= 3, $"Maksimum eşzamanlı işlem ({maxObservedExecuting}) 3 sınırını aştı!");
    }

    [Fact]
    public async Task ExceptionMiddleware_CircuitBrokenException_Icin_503_Ve_ProblemDetails_Uretir()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/v1/schema/generate";

        var middleware = new ExceptionMiddleware(
            next: (ctx) => throw new CircuitBrokenException("GroqAI", "Groq AI devre kesici açık", TimeSpan.FromSeconds(30)),
            logger: NullLogger<ExceptionMiddleware>.Instance,
            env: new TestWebHostEnvironment { EnvironmentName = "Production" });

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal("30", context.Response.Headers["Retry-After"].ToString());

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("https://namines.dev/errors/circuit-breaker", root.GetProperty("type").GetString());
        Assert.Equal(503, root.GetProperty("status").GetInt32());
        Assert.Equal("CIRCUIT_BREAKER_OPEN", root.GetProperty("code").GetString());
        Assert.Equal("/api/v1/schema/generate", root.GetProperty("instance").GetString());

        // Geriye dönük uyumluluk alanları:
        Assert.Equal(503, root.GetProperty("statusCode").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("message").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("correlationId").GetString()));
    }

    [Fact]
    public async Task ExceptionMiddleware_TimeoutRejectedException_Icin_504_Uretir()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/v1/gateway/query";

        var middleware = new ExceptionMiddleware(
            next: (ctx) => throw new TimeoutException("Database query took too long"),
            logger: NullLogger<ExceptionMiddleware>.Instance,
            env: new TestWebHostEnvironment { EnvironmentName = "Production" });

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status504GatewayTimeout, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("https://namines.dev/errors/gateway-timeout", root.GetProperty("type").GetString());
        Assert.Equal(504, root.GetProperty("status").GetInt32());
        Assert.Equal("GATEWAY_TIMEOUT", root.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExceptionMiddleware_AiRateLimitException_Icin_429_Ve_RetryAfter_Uretir()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/v1/ai/complete";

        var middleware = new ExceptionMiddleware(
            next: (ctx) => throw new AiRateLimitException("45", "Groq rate limit TPM exceeded"),
            logger: NullLogger<ExceptionMiddleware>.Instance,
            env: new TestWebHostEnvironment { EnvironmentName = "Production" });

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal("45", context.Response.Headers["Retry-After"].ToString());

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("https://namines.dev/errors/rate-limit", root.GetProperty("type").GetString());
        Assert.Equal(429, root.GetProperty("status").GetInt32());
        Assert.Equal("AI_RATE_LIMIT_EXCEEDED", root.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExceptionMiddleware_Genel_Hata_Productionda_Detay_Sizdirmaz()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/v1/secret";

        var middleware = new ExceptionMiddleware(
            next: (ctx) => throw new InvalidOperationException("Gizli veritabanı şifresi veya hassas stacktrace!"),
            logger: NullLogger<ExceptionMiddleware>.Instance,
            env: new TestWebHostEnvironment { EnvironmentName = "Production" });

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Production'da detailed null olmalı
        Assert.False(root.TryGetProperty("detailed", out var detailedProp) && detailedProp.ValueKind != JsonValueKind.Null);
        // Gizli şifre veya stacktrace sızmamalı
        Assert.DoesNotContain("Gizli veritabanı şifresi", json);
    }

    [Fact]
    public async Task ExceptionMiddleware_Development_Modunda_Detayli_Hata_Doner()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/v1/debug";

        var middleware = new ExceptionMiddleware(
            next: (ctx) => throw new InvalidOperationException("Geliştirici debug hatası!"),
            logger: NullLogger<ExceptionMiddleware>.Instance,
            env: new TestWebHostEnvironment { EnvironmentName = "Development" });

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Development'ta detailed alanı dolu dönmeli
        Assert.True(root.TryGetProperty("detailed", out var detailedProp));
        Assert.Contains("Geliştirici debug hatası!", detailedProp.GetString());
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException), StatusCodes.Status403Forbidden, "FORBIDDEN")]
    [InlineData(typeof(GithubRepositoryUnavailableException), StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND")]
    [InlineData(typeof(NslParseException), StatusCodes.Status400BadRequest, "BAD_REQUEST")]
    [InlineData(typeof(BrokenCircuitException), StatusCodes.Status503ServiceUnavailable, "CIRCUIT_BREAKER_OPEN")]
    public async Task ExceptionMiddleware_Ozel_Hatalari_Dogru_Status_Ve_Koda_Esler(Type exceptionType, int expectedStatus, string expectedCode)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/v1/test";

        Exception exceptionToThrow = exceptionType == typeof(GithubRepositoryUnavailableException)
            ? new GithubRepositoryUnavailableException("Repo bulunamadı")
            : exceptionType == typeof(NslParseException)
                ? new NslParseException(1, "Sözdizimi hatası")
                : exceptionType == typeof(BrokenCircuitException)
                    ? new BrokenCircuitException("Devre kesici açık")
                    : (Exception)Activator.CreateInstance(exceptionType)!;

        var middleware = new ExceptionMiddleware(
            next: (ctx) => throw exceptionToThrow,
            logger: NullLogger<ExceptionMiddleware>.Instance,
            env: new TestWebHostEnvironment { EnvironmentName = "Production" });

        await middleware.InvokeAsync(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(expectedStatus, root.GetProperty("status").GetInt32());
        Assert.Equal(expectedCode, root.GetProperty("code").GetString());
    }
}
