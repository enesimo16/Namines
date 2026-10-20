using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Namines.Core.Interfaces;

namespace Namines.Infrastructure.Observability;

/// <summary>
/// PostHog ürün analitiği ve özellik bayrakları (feature flags) istemcisi.
///
/// <b>Kota Koruma ve Güvenilirlik İlkeleri:</b>
/// 1. <b>Fail-Open (Kullanıcı Akışını Asla Kesmeme):</b> Ağ kesintisi, PostHog 5xx yanıtları
///    veya kota limitlerinde asla istisna fırlatmaz; sessizce loglar ve iş mantığının sürmesini sağlar.
/// 2. <b>Mükerrer İstek Engelleme (Throttling / Deduplication):</b> Aynı distinctId ve eventName
///    kombinasyonunun milisaniyeler içinde tekrarlanmasını bellek içi zaman penceresiyle engeller.
/// 3. <b>IHttpClientFactory:</b> Socket exhaustion'ı önlemek için ASP.NET Core IHttpClientFactory
///    üzerinden yönetilen named istemciyi kullanır.
/// </summary>
public sealed class PostHogClient : IPostHogService
{
    public const string HttpClientName = "PostHogClient";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PostHogOptions _options;
    private readonly ILogger<PostHogClient> _logger;

    // Mükerrer olayları filtrelemek için hafif bellek içi pencere (1500 ms)
    private readonly ConcurrentDictionary<string, long> _recentEvents = new();
    private const long DedupeWindowTicks = 15_000_000; // 1.5 saniye (100-ns ticks)

    public PostHogClient(
        IHttpClientFactory httpClientFactory,
        IOptions<PostHogOptions> options,
        ILogger<PostHogClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.Enabled && !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task CaptureAsync(
        string distinctId,
        string eventName,
        Dictionary<string, object>? properties = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return;
        }

        // Kota koruması: hızlı art arda gelen özdeş olayları süz
        var dedupeKey = $"{distinctId}:{eventName}";
        var nowTicks = DateTime.UtcNow.Ticks;
        if (_recentEvents.TryGetValue(dedupeKey, out var lastTicks) && (nowTicks - lastTicks) < DedupeWindowTicks)
        {
            _logger.LogDebug("PostHog olayı kota koruması nedeniyle filtrelendi (deduplicated): {EventName}", eventName);
            return;
        }
        _recentEvents[dedupeKey] = nowTicks;

        // Bellek temizliği (100 elemanı aştığında eski kayıtları boşalt)
        if (_recentEvents.Count > 200)
        {
            foreach (var kvp in _recentEvents)
            {
                if (nowTicks - kvp.Value > DedupeWindowTicks * 2)
                {
                    _recentEvents.TryRemove(kvp.Key, out _);
                }
            }
        }

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var payload = new Dictionary<string, object>
            {
                ["api_key"] = _options.ApiKey!,
                ["event"] = eventName,
                ["distinct_id"] = distinctId,
                ["timestamp"] = DateTime.UtcNow.ToString("o"),
                ["properties"] = properties ?? new Dictionary<string, object>()
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("/capture/", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "PostHog capture yanıtı başarısız: HTTP {StatusCode} (Event: {EventName})",
                    (int)response.StatusCode,
                    eventName);
            }
        }
        catch (Exception ex)
        {
            // Analitik hatası hiçbir zaman çağıran servisi kırmamalıdır
            _logger.LogDebug(ex, "PostHog olayı gönderilirken hata oluştu (Fail-Open): {EventName}", eventName);
        }
    }

    public async Task IdentifyAsync(
        string distinctId,
        Dictionary<string, object>? properties = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return;
        }

        var identifyProps = new Dictionary<string, object>
        {
            ["$set"] = properties ?? new Dictionary<string, object>()
        };

        await CaptureAsync(distinctId, "$identify", identifyProps, cancellationToken);
    }

    public async Task<bool> IsFeatureFlagEnabledAsync(
        string distinctId,
        string flagKey,
        bool defaultValue = false,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return defaultValue;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var requestBody = new Dictionary<string, object>
            {
                ["api_key"] = _options.ApiKey!,
                ["distinct_id"] = distinctId
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("/decide/?v=3", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PostHog decide yanıtı başarısız: HTTP {StatusCode}", (int)response.StatusCode);
                return defaultValue;
            }

            var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseString);

            if (doc.RootElement.TryGetProperty("featureFlags", out var flagsElement) &&
                flagsElement.ValueKind == JsonValueKind.Object &&
                flagsElement.TryGetProperty(flagKey, out var flagValue))
            {
                if (flagValue.ValueKind == JsonValueKind.True) return true;
                if (flagValue.ValueKind == JsonValueKind.False) return false;
                if (flagValue.ValueKind == JsonValueKind.String)
                {
                    var str = flagValue.GetString();
                    return !string.IsNullOrWhiteSpace(str) && !str.Equals("false", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PostHog feature flag denetlenirken hata oluştu: {FlagKey}", flagKey);
        }

        return defaultValue;
    }
}
