using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Namines.BuildingBlocks.Persistence;
using Namines.Hub.Application;
using Namines.Hub.Infrastructure.Persistence;

namespace Namines.Hub.DependencyInjection;

/// <summary>Hub modülünün DI kaydı ve migration zinciri.</summary>
public static class HubServiceCollectionExtensions
{
    public const int MigrationOrder = 90;

    public static ModuleMigrationDescriptor MigrationDescriptor { get; } = new(
        "Hub",
        MigrationOrder,
        HubDbContext.SchemaName,
        ModuleMigrationOwnership.OwnsChain,
        connectionString => new HubDbContext(
            ModuleDbContextRegistration.MigrationOptions<HubDbContext>(connectionString, HubDbContext.SchemaName)));

    public static IServiceCollection AddHubModule(
        this IServiceCollection services,
        Func<IConfiguration, string>? connectionStringFactory = null,
        Action<DbContextOptionsBuilder>? configureDbContext = null)
    {
        services.AddModuleDbContext<HubDbContext>(HubDbContext.SchemaName, connectionStringFactory, configureDbContext);
        services.AddScoped<IHubData>(sp => sp.GetRequiredService<HubDbContext>());
        services.AddSingleton(MigrationDescriptor);
        services.AddScoped<ITransactionParticipant>(sp => new TransactionParticipant(sp.GetRequiredService<HubDbContext>()));

        services.AddScoped<HubApplicationService>();
        services.AddScoped<IHubApplicationService>(sp => sp.GetRequiredService<HubApplicationService>());
        services.AddScoped<Namines.BuildingBlocks.Contracts.IProjectReadModelWriter>(sp => sp.GetRequiredService<HubApplicationService>());
        services.AddScoped<IHubRepositoryService, HubRepositoryService>();
        services.AddScoped<HubIssueService>();
        services.AddScoped<IHubIssueService>(sp => sp.GetRequiredService<HubIssueService>());
        services.AddScoped<IHubIssueQueries>(sp => sp.GetRequiredService<HubIssueService>());
        return services;
    }
}
