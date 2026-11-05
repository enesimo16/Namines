using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Namines.Core.Interfaces;
using Namines.Infrastructure.AI;
using Namines.Ops.Application;
using Namines.Ops.Domain;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Dinamik AI Yönlendiricisi: kuralları Ops'tan (<see cref="IAiRouteCatalog"/>) okur, sağlayıcıyı adına
/// göre seçer. Tüm sağlayıcılar <see cref="IChatCompletionProvider"/> arayüzünden türer.
/// </summary>
public sealed class AiToolRouter : IAiToolRouter
{
    private readonly ConcurrentDictionary<string, IChatCompletionProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly IAiRouteCatalog _routes;
    private readonly ILogger<AiToolRouter> _logger;
    private readonly IConfiguration _configuration;

    public AiToolRouter(
        IEnumerable<IChatCompletionProvider> providers,
        IAiRouteCatalog routes,
        ILogger<AiToolRouter> logger,
        IConfiguration configuration)
    {
        _routes = routes;
        _logger = logger;
        _configuration = configuration;

        foreach (var p in providers)
        {
            _providers[p.Name] = p;
        }

        // Eğer DI'da olmayan ama config'den dinamik oluşturulabilecek sağlayıcılar varsa ekle
        EnsureBuiltInProviders();
    }

    private void EnsureBuiltInProviders()
    {
        if (!_providers.ContainsKey("groq"))
        {
            _providers["groq"] = new GroqChatCompletionProvider(_configuration);
        }

        if (!_providers.ContainsKey("deepseek"))
        {
            _providers["deepseek"] = new DeepSeekChatCompletionProvider(_configuration);
        }
    }

    public Task<AiToolRoute> GetRouteAsync(string toolKey, CancellationToken ct = default) =>
        _routes.GetRouteAsync(toolKey, ct);

    public Task<IReadOnlyList<AiToolRoute>> GetAllRoutesAsync(CancellationToken ct = default) =>
        _routes.GetAllRoutesAsync(ct);

    public Task UpdateRouteAsync(
        string toolKey,
        string provider,
        string modelName,
        double temperature,
        string? fallbackProvider = null,
        CancellationToken ct = default) =>
        _routes.UpdateRouteAsync(toolKey, provider, modelName, temperature, fallbackProvider, ct);

    public IChatCompletionProvider GetProvider(string providerName)
    {
        if (_providers.TryGetValue(providerName, out var p))
        {
            return p;
        }

        _logger.LogWarning("İstenen AI sağlayıcısı bulunamadı: '{ProviderName}'. Varsayılan 'groq' kullanılıyor.", providerName);
        return _providers.GetValueOrDefault("groq") ?? new GroqChatCompletionProvider(_configuration);
    }

    public IReadOnlyList<string> GetAvailableProviders()
    {
        return _providers.Keys.OrderBy(k => k).ToList();
    }
}
