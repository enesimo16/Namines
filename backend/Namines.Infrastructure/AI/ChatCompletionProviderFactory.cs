using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Namines.Core.Interfaces;

namespace Namines.Infrastructure.AI;

/// <summary>
/// <c>Ai:Provider</c> yapılandırmasına göre sağlayıcıyı seçer.
/// Varsayılan Groq — bugün çalışan yol odur.
/// </summary>
public static class ChatCompletionProviderFactory
{
    public static IChatCompletionProvider Create(
        IConfiguration configuration, ILogger? logger = null)
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

            default:
                // Tanınmayan ad varsayılana düşer, uygulamayı DÜŞÜRMEZ: bir yazım
                // hatası yüzünden hiç açılmamak, yapılandırma hatasının bedelini
                // orantısız kılardı. Ama sessizce de geçilmez — yoksa yanlış
                // sağlayıcıyla çalıştığı hiç fark edilmez.
                logger?.LogWarning(
                    "Unknown Ai:Provider value '{Configured}'; falling back to groq.", configured);
                return new GroqChatCompletionProvider(configuration);
        }
    }
}
