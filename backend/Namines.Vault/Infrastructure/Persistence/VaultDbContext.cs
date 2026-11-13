using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Vault.Configurations;
using Namines.Vault.Domain;

namespace Namines.Vault.Infrastructure.Persistence;

/// <summary>
/// Namines Vault modülünün izole EF Core <see cref="DbContext"/>'i
/// (Kritik Kontrol #5: EF Core Migration Isolation).
///
/// <b>İzolasyon sınırları:</b>
/// <list type="bullet">
/// <item>Yalnızca <c>namines_vault</c> şemasındaki varlıkları (<see cref="VaultBackup"/>,
/// <see cref="VaultRestore"/>, <see cref="VaultSchedule"/>) izler.</item>
/// <item>Diğer modüllerin varlıklarını (<c>CloudProject</c>, <c>ApplicationUser</c>) EF modeline
/// dahil etmez; şemalar arası FK bütünlüğü PostgreSQL DDL seviyesinde korunur.</item>
/// </list>
/// </summary>
internal sealed class VaultDbContext : DbContext, Namines.Vault.Application.IVaultData
{
    public const string SchemaName = VaultModelConfiguration.DefaultSchema;
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public VaultDbContext(DbContextOptions<VaultDbContext> options) : base(options)
    {
    }

    public DbSet<VaultBackup> VaultBackups => Set<VaultBackup>();
    public DbSet<VaultRestore> VaultRestores => Set<VaultRestore>();
    public DbSet<VaultSchedule> VaultSchedules => Set<VaultSchedule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var isRelationalSchemasSupported = Database.ProviderName?.Contains("Npgsql", System.StringComparison.OrdinalIgnoreCase) == true;
        if (isRelationalSchemasSupported)
            modelBuilder.HasDefaultSchema(SchemaName);
        VaultModelConfiguration.Configure(
            modelBuilder,
            schema: isRelationalSchemasSupported ? SchemaName : null,
            configureCrossModuleForeignKeys: false);

    }
}

internal sealed class VaultDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<VaultDbContext>
{
    public VaultDbContext CreateDbContext(string[] args) =>
        new(Namines.BuildingBlocks.Persistence.ModuleDbContextRegistration.MigrationOptions<VaultDbContext>(
            Namines.BuildingBlocks.Persistence.ModuleDbContextRegistration.DesignTimeConnectionString(), VaultDbContext.SchemaName));
}
