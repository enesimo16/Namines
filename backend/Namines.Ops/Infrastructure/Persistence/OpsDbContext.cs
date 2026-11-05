using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;
using Namines.Ops.Domain;

namespace Namines.Ops.Infrastructure.Persistence;

/// <summary>
/// Ops'un <c>ops</c> şemasındaki tabloları ve migration zincirinin sahibi. Internal: modül dışından
/// doğrudan erişilemez. <c>Feedbacks.UserId → AspNetUsers</c> FK'si DB'de kalıyor (SET NULL) ama
/// bu modelde yok: Ops kimlik tablolarını tanımaz.
/// </summary>
internal sealed class OpsDbContext : DbContext
{
    public const string SchemaName = "ops";

    public OpsDbContext(DbContextOptions<OpsDbContext> options) : base(options)
    {
    }

    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<SqlExecutionAudit> SqlExecutionAudits => Set<SqlExecutionAudit>();
    public DbSet<AdminFeatureFlag> FeatureFlags => Set<AdminFeatureFlag>();
    public DbSet<AiToolRoute> AiToolRoutes => Set<AiToolRoute>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var isPostgres = Database.IsNpgsql();
        var schema = isPostgres ? SchemaName : null;
        if (isPostgres) modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<Feedback>(entity =>
        {
            entity.ToTable("Feedbacks", schema, t =>
            {
                if (isPostgres)
                    t.HasCheckConstraint(CheckConstraintSql.Name("Feedbacks", "Message", "Length"), CheckConstraintSql.MaxLength("Message", 4000));
            });
            entity.HasKey(f => f.Id);
            entity.HasIndex(f => f.UserId);
        });

        modelBuilder.Entity<SqlExecutionAudit>(entity =>
        {
            entity.ToTable("SqlExecutionAudits", schema);
            entity.HasKey(a => a.Id);
            if (isPostgres)
                entity.Property(a => a.Id).HasDefaultValueSql("gen_random_uuid()::text").ValueGeneratedNever();
            // Denetim kaydı iki soruyla okunuyor: "bu kullanıcı ne yaptı" ve "son dönemde neler oldu".
            entity.HasIndex(a => new { a.UserId, a.CreatedAt });
            entity.HasIndex(a => a.CreatedAt);
            entity.HasIndex(a => a.ProjectId);
        });

        modelBuilder.Entity<AdminFeatureFlag>(entity =>
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

        modelBuilder.Entity<AiToolRoute>(entity =>
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

internal sealed class OpsDbContextFactory : IDesignTimeDbContextFactory<OpsDbContext>
{
    public OpsDbContext CreateDbContext(string[] args) =>
        new(ModuleDbContextRegistration.MigrationOptions<OpsDbContext>(
            ModuleDbContextRegistration.DesignTimeConnectionString(), OpsDbContext.SchemaName));
}
