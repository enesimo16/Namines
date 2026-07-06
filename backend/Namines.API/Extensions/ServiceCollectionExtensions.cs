using System;
using Microsoft.Extensions.DependencyInjection;
using Namines.Core.Interfaces;
using Namines.Infrastructure;
using Namines.Infrastructure.AI;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Infrastructure.Generators.EfCoreGenerator;
using Namines.Infrastructure.Generators.DocumentationGenerator;
using Namines.Infrastructure.Services;
using Namines.API.Services;

namespace Namines.API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNaminesServices(this IServiceCollection services)
    {
        services.AddHttpClient<GroqAIService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        
        services.AddHttpClient<OllamaAIService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        
        // Scoped: a new instance per HTTP request scope
        services.AddScoped<GroqAIService>();
        services.AddScoped<OllamaAIService>();
        
        // IAIFactory as Scoped (resolved per request)
        services.AddScoped<IAIFactory>(sp => new AIFactory(new IAIService[]
        {
            sp.GetRequiredService<GroqAIService>(),
            sp.GetRequiredService<OllamaAIService>()
        }));
        
        // Default IAIService = Groq (for backward compat with other controllers)
        services.AddScoped<IAIService>(sp => sp.GetRequiredService<GroqAIService>());

        services.AddScoped<ILinterService, LinterService>();
        services.AddScoped<IDdlGeneratorFactory, DdlGeneratorFactory>();
        services.AddScoped<IEfCoreGenerator, EfCoreGeneratorService>();
        services.AddScoped<IDocumentationGenerator, DocumentationGeneratorService>();
        
        services.AddSingleton<DockerJobManager>();
        services.AddScoped<IDockerService, DockerBackupService>();
        services.AddScoped<ICoderAIPackager, CoderAIPackagerService>();
        services.AddScoped<IDatabaseExecutor, DatabaseExecutorService>();
        
        // AI DBA (Otonom Performans Danışmanı) registration
        services.AddScoped<IAIDbaService, AIDbaService>();
        
        // Smart Seeding (Domain-Aware Test Verisi Üretimi) registration
        services.AddScoped<ISmartSeedService, SmartSeedService>();

        // AI Migration Engine (Zaman Makinesi) registration
        services.AddScoped<IMigrationService, MigrationService>();

        // Arka Plan Docker Sweeper (Sunucu Kilitlenmesi Önleyici)
        services.AddHostedService<DockerSweeperBackgroundService>();

        // Otonom Full-Stack Scaffolder & SDK Generator registration
        services.AddScoped<IScaffolderService, ScaffolderService>();

        // Semantic Cache Service registration
        services.AddScoped<SemanticCacheService>();
        
        return services;
    }
}
