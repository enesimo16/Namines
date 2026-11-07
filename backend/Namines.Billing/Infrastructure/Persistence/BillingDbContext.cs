using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Namines.Billing.Application;
using Namines.Billing.Domain;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;

namespace Namines.Billing.Infrastructure.Persistence;

/// <summary>
/// Billing'in <c>billing</c> şemasındaki tabloları ve migration zincirinin sahibi. Internal: modül dışından
/// doğrudan erişilemez. <c>UserId → AspNetUsers</c> ve <c>OrgAiUsage.OrganizationId → Organizations</c> FK'leri
/// DB'de kalıyor (CASCADE) ama bu modelde yok: Billing kimlik tablolarını tanımaz.
/// </summary>
internal sealed class BillingDbContext : DbContext, IBillingData
{
    public const string SchemaName = "billing";

    public BillingDbContext(DbContextOptions<BillingDbContext> options) : base(options)
    {
    }

    public DbSet<BillingCustomer> Customers => Set<BillingCustomer>();
    public DbSet<BillingSubscription> Subscriptions => Set<BillingSubscription>();
    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();
    public DbSet<UserAIQuota> UserAIQuotas => Set<UserAIQuota>();
    public DbSet<UserBillingSettings> UserBillingSettings => Set<UserBillingSettings>();
    public DbSet<GlobalAiUsage> GlobalAiUsages => Set<GlobalAiUsage>();
    public DbSet<OrgAiUsage> OrgAiUsages => Set<OrgAiUsage>();
    public DbSet<StripeWebhookEvent> StripeWebhookEvents => Set<StripeWebhookEvent>();
    public DbSet<PendingSubscriptionCancellation> PendingSubscriptionCancellations => Set<PendingSubscriptionCancellation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var isPostgres = Database.IsNpgsql();
        var schema = isPostgres ? SchemaName : null;
        if (isPostgres) modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<BillingCustomer>(entity =>
        {
            entity.ToTable("customers", schema);
            entity.HasKey(c => c.UserId);
            entity.Property(c => c.StripeCustomerId).IsRequired();
            // Webhook her çağrıda müşteri kimliğiyle kullanıcı arıyor; aynı müşteri iki kullanıcıya bağlanamaz.
            entity.HasIndex(c => c.StripeCustomerId).IsUnique();
        });

        modelBuilder.Entity<BillingSubscription>(entity =>
        {
            entity.ToTable("subscriptions", schema);
            entity.HasKey(s => s.UserId);
            entity.HasIndex(s => s.PlanCode);
            // Aynı Stripe aboneliği iki kullanıcıya bağlanamaz (webhook yanlış kişiyi yükseltirdi); boş olanlar çakışmaz.
            entity.HasIndex(s => s.StripeSubscriptionId).IsUnique().HasFilter("\"StripeSubscriptionId\" IS NOT NULL");
        });

        modelBuilder.Entity<PendingSubscriptionCancellation>(entity =>
        {
            entity.ToTable("PendingSubscriptionCancellations", schema);
            entity.Property(c => c.SubscriptionId).IsRequired();
            // İşçi yalnızca açık satırlara bakıyor; tamamlananlar indeksi şişirmesin.
            entity.HasIndex(c => c.NextAttemptAt)
                .HasDatabaseName("IX_PendingSubscriptionCancellations_Open")
                .HasFilter("\"CompletedAt\" IS NULL");
            if (isPostgres)
                entity.Property(c => c.Id).HasDefaultValueSql("gen_random_uuid()::text").ValueGeneratedNever();
        });

        modelBuilder.Entity<UserAIQuota>(entity =>
        {
            entity.ToTable("UserAIQuotas", schema);
            entity.HasKey(q => q.UserId);
        });

        // Global token havuzu: gün başına tek satır (yarışta ikinci insert unique index ile düşer).
        modelBuilder.Entity<GlobalAiUsage>(entity =>
        {
            entity.ToTable("GlobalAiUsages", schema);
            entity.HasIndex(g => g.Date).IsUnique();
        });

        // Ekip havuzu: organizasyon + gün başına tek satır; aynı yarış-güvenli desen.
        modelBuilder.Entity<OrgAiUsage>(entity =>
        {
            entity.ToTable("OrgAiUsages", schema);
            entity.HasIndex(o => new { o.OrganizationId, o.Date }).IsUnique();
        });

        modelBuilder.Entity<UsageEvent>(entity =>
        {
            entity.ToTable("UsageEvents", schema, t =>
            {
                if (!isPostgres) return;
                t.HasCheckConstraint(CheckConstraintSql.Name("UsageEvents", "Resource"), CheckConstraintSql.EnumIn<UsageResource>("Resource"));
                t.HasCheckConstraint(CheckConstraintSql.Name("UsageEvents", "Quantity"), CheckConstraintSql.AtLeast("Quantity", 0));
            });
            // Kullanım sorguları her zaman (kullanıcı, dönem, kaynak) üçlüsüyle filtreleniyor.
            entity.HasIndex(e => new { e.UserId, e.BillingPeriod, e.Resource });
            // Admin kullanım sorguları (Resource == X && OccurredAt aralığı) — DB-16.
            entity.HasIndex(e => new { e.Resource, e.OccurredAt });
            // Parasal alan: double kullanmak kuruş kayıplarına yol açar.
            entity.Property(e => e.Quantity).HasPrecision(18, 4);
        });

        modelBuilder.Entity<UserBillingSettings>(entity =>
        {
            entity.ToTable("UserBillingSettings", schema);
            entity.HasKey(s => s.UserId);
            entity.Property(s => s.MonthlyCapUsd).HasPrecision(18, 2);
        });

        // Stripe olay kimliği BİRİNCİL ANAHTAR: ikinci ekleme denemesi veritabanı seviyesinde çakışır.
        modelBuilder.Entity<StripeWebhookEvent>(entity =>
        {
            entity.ToTable("StripeWebhookEvents", schema);
            entity.HasKey(e => e.Id);
            // Sıra kontrolü müşteri başına ve olayın ÜRETİLME zamanına göre okunuyor.
            entity.HasIndex(e => new { e.StripeCustomerId, e.EventCreatedAt });
        });
    }
}

internal sealed class BillingDbContextFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    public BillingDbContext CreateDbContext(string[] args) =>
        new(ModuleDbContextRegistration.MigrationOptions<BillingDbContext>(
            ModuleDbContextRegistration.DesignTimeConnectionString(), BillingDbContext.SchemaName));
}
