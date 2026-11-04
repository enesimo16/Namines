using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;

namespace Namines.BuildingBlocks.Modules;

/// <summary>
/// Gateway (REST/Query API Anahtarları, Tablo İzinleri ve Denetim İzi) modülünün
/// dışa açtığı resmi sözleşme (Order: 100, Şema: <c>gateway</c>).
/// </summary>
public interface IGatewayModule
{
    GatewayDbContext DbContext { get; }
}

public sealed class GatewayModule : IGatewayModule
{
    public GatewayModule(GatewayDbContext dbContext)
    {
        DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public GatewayDbContext DbContext { get; }
}

public static class GatewayModelConfiguration
{
    public const string DefaultSchemaName = "gateway";

    public static void Configure(
        ModelBuilder builder,
        string? schema = DefaultSchemaName,
        bool configureCrossModuleForeignKeys = false)
    {
        if (!configureCrossModuleForeignKeys)
        {
            builder.Ignore<ApplicationUser>();
            builder.Ignore<Organization>();
            builder.Ignore<OrganizationMember>();
            builder.Ignore<CloudProject>();
        }

        builder.Entity<GatewayApiKey>(entity =>
        {
            entity.ToTable("GatewayApiKeys", schema);
            entity.HasKey(k => k.Id);
            entity.HasIndex(k => k.Prefix);
            entity.HasIndex(k => k.KeyHash).IsUnique();

            if (configureCrossModuleForeignKeys)
            {
                entity.HasOne(k => k.Project)
                    .WithMany()
                    .HasForeignKey(k => k.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(k => k.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(k => k.CreatedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);
            }
            else
            {
                entity.Ignore(k => k.Project);
                entity.Ignore(k => k.CreatedByUser);
            }
        });

        builder.Entity<GatewayTablePermission>(entity =>
        {
            entity.ToTable("GatewayTablePermissions", schema);
            entity.HasKey(p => p.Id);
            entity.HasIndex(p => new { p.ProjectId, p.TableName }).IsUnique();

            if (configureCrossModuleForeignKeys)
            {
                entity.HasOne(p => p.Project)
                    .WithMany()
                    .HasForeignKey(p => p.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
            }
            else
            {
                entity.Ignore(p => p.Project);
            }
        });

        builder.Entity<GatewayAuditEntry>(entity =>
        {
            entity.ToTable("GatewayAuditEntries", schema);
            entity.HasKey(a => a.Id);
            entity.HasIndex(a => new { a.ProjectId, a.CreatedAt }).IsDescending(false, true);
        });
    }
}

public sealed class GatewayDbContext : DbContext
{
    public const string DefaultSchema = GatewayModelConfiguration.DefaultSchemaName;
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public GatewayDbContext(DbContextOptions<GatewayDbContext> options) : base(options)
    {
    }

    public DbSet<GatewayApiKey> GatewayApiKeys => Set<GatewayApiKey>();
    public DbSet<GatewayTablePermission> GatewayTablePermissions => Set<GatewayTablePermission>();
    public DbSet<GatewayAuditEntry> GatewayAuditEntries => Set<GatewayAuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var isRelational = Database.IsRelational();
        var schema = isRelational && Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
            ? DefaultSchema
            : null;

        if (schema is not null)
            modelBuilder.HasDefaultSchema(schema);

        GatewayModelConfiguration.Configure(modelBuilder, schema, configureCrossModuleForeignKeys: false);
    }
}
