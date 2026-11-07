using Microsoft.EntityFrameworkCore;
using Namines.Core.Models;
using Namines.Flow.Configurations;

namespace Namines.Flow.Infrastructure.Persistence;

/// <summary>
/// Namines Flow modülünün izole EF Core <see cref="DbContext"/>'i
/// (Kritik Kontrol #5: EF Core Migration Isolation).
/// </summary>
internal sealed class FlowDbContext : DbContext, Namines.Flow.Application.IFlowData
{
    public const string SchemaName = FlowModelConfiguration.DefaultSchema;
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public FlowDbContext(DbContextOptions<FlowDbContext> options) : base(options)
    {
    }

    public DbSet<AutomationRule> AutomationRules => Set<AutomationRule>();
    public DbSet<AutomationAction> AutomationActions => Set<AutomationAction>();
    public DbSet<AutomationRunLog> AutomationRunLogs => Set<AutomationRunLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var isRelationalSchemasSupported = Database.ProviderName?.Contains("Npgsql", System.StringComparison.OrdinalIgnoreCase) == true;
        if (isRelationalSchemasSupported)
            modelBuilder.HasDefaultSchema(SchemaName);
        FlowModelConfiguration.Configure(
            modelBuilder,
            schema: isRelationalSchemasSupported ? SchemaName : null,
            configureCrossModuleForeignKeys: false);

    }
}

internal sealed class FlowDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<FlowDbContext>
{
    public FlowDbContext CreateDbContext(string[] args) =>
        new(Namines.BuildingBlocks.Persistence.ModuleDbContextRegistration.MigrationOptions<FlowDbContext>(
            Namines.BuildingBlocks.Persistence.ModuleDbContextRegistration.DesignTimeConnectionString(), FlowDbContext.SchemaName));
}
