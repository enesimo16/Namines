using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Modules;
using Namines.BuildingBlocks.Persistence;

namespace Namines.BuildingBlocks.DependencyInjection;

/// <summary>
/// Modüler monolit paylaşılan altyapı bileşenlerini (migration orkestratörü,
/// paylaşılan transaction yöneticisi ve çekirdek 7 modülün izole DbContext/Migrator/Facade kayıtları)
/// DI konteynerine kaydeder.
/// </summary>
public static class BuildingBlocksServiceCollectionExtensions
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=namines_control;Username=postgres;Password=postgres";

    public static IServiceCollection AddNaminesBuildingBlocks(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDbContext = null)
    {
        services.TryAddScoped<ISharedTransactionManager, SharedTransactionManager>();
        services.TryAddScoped<ModuleMigrationOrchestrator>();

        RegisterModule<IdentityModuleDbContext, IIdentityModule, IdentityModule>(
            services, configureDbContext, order: 10, moduleName: "Identity",
            schemaName: IdentityModuleDbContext.DefaultSchema,
            historyTable: IdentityModuleDbContext.MigrationsHistoryTableName,
            db => new IdentityModule(db));

        RegisterModule<StudioDbContext, IStudioModule, StudioModule>(
            services, configureDbContext, order: 20, moduleName: "Studio",
            schemaName: StudioDbContext.DefaultSchema,
            historyTable: StudioDbContext.MigrationsHistoryTableName,
            db => new StudioModule(db, new StudioApplicationService(db)));

        services.TryAddScoped<IStudioApplicationService>(sp =>
            sp.GetRequiredService<IStudioModule>().Projects);

        RegisterModule<BillingDbContext, IBillingModule, BillingModule>(
            services, configureDbContext, order: 30, moduleName: "Billing",
            schemaName: BillingDbContext.DefaultSchema,
            historyTable: BillingDbContext.MigrationsHistoryTableName,
            db => new BillingModule(db));

        RegisterModule<GovernanceDbContext, IGovernanceModule, GovernanceModule>(
            services, configureDbContext, order: 40, moduleName: "Governance",
            schemaName: GovernanceDbContext.DefaultSchema,
            historyTable: GovernanceDbContext.MigrationsHistoryTableName,
            db => new GovernanceModule(db));

        RegisterModule<HubDbContext, IHubModule, HubModule>(
            services, configureDbContext, order: 90, moduleName: "Hub",
            schemaName: HubDbContext.DefaultSchema,
            historyTable: HubDbContext.MigrationsHistoryTableName,
            db => new HubModule(db, new HubApplicationService(db)));

        services.TryAddScoped<IHubApplicationService>(sp =>
            sp.GetRequiredService<IHubModule>().Repositories);

        RegisterModule<GatewayDbContext, IGatewayModule, GatewayModule>(
            services, configureDbContext, order: 100, moduleName: "Gateway",
            schemaName: GatewayDbContext.DefaultSchema,
            historyTable: GatewayDbContext.MigrationsHistoryTableName,
            db => new GatewayModule(db));

        RegisterModule<OpsDbContext, IOpsModule, OpsModule>(
            services, configureDbContext, order: 110, moduleName: "Ops",
            schemaName: OpsDbContext.DefaultSchema,
            historyTable: OpsDbContext.MigrationsHistoryTableName,
            db => new OpsModule(db));

        return services;
    }

    private static void RegisterModule<TDbContext, TModuleInterface, TModuleImplementation>(
        IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDbContext,
        int order,
        string moduleName,
        string schemaName,
        string historyTable,
        Func<TDbContext, TModuleInterface> moduleFactory)
        where TDbContext : DbContext
        where TModuleInterface : class
        where TModuleImplementation : class, TModuleInterface
    {
        if (configureDbContext is not null)
        {
            services.AddDbContext<TDbContext>(configureDbContext);
        }
        else
        {
            services.AddDbContext<TDbContext>((sp, options) =>
            {
                var config = sp.GetService<IConfiguration>();
                var connStr = config?.GetConnectionString("DefaultConnection") ?? FallbackConnectionString;
                options.UseNpgsql(
                    connStr,
                    npgsql => npgsql.MigrationsHistoryTable(historyTable, schemaName));
            });
        }

        services.AddSingleton<IModuleDbContextMigrator>(sp =>
            new ModuleDbContextMigrator<TDbContext>(
                sp.GetRequiredService<IServiceScopeFactory>(),
                order: order,
                moduleName: moduleName,
                schemaName: schemaName));

        services.TryAddScoped<TModuleInterface>(sp =>
            moduleFactory(sp.GetRequiredService<TDbContext>()));
    }
}
