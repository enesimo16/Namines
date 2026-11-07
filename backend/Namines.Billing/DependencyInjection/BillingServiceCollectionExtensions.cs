using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Namines.Billing.Application;
using Namines.Billing.Infrastructure.Persistence;
using Namines.BuildingBlocks.Contracts;
using Namines.BuildingBlocks.Persistence;

namespace Namines.Billing.DependencyInjection;

/// <summary>Billing modülünün DI kaydı ve migration zinciri.</summary>
public static class BillingServiceCollectionExtensions
{
    public const int MigrationOrder = 30;

    public static ModuleMigrationDescriptor MigrationDescriptor { get; } = new(
        "Billing",
        MigrationOrder,
        BillingDbContext.SchemaName,
        ModuleMigrationOwnership.OwnsChain,
        connectionString => new BillingDbContext(
            ModuleDbContextRegistration.MigrationOptions<BillingDbContext>(connectionString, BillingDbContext.SchemaName)));

    public static IServiceCollection AddBillingModule(
        this IServiceCollection services,
        Func<IConfiguration, string>? connectionStringFactory = null,
        Action<DbContextOptionsBuilder>? configureDbContext = null,
        bool registerWorkers = true)
    {
        services.AddModuleDbContext<BillingDbContext>(BillingDbContext.SchemaName, connectionStringFactory, configureDbContext);
        services.AddScoped<IBillingData>(sp => sp.GetRequiredService<BillingDbContext>());
        services.AddSingleton(MigrationDescriptor);
        services.AddScoped<ITransactionParticipant>(sp => new TransactionParticipant(sp.GetRequiredService<BillingDbContext>()));

        // Kota hem middleware'de hem Gateway'de hem Flow'da kullanılıyor: tek örnek, tek kural.
        services.AddScoped<AiQuotaService>();
        services.AddScoped<IAiQuota>(sp => sp.GetRequiredService<AiQuotaService>());
        services.AddScoped<IAiQuotaReserver>(sp => sp.GetRequiredService<AiQuotaService>());
        services.AddScoped<IUsageMeter, UsageMeterService>();
        services.AddScoped<BillingAccountService>();
        services.AddScoped<IBillingAccounts>(sp => sp.GetRequiredService<BillingAccountService>());
        services.AddScoped<IBillingMaintenance>(sp => sp.GetRequiredService<BillingAccountService>());
        services.AddScoped<ISubscriptionService, BillingSubscriptionService>();
        services.AddScoped<IStripeWebhookService, StripeWebhookService>();
        services.TryAddScoped<IStripeGateway, StripeGateway>();
        services.TryAddScoped<ISubscriptionCanceller, StripeSubscriptionCanceller>();

        if (registerWorkers)
            services.AddHostedService<SubscriptionCancellationWorker>();
        return services;
    }
}
