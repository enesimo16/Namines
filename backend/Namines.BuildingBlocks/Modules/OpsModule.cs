using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models;
using Namines.Core.Models.Auth;

namespace Namines.BuildingBlocks.Modules;

/// <summary>
/// Ops (Özellik Bayrakları, AI Model Yönlendirme ve Kullanıcı Geri Bildirimleri) modülünün
/// dışa açtığı resmi sözleşme (Order: 110, Şema: <c>ops</c>).
/// </summary>
public interface IOpsModule
{
    OpsDbContext DbContext { get; }
}

public sealed class OpsModule : IOpsModule
{
    public OpsModule(OpsDbContext dbContext)
    {
        DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public OpsDbContext DbContext { get; }
}

public static class OpsModelConfiguration
{
    public const string DefaultSchemaName = "ops";

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
        }

        builder.Entity<Feedback>(entity =>
        {
            entity.ToTable("Feedbacks", schema);
            entity.HasKey(f => f.Id);

            if (configureCrossModuleForeignKeys)
            {
                entity.HasOne<ApplicationUser>()
                    .WithMany()
                    .HasForeignKey(f => f.UserId)
                    .OnDelete(DeleteBehavior.SetNull);
            }
        });

        builder.Entity<AdminFeatureFlag>(entity =>
        {
            entity.ToTable("FeatureFlags", schema);
            entity.HasKey(f => f.FeatureKey);
            entity.Property(f => f.FeatureKey).HasMaxLength(64);
            entity.Property(f => f.DisplayName).HasMaxLength(128);
            entity.Property(f => f.Description).HasMaxLength(512);
            entity.Property(f => f.Category).HasMaxLength(64);
            entity.Property(f => f.Mode).HasMaxLength(32);
            entity.Property(f => f.NoticeMessage).HasMaxLength(512);
        });

        builder.Entity<AiToolRoute>(entity =>
        {
            entity.ToTable("AiToolRoutes", schema);
            entity.HasKey(r => r.ToolKey);
            entity.Property(r => r.ToolKey).HasMaxLength(64);
            entity.Property(r => r.DisplayName).HasMaxLength(128);
            entity.Property(r => r.Provider).HasMaxLength(64);
            entity.Property(r => r.ModelName).HasMaxLength(128);
            entity.Property(r => r.FallbackProvider).HasMaxLength(64);
        });
    }
}

public sealed class OpsDbContext : DbContext
{
    public const string DefaultSchema = OpsModelConfiguration.DefaultSchemaName;
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public OpsDbContext(DbContextOptions<OpsDbContext> options) : base(options)
    {
    }

    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<AdminFeatureFlag> FeatureFlags => Set<AdminFeatureFlag>();
    public DbSet<AiToolRoute> AiToolRoutes => Set<AiToolRoute>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var isRelational = Database.IsRelational();
        var schema = isRelational && Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
            ? DefaultSchema
            : null;

        if (schema is not null)
            modelBuilder.HasDefaultSchema(schema);

        OpsModelConfiguration.Configure(modelBuilder, schema, configureCrossModuleForeignKeys: false);
    }
}
