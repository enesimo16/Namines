using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;
using Namines.Core.Models.Hub;

namespace Namines.BuildingBlocks.Modules;

/// <summary>
/// Hub (Topluluk Kataloğu, Issue, Comment, Star, Repositories ve ProjectTags) modülünün
/// dışa açtığı resmi sözleşme (Order: 90, Şema: <c>hub</c>).
/// </summary>
public interface IHubModule
{
    HubDbContext DbContext { get; }
    IHubApplicationService Repositories { get; }
}

public sealed class HubModule : IHubModule
{
    public HubModule(HubDbContext dbContext, IHubApplicationService? repositories = null)
    {
        DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        Repositories = repositories ?? new HubApplicationService(dbContext);
    }

    public HubDbContext DbContext { get; }
    public IHubApplicationService Repositories { get; }
}

public static class HubModelConfiguration
{
    public const string DefaultSchemaName = "hub";

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

        builder.Entity<RepoIssue>(entity =>
        {
            entity.ToTable("RepoIssues", schema);
            entity.HasKey(i => i.Id);
            entity.HasIndex(i => new { i.ProjectId, i.IssueNumber }).IsUnique();
            entity.HasIndex(i => new { i.ProjectId, i.IsClosed });
            entity.Property(i => i.Title).HasMaxLength(256);
            entity.Property(i => i.RelatedTableName).HasMaxLength(128);
            entity.Property(i => i.NslRuleCode).HasMaxLength(32);
            entity.Property(i => i.Labels).HasMaxLength(256);

            if (configureCrossModuleForeignKeys)
            {
                entity.HasOne(i => i.Project)
                    .WithMany()
                    .HasForeignKey(i => i.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(i => i.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(i => i.CreatedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);
            }
            else
            {
                entity.Ignore(i => i.Project);
                entity.Ignore(i => i.CreatedByUser);
            }
        });

        builder.Entity<RepoIssueComment>(entity =>
        {
            entity.ToTable("RepoIssueComments", schema);
            entity.HasKey(c => c.Id);
            entity.HasIndex(c => c.IssueId);
            entity.HasOne(c => c.Issue)
                .WithMany()
                .HasForeignKey(c => c.IssueId)
                .OnDelete(DeleteBehavior.Cascade);

            if (configureCrossModuleForeignKeys)
            {
                entity.HasOne(c => c.AuthorUser)
                    .WithMany()
                    .HasForeignKey(c => c.AuthorUserId)
                    .OnDelete(DeleteBehavior.SetNull);
            }
            else
            {
                entity.Ignore(c => c.AuthorUser);
            }
        });

        builder.Entity<RepoStar>(entity =>
        {
            entity.ToTable("RepoStars", schema);
            entity.HasKey(s => new { s.ProjectId, s.UserId });

            if (configureCrossModuleForeignKeys)
            {
                entity.HasOne(s => s.Project)
                    .WithMany()
                    .HasForeignKey(s => s.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(s => s.User)
                    .WithMany()
                    .HasForeignKey(s => s.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            }
            else
            {
                entity.Ignore(s => s.Project);
                entity.Ignore(s => s.User);
            }
        });

        // ── 3NF Normalized Entity: hub.project_publications (DATABASE_NORMALIZATION.md) ──
        builder.Entity<ProjectPublication>(entity =>
        {
            entity.ToTable("project_publications", schema);
            entity.HasKey(p => p.ProjectId);
            entity.Property(p => p.ProjectId).HasMaxLength(64);
            entity.Property(p => p.OwnerUserId).HasMaxLength(64).IsRequired();
            entity.Property(p => p.Slug).HasMaxLength(120).IsRequired();
            entity.Property(p => p.Description).HasMaxLength(500);
            entity.Property(p => p.Category).HasMaxLength(60);
            entity.Property(p => p.Tags).HasMaxLength(400);
            entity.HasIndex(p => p.Slug);
            entity.HasIndex(p => p.ForkedFromProjectId);

            if (configureCrossModuleForeignKeys)
            {
                entity.HasOne<CloudProject>()
                    .WithOne()
                    .HasForeignKey<ProjectPublication>(p => p.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
            }
        });

        // ── 3NF Normalized Entities: hub.repositories & hub.project_tags (Phase 14.2B) ──
        builder.Entity<ProjectRepository>(entity =>
        {
            entity.ToTable("repositories", schema);
            entity.HasKey(r => r.ProjectId);
            entity.Property(r => r.ProjectId).HasMaxLength(64);
            entity.Property(r => r.Slug).HasMaxLength(128).IsRequired();
            entity.Property(r => r.Description).HasMaxLength(1000);
            entity.Property(r => r.Category).HasMaxLength(64).IsRequired();
            entity.Property(r => r.ForkedFromProjectId).HasMaxLength(64);
            entity.Property(r => r.LinkedGithubRepo).HasMaxLength(140);
            entity.HasIndex(r => r.Slug);
            entity.HasIndex(r => new { r.Visibility, r.StarCount, r.PublishedAt });
        });

        builder.Entity<ProjectTag>(entity =>
        {
            entity.ToTable("project_tags", schema);
            entity.HasKey(t => new { t.ProjectId, t.Tag });
            entity.Property(t => t.ProjectId).HasMaxLength(64);
            entity.Property(t => t.Tag).HasMaxLength(50);
            entity.HasIndex(t => t.Tag);
            entity.HasOne<ProjectRepository>()
                .WithMany()
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class HubDbContext : DbContext
{
    public const string DefaultSchema = HubModelConfiguration.DefaultSchemaName;
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public HubDbContext(DbContextOptions<HubDbContext> options) : base(options)
    {
    }

    public DbSet<ProjectRepository> Repositories => Set<ProjectRepository>();
    public DbSet<ProjectTag> ProjectTags => Set<ProjectTag>();
    public DbSet<RepoIssue> RepoIssues => Set<RepoIssue>();
    public DbSet<RepoIssueComment> RepoIssueComments => Set<RepoIssueComment>();
    public DbSet<RepoStar> RepoStars => Set<RepoStar>();
    public DbSet<ProjectPublication> ProjectPublications => Set<ProjectPublication>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var isRelational = Database.IsRelational();
        var schema = isRelational && Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
            ? DefaultSchema
            : null;

        if (schema is not null)
            modelBuilder.HasDefaultSchema(schema);

        HubModelConfiguration.Configure(modelBuilder, schema, configureCrossModuleForeignKeys: false);
    }
}
