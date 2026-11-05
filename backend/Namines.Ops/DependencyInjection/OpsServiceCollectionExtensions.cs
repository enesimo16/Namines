using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Namines.BuildingBlocks.Persistence;
using Namines.Ops.Application;
using Namines.Ops.Infrastructure.Persistence;

namespace Namines.Ops.DependencyInjection;

/// <summary>Ops modülünün DI kaydı ve migration zinciri.</summary>
public static class OpsServiceCollectionExtensions
{
    public const int MigrationOrder = 110;

    public static ModuleMigrationDescriptor MigrationDescriptor { get; } = new(
        "Ops",
        MigrationOrder,
        OpsDbContext.SchemaName,
        ModuleMigrationOwnership.OwnsChain,
        connectionString => new OpsDbContext(
            ModuleDbContextRegistration.MigrationOptions<OpsDbContext>(connectionString, OpsDbContext.SchemaName)));

    public static IServiceCollection AddOpsModule(
        this IServiceCollection services,
        Func<IConfiguration, string>? connectionStringFactory = null,
        Action<DbContextOptionsBuilder>? configureDbContext = null)
    {
        services.AddModuleDbContext<OpsDbContext>(OpsDbContext.SchemaName, connectionStringFactory, configureDbContext);
        services.AddScoped<IOpsData>(sp => sp.GetRequiredService<OpsDbContext>());
        services.AddSingleton(MigrationDescriptor);
        services.AddScoped<ITransactionParticipant>(sp => new TransactionParticipant(sp.GetRequiredService<OpsDbContext>()));

        services.AddScoped<IFeedbackService, FeedbackService>();
        services.AddScoped<ISqlExecutionAuditService, SqlExecutionAuditService>();
        services.AddScoped<IOpsRetentionService, OpsRetentionService>();
        services.AddScoped<IOpsAdminService, OpsAdminService>();

        // Bellek içi önbellek taşıdıkları için singleton; DB erişimi kendi kapsamlarında.
        services.AddSingleton<IFeatureFlagService, FeatureFlagService>();
        services.AddSingleton<IAiRouteCatalog, AiRouteCatalog>();
        return services;
    }
}
