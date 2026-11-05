using System;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Application;
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

    /// <summary>
    /// Bağlantı ve paylaşım tablolarının TEK eşlemesi. Hem <see cref="StudioDbContext"/> hem de
    /// migration zincirinin sahibi AuthDbContext buradan okur; iki kopya zamanla ayrışırdı.
    /// </summary>
    public static void ConfigureOwnedTables(ModelBuilder builder, string? schema, bool excludeFromMigrations)
    {
        builder.Entity<ProjectConnection>(entity =>
        {
            entity.ToTable("project_connections", schema, t =>
            {
                if (excludeFromMigrations) t.ExcludeFromMigrations();
            });
            entity.HasKey(c => c.ProjectId);
            entity.HasOne<CloudProject>()
                .WithOne()
                .HasForeignKey<ProjectConnection>(c => c.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProjectShare>(entity =>
        {
            entity.ToTable("project_shares", schema, t =>
            {
                if (excludeFromMigrations) t.ExcludeFromMigrations();
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
        });
    }
}

/// <summary>
/// Studio'nun çalışma zamanı bağlamı. Yalnızca taşınmış tabloları eşler; Branch/SchemaVersion gibi
/// henüz <c>public</c>'te duran tablolar kendi fazına (D-1) kadar burada YOK: var olmayan bir
/// ilişkiye eşlemek çalışma zamanında patlıyordu.
///
/// Şemayı AuthDbContext migration zinciri kuruyor, bu yüzden tablolar burada
/// <c>ExcludeFromMigrations</c>: iki bağlam aynı DDL'i üretmesin.
/// </summary>
public sealed class StudioDbContext : DbContext
{
    public const string DefaultSchema = StudioModelConfiguration.DefaultSchemaName;
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public StudioDbContext(DbContextOptions<StudioDbContext> options) : base(options)
    {
    }

    public DbSet<CloudProject> Projects => Set<CloudProject>();
    public DbSet<ProjectConnection> ProjectConnections => Set<ProjectConnection>();
    public DbSet<ProjectShare> ProjectShares => Set<ProjectShare>();

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

        modelBuilder.Entity<CloudProject>(entity =>
        {
            entity.ToTable("projects", schema, t => t.ExcludeFromMigrations());
            entity.HasKey(p => p.Id);
            entity.Ignore(p => p.User);
            entity.Ignore(p => p.Organization);

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
        });

        StudioModelConfiguration.ConfigureOwnedTables(modelBuilder, schema, excludeFromMigrations: true);
    }
}
