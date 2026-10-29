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
    // DB yüklemesi başarısız olursa "başlatıldı" işaretlenmez; kısa aralıkla tekrar denenir (bkz. FeatureFlagService).
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);
    private DateTime _nextRefreshUtc = DateTime.MinValue;
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
        if (DateTime.UtcNow < _nextRefreshUtc) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow < _nextRefreshUtc) return;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetService<AuthDbContext>();
            var loaded = db == null;
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
                    loaded = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AiToolRoutes tablosu okunamadı veya henüz migrate edilmedi. Bellek içi rotalar kullanılıyor, kısa süre sonra tekrar denenecek.");
                }
            }

            _nextRefreshUtc = DateTime.UtcNow + (loaded ? RefreshInterval : RetryInterval);
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

        var current = await GetRouteAsync(toolKey, ct);

        // Önbellekteki nesne paylaşımlı: DB yazımı başarısız olursa yarım güncelleme kalmasın diye kopya üzerinde çalışılır.
        var route = new AiToolRoute
        {
            ToolKey = toolKey,
            DisplayName = current.DisplayName,
            Provider = provider.ToLowerInvariant().Trim(),
            ModelName = modelName.Trim(),
            Temperature = Math.Clamp(temperature, 0.0, 1.0),
            FallbackProvider = string.IsNullOrWhiteSpace(fallbackProvider) ? null : fallbackProvider.ToLowerInvariant().Trim(),
            UpdatedAt = DateTime.UtcNow
        };

        // Önce DB'ye kalıcı yaz; başarısızsa çağıran hatayı görür, önbellek eski (doğru) değerde kalır.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<AuthDbContext>();
        if (db != null)
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
                db.AiToolRoutes.Add(new AiToolRoute
                {
                    ToolKey = route.ToolKey,
                    DisplayName = route.DisplayName,
                    Provider = route.Provider,
                    ModelName = route.ModelName,
                    Temperature = route.Temperature,
                    FallbackProvider = route.FallbackProvider,
                    UpdatedAt = route.UpdatedAt
                });
            }
            await db.SaveChangesAsync(ct);
        }

        _routesCache[toolKey] = route;

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
