using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;
using Namines.Studio.Domain;

namespace Namines.Studio.Infrastructure.Persistence;

/// <summary>
/// <c>studio</c> şemasının tek bağlamı ve migration zincirinin sahibi. Internal: modül dışından
/// doğrudan erişilemez. Kimlik tablolarına giden FK'ler (kurucu, organizasyon, yazar) DB'de duruyor,
/// bu modelde yok: Studio <c>ApplicationUser</c>'ı tanımaz, kimlik bilgisi IIdentityDirectory'den gelir.
/// </summary>
internal sealed class StudioDbContext : DbContext
{
    public const string SchemaName = "studio";

    /// <summary>SchemaJson satır başına tavanı; istek gövdesi sınırı ayrıca API'de.</summary>
    public const int MaxSchemaJsonLength = 10_000_000;

    public StudioDbContext(DbContextOptions<StudioDbContext> options) : base(options)
    {
    }

    public DbSet<CloudProject> Projects => Set<CloudProject>();
    public DbSet<ProjectConnection> ProjectConnections => Set<ProjectConnection>();
    public DbSet<ProjectShare> ProjectShares => Set<ProjectShare>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<SchemaVersion> SchemaVersions => Set<SchemaVersion>();
    public DbSet<CrossDatabaseRelation> CrossDatabaseRelations => Set<CrossDatabaseRelation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var isPostgres = Database.IsNpgsql();
        var schema = isPostgres ? SchemaName : null;
        if (isPostgres) modelBuilder.HasDefaultSchema(SchemaName);

        static string N(string table, string col, string? suffix = null) => CheckConstraintSql.Name(table, col, suffix);

        modelBuilder.Entity<CloudProject>(entity =>
        {
            entity.ToTable("projects", schema, t =>
            {
                if (!isPostgres) return;
                // Name: Hub PATCH 128 sınırlıyor ama kaydetme yolu doğrulamıyor → cömert tavan.
                t.HasCheckConstraint(N("projects", "Name", "Length"), CheckConstraintSql.MaxLength("Name", 512));
                t.HasCheckConstraint(N("projects", "SchemaJson", "Length"), CheckConstraintSql.MaxLength("SchemaJson", MaxSchemaJsonLength));
            });
            entity.HasKey(p => p.Id);
            entity.HasIndex(p => p.UserId);
            entity.HasIndex(p => p.OrganizationId);

            if (isPostgres)
            {
                entity.Property(p => p.Id).HasDefaultValueSql("gen_random_uuid()::text").ValueGeneratedNever();
                // xmin: ayrı kolon backfill ve elle artırma isterdi; atlanırsa koruma sessizce yok olurdu.
                entity.Property(p => p.RowVersion)
                    .HasColumnName("xmin")
                    .HasColumnType("xid")
                    .ValueGeneratedOnAddOrUpdate()
                    .IsConcurrencyToken();
            }
            else
            {
                // SQLite'ta xmin diye gerçek bir NOT NULL kolon doğar ve her ekleme patlardı.
                entity.Ignore(p => p.RowVersion);
            }
        });

        modelBuilder.Entity<ProjectConnection>(entity =>
        {
            entity.ToTable("project_connections", schema);
            entity.HasKey(c => c.ProjectId);
            entity.HasOne<CloudProject>()
                .WithOne()
                .HasForeignKey<ProjectConnection>(c => c.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProjectShare>(entity =>
        {
            entity.ToTable("project_shares", schema, t =>
            {
                if (isPostgres)
                    t.HasCheckConstraint(N("project_shares", "ShareTokenHash", "Length"), "char_length(\"ShareTokenHash\") = 64");
            });
            entity.HasKey(s => s.Id);
            entity.Property(s => s.ShareTokenHash).IsRequired();
            entity.HasIndex(s => s.ShareTokenHash).IsUnique();
            // Üye başına etkin bağlantı sayımı ve toplu iptal ProjectId ile arıyor.
            entity.HasIndex(s => s.ProjectId);
            entity.HasOne<CloudProject>()
                .WithMany()
                .HasForeignKey(s => s.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            if (isPostgres)
                entity.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()::text").ValueGeneratedNever();
        });

        modelBuilder.Entity<Branch>(entity =>
        {
            entity.ToTable("Branches", schema);
            entity.HasKey(b => b.Id);
            entity.HasOne<CloudProject>()
                .WithMany()
                .HasForeignKey(b => b.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Branch>()
                .WithMany()
                .HasForeignKey(b => b.ParentBranchId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(b => b.ParentBranchId);
            entity.HasIndex(b => b.CreatedByUserId);
            // Proje içinde branch adı benzersiz: aynı projede iki "main" olamaz.
            entity.HasIndex(b => new { b.ProjectId, b.Name }).IsUnique();
            // Bir projede en fazla bir varsayılan branch.
            entity.HasIndex(b => b.ProjectId).IsUnique().HasFilter("\"IsDefault\" = true");
            if (isPostgres)
                entity.Property(b => b.Id).HasDefaultValueSql("gen_random_uuid()::text").ValueGeneratedNever();
        });

        modelBuilder.Entity<SchemaVersion>(entity =>
        {
            entity.ToTable("SchemaVersions", schema, t =>
            {
                if (isPostgres)
                    t.HasCheckConstraint(N("SchemaVersions", "SchemaJson", "Length"), CheckConstraintSql.MaxLength("SchemaJson", MaxSchemaJsonLength));
            });
            entity.HasKey(v => v.Id);
            entity.HasOne<CloudProject>()
                .WithMany()
                .HasForeignKey(v => v.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Branch>()
                .WithMany()
                .HasForeignKey(v => v.BranchId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(v => v.AuthorUserId);
            // Branch içinde sürüm numarası benzersiz: commit sırasının garantisi.
            entity.HasIndex(v => new { v.BranchId, v.Version }).IsUnique();
            entity.HasIndex(v => new { v.ProjectId, v.CreatedAt });
            if (isPostgres)
                entity.Property(v => v.Id).HasDefaultValueSql("gen_random_uuid()::text").ValueGeneratedNever();
        });

        modelBuilder.Entity<CrossDatabaseRelation>(entity =>
        {
            entity.ToTable("CrossDatabaseRelations", schema);
            entity.HasKey(r => r.Id);
            // İki yön de Restrict: projeyi silmek ilişkileri sessizce yetim bırakmamalı, önce kaldırılır.
            entity.HasOne<CloudProject>()
                .WithMany()
                .HasForeignKey(r => r.SourceProjectId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CloudProject>()
                .WithMany()
                .HasForeignKey(r => r.TargetProjectId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(r => r.SourceProjectId);
            entity.HasIndex(r => r.TargetProjectId);
            entity.HasIndex(r => r.CreatedByUserId);
            if (isPostgres)
                entity.Property(r => r.Id).HasDefaultValueSql("gen_random_uuid()::text").ValueGeneratedNever();
        });
    }
}

internal sealed class StudioDbContextFactory : IDesignTimeDbContextFactory<StudioDbContext>
{
    public StudioDbContext CreateDbContext(string[] args) =>
        new(ModuleDbContextRegistration.MigrationOptions<StudioDbContext>(
            ModuleDbContextRegistration.DesignTimeConnectionString(), StudioDbContext.SchemaName));
}
