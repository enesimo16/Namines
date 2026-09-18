using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Namines.Core.Analysis;
using Namines.Core.Interfaces;

namespace Namines.Infrastructure.AI;

/// <summary>Varsayılan sağlayıcı. Bugünkü davranışın birebir karşılığı.</summary>
public sealed class GroqChatCompletionProvider : IChatCompletionProvider
{
    private readonly string? _apiKey;

    public GroqChatCompletionProvider(IConfiguration configuration)
    {
        var key = configuration["Groq:ApiKey"];
        _apiKey = string.IsNullOrWhiteSpace(key) ? null : key;
    }

    public string Name => "groq";

    public string DisplayName => "Groq";

    public Uri BaseAddress => new("https://api.groq.com/openai/v1/");

    public bool IsConfigured => _apiKey is not null;

    public IModelCatalog Models { get; } = new GroqModelCatalog();

    public void Authenticate(HttpRequestMessage request)
    {
        if (_apiKey is null) throw new AiNotConfiguredException("Groq");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }
}
