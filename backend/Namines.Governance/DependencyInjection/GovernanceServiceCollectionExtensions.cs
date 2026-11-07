using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Namines.BuildingBlocks.Contracts;
using Namines.BuildingBlocks.Persistence;
using Namines.Governance.Application;
using Namines.Governance.Infrastructure.Persistence;

namespace Namines.Governance.DependencyInjection;

/// <summary>Governance modülünün DI kaydı ve migration zinciri.</summary>
public static class GovernanceServiceCollectionExtensions
{
    public const int MigrationOrder = 40;

    public static ModuleMigrationDescriptor MigrationDescriptor { get; } = new(
        "Governance",
        MigrationOrder,
        GovernanceDbContext.SchemaName,
        ModuleMigrationOwnership.OwnsChain,
        connectionString => new GovernanceDbContext(
            ModuleDbContextRegistration.MigrationOptions<GovernanceDbContext>(connectionString, GovernanceDbContext.SchemaName)));

    public static IServiceCollection AddGovernanceModule(
        this IServiceCollection services,
        Func<IConfiguration, string>? connectionStringFactory = null,
        Action<DbContextOptionsBuilder>? configureDbContext = null)
    {
        services.AddModuleDbContext<GovernanceDbContext>(GovernanceDbContext.SchemaName, connectionStringFactory, configureDbContext);
        services.AddScoped<IGovernanceData>(sp => sp.GetRequiredService<GovernanceDbContext>());
        services.AddSingleton(MigrationDescriptor);
        services.AddScoped<ITransactionParticipant>(sp => new TransactionParticipant(sp.GetRequiredService<GovernanceDbContext>()));

        services.AddScoped<IAiPolicies, AiPolicyService>();
        services.AddScoped<GovernanceAuditService>();
        services.AddScoped<IGovernanceAudit>(sp => sp.GetRequiredService<GovernanceAuditService>());
        services.AddScoped<IGovernanceMaintenance>(sp => sp.GetRequiredService<GovernanceAuditService>());
        services.AddScoped<IChangeRequestStats>(sp => sp.GetRequiredService<GovernanceAuditService>());
        services.AddScoped<IGovernanceAdminQueries>(sp => sp.GetRequiredService<GovernanceAuditService>());
        services.AddScoped<ISubscriptionCancellationObserver>(sp => sp.GetRequiredService<GovernanceAuditService>());
        services.AddScoped<IChangeRequestService, ChangeRequestService>();
        return services;
    }
}
