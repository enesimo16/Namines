using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Namines.Core.Interfaces;

namespace Namines.AI.Infrastructure.Providers;

/// <summary>
/// <c>Ai:Provider</c> yapılandırmasına göre sohbet tamamlama sağlayıcısını seçer.
/// </summary>
public static class ChatCompletionProviderFactory
{
    public static IChatCompletionProvider Create(
        IConfiguration configuration,
        ILogger? logger = null)
    {
        var configured = configuration["Ai:Provider"]?.Trim();

        if (string.IsNullOrWhiteSpace(configured))
            return new GroqChatCompletionProvider(configuration);

        switch (configured.ToLowerInvariant())
        {
            case "groq":
                return new GroqChatCompletionProvider(configuration);

            case "deepseek":
                return new DeepSeekChatCompletionProvider(configuration);

            case "ollama":
                return new OllamaChatCompletionProvider(configuration);

            default:
                logger?.LogWarning(
                    "Unknown Ai:Provider value '{Configured}'; falling back to groq.",
                    configured);
                return new GroqChatCompletionProvider(configuration);
        }
    }
}
