using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Namines.Core.Analysis;
using Namines.Core.Interfaces;

namespace Namines.AI.Infrastructure.Providers;

/// <summary>
/// Yerel Ollama OpenAI-uyumlu uç noktası için model kataloğu.
/// </summary>
public sealed class OllamaModelCatalog : IModelCatalog
{
    private readonly Dictionary<NaiModel, (string Id, int MaxCompletionTokens)> _models;

    public OllamaModelCatalog(
        string? flash = null,
        string? standard = null,
        string? pro = null)
    {
        _models = new Dictionary<NaiModel, (string, int)>
        {
            [NaiModel.Flash] = (Or(flash, "qwen2.5-coder:7b"), 32_768),
            [NaiModel.Standard] = (Or(standard, "qwen2.5-coder:14b"), 65_536),
            [NaiModel.Pro] = (Or(pro, "qwen2.5-coder:32b"), 65_536),
        };
    }

    private static string Or(string? configured, string fallback)
        => string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();

    public string UpstreamModel(NaiModel model) => _models[model].Id;

    public int MaxCompletionTokens(NaiModel model) => _models[model].MaxCompletionTokens;

    public int ClampToModelLimit(string? upstreamModel, int requestedMaxTokens)
    {
        if (string.IsNullOrWhiteSpace(upstreamModel)) return requestedMaxTokens;

        foreach (var entry in _models.Values)
        {
            if (string.Equals(entry.Id, upstreamModel, StringComparison.OrdinalIgnoreCase))
                return Math.Min(requestedMaxTokens, entry.MaxCompletionTokens);
        }

        return requestedMaxTokens;
    }
}

/// <summary>
/// Yerel veya özel sunucuda koşan Ollama OpenAI-uyumlu API sağlayıcısı.
/// </summary>
public sealed class OllamaChatCompletionProvider : IChatCompletionProvider
{
    private readonly string? _apiKey;
    private readonly Uri _baseAddress;

    public OllamaChatCompletionProvider(IConfiguration configuration)
    {
        var configuredUrl = configuration["Ollama:BaseUrl"]?.Trim();
        _baseAddress = Uri.TryCreate(configuredUrl, UriKind.Absolute, out var uri)
            ? uri
            : new Uri("http://localhost:11434/v1/");

        _apiKey = configuration["Ollama:ApiKey"]?.Trim();
        Models = new OllamaModelCatalog(
            configuration["Ollama:Models:Flash"],
            configuration["Ollama:Models:Standard"],
            configuration["Ollama:Models:Pro"] ?? configuration["Ollama:DefaultModel"]);
    }

    public string Name => "ollama";
    public string DisplayName => "Ollama (Local)";
    public Uri BaseAddress => _baseAddress;
    public bool IsConfigured => true;
    public IModelCatalog Models { get; }

    public void Authenticate(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }
}
