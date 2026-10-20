using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Retry;
using Polly.Timeout;

namespace Namines.Infrastructure.Resilience;

/// <summary>
/// Namines Polly v8 dayanıklılık kuralları ve boru hattı (pipeline) fabrikası.
/// Dış bağımlılıkların (AI, Cloud DB, Docker) arızalarının tüm sistemi kilitlemesini önler.
/// </summary>
public static class NaminesResiliencePipelines
{
    /// <summary>
    /// AI servisleri (Groq, Ollama, DeepSeek) için boru hattı:
    /// 1. Timeout (60 sn)
    /// 2. Jittered Exponential Backoff Retry (3 deneme)
    /// 3. Circuit Breaker (%50 hata oranı, 4 istek minimum örneklem, 30 sn devre açma)
    /// </summary>
    public static ResiliencePipeline CreateAiPipeline(ILogger? logger = null)
    {
        return new ResiliencePipelineBuilder()
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(60),
                OnTimeout = args =>
                {
                    logger?.LogWarning("AI işlemi zaman aşımına uğradı (Timeout: 60s). İstek iptal ediliyor.");
                    return default;
                }
            })
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(500),
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutException>()
                    .Handle<IOException>(),
                OnRetry = args =>
                {
                    logger?.LogWarning("AI isteği geçici hata nedeniyle tekrar deneniyor (Deneme {AttemptNumber}): {Message}",
                        args.AttemptNumber, args.Outcome.Exception?.Message);
                    return default;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 4,
                BreakDuration = TimeSpan.FromSeconds(30),
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutException>()
                    .Handle<IOException>(),
                OnOpened = args =>
                {
                    logger?.LogError("AI Circuit Breaker DEVREYE GİRDİ (AÇIK). Sağlayıcı 30 saniye süreyle korunmaya alındı. Hata: {Message}",
                        args.Outcome.Exception?.Message);
                    return default;
                },
                OnClosed = args =>
                {
                    logger?.LogInformation("AI Circuit Breaker KAPANDI. Sağlayıcı normale döndü.");
                    return default;
                },
                OnHalfOpened = args =>
                {
                    logger?.LogInformation("AI Circuit Breaker YARI-AÇIK modda. Deneme isteği bekleniyor.");
                    return default;
                }
            })
            .Build();
    }

    /// <summary>
    /// Cloud Veritabanı yönetim API'leri (Neon, Supabase) için boru hattı:
    /// Hızlı 15s timeout, 3 retry ve kesinti koruması.
    /// </summary>
    public static ResiliencePipeline CreateCloudDbPipeline(ILogger? logger = null)
    {
        return new ResiliencePipelineBuilder()
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(20),
                OnTimeout = args =>
                {
                    logger?.LogWarning("Cloud DB REST isteği zaman aşımına uğradı (20s).");
                    return default;
                }
            })
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(300),
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutException>()
                    .Handle<IOException>()
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 4,
                BreakDuration = TimeSpan.FromSeconds(30),
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutException>()
                    .Handle<IOException>(),
                OnOpened = args =>
                {
                    logger?.LogError("Cloud DB Circuit Breaker DEVREYE GİRDİ (AÇIK). Servis 30s süreyle kilitlendi.");
                    return default;
                }
            })
            .Build();
    }

    /// <summary>
    /// Docker Daemon / docker.sock işlemleri için boru hattı:
    /// WSL2 ve Docker Desktop stabilitesini korumak için Concurrency Limiter (En fazla 3 eşzamanlı işlem)
    /// ve 45 saniyelik zaman aşımı kuralı içerir.
    /// </summary>
    public static ResiliencePipeline CreateDockerSocketPipeline(ILogger? logger = null)
    {
        return new ResiliencePipelineBuilder()
            .AddConcurrencyLimiter(permitLimit: 3, queueLimit: 10)
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(45),
                OnTimeout = args =>
                {
                    logger?.LogWarning("Docker daemon işlemi zaman aşımına uğradı (45s).");
                    return default;
                }
            })
            .Build();
    }

    /// <summary>
    /// Standart genel HTTP ve webhook çağrıları için temel koruma hattı.
    /// </summary>
    public static ResiliencePipeline CreateExternalHttpPipeline(ILogger? logger = null)
    {
        return new ResiliencePipelineBuilder()
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(15)
            })
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                BackoffType = DelayBackoffType.Constant,
                Delay = TimeSpan.FromMilliseconds(500),
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutException>()
            })
            .Build();
    }
}
