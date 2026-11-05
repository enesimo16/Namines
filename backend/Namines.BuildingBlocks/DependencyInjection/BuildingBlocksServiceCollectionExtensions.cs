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
/// Modüler monolit paylaşılan altyapı bileşenlerini (paylaşılan transaction yöneticisi ve çekirdek
/// 7 modülün izole DbContext/Facade kayıtları)
/// DI konteynerine kaydeder.
/// </summary>
public static class BuildingBlocksServiceCollectionExtensions
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=namines_control;Username=postgres;Password=postgres";

    public static IServiceCollection AddNaminesBuildingBlocks(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDbContext = null,
        Func<IConfiguration, string>? connectionStringFactory = null)
    {
        services.TryAddScoped<ISharedTransactionManager, SharedTransactionManager>();

        RegisterModule<IdentityModuleDbContext, IIdentityModule, IdentityModule>(
            services, configureDbContext, connectionStringFactory,
            historyTable: IdentityModuleDbContext.MigrationsHistoryTableName,
            schemaName: IdentityModuleDbContext.DefaultSchema,
            db => new IdentityModule(db));

        RegisterModule<BillingDbContext, IBillingModule, BillingModule>(
            services, configureDbContext, connectionStringFactory,
            historyTable: BillingDbContext.MigrationsHistoryTableName,
            schemaName: BillingDbContext.DefaultSchema,
            db => new BillingModule(db));

        RegisterModule<GovernanceDbContext, IGovernanceModule, GovernanceModule>(
            services, configureDbContext, connectionStringFactory,
            historyTable: GovernanceDbContext.MigrationsHistoryTableName,
            schemaName: GovernanceDbContext.DefaultSchema,
            db => new GovernanceModule(db));

        RegisterModule<GatewayDbContext, IGatewayModule, GatewayModule>(
            services, configureDbContext, connectionStringFactory,
            historyTable: GatewayDbContext.MigrationsHistoryTableName,
            schemaName: GatewayDbContext.DefaultSchema,
            db => new GatewayModule(db));

        return services;
    }

    private static void RegisterModule<TDbContext, TModuleInterface, TModuleImplementation>(
        IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDbContext,
        Func<IConfiguration, string>? connectionStringFactory,
        string historyTable,
        string schemaName,
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
                // Studio/Hub artık istek yolunda: AuthDbContext ile AYNI dizge = aynı havuz. Ayrı dizge,
                // ayrı (100'lük) havuz demek ve anonim yükte PostgreSQL "too many clients" veriyordu.
                var connStr = config is not null && connectionStringFactory is not null
                    ? connectionStringFactory(config)
                    : config?.GetConnectionString("DefaultConnection") ?? FallbackConnectionString;
                options.UseNpgsql(
                    connStr,
                    npgsql => npgsql.MigrationsHistoryTable(historyTable, schemaName));
            });
        }

        services.TryAddScoped<TModuleInterface>(sp =>
            moduleFactory(sp.GetRequiredService<TDbContext>()));
    }
}
