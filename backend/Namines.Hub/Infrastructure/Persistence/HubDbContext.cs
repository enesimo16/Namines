using Namines.Hub.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;
using Namines.Hub.Domain;

namespace Namines.Hub.Infrastructure.Persistence;

/// <summary>
/// <c>hub</c> şemasının tek bağlamı ve migration zincirinin sahibi. Internal: Hub dışından erişilemez.
/// Studio ve kimlik tablolarına giden FK'ler (projeye cascade, yazara SET NULL) DB'de duruyor, bu
/// modelde yok: Hub projeyi ve kullanıcıyı yalnızca kimlikle tanır, veriyi sözleşmeden alır.
/// </summary>
internal sealed class HubDbContext : DbContext, IHubData
{
    public const string SchemaName = "hub";

    public HubDbContext(DbContextOptions<HubDbContext> options) : base(options)
    {
    }

    public DbSet<ProjectRepository> Repositories => Set<ProjectRepository>();
    public DbSet<ProjectTag> ProjectTags => Set<ProjectTag>();
    public DbSet<RepoStar> RepoStars => Set<RepoStar>();
    public DbSet<RepoIssue> RepoIssues => Set<RepoIssue>();
    public DbSet<RepoIssueComment> RepoIssueComments => Set<RepoIssueComment>();
    public DbSet<IssueLabel> IssueLabels => Set<IssueLabel>();
    public DbSet<RepoIssueLabel> RepoIssueLabels => Set<RepoIssueLabel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var isPostgres = Database.IsNpgsql();
        var schema = isPostgres ? SchemaName : null;
        if (isPostgres) modelBuilder.HasDefaultSchema(SchemaName);

        static string N(string table, string col, string? suffix = null) => CheckConstraintSql.Name(table, col, suffix);

