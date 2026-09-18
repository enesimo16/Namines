using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Namines.Core.Analysis;
using Namines.Core.Interfaces;

namespace Namines.Infrastructure.AI;

/// <summary>
/// İkinci sağlayıcı. OpenAI uyumlu <c>chat/completions</c> kullandığı için gövde
/// şekli Groq'unkiyle aynı — değişen yalnızca adres, anahtar ve modeller.
///
/// <b>CANLI DOĞRULANMADI:</b> bkz. <see cref="DeepSeekModelCatalog"/>.
/// </summary>
public sealed class DeepSeekChatCompletionProvider : IChatCompletionProvider
{
    private readonly string? _apiKey;

    public DeepSeekChatCompletionProvider(IConfiguration configuration)
    {
        var key = configuration["DeepSeek:ApiKey"];
        _apiKey = string.IsNullOrWhiteSpace(key) ? null : key;

        Models = new DeepSeekModelCatalog(
            configuration["DeepSeek:Models:Flash"],
            configuration["DeepSeek:Models:Standard"],
            configuration["DeepSeek:Models:Pro"]);
    }

    public string Name => "deepseek";

    public Uri BaseAddress => new("https://api.deepseek.com/v1/");

    public bool IsConfigured => _apiKey is not null;

    public IModelCatalog Models { get; }

    public void Authenticate(HttpRequestMessage request)
    {
        if (_apiKey is null) throw new AiNotConfiguredException("DeepSeek");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }
}
