using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Namines.AI.Application;
using Namines.Core.Analysis;
using Namines.Core.Interfaces;

namespace Namines.AI.Infrastructure.Providers;

internal static class GroqResponseReader
{
    public static string ReadContentOrThrow(JsonElement root, int maxTokens)
    {
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("LLM response contained no choices.");
        }

        var first = choices[0];
        var finishReason = first.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String
            ? fr.GetString()
            : null;

        if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
            throw new AiOutputTruncatedException(maxTokens);

        if (first.TryGetProperty("message", out var msg) &&
            msg.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}

/// <summary>
/// Tek sorumluluklu, OpenAI-uyumlu HTTP tamamlama + 429 hız-sınırı yeniden deneme +
/// token kesilme algılama + çoklu sağlayıcı (Groq / DeepSeek / Ollama / Gemini) motoru.
/// </summary>
public sealed class OpenAiCompatibleLlmEngine : ILlmCompletionEngine, IAgentChatClient
{
    private static readonly Regex RetryBodyRegex = new(
        @"(?:try again in|retry after)\s+(?<val>\d+(?:\.\d+)?)\s*(?<unit>ms|s)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly IChatCompletionProvider _defaultProvider;
    private readonly ILogger<OpenAiCompatibleLlmEngine> _logger;
    private readonly IAiUsageTracker? _usage;
    private readonly AiRetryBudget _retryBudget;
    private readonly IHttpContextAccessor? _httpContext;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public OpenAiCompatibleLlmEngine(
        HttpClient http,
        IConfiguration config,
        IChatCompletionProvider defaultProvider,
        ILogger<OpenAiCompatibleLlmEngine> logger,
        IAiUsageTracker? usage = null,
        AiRetryBudget? retryBudget = null,
        IHttpContextAccessor? httpContext = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _defaultProvider = defaultProvider ?? throw new ArgumentNullException(nameof(defaultProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _usage = usage;
        _retryBudget = retryBudget ?? new AiRetryBudget(AiRetryPolicy.Disabled);
        _httpContext = httpContext;
        _delay = delay ?? Task.Delay;
    }

    public bool IsConfiguredFor(string? providerHint = null)
        => ResolveProvider(providerHint).IsConfigured;

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var provider = ResolveProvider(request.ProviderHint);
        var model = ResolveModel(provider, request.ModelHint);
        var settings = ReadHeaderSettings();
        var desiredMaxTokens = request.MaxOutputTokens ?? settings.MaxTokensValue;
        var maxTokens = provider.Models.ClampToModelLimit(model, desiredMaxTokens);

        object userContent;
        if (request.ImageBytes is { Length: > 0 })
        {
            var mime = string.IsNullOrWhiteSpace(request.ImageMimeType) ? "image/png" : request.ImageMimeType;
            var dataUrl = $"data:{mime};base64,{Convert.ToBase64String(request.ImageBytes)}";
            userContent = new object[]
            {
                new { type = "text", text = request.UserPrompt },
                new { type = "image_url", image_url = new { url = dataUrl } },
            };
        }
        else
        {
            userContent = request.UserPrompt;
        }

        var messages = new List<object>
        {
            new { role = "system", content = request.SystemPrompt },
            new { role = "user", content = userContent },
        };

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages,
            ["temperature"] = request.Temperature,
            ["max_tokens"] = maxTokens,
        };

        if (request.JsonResponse)
            payload["response_format"] = new { type = "json_object" };

        using var doc = await SendWithRetryAsync(provider, payload, ct).ConfigureAwait(false);
        var root = doc.RootElement;
        var text = GroqResponseReader.ReadContentOrThrow(root, maxTokens);

        var (promptTok, completionTok) = ExtractAndRecordUsage(root);

        return new LlmResponse(
            Content: text,
            ModelUsed: model,
            PromptTokens: promptTok,
            CompletionTokens: completionTok,
            IsTruncated: false);
    }

    public async Task<AgentChatResponse> CompleteAsync(
        IReadOnlyList<AgentChatMessage> messages,
        IReadOnlyList<AgentToolDefinition> tools,
        double temperature,
        CancellationToken cancellationToken = default,
        int? maxOutputTokens = null)
    {
        var provider = _defaultProvider;
        var model = ResolveModel(provider, null);
        var maxTokens = provider.Models.ClampToModelLimit(
            model,
            maxOutputTokens ?? AiAdvancedSettings.Default.MaxTokensValue);

        var wireMessages = new List<object>(messages.Count);
        foreach (var m in messages)
        {
            var obj = new Dictionary<string, object?> { ["role"] = m.Role };
            if (m.Content is not null) obj["content"] = m.Content;
            if (m.ToolCallId is not null) obj["tool_call_id"] = m.ToolCallId;
            if (m.ToolCalls is { Count: > 0 })
            {
                obj["tool_calls"] = m.ToolCalls.Select(tc => new
                {
                    id = tc.Id,
                    type = "function",
                    function = new { name = tc.Name, arguments = tc.ArgumentsJson },
                }).ToList();
            }
            wireMessages.Add(obj);
        }

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = wireMessages,
            ["temperature"] = temperature,
            ["max_tokens"] = maxTokens,
        };

        if (tools.Count > 0 && SupportsToolCalling(model))
        {
            payload["tools"] = tools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = JsonSerializer.Deserialize<JsonElement>(t.ParametersJsonSchema),
                },
            }).ToList();
        }

        using var doc = await SendWithRetryAsync(provider, payload, cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        ExtractAndRecordUsage(root);

        var content = GroqResponseReader.ReadContentOrThrow(root, maxTokens);
        var toolCalls = new List<AgentToolCall>();

        if (root.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out var msg) &&
            msg.TryGetProperty("tool_calls", out var tcArray) &&
            tcArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in tcArray.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
                if (item.TryGetProperty("function", out var fn))
                {
                    var name = fn.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? string.Empty : string.Empty;
                    var args = fn.TryGetProperty("arguments", out var aEl) ? aEl.GetString() ?? "{}" : "{}";
                    toolCalls.Add(new AgentToolCall(id, name, args));
                }
            }
        }

        return new AgentChatResponse(string.IsNullOrEmpty(content) ? null : content, toolCalls);
    }

    public static bool SupportsToolCalling(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return false;
        var lower = model.Trim().ToLowerInvariant();
        if (lower.Contains("whisper", StringComparison.Ordinal) ||
            lower.Contains("embed", StringComparison.Ordinal) ||
            lower.Contains("tts", StringComparison.Ordinal))
        {
            return false;
        }
        return true;
    }

    internal static bool ParseRetryAfter(HttpResponseMessage response, string body, out TimeSpan wait)
    {
        wait = TimeSpan.Zero;

        if (response.Headers.RetryAfter is { } retryHeader)
        {
            if (retryHeader.Delta is { } delta)
            {
                wait = delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
                return true;
            }
            if (retryHeader.Date is { } date)
            {
                var diff = date - DateTimeOffset.UtcNow;
                wait = diff < TimeSpan.Zero ? TimeSpan.Zero : diff;
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            var match = RetryBodyRegex.Match(body);
            if (match.Success &&
                double.TryParse(match.Groups["val"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
            {
                var isMs = string.Equals(match.Groups["unit"].Value, "ms", StringComparison.OrdinalIgnoreCase);
                wait = isMs ? TimeSpan.FromMilliseconds(numeric) : TimeSpan.FromSeconds(numeric);
                return true;
            }
        }

        return false;
    }

    private async Task<JsonDocument> SendWithRetryAsync(
        IChatCompletionProvider provider,
        Dictionary<string, object?> payload,
        CancellationToken ct)
    {
        var jsonBody = JsonSerializer.Serialize(payload);
        var endpoint = new Uri(provider.BaseAddress, "chat/completions");

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
            };

            // Sağlayıcı API anahtarını header'a yazar; yapılandırılmamışsa AiNotConfiguredException fırlatır.
            provider.Authenticate(req);

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var hasHint = ParseRetryAfter(resp, body, out var requested);
                if (!hasHint) requested = TimeSpan.FromMinutes(5);

                if (_retryBudget.Policy.TryNextDelay(requested, out var granted))
                {
                    _logger.LogWarning(
                        "Provider {Provider} returned 429; waiting {WaitMs}ms before retry.",
                        provider.Name,
                        granted.TotalMilliseconds);
                    await _delay(granted, ct).ConfigureAwait(false);
                    continue;
                }

                var sec = ((int)Math.Ceiling(requested.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                throw new AiRateLimitException(sec, body);
            }

            if (!resp.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"AI provider '{provider.Name}' failed with {(int)resp.StatusCode}: {Truncate(body, 500)}",
                    null,
                    resp.StatusCode);
            }

            return JsonDocument.Parse(body);
        }
    }

    private (int PromptTokens, int CompletionTokens) ExtractAndRecordUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage))
            return (0, 0);

        var promptTok = usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var pv) ? pv : 0;
        var completionTok = usage.TryGetProperty("completion_tokens", out var c) && c.TryGetInt32(out var cv) ? cv : 0;
        var total = usage.TryGetProperty("total_tokens", out var t) && t.TryGetInt32(out var tv)
            ? tv
            : promptTok + completionTok;

        if (total > 0)
            _usage?.Record(total);

        return (promptTok, completionTok);
    }

    private IChatCompletionProvider ResolveProvider(string? providerHint)
    {
        if (string.IsNullOrWhiteSpace(providerHint))
            return _defaultProvider;

        var hint = providerHint.Trim().ToLowerInvariant();
        if (string.Equals(hint, _defaultProvider.Name, StringComparison.OrdinalIgnoreCase))
            return _defaultProvider;

        return hint switch
        {
            "ollama" => new OllamaChatCompletionProvider(_config),
            "deepseek" => new DeepSeekChatCompletionProvider(_config),
            "groq" => new GroqChatCompletionProvider(_config),
            _ => _defaultProvider,
        };
    }

    private static string ResolveModel(IChatCompletionProvider provider, string? modelHint)
    {
        if (string.IsNullOrWhiteSpace(modelHint))
            return provider.Models.UpstreamModel(NaiModel.Pro);

        var trimmed = modelHint.Trim();
        if (trimmed.StartsWith("nai", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "flash", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "standard", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "pro", StringComparison.OrdinalIgnoreCase))
        {
            var tier = NaiCatalog.Resolve(trimmed);
            return provider.Models.UpstreamModel(tier);
        }

        return trimmed;
    }

    private AiAdvancedSettings ReadHeaderSettings()
    {
        var rawHeader = _httpContext?.HttpContext?.Request.Headers["X-Ai-Settings"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(rawHeader)
            ? AiAdvancedSettings.Default
            : AiAdvancedSettings.Parse(rawHeader);
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "...";
}
