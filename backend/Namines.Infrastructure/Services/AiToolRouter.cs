using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Infrastructure.AI;
using Namines.Infrastructure.Data;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Dinamik AI Yönlendiricisi (Tool-Based Model Router).
///
/// <b>Genişletilebilirlik Prensibi:</b>
/// Tüm sağlayıcılar (Groq, DeepSeek, ileride eklenecek OpenAI, Claude, Gemini vb.)
/// <see cref="IChatCompletionProvider"/> arayüzünden türer.
/// Bu servis, DI havuzundaki tüm sağlayıcıları adına göre toplar ve her bir aracı
/// admin panelinden belirlenen modele dinamik olarak yönlendirir.
/// </summary>
public sealed class AiToolRouter : IAiToolRouter
{
    private readonly ConcurrentDictionary<string, IChatCompletionProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AiToolRoute> _routesCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AiToolRouter> _logger;
    private readonly IConfiguration _configuration;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public AiToolRouter(
        IEnumerable<IChatCompletionProvider> providers,
        IServiceScopeFactory scopeFactory,
        ILogger<AiToolRouter> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration;

        foreach (var p in providers)
        {
            _providers[p.Name] = p;
        }

        // Eğer DI'da olmayan ama config'den dinamik oluşturulabilecek sağlayıcılar varsa ekle
        EnsureBuiltInProviders();

        // Varsayılan rotaları hafızaya yükle
        foreach (var r in GetDefaultRoutes())
        {
            _routesCache[r.ToolKey] = r;
        }
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

    private static List<AiToolRoute> GetDefaultRoutes() => new()
    {
        new AiToolRoute
        {
            ToolKey = "schema_generator",
            DisplayName = "Prompt'tan Şema Üretimi (Schema Generator)",
            Provider = "groq",
            ModelName = "qwen-2.5-coder-32b",
            Temperature = 0.2,
            FallbackProvider = "deepseek"
        },
        new AiToolRoute
        {
            ToolKey = "ai_dba",
            DisplayName = "AI DBA Optimizasyon & İndeks Önericisi",
            Provider = "groq",
            ModelName = "llama-3.3-70b-versatile",
            Temperature = 0.1,
            FallbackProvider = "deepseek"
        },
        new AiToolRoute
        {
            ToolKey = "flow_automation",
            DisplayName = "Namines Flow İş Akışı Otomasyonu",
            Provider = "groq",
            ModelName = "llama-3.3-70b-versatile",
            Temperature = 0.2,
            FallbackProvider = "deepseek"
        },
        new AiToolRoute
        {
            ToolKey = "smart_seed",
            DisplayName = "Akıllı Sentetik Veri Tohumlayıcı (Smart Seed)",
            Provider = "groq",
            ModelName = "qwen-2.5-coder-32b",
            Temperature = 0.4,
            FallbackProvider = "deepseek"
        },
        new AiToolRoute
        {
            ToolKey = "migration_explainer",
            DisplayName = "Değişiklik İnceleme Risk Açıklayıcısı (Impact Explainer)",
            Provider = "groq",
            ModelName = "llama-3.3-70b-versatile",
            Temperature = 0.2,
            FallbackProvider = "deepseek"
        },
        new AiToolRoute
        {
            ToolKey = "code_schema",
            DisplayName = "Tersine Mühendislik & Koddan Şema Çıkarıcı",
            Provider = "groq",
            ModelName = "qwen-2.5-coder-32b",
            Temperature = 0.1,
            FallbackProvider = "deepseek"
        }
    };

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetService<AuthDbContext>();
            if (db != null)
            {
                try
                {
                    var routes = await db.AiToolRoutes.AsNoTracking().ToListAsync(ct);
                    if (routes.Count > 0)
                    {
                        foreach (var route in routes)
                        {
                            _routesCache[route.ToolKey] = route;
                        }
                    }
                    else
                    {
                        // İlk çalıştırmada varsayılanları veritabanına tohumla
                        var defaults = GetDefaultRoutes();
                        db.AiToolRoutes.AddRange(defaults);
                        await db.SaveChangesAsync(ct);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "AiToolRoutes tablosu okunamadı veya henüz migrate edilmedi. Bellek içi rotalar kullanılıyor.");
                }
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<AiToolRoute> GetRouteAsync(string toolKey, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        if (_routesCache.TryGetValue(toolKey, out var route))
        {
            return route;
        }

        // Bilinmeyen tool için varsayılan fallback
        return new AiToolRoute
        {
            ToolKey = toolKey,
            DisplayName = toolKey,
            Provider = "groq",
            ModelName = "qwen-2.5-coder-32b",
            Temperature = 0.2
        };
    }

    public async Task<IReadOnlyList<AiToolRoute>> GetAllRoutesAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        return _routesCache.Values.OrderBy(r => r.DisplayName).ToList();
    }

    public async Task UpdateRouteAsync(
        string toolKey,
        string provider,
        string modelName,
        double temperature,
        string? fallbackProvider = null,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var route = await GetRouteAsync(toolKey, ct);
        route.Provider = provider.ToLowerInvariant().Trim();
        route.ModelName = modelName.Trim();
        route.Temperature = Math.Clamp(temperature, 0.0, 1.0);
        route.FallbackProvider = string.IsNullOrWhiteSpace(fallbackProvider) ? null : fallbackProvider.ToLowerInvariant().Trim();
        route.UpdatedAt = DateTime.UtcNow;

        _routesCache[toolKey] = route;

        // DB'ye kalıcı kaydet
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<AuthDbContext>();
        if (db != null)
        {
            try
            {
                var existing = await db.AiToolRoutes.FirstOrDefaultAsync(r => r.ToolKey == toolKey, ct);
                if (existing != null)
                {
                    existing.Provider = route.Provider;
                    existing.ModelName = route.ModelName;
                    existing.Temperature = route.Temperature;
                    existing.FallbackProvider = route.FallbackProvider;
                    existing.UpdatedAt = route.UpdatedAt;
                }
                else
                {
                    db.AiToolRoutes.Add(route);
                }
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AiToolRoute veritabanına kaydedilirken hata oluştu: {ToolKey}", toolKey);
            }
        }

        _logger.LogInformation("AI Tool rotası güncellendi: {ToolKey} -> Provider: {Provider}, Model: {ModelName}",
            toolKey, route.Provider, route.ModelName);
    }

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
