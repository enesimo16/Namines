using System;
using Namines.Core.Github;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Namines.Core.Interfaces;
using Namines.Infrastructure;
using Namines.Infrastructure.AI;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Infrastructure.Generators.EfCoreGenerator;
using Namines.Infrastructure.Generators.Eject;
using Namines.Infrastructure.Generators.PrismaGenerator;
using Namines.Infrastructure.Generators.DocumentationGenerator;
using Namines.Infrastructure.Realtime;
using Namines.Infrastructure.Services;
using Namines.API.Services;
using StackExchange.Redis;

namespace Namines.API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNaminesServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<GroqAIService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        
        services.AddHttpClient<OllamaAIService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        // NOT: AddHttpClient<T> zaten T'yi typed-client olarak kaydeder (IHttpClientFactory
        // üzerinden HttpClient enjekte edilir). Ayrıca AddScoped<T> yazmak bu kaydı ezer ve
        // constructor'daki HttpClient DI'dan çözülemez → çalışmaz. Bu yüzden eklenmez.

        // IAIFactory as Scoped (resolved per request)
        services.AddScoped<IAIFactory>(sp => new AIFactory(new IAIService[]
        {
            sp.GetRequiredService<GroqAIService>(),
            sp.GetRequiredService<OllamaAIService>()
        }));
        
        // Default IAIService = Groq (for backward compat with other controllers)
        services.AddScoped<IAIService>(sp => sp.GetRequiredService<GroqAIService>());

        services.AddScoped<ILinterService, LinterService>();
        // Singleton: tamamen durumsuz bir fabrika. Scoped kalsaydı, onu tüketen
        // singleton'lar (branch veritabanı sağlayıcısı, arka plan süpürücüsü)
        // tutsak bağımlılık üretirdi.
        services.AddSingleton<IDdlGeneratorFactory, DdlGeneratorFactory>();
        services.AddScoped<IEfCoreGenerator, EfCoreGeneratorService>();
        services.AddScoped<IPrismaGenerator, PrismaGeneratorService>();
        // Singleton: üreticiler durumsuz, kayıt defteri de öyle.
        services.AddSingleton<IEjectGeneratorRegistry, EjectGeneratorRegistry>();

        // Namines Bot (11 §7). HttpClient fabrikadan alınıyor: her çağrıda yeni bir
        // HttpClient üretmek soket tükenmesine, tek bir statik örnek ise DNS
        // değişikliklerini görmemeye yol açar.
        // Token deposu SINGLETON, istemci transient: AddHttpClient her çözümlemede
        // yeni bir istemci verir ve önbellek onun içinde olsaydı hiç çalışmazdı.
        services.AddSingleton<GithubInstallationTokenCache>();
        services.AddHttpClient<IGithubClient, GithubClient>();
        services.AddScoped<IGithubBotService, GithubBotService>();
        // Singleton: tek bir DockerClient tutar ve durumu container etiketlerinde
        // yaşar, bellekte değil — süreç yeniden başlasa bile branch veritabanları
        // bulunabilir kalır.
        services.AddSingleton<IBranchDatabaseProvisioner, BranchDatabaseProvisioner>();
        services.AddScoped<IDbPrivilegeInspector, DbPrivilegeInspector>();
        services.AddScoped<IDocumentationGenerator, DocumentationGeneratorService>();
        
        services.AddSingleton<DockerJobManager>();
        // Sweeper (Infrastructure) canlı işleri bu arayüz üzerinden görür; aynı singleton
        // örneğine çözülmeli, yoksa boş bir kayıt okur ve çalışan container'ları siler.
        services.AddSingleton<ISandboxJobRegistry>(sp => sp.GetRequiredService<DockerJobManager>());
        services.AddScoped<IDockerService, DockerBackupService>();
        services.AddScoped<ICoderAIPackager, CoderAIPackagerService>();
        services.AddScoped<IDatabaseExecutor, DatabaseExecutorService>();
        
        // AI DBA (Otonom Performans Danışmanı) registration
        services.AddScoped<IAIDbaService, AIDbaService>();
        
        // Smart Seeding (Domain-Aware Test Verisi Üretimi) registration
        services.AddScoped<ISmartSeedService, SmartSeedService>();

        // AI Migration Engine (Zaman Makinesi) registration
        services.AddScoped<IMigrationService, MigrationService>();
        services.AddScoped<IBranchTestRunner, BranchTestRunnerService>();
        services.AddScoped<IGatewayService, GatewayService>();
        // SSRF politikası — üretimde daima sıkı; yalnızca Development + açık bayrakla gevşer.
        services.AddSingleton<Namines.Core.Security.IDbHostAccessPolicy, Namines.Infrastructure.Security.DbHostAccessPolicy>();

        // Arka Plan Docker Sweeper (Sunucu Kilitlenmesi Önleyici)
        services.AddHostedService<DockerSweeperBackgroundService>();

        // Otonom Full-Stack Scaffolder & SDK Generator registration
        services.AddScoped<IScaffolderService, ScaffolderService>();

        // Semantic Cache Service registration
        services.AddScoped<SemanticCacheService>();

        // Canlı DB tersine mühendislik (INFORMATION_SCHEMA)
        services.AddScoped<IDbIntrospectionService, DbIntrospectionService>();

        // ── Presence deposu (CanvasHub'ın oda üyeliği takibi) ───────────────────
        // Redis yapılandırılmışsa çok instance'lı dağıtımda doğru çalışan Redis
        // implementasyonu; aksi halde tek instance için bellek-içi implementasyon.
        // Aynı IConnectionMultiplexer, aşağıda SignalR backplane'i tarafından da
        // yeniden kullanılabilir (Program.cs bunu ayrıca yapılandırır çünkü
        // AddStackExchangeRedis kendi bağlantısını kurar — burada sadece presence
        // deposu için singleton bir multiplexer kaydediyoruz).
        var redisConnectionString = configuration["Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(redisConnectionString));
            services.AddSingleton<IPresenceStore, RedisPresenceStore>();
        }
        else
        {
            services.AddSingleton<IPresenceStore, InMemoryPresenceStore>();
        }

        return services;
    }
}
