using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;

namespace Namines.BuildingBlocks.Modules;

/// <summary>
/// Studio modülünün dışa açtığı resmi sözleşme (Order: 20, Şema: <c>studio</c>).
/// </summary>
public interface IStudioModule
{
    StudioDbContext DbContext { get; }
    IStudioApplicationService Projects { get; }
}

public sealed class StudioModule : IStudioModule
{
    public StudioModule(StudioDbContext dbContext, IStudioApplicationService? projects = null)
    {
        DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        Projects = projects ?? new StudioApplicationService(dbContext);
    }

    public StudioDbContext DbContext { get; }
    public IStudioApplicationService Projects { get; }
}

public static class StudioModelConfiguration
{
    public const string DefaultSchemaName = "studio";

    public static void Configure(
        ModelBuilder builder,
        string? schema = DefaultSchemaName,
        bool configureCrossModuleForeignKeys = false,
        bool isPostgres = false)
    {
        if (!configureCrossModuleForeignKeys)
        {
            builder.Ignore<ApplicationUser>();
            builder.Ignore<Organization>();
            builder.Ignore<OrganizationMember>();
        }

        builder.Entity<CloudProject>(entity =>
        {
            entity.ToTable("projects", schema);
            entity.HasKey(p => p.Id);
            entity.HasIndex(p => p.UserId);
            entity.HasIndex(p => p.OrganizationId);

            // ── Phase 14.2B: Unmap Legacy God Entity Columns (Bounded Context Decomposition) ──
            entity.Ignore(p => p.EncryptedConnectionString);
            entity.Ignore(p => p.ConnectionDbType);
            entity.Ignore(p => p.AllowDeskSql);
            entity.Ignore(p => p.ShareToken);
            entity.Ignore(p => p.ShareTokenHash);
            entity.Ignore(p => p.Slug);
            entity.Ignore(p => p.Description);
            entity.Ignore(p => p.Visibility);
            entity.Ignore(p => p.Category);
            entity.Ignore(p => p.Tags);
            entity.Ignore(p => p.StarCount);
            entity.Ignore(p => p.ForkCount);
            entity.Ignore(p => p.ForkedFromProjectId);
            entity.Ignore(p => p.LinkedGithubRepo);

            if (isPostgres)
            {
                entity.Property(p => p.RowVersion)
                    .HasColumnName("xmin")
                    .HasColumnType("xid")
                    .ValueGeneratedOnAddOrUpdate()
                    .IsConcurrencyToken();
            }
            else
            {
                entity.Ignore(p => p.RowVersion);
            }

            if (configureCrossModuleForeignKeys)
            {
                entity.HasOne(p => p.User)
                    .WithMany()
                    .HasForeignKey(p => p.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(p => p.Organization)
                    .WithMany()
                    .HasForeignKey(p => p.OrganizationId)
                    .OnDelete(DeleteBehavior.Cascade);
            }
            else
            {
                entity.Ignore(p => p.User);
                entity.Ignore(p => p.Organization);
            }
        });

        builder.Entity<Branch>(entity =>
        {
            entity.ToTable("Branches", schema);
            entity.HasKey(b => b.Id);
            entity.HasOne(b => b.Project)
                .WithMany()
                .HasForeignKey(b => b.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(b => b.ParentBranch)
                .WithMany()
                .HasForeignKey(b => b.ParentBranchId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(b => new { b.ProjectId, b.Name }).IsUnique();
            entity.HasIndex(b => b.ProjectId)
                .IsUnique()
                .HasFilter("\"IsDefault\" = true");

            if (configureCrossModuleForeignKeys)
            {
                entity.HasOne(b => b.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(b => b.CreatedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);
            }
            else
            {
                entity.Ignore(b => b.CreatedByUser);
            }
        });

        builder.Entity<SchemaVersion>(entity =>
        {
            entity.ToTable("SchemaVersions", schema);
            entity.HasKey(v => v.Id);
            entity.HasOne(v => v.Project)
                .WithMany()
                .HasForeignKey(v => v.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(v => v.Branch)
                .WithMany()
                .HasForeignKey(v => v.BranchId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(v => new { v.BranchId, v.Version }).IsUnique();
            entity.HasIndex(v => new { v.ProjectId, v.CreatedAt });

            if (configureCrossModuleForeignKeys)
            {
                entity.HasOne(v => v.AuthorUser)
                    .WithMany()
                    .HasForeignKey(v => v.AuthorUserId)
                    .OnDelete(DeleteBehavior.SetNull);
            }
            else
            {
                entity.Ignore(v => v.AuthorUser);
            }
        });

        builder.Entity<CrossDatabaseRelation>(entity =>
        {
            entity.ToTable("CrossDatabaseRelations", schema);
            entity.HasKey(r => r.Id);
            entity.HasOne(r => r.SourceProject)
                .WithMany()
                .HasForeignKey(r => r.SourceProjectId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(r => r.TargetProject)
                .WithMany()
                .HasForeignKey(r => r.TargetProjectId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(r => r.SourceProjectId);
            entity.HasIndex(r => r.TargetProjectId);

            if (configureCrossModuleForeignKeys)
            {
                entity.HasOne(r => r.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(r => r.CreatedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);
            }
            else
            {
                entity.Ignore(r => r.CreatedByUser);
            }
        });

        // ── 3NF Normalized Entities (DATABASE_NORMALIZATION.md) ──
        builder.Entity<ProjectConnection>(entity =>
        {
            entity.ToTable("project_connections", schema);
            entity.HasKey(c => c.ProjectId);
            entity.Property(c => c.ProjectId).HasMaxLength(64);
            entity.Property(c => c.ConnectionDbType).HasMaxLength(32).IsRequired();
            entity.Property(c => c.EncryptedConnectionString).IsRequired();
            entity.HasOne<CloudProject>()
                .WithOne()
                .HasForeignKey<ProjectConnection>(c => c.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProjectGitRepository>(entity =>
        {
            entity.ToTable("project_git_repositories", schema);
            entity.HasKey(g => g.ProjectId);
            entity.Property(g => g.ProjectId).HasMaxLength(64);
            entity.Property(g => g.GithubRepoFullName).HasMaxLength(200).IsRequired();
            entity.Property(g => g.GithubDefaultBranch).HasMaxLength(120);
            entity.HasIndex(g => g.GithubRepoFullName);
            entity.HasOne<CloudProject>()
                .WithOne()
                .HasForeignKey<ProjectGitRepository>(g => g.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SchemaShareLink>(entity =>
        {
            entity.ToTable("schema_share_links", schema);
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).HasMaxLength(64);
            entity.Property(s => s.BranchId).HasMaxLength(64).IsRequired();
            entity.Property(s => s.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(s => s.CreatedByUserId).HasMaxLength(64);
            entity.HasIndex(s => s.TokenHash).IsUnique();
            entity.HasIndex(s => s.BranchId);
            entity.HasOne<Branch>()
                .WithMany()
                .HasForeignKey(s => s.BranchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── 3NF Normalized Entity: studio.project_shares (Phase 14.2B) ──
        builder.Entity<ProjectShare>(entity =>
        {
            entity.ToTable("project_shares", schema);
            entity.HasKey(s => s.ProjectId);
            entity.Property(s => s.ProjectId).HasMaxLength(64);
            entity.Property(s => s.ShareTokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(s => s.ShareTokenHash).IsUnique();
            entity.HasOne<CloudProject>()
                .WithOne()
                .HasForeignKey<ProjectShare>(s => s.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class StudioDbContext : DbContext
{
    public const string DefaultSchema = StudioModelConfiguration.DefaultSchemaName;
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public StudioDbContext(DbContextOptions<StudioDbContext> options) : base(options)
    {
    }

    public DbSet<CloudProject> Projects => Set<CloudProject>();
    public DbSet<CloudProject> CloudProjects => Set<CloudProject>();
    public DbSet<ProjectShare> ProjectShares => Set<ProjectShare>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<SchemaVersion> SchemaVersions => Set<SchemaVersion>();
    public DbSet<CrossDatabaseRelation> CrossDatabaseRelations => Set<CrossDatabaseRelation>();
    public DbSet<ProjectConnection> ProjectConnections => Set<ProjectConnection>();
    public DbSet<ProjectGitRepository> ProjectGitRepositories => Set<ProjectGitRepository>();
    public DbSet<SchemaShareLink> SchemaShareLinks => Set<SchemaShareLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var isRelational = Database.IsRelational();
        var isPostgres = isRelational && Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
        var schema = isPostgres ? DefaultSchema : null;

        if (schema is not null)
            modelBuilder.HasDefaultSchema(schema);

        StudioModelConfiguration.Configure(
            modelBuilder,
            schema,
            configureCrossModuleForeignKeys: false,
            isPostgres: isPostgres);
    }
}
