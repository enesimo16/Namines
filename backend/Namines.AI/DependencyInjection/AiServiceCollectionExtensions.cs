using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Namines.AI.Application;
using Namines.AI.Infrastructure.Providers;
using Namines.Core.Interfaces;

namespace Namines.AI.DependencyInjection;

/// <summary>
/// Namines.AI v2 otonom diyalektik orkestrasyon ve çoklu sağlayıcı DI kayıtları.
/// </summary>
public static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddAiModule(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddMemoryCache();

        services.AddSingleton<IChatCompletionProvider>(sp =>
            ChatCompletionProviderFactory.Create(
                sp.GetRequiredService<IConfiguration>(),
                sp.GetService<ILoggerFactory>()?.CreateLogger(nameof(ChatCompletionProviderFactory))));

        services.AddScoped(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            return new AiRetryBudget(new AiRetryPolicy(
                ReadInt(config, "Ai:RateLimitRetry:MaxTotalWaitSeconds", 600),
                ReadInt(config, "Ai:RateLimitRetry:MaxSingleWaitSeconds", 60),
                ReadInt(config, "Ai:RateLimitRetry:MaxAttempts", AiRetryPolicy.DefaultMaxAttempts)));
        });

        services.AddHttpClient<OpenAiCompatibleLlmEngine>(client => client.Timeout = TimeSpan.FromMinutes(5));
        services.AddScoped<ILlmCompletionEngine>(sp => sp.GetRequiredService<OpenAiCompatibleLlmEngine>());
        services.AddScoped<IAgentChatClient>(sp => sp.GetRequiredService<OpenAiCompatibleLlmEngine>());
        services.AddHttpClient<ISpeechTranscriber, GroqSpeechTranscriber>(client => client.Timeout = TimeSpan.FromMinutes(2));

        // Bağlam & dinamik sürü çalışma zamanı + canlı admin telemetrisi
        services.AddSingleton<ISchemaContextEngine, SchemaContextEngine>();
        services.AddSingleton<IAutonomousSwarmTelemetry, AutonomousSwarmTelemetry>();
        services.AddScoped<IAgentSwarmRuntime, AgentSwarmRuntime>();
        services.AddScoped<IAiWorkspaceToolRegistry>(sp => new WorkspaceToolRegistry(
            sp.GetRequiredService<ISchemaContextEngine>(),
            sp.GetService<IDdlGeneratorFactory>(),
            sp.GetService<IApiSpecExtractor>(),
            sp.GetService<IRepositoryScanner>()));

        // Uzman sub-agent'lar (16 ajanlık diyalektik sürü)
        services.AddScoped<CartographerSubAgent>();
        services.AddScoped<TopologicalPlannerSubAgent>();
        services.AddScoped<NslDomainArchitectSubAgent>();
        services.AddScoped<CanvasEvolutionSubAgent>();
        services.AddScoped<RelationWeaverSubAgent>();
        services.AddScoped<AuditAndTemporalWeaverSubAgent>();
        services.AddScoped<ProgrammabilityArchitectSubAgent>();
        services.AddScoped<DeterministicRepairSubAgent>();
        services.AddScoped<SurgicalMutationSubAgent>();
        services.AddScoped<SemanticAndPortabilityCriticSubAgent>();
        services.AddScoped<NormalizationCriticSubAgent>();
        services.AddScoped<MultiTenancyAndRlsCriticSubAgent>();
        services.AddScoped<IndexAndConstraintCriticSubAgent>();
        services.AddScoped<TriggerAndProcedureCriticSubAgent>();
        services.AddScoped<AdversarialDbaSubAgent>();
        services.AddScoped<LifecycleTaskSubAgent>();

        // 3 Katmanlı Diyalektik Orkestrasyon: Master + [+] + [-]
        services.AddScoped<IPositiveOrchestrator, PositiveOrchestrator>();
        services.AddScoped<INegativeOrchestrator, NegativeOrchestrator>();
        services.AddScoped<IMasterOrchestrator, MasterOrchestrator>();

        // Birleşik cephe & yaşam döngüsü sözleşmeleri
        services.AddScoped<UnifiedAiService>();
        services.AddScoped<IAIService>(sp => sp.GetRequiredService<UnifiedAiService>());
        services.AddScoped<IDbaSchemaAnalyzer>(sp => sp.GetRequiredService<UnifiedAiService>());
        services.AddScoped<IAIFactory, UnifiedAiFactory>();
        services.AddScoped<IAiUsageTracker, AiUsageTracker>();

        return services;
    }

    private static int ReadInt(IConfiguration config, string key, int fallback)
        => int.TryParse(config[key], out var parsed) ? parsed : fallback;
}
