using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Desk.Configurations;

namespace Namines.Desk.Infrastructure.Persistence;

/// <summary>
/// Namines Desk modülünün izole EF Core <see cref="DbContext"/>'i
/// (Kritik Kontrol #5: EF Core Migration Isolation).
/// </summary>
public sealed class DeskDbContext : DbContext
{
    public const string SchemaName = DeskModelConfiguration.DefaultSchema;
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public DeskDbContext(DbContextOptions<DeskDbContext> options) : base(options)
    {
    }

    public DbSet<DeskHandoffToken> DeskHandoffTokens => Set<DeskHandoffToken>();
    public DbSet<SqlQueryHistoryEntry> SqlQueryHistory => Set<SqlQueryHistoryEntry>();
    public DbSet<SavedQuery> SavedQueries => Set<SavedQuery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var isRelationalSchemasSupported = Database.ProviderName?.Contains("Npgsql", System.StringComparison.OrdinalIgnoreCase) == true;
        if (isRelationalSchemasSupported)
            modelBuilder.HasDefaultSchema(SchemaName);
        DeskModelConfiguration.Configure(
            modelBuilder,
            schema: isRelationalSchemasSupported ? SchemaName : null,
            configureCrossModuleForeignKeys: false);
    }
}
