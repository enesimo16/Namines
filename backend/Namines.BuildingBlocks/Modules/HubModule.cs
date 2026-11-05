using System;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Application;
using Namines.Core.Models.Auth;
using Namines.Core.Models.Hub;

namespace Namines.BuildingBlocks.Modules;

/// <summary>
/// Hub (Repositories, ProjectTags, Stars) modülünün dışa açtığı resmi sözleşme
/// (Order: 90, Şema: <c>hub</c>).
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

    /// <summary>
    /// repositories/project_tags tablolarının TEK eşlemesi. Hem <see cref="HubDbContext"/> hem de
    /// migration zincirinin sahibi AuthDbContext buradan okur. Studio'ya giden FK yalnızca
    /// AuthDbContext tarafında tanımlı, çünkü Hub bağlamı <see cref="CloudProject"/>'i tanımaz.
    /// </summary>
    /// <summary>
    /// Issue etiketleri. <c>RepoIssues</c> henüz HubDbContext'te değil (D-1), bu yüzden yalnızca
    /// AuthDbContext çağırıyor; tablolar şimdiden Hub şemasında.
    /// </summary>
    public static void ConfigureIssueLabelTables(ModelBuilder builder, string? schema)
    {
        builder.Entity<IssueLabel>(entity =>
        {
            entity.ToTable("issue_labels", schema, t =>
            {
                if (schema is not null)
                    t.HasCheckConstraint("CK_issue_labels_NormalizedName", "\"NormalizedName\" <> ''");
            });
            entity.HasKey(l => l.Id);
            entity.Property(l => l.ProjectId).IsRequired();
            entity.Property(l => l.Name).IsRequired().HasMaxLength(IssueLabelRules.MaxNameLength);
            entity.Property(l => l.NormalizedName).IsRequired().HasMaxLength(IssueLabelRules.MaxNameLength);
            entity.HasIndex(l => new { l.ProjectId, l.NormalizedName }).IsUnique();
            entity.HasOne<CloudProject>()
                .WithMany()
                .HasForeignKey(l => l.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RepoIssueLabel>(entity =>
        {
            entity.ToTable("repo_issue_labels", schema);
            entity.HasKey(l => new { l.RepoIssueId, l.IssueLabelId });
            entity.Property(l => l.DisplayName).IsRequired().HasMaxLength(IssueLabelRules.MaxNameLength);
            // Filtre etiketten issue'ya gider; PK ters yönde olduğu için bunu karşılamaz.
            entity.HasIndex(l => new { l.IssueLabelId, l.RepoIssueId });
            entity.HasOne<RepoIssue>()
                .WithMany(i => i.LabelLinks)
                .HasForeignKey(l => l.RepoIssueId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(l => l.Label)
                .WithMany()
                .HasForeignKey(l => l.IssueLabelId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public static void ConfigureOwnedTables(ModelBuilder builder, string? schema, bool excludeFromMigrations)
    {
        builder.Entity<ProjectRepository>(entity =>
        {
            entity.ToTable("repositories", schema, t =>
            {
                if (excludeFromMigrations) t.ExcludeFromMigrations();
            });
            entity.HasKey(r => r.ProjectId);
            entity.Property(r => r.OwnerUserId).IsRequired();
            entity.Property(r => r.ProjectName).IsRequired();
            entity.Property(r => r.ProjectDbType).IsRequired();
            entity.Property(r => r.Category).IsRequired();

            // Slug boş olabilir: benzersizlik yalnızca dolu slug'larda ve sahip bazında (taşınan
            // IX_CloudProjects_UserId_Slug ile aynı kural).
            entity.HasIndex(r => new { r.OwnerUserId, r.Slug })
                .IsUnique()
                .HasFilter("\"Slug\" IS NOT NULL AND \"Slug\" <> ''");
            entity.HasIndex(r => r.Slug);

            // Explore (anonim): yalnızca Public, iki sıralama. 2 = ProjectVisibility.Public.
            entity.HasIndex(r => new { r.StarCount, r.CreatedAt, r.ProjectId })
                .IsDescending(true, true, true)
                .HasDatabaseName("IX_repositories_Explore_Popular")
                .HasFilter("\"Visibility\" = 2");
            entity.HasIndex(r => new { r.CreatedAt, r.ProjectId })
                .IsDescending(true, true)
                .HasDatabaseName("IX_repositories_Explore_Recent")
                .HasFilter("\"Visibility\" = 2");

            entity.HasIndex(r => r.ForkedFromProjectId);
            entity.HasOne<ProjectRepository>()
                .WithMany()
                .HasForeignKey(r => r.ForkedFromProjectId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ProjectTag>(entity =>
        {
            entity.ToTable("project_tags", schema, t =>
            {
                if (excludeFromMigrations) t.ExcludeFromMigrations();
            });
            entity.HasKey(t => new { t.ProjectId, t.Tag });
            entity.Property(t => t.DisplayTag).IsRequired();
            // Etiket filtresi Tag ile başlıyor; PK ProjectId ile başladığı için onu karşılamaz.
            entity.HasIndex(t => new { t.Tag, t.ProjectId });
            entity.HasOne<ProjectRepository>()
                .WithMany()
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>
/// Hub'ın çalışma zamanı bağlamı. RepoIssues/RepoIssueComments kendi fazına (D-1) kadar eşli
/// değil; RepoStars <c>public</c>'te duruyor ama sayaçla aynı işlemde yazıldığı için burada,
/// tablonun GERÇEK konumuna eşli.
///
/// Şemayı AuthDbContext migration zinciri kuruyor, bu yüzden tablolar burada
/// <c>ExcludeFromMigrations</c>: iki bağlam aynı DDL'i üretmesin.
/// </summary>
public sealed class HubDbContext : DbContext
{
    public const string DefaultSchema = HubModelConfiguration.DefaultSchemaName;
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public HubDbContext(DbContextOptions<HubDbContext> options) : base(options)
    {
    }

    public DbSet<ProjectRepository> Repositories => Set<ProjectRepository>();
    public DbSet<ProjectTag> ProjectTags => Set<ProjectTag>();
    public DbSet<RepoStar> RepoStars => Set<RepoStar>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var isPostgres = Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
        var schema = isPostgres ? DefaultSchema : null;

        if (schema is not null)
            modelBuilder.HasDefaultSchema(schema);

        modelBuilder.Ignore<ApplicationUser>();
        modelBuilder.Ignore<Organization>();
        modelBuilder.Ignore<OrganizationMember>();
        modelBuilder.Ignore<CloudProject>();

        modelBuilder.Entity<RepoStar>(entity =>
        {
            entity.ToTable("RepoStars", isPostgres ? "public" : null, t => t.ExcludeFromMigrations());
            entity.HasKey(s => new { s.ProjectId, s.UserId });
            entity.Ignore(s => s.Project);
            entity.Ignore(s => s.User);
        });

        HubModelConfiguration.ConfigureOwnedTables(modelBuilder, schema, excludeFromMigrations: true);
    }
}
