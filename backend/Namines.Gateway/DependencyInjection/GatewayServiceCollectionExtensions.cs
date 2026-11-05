using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Namines.BuildingBlocks.Persistence;
using Namines.Gateway.Application;
using Namines.Gateway.Infrastructure.Persistence;

namespace Namines.Gateway.DependencyInjection;

/// <summary>Gateway modülünün DI kaydı ve migration zinciri.</summary>
public static class GatewayServiceCollectionExtensions
{
    public const int MigrationOrder = 100;

    public static ModuleMigrationDescriptor MigrationDescriptor { get; } = new(
        "Gateway",
        MigrationOrder,
        GatewayDbContext.SchemaName,
        ModuleMigrationOwnership.OwnsChain,
        connectionString => new GatewayDbContext(
            ModuleDbContextRegistration.MigrationOptions<GatewayDbContext>(connectionString, GatewayDbContext.SchemaName)));

    public static IServiceCollection AddGatewayModule(
        this IServiceCollection services,
        Func<IConfiguration, string>? connectionStringFactory = null,
        Action<DbContextOptionsBuilder>? configureDbContext = null)
    {
        services.AddModuleDbContext<GatewayDbContext>(GatewayDbContext.SchemaName, connectionStringFactory, configureDbContext);
        services.AddSingleton(MigrationDescriptor);
        services.AddScoped<ITransactionParticipant>(sp => new TransactionParticipant(sp.GetRequiredService<GatewayDbContext>()));

        services.AddScoped<IGatewayAccess, GatewayAccessService>();
        services.AddScoped<IGatewayAuditLog, GatewayAuditLog>();
        services.AddScoped<GatewayKeyService>();
        services.AddScoped<IGatewayKeyService>(sp => sp.GetRequiredService<GatewayKeyService>());
        services.AddScoped<IGatewayMaintenance>(sp => sp.GetRequiredService<GatewayKeyService>());
        return services;
    }
}
