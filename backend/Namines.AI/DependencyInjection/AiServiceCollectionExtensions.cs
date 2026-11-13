using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Namines.AI.Application;
using Namines.AI.Infrastructure.Providers;
using Namines.Core.Interfaces;

namespace Namines.AI.DependencyInjection;

/// <summary>AI modülünün tek kayıt noktası. Önceden bu kayıtlar API'nin genel servis listesine dağılmıştı.</summary>
public static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddAiModule(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddMemoryCache();

        // Sohbet sağlayıcısı: yapılandırmayla seçilir, varsayılan Groq. Singleton çünkü tamamen durumsuz:
        // yalnızca adres, anahtar ve model tablosu taşıyor.
        services.AddSingleton<IChatCompletionProvider>(sp =>
            ChatCompletionProviderFactory.Create(
                sp.GetRequiredService<IConfiguration>(),
                sp.GetService<ILoggerFactory>()?.CreateLogger(nameof(ChatCompletionProviderFactory))));

        // Hız-sınırı bekleme bütçesi İSTEK başına, çağrı başına DEĞİL: parçalı bir şema üretimi yedi ayrı
        // sağlayıcı çağrısı yapıyor ve her birine ayrı bütçe vermek, söz verilen toplam bekleme süresini parça
        // sayısıyla çarpardı. Aynı istekteki paralel parçalar bu tek nesneyi paylaşıyor.
        services.AddScoped(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            return new AiRetryBudget(new AiRetryPolicy(
                ReadInt(config, "Ai:RateLimitRetry:MaxTotalWaitSeconds", 600),
                ReadInt(config, "Ai:RateLimitRetry:MaxSingleWaitSeconds", 60),
                ReadInt(config, "Ai:RateLimitRetry:MaxAttempts", AiRetryPolicy.DefaultMaxAttempts)));
        });

        // AddHttpClient<T> T'yi typed-client olarak kaydeder; ayrıca AddScoped<T> yazmak bu kaydı ezer ve
        // kurucudaki HttpClient DI'dan çözülemez. Bu yüzden eklenmez.
        services.AddHttpClient<GroqAIService>(client => client.Timeout = TimeSpan.FromMinutes(5));
        services.AddHttpClient<OllamaAIService>(client => client.Timeout = TimeSpan.FromMinutes(5));
        services.AddHttpClient<ISpeechTranscriber, GroqSpeechTranscriber>(client => client.Timeout = TimeSpan.FromMinutes(2));

        services.AddScoped<IAIFactory>(sp => new AIFactory(new IAIService[]
        {
            sp.GetRequiredService<GroqAIService>(),
            sp.GetRequiredService<OllamaAIService>(),
        }));

        // Varsayılan IAIService = Groq (diğer denetleyicilerle geriye dönük uyum).
        services.AddScoped<IAIService>(sp => sp.GetRequiredService<GroqAIService>());
        services.AddScoped<IDbaSchemaAnalyzer>(sp => sp.GetRequiredService<GroqAIService>());

        // İstek kapsamlı: bir isteğin tüm sağlayıcı çağrılarında biriken GERÇEK token kullanımı. Kota sabit
        // tahminle değil bununla uzlaştırılıyor (bkz. IAiUsageTracker, AIQuotaMiddleware.ActualTokens).
        services.AddScoped<IAiUsageTracker, AiUsageTracker>();

        return services;
    }

    private static int ReadInt(IConfiguration configuration, string key, int fallback)
        => int.TryParse(configuration[key], out var value) ? value : fallback;
}
