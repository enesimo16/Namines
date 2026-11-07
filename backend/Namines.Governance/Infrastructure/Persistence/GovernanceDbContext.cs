using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Enums;
using Namines.Core.Models.Auth;
using Namines.Governance.Application;
using Namines.Governance.Domain;

namespace Namines.Governance.Infrastructure.Persistence;

/// <summary>
/// Governance'ın <c>governance</c> şemasındaki tabloları ve migration zincirinin sahibi. Internal: modül dışından
/// doğrudan erişilemez. Kullanıcıya, projeye, branch'e ve sürüme giden FK'ler DB'de kalıyor ama bu modelde yok:
/// Governance kimlik ve Studio tablolarını tanımaz.
/// </summary>
internal sealed class GovernanceDbContext : DbContext, IGovernanceData
{
    public const string SchemaName = "governance";

    public GovernanceDbContext(DbContextOptions<GovernanceDbContext> options) : base(options)
    {
    }

    public DbSet<ChangeRequest> ChangeRequests => Set<ChangeRequest>();
    public DbSet<ChangeRequestApproval> ChangeRequestApprovals => Set<ChangeRequestApproval>();
    public DbSet<ChangeRequestAuditLog> ChangeRequestAuditLogs => Set<ChangeRequestAuditLog>();
    public DbSet<UserAIPolicy> UserAIPolicies => Set<UserAIPolicy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var isPostgres = Database.IsNpgsql();
        var schema = isPostgres ? SchemaName : null;
        if (isPostgres) modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<ChangeRequest>(entity =>
        {
            entity.ToTable("ChangeRequests", schema, t =>
            {
                t.HasCheckConstraint(CheckConstraintSql.Name("ChangeRequests", "Status"), CheckConstraintSql.EnumIn<ChangeRequestStatus>("Status"));
                t.HasCheckConstraint(CheckConstraintSql.Name("ChangeRequests", "RiskLevel"), CheckConstraintSql.EnumIn<RiskLevel>("RiskLevel"));
            });
            entity.HasKey(c => c.Id);
            entity.HasIndex(c => new { c.ProjectId, c.CreatedAt });
            // Studio'ya giden FK'ler DB'de duruyor (cascade/no action), modelde yok: indeksleri açıkça tutulur.
            entity.HasIndex(c => c.BranchId);
            entity.HasIndex(c => c.HeadVersionId);
            entity.HasIndex(c => c.BaseVersionId);
            if (isPostgres) entity.Property(c => c.Id).HasDefaultValueSql("gen_random_uuid()::text").ValueGeneratedNever();
        });

        modelBuilder.Entity<ChangeRequestApproval>(entity =>
        {
            entity.ToTable("ChangeRequestApprovals", schema, t =>
                t.HasCheckConstraint(CheckConstraintSql.Name("ChangeRequestApprovals", "Decision"), CheckConstraintSql.EnumIn<ApprovalDecision>("Decision")));
            entity.HasKey(a => a.Id);
            entity.HasOne(a => a.ChangeRequest)
                .WithMany(c => c.Approvals)
                .HasForeignKey(a => a.ChangeRequestId)
                .OnDelete(DeleteBehavior.Cascade);
            // Bir kullanıcı aynı CR'a yalnızca bir kez oy verebilir.
            entity.HasIndex(a => new { a.ChangeRequestId, a.UserId }).IsUnique();
            if (isPostgres) entity.Property(a => a.Id).HasDefaultValueSql("gen_random_uuid()::text").ValueGeneratedNever();
        });

        // DENETİM İZİ YAŞAMALI (DB-10): ChangeRequestId'ye bilinçli olarak FK YOK. Proje/branch silinince
        // "migration'ı kim onayladı" kanıtı da gitmesin; ProjectId / ChangeRequestTitle anlık kopyadır.
        // Satırlar değiştirilemez: yönetim tetikleyicisi migration'da (Governance_01).
        modelBuilder.Entity<ChangeRequestAuditLog>(entity =>
        {
            entity.ToTable("ChangeRequestAuditLogs", schema, t =>
                t.HasCheckConstraint(CheckConstraintSql.Name("ChangeRequestAuditLogs", "Action"), CheckConstraintSql.EnumIn<ChangeRequestAuditAction>("Action")));
            entity.HasKey(a => a.Id);
            entity.HasIndex(a => new { a.ProjectId, a.CreatedAt });
            entity.HasIndex(a => new { a.ChangeRequestId, a.CreatedAt });
            if (isPostgres) entity.Property(a => a.Id).HasDefaultValueSql("gen_random_uuid()::text").ValueGeneratedNever();
        });

        modelBuilder.Entity<UserAIPolicy>(entity =>
        {
            entity.ToTable("UserAIPolicies", schema, t =>
            {
                foreach (var col in new[] { "SmartSeed", "Documentation", "Scaffolding", "SchemaGeneration", "SchemaRevision", "DbaAnalysis", "Migration", "Voice" })
                    t.HasCheckConstraint(CheckConstraintSql.Name("UserAIPolicies", col), CheckConstraintSql.EnumIn<AIMode>(col));
            });
            entity.HasKey(p => p.UserId);
        });
    }
}

internal sealed class GovernanceDbContextFactory : IDesignTimeDbContextFactory<GovernanceDbContext>
{
    public GovernanceDbContext CreateDbContext(string[] args) =>
        new(ModuleDbContextRegistration.MigrationOptions<GovernanceDbContext>(
            ModuleDbContextRegistration.DesignTimeConnectionString(), GovernanceDbContext.SchemaName));
}
