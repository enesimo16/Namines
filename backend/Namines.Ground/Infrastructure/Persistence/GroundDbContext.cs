using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Ground.Configurations;

namespace Namines.Ground.Infrastructure.Persistence;

/// <summary>
/// Namines Ground modülünün izole EF Core <see cref="DbContext"/>'i
/// (Kritik Kontrol #5: EF Core Migration Isolation).
/// </summary>
public sealed class GroundDbContext : DbContext
{
    public const string SchemaName = GroundModelConfiguration.DefaultSchema;
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public GroundDbContext(DbContextOptions<GroundDbContext> options) : base(options)
    {
    }

    public DbSet<GroundDatabase> GroundDatabases => Set<GroundDatabase>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var isRelationalSchemasSupported = Database.ProviderName?.Contains("Npgsql", System.StringComparison.OrdinalIgnoreCase) == true;
        if (isRelationalSchemasSupported)
            modelBuilder.HasDefaultSchema(SchemaName);
        GroundModelConfiguration.Configure(
            modelBuilder,
            schema: isRelationalSchemasSupported ? SchemaName : null,
            configureCrossModuleForeignKeys: false);
    }
}