        modelBuilder.Entity<ProjectRepository>(entity =>
        {
            entity.ToTable("repositories", schema, t =>
            {
                t.HasCheckConstraint(N("repositories", "Visibility"), CheckConstraintSql.EnumIn<ProjectVisibility>("Visibility"));
                t.HasCheckConstraint(N("repositories", "StarCount"), CheckConstraintSql.AtLeast("StarCount", 0));
                t.HasCheckConstraint(N("repositories", "ForkCount"), CheckConstraintSql.AtLeast("ForkCount", 0));
                if (!isPostgres) return;
                t.HasCheckConstraint(N("repositories", "Description", "Length"), CheckConstraintSql.MaxLength("Description", 1000));
                t.HasCheckConstraint(N("repositories", "Category", "Length"), CheckConstraintSql.MaxLength("Category", 64));
                t.HasCheckConstraint(N("repositories", "LinkedGithubRepo", "Length"), CheckConstraintSql.MaxLength("LinkedGithubRepo", 140));
            });
            entity.HasKey(r => r.ProjectId);
            entity.Property(r => r.OwnerUserId).IsRequired();
            entity.Property(r => r.ProjectName).IsRequired();
            entity.Property(r => r.ProjectDbType).IsRequired();
            entity.Property(r => r.Category).IsRequired();

            // Slug boş olabilir: benzersizlik yalnızca dolu slug'larda ve sahip bazında.
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

        modelBuilder.Entity<ProjectTag>(entity =>
        {
            entity.ToTable("project_tags", schema);
            entity.HasKey(t => new { t.ProjectId, t.Tag });
            entity.Property(t => t.DisplayTag).IsRequired();
            // Etiket filtresi Tag ile başlıyor; PK ProjectId ile başladığı için onu karşılamaz.
            entity.HasIndex(t => new { t.Tag, t.ProjectId });
            entity.HasOne<ProjectRepository>()
                .WithMany()
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RepoStar>(entity =>
        {
            entity.ToTable("RepoStars", schema);
            entity.HasKey(s => new { s.ProjectId, s.UserId });
            entity.HasIndex(s => s.UserId);
        });

        modelBuilder.Entity<RepoIssue>(entity =>
        {
            entity.ToTable("RepoIssues", schema, t =>
            {
                if (isPostgres)
                    t.HasCheckConstraint(N("RepoIssues", "Body", "Length"), CheckConstraintSql.MaxLength("Body", 20_000));
            });
            entity.HasKey(i => i.Id);
            // Bileşik FK hedefi: etiket bağının proje bütünlüğünü DB zorlar (BL-C1).
            entity.HasAlternateKey(i => new { i.Id, i.ProjectId }).HasName("AK_RepoIssues_Id_ProjectId");
            entity.HasIndex(i => new { i.ProjectId, i.IssueNumber }).IsUnique();
            entity.HasIndex(i => new { i.ProjectId, i.IsClosed });
            entity.HasIndex(i => i.CreatedByUserId);
            entity.Property(i => i.Title).HasMaxLength(256);
            entity.Property(i => i.RelatedTableName).HasMaxLength(128);
            entity.Property(i => i.NslRuleCode).HasMaxLength(32);
            if (isPostgres)
                entity.Property(i => i.Id).HasDefaultValueSql("gen_random_uuid()::text").ValueGeneratedNever();
        });

        modelBuilder.Entity<RepoIssueComment>(entity =>
        {
            entity.ToTable("RepoIssueComments", schema, t =>
            {
                if (isPostgres)
                    t.HasCheckConstraint(N("RepoIssueComments", "Body", "Length"), CheckConstraintSql.MaxLength("Body", 10_000));
            });
            entity.HasKey(c => c.Id);
            entity.HasIndex(c => c.IssueId);
            entity.HasIndex(c => c.AuthorUserId);
            entity.HasOne<RepoIssue>()
                .WithMany()
                .HasForeignKey(c => c.IssueId)
                .OnDelete(DeleteBehavior.Cascade);
            if (isPostgres)
                entity.Property(c => c.Id).HasDefaultValueSql("gen_random_uuid()::text").ValueGeneratedNever();
        });

        modelBuilder.Entity<IssueLabel>(entity =>
        {
            entity.ToTable("issue_labels", schema, t =>
            {
                if (isPostgres)
                    t.HasCheckConstraint("CK_issue_labels_NormalizedName", "\"NormalizedName\" <> ''");
            });
            entity.HasKey(l => l.Id);
            entity.HasAlternateKey(l => new { l.Id, l.ProjectId }).HasName("AK_issue_labels_Id_ProjectId");
            entity.Property(l => l.ProjectId).IsRequired();
            entity.Property(l => l.Name).IsRequired().HasMaxLength(IssueLabelRules.MaxNameLength);
            entity.Property(l => l.NormalizedName).IsRequired().HasMaxLength(IssueLabelRules.MaxNameLength);
            entity.HasIndex(l => new { l.ProjectId, l.NormalizedName }).IsUnique();
            if (isPostgres)
                entity.Property(l => l.Id).HasDefaultValueSql("gen_random_uuid()::text").ValueGeneratedNever();
        });

        modelBuilder.Entity<RepoIssueLabel>(entity =>
        {
            entity.ToTable("repo_issue_labels", schema);
            entity.HasKey(l => new { l.RepoIssueId, l.IssueLabelId });
            entity.Property(l => l.ProjectId).IsRequired();
            entity.Property(l => l.DisplayName).IsRequired().HasMaxLength(IssueLabelRules.MaxNameLength);
            // Filtre etiketten issue'ya gider; PK ters yönde olduğu için bunu karşılamaz.
            entity.HasIndex(l => new { l.IssueLabelId, l.RepoIssueId });
            // İki FK de ProjectId'yi içerir: bir projenin issue'su başka projenin etiketini alamaz.
            entity.HasOne<RepoIssue>()
                .WithMany(i => i.LabelLinks)
                .HasForeignKey(l => new { l.RepoIssueId, l.ProjectId })
                .HasPrincipalKey(i => new { i.Id, i.ProjectId })
                .HasConstraintName("FK_repo_issue_labels_RepoIssues_Project")
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(l => l.Label)
                .WithMany()
                .HasForeignKey(l => new { l.IssueLabelId, l.ProjectId })
                .HasPrincipalKey(i => new { i.Id, i.ProjectId })
                .HasConstraintName("FK_repo_issue_labels_issue_labels_Project")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

internal sealed class HubDbContextFactory : IDesignTimeDbContextFactory<HubDbContext>
{
    public HubDbContext CreateDbContext(string[] args) =>
        new(ModuleDbContextRegistration.MigrationOptions<HubDbContext>(
            ModuleDbContextRegistration.DesignTimeConnectionString(), HubDbContext.SchemaName));
}
