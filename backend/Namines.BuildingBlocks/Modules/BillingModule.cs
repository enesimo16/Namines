using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;

namespace Namines.BuildingBlocks.Modules;

/// <summary>
/// Billing modülünün dışa açtığı resmi sözleşme (Order: 30, Şema: <c>billing</c>).
/// </summary>
public interface IBillingModule
{
    BillingDbContext DbContext { get; }
}

public sealed class BillingModule : IBillingModule
{
    public BillingModule(BillingDbContext dbContext)
    {
        DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public BillingDbContext DbContext { get; }
}

public static class BillingModelConfiguration
{
    public const string DefaultSchemaName = "billing";

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

        builder.Entity<UserAIPolicy>(entity =>
        {
            entity.ToTable("UserAIPolicies", schema);
            entity.HasKey(p => p.UserId);
            if (configureCrossModuleForeignKeys)
            {
                entity.HasOne(p => p.User)
                    .WithOne()
                    .HasForeignKey<UserAIPolicy>(p => p.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            }
            else
            {
                entity.Ignore(p => p.User);
            }
        });

        builder.Entity<UserAIQuota>(entity =>
        {
            entity.ToTable("UserAIQuotas", schema);
            entity.HasKey(q => q.UserId);
            if (configureCrossModuleForeignKeys)
            {
                entity.HasOne(q => q.User)
                    .WithOne()
                    .HasForeignKey<UserAIQuota>(q => q.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            }
            else
            {
                entity.Ignore(q => q.User);
            }
        });

        builder.Entity<GlobalAiUsage>(entity =>
        {
            entity.ToTable("GlobalAiUsages", schema);
            entity.HasKey(g => g.Id);
            entity.HasIndex(g => g.Date).IsUnique();
        });

        builder.Entity<OrgAiUsage>(entity =>
        {
            entity.ToTable("OrgAiUsages", schema);
            entity.HasKey(o => o.Id);
            entity.HasIndex(o => new { o.OrganizationId, o.Date }).IsUnique();
            if (configureCrossModuleForeignKeys)
            {
                entity.HasOne<Organization>()
                    .WithMany()
                    .HasForeignKey(o => o.OrganizationId)
                    .OnDelete(DeleteBehavior.Cascade);
            }
        });

        builder.Entity<UsageEvent>(entity =>
        {
            entity.ToTable("UsageEvents", schema);
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.BillingPeriod, e.Resource });
            entity.HasIndex(e => new { e.Resource, e.OccurredAt });
            entity.Property(e => e.Quantity).HasPrecision(18, 4);
            if (configureCrossModuleForeignKeys)
            {
                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.SetNull);
            }
            else
            {
                entity.Ignore(e => e.User);
            }
        });

        builder.Entity<UserBillingSettings>(entity =>
        {
            entity.ToTable("UserBillingSettings", schema);
            entity.HasKey(s => s.UserId);
            entity.Property(s => s.MonthlyCapUsd).HasPrecision(18, 2);
            if (configureCrossModuleForeignKeys)
            {
                entity.HasOne(s => s.User)
                    .WithMany()
                    .HasForeignKey(s => s.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            }
            else
            {
                entity.Ignore(s => s.User);
            }
        });

        builder.Entity<StripeWebhookEvent>(entity =>
        {
            entity.ToTable("StripeWebhookEvents", schema);
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.StripeCustomerId, e.EventCreatedAt });
        });
    }
}

public sealed class BillingDbContext : DbContext
{
    public const string DefaultSchema = BillingModelConfiguration.DefaultSchemaName;
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public BillingDbContext(DbContextOptions<BillingDbContext> options) : base(options)
    {
    }

    public DbSet<UserAIPolicy> UserAIPolicies => Set<UserAIPolicy>();
    public DbSet<UserAIQuota> UserAIQuotas => Set<UserAIQuota>();
    public DbSet<GlobalAiUsage> GlobalAiUsages => Set<GlobalAiUsage>();
    public DbSet<OrgAiUsage> OrgAiUsages => Set<OrgAiUsage>();
    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();
    public DbSet<UserBillingSettings> UserBillingSettings => Set<UserBillingSettings>();
    public DbSet<StripeWebhookEvent> StripeWebhookEvents => Set<StripeWebhookEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var isRelational = Database.IsRelational();
        var schema = isRelational && Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
            ? DefaultSchema
            : null;

        if (schema is not null)
            modelBuilder.HasDefaultSchema(schema);

        BillingModelConfiguration.Configure(modelBuilder, schema, configureCrossModuleForeignKeys: false);
    }
}
