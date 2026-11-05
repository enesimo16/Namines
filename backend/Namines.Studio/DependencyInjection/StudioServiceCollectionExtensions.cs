using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.BuildingBlocks.Persistence;
using Namines.BuildingBlocks.Security;
using Namines.Studio.Application;
using Namines.Studio.Infrastructure.Persistence;

namespace Namines.Studio.DependencyInjection;

/// <summary>Studio modülünün DI kaydı ve migration zinciri.</summary>
public static class StudioServiceCollectionExtensions
{
    public const int MigrationOrder = 20;

    public static ModuleMigrationDescriptor MigrationDescriptor { get; } = new(
        "Studio",
        MigrationOrder,
        StudioDbContext.SchemaName,
        ModuleMigrationOwnership.OwnsChain,
        connectionString => new StudioDbContext(
            ModuleDbContextRegistration.MigrationOptions<StudioDbContext>(connectionString, StudioDbContext.SchemaName)));

    public static IServiceCollection AddStudioModule(
        this IServiceCollection services,
        Func<IConfiguration, string>? connectionStringFactory = null,
        Action<DbContextOptionsBuilder>? configureDbContext = null)
    {
        services.AddModuleDbContext<StudioDbContext>(StudioDbContext.SchemaName, connectionStringFactory, configureDbContext);
        services.AddScoped<IStudioData>(sp => sp.GetRequiredService<StudioDbContext>());
        services.AddSingleton(MigrationDescriptor);
        services.AddScoped<ITransactionParticipant>(sp => new TransactionParticipant(sp.GetRequiredService<StudioDbContext>()));

        services.AddScoped<StudioProjectService>();
        services.AddScoped<IStudioProjects>(sp => sp.GetRequiredService<StudioProjectService>());
        services.AddScoped<IStudioAdminQueries>(sp => sp.GetRequiredService<StudioProjectService>());

        services.AddScoped<StudioBranchService>();
        services.AddScoped<IStudioBranches>(sp => sp.GetRequiredService<StudioBranchService>());
        services.AddScoped<IBranchService>(sp => sp.GetRequiredService<StudioBranchService>());

        services.AddScoped<CrossDatabaseRelationService>();
        services.AddScoped<ICrossDatabaseRelationService>(sp => sp.GetRequiredService<CrossDatabaseRelationService>());
        services.AddScoped<IStudioRelations>(sp => sp.GetRequiredService<CrossDatabaseRelationService>());

        services.AddScoped<IProjectWorkspaceService, ProjectWorkspaceService>();
        services.AddScoped<IShareService, ShareService>();
        services.AddScoped<IStudioApplicationService, StudioApplicationService>();

        services.AddScoped<TenantProjectAuthorityService>();
        services.AddScoped<ITenantProjectAuthority>(sp => sp.GetRequiredService<TenantProjectAuthorityService>());
        services.AddScoped<IProjectConnectionSecretProvider>(sp => sp.GetRequiredService<TenantProjectAuthorityService>());
        services.AddScoped<IConnectionSecretRotation, ConnectionSecretRotation>();
        return services;
    }
}
