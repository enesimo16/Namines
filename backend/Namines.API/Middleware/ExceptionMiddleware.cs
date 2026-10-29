using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Namines.Core.Exceptions;
using Namines.Core.Github;
using Namines.Core.Interfaces;
using Namines.Core.Nsl;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Namines.API.Middleware;

/// <summary>
/// RFC 7807 ProblemDetails uyumlu küresel istisna yakalama ara yazılımı.
///
/// Tüm işlenmemiş veya harici servis istisnalarını (Polly Circuit Breaker, Timeout, AI Rate Limit vb.)
/// standart application/problem+json formatına, uygun HTTP durum kodlarına ve Retry-After başlıklarına dönüştürür.
/// Geriye dönük uyumluluk için eski istemcilerin beklediği alanları da (Message, StatusCode, Detailed) barındırır.
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IWebHostEnvironment _env;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ExceptionMiddleware(
        RequestDelegate next,
        ILogger<ExceptionMiddleware> logger,
        IWebHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        // İstemci bağlantıyı kopardı/iptal etti: sunucu hatası değil. 500 + Error log'u
        // hem yanlış alarm üretir hem de hata oranı metriğini şişirir.
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogDebug("İstek istemci tarafından iptal edildi. Path={Path}", context.Request.Path);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 499;
            }
        }
        catch (Exception ex)
        {
            var correlationId = context.TraceIdentifier;

            _logger.LogError(
                ex,
                "Unhandled exception [CorrelationId={CorrelationId}] Path={Path}",
                correlationId,
                context.Request.Path);

            await HandleExceptionAsync(context, ex, correlationId, _env.IsDevelopment());
        }
    }

    private static Task HandleExceptionAsync(
        HttpContext context,
        Exception exception,
        string correlationId,
        bool isDevelopment)
    {
        // Yanıt ZATEN başlamışsa (ör. SSE akışı ilk baytını gönderdiyse) header/
        // status kodu artık ayarlanamaz — akış zaten başladıysa bağlantıyı sonlandır.
        if (context.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        var (statusCode, type, title, code, detail, retryAfterSeconds) = MapException(exception, correlationId, isDevelopment);

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = statusCode;

        if (!string.IsNullOrEmpty(retryAfterSeconds))
        {
            context.Response.Headers.Append("Retry-After", retryAfterSeconds);
        }

        var traceId = Activity.Current?.Id ?? correlationId;

        var problem = new NaminesProblemDetails
        {
            Type = type,
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = context.Request.Path,
            Code = code,
            CorrelationId = correlationId,
            TraceId = traceId,
            Timestamp = DateTimeOffset.UtcNow,
            Detailed = isDevelopment ? exception.ToString() : null
        };

        var json = JsonSerializer.Serialize(problem, JsonOptions);
        return context.Response.WriteAsync(json);
    }

    private static (int StatusCode, string Type, string Title, string Code, string Detail, string? RetryAfter)
        MapException(Exception exception, string correlationId, bool isDevelopment)
    {
        return exception switch
        {
            CircuitBrokenException cb => (
                StatusCodes.Status503ServiceUnavailable,
                "https://namines.dev/errors/circuit-breaker",
                "Service Temporarily Shielded",
                "CIRCUIT_BREAKER_OPEN",
                cb.Message,
                ((int)(cb.RetryAfter?.TotalSeconds ?? 30)).ToString()
            ),

            BrokenCircuitException => (
                StatusCodes.Status503ServiceUnavailable,
                "https://namines.dev/errors/circuit-breaker",
                "Downstream Service Unavailable",
                "CIRCUIT_BREAKER_OPEN",
                "The target service is temporarily unavailable due to high failure rate protection.",
                "30"
            ),

            TimeoutRejectedException or TimeoutException => (
                StatusCodes.Status504GatewayTimeout,
                "https://namines.dev/errors/gateway-timeout",
                "Gateway Timeout",
                "GATEWAY_TIMEOUT",
                "The requested operation exceeded the maximum execution time limit.",
                null
            ),

            AiRateLimitException rl => (
                StatusCodes.Status429TooManyRequests,
                "https://namines.dev/errors/rate-limit",
                "AI Rate Limit Exceeded",
                "AI_RATE_LIMIT_EXCEEDED",
                rl.Message,
                rl.RetryAfterSeconds
            ),

            GithubRateLimitedException ghRl => (
                StatusCodes.Status429TooManyRequests,
                "https://namines.dev/errors/rate-limit",
                "GitHub Rate Limit Exceeded",
                "GITHUB_RATE_LIMIT_EXCEEDED",
                ghRl.Message,
                "60"
            ),

            GithubRepositoryUnavailableException => (
                StatusCodes.Status404NotFound,
                "https://namines.dev/errors/not-found",
                "Resource Not Found",
                "RESOURCE_NOT_FOUND",
                exception.Message,
                null
            ),

            // KeyNotFoundException mesajı sözlük anahtarını/iç tanımlayıcıyı içerebilir; üretimde yansıtılmaz.
            System.Collections.Generic.KeyNotFoundException => (
                StatusCodes.Status404NotFound,
                "https://namines.dev/errors/not-found",
                "Resource Not Found",
                "RESOURCE_NOT_FOUND",
                isDevelopment ? exception.Message : "The requested resource was not found.",
                null
            ),

            AiNotConfiguredException nc => (
                StatusCodes.Status503ServiceUnavailable,
                "https://namines.dev/errors/ai-not-configured",
                "AI Service Not Configured",
                "AI_NOT_CONFIGURED",
                nc.Message,
                null
            ),

            // 401 değil 403: bu istisna dosya sistemi/IO kodundan da fırlar ve 401,
            // frontend'in kullanıcıyı oturumdan atmasına yol açar. Mesaj yansıtılmaz.
            UnauthorizedAccessException => (
                StatusCodes.Status403Forbidden,
                "https://namines.dev/errors/forbidden",
                "Forbidden",
                "FORBIDDEN",
                "Access denied.",
                null
            ),

            NslParseException nsl => (
                StatusCodes.Status400BadRequest,
                "https://namines.dev/errors/bad-request",
                "Bad Request",
                "BAD_REQUEST",
                nsl.Message,
                null
            ),

            ArgumentException arg => (
                StatusCodes.Status400BadRequest,
                "https://namines.dev/errors/bad-request",
                "Bad Request",
                "BAD_REQUEST",
                // Güvenlik (M-2): Production ortamında ArgumentException mesajındaki dosya yolları veya dahili parametre detaylarının sızması önlenir.
                isDevelopment || (!arg.Message.Contains('\\') && !arg.Message.Contains('/') && !arg.Message.Contains("Parameter '"))
                    ? arg.Message
                    : "Invalid request argument.",
                null
            ),

            _ => (
                StatusCodes.Status500InternalServerError,
                "https://namines.dev/errors/internal-server-error",
                "Internal Server Error",
                "INTERNAL_SERVER_ERROR",
                isDevelopment ? exception.Message : $"Something went wrong. Reference: {correlationId}",
                null
            )
        };
    }
}

/// <summary>
/// RFC 7807 ProblemDetails uyumlu hata modeli.
/// Eski istemciler için Message, StatusCode ve Detailed geriye dönük uyumluluk alanlarını da içerir.
/// </summary>
public sealed class NaminesProblemDetails
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("instance")]
    public string? Instance { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; set; } = string.Empty;

    [JsonPropertyName("traceId")]
    public string? TraceId { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    // Geriye dönük tam uyumluluk (Legacy istemciler için):
    [JsonPropertyName("statusCode")]
    public int StatusCode => Status;

    [JsonPropertyName("message")]
    public string Message => Detail ?? Title;

    [JsonPropertyName("detailed")]
    public string? Detailed { get; set; }
}
