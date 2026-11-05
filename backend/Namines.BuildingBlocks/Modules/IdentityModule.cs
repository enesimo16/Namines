using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;

namespace Namines.BuildingBlocks.Modules;

/// <summary>
/// Identity modülünün dışa açtığı resmi sözleşme (Order: 10, Şema: <c>identity</c>).
/// </summary>
public interface IIdentityModule
{
    IdentityModuleDbContext DbContext { get; }
}

public sealed class IdentityModule : IIdentityModule
{
    public IdentityModule(IdentityModuleDbContext dbContext)
    {
        DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public IdentityModuleDbContext DbContext { get; }
}

public static class IdentityModelConfiguration
{
    public const string DefaultSchemaName = "identity";

    public static void Configure(ModelBuilder builder, string? schema = DefaultSchemaName)
    {
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("AspNetUsers", schema);
            entity.HasKey(u => u.Id);
            entity.HasIndex(u => u.StripeCustomerId);
            entity.HasIndex(u => u.NormalizedEmail)
                .HasDatabaseName("EmailIndex")
                .IsUnique();
        });

        builder.Entity<Organization>(entity =>
        {
            entity.ToTable("Organizations", schema);
            entity.HasKey(o => o.Id);
            entity.HasOne(o => o.CreatedByUser)
                .WithMany()
                .HasForeignKey(o => o.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(o => o.CreatedByUserId)
                .IsUnique()
                .HasDatabaseName("UX_Organizations_PersonalOwner")
                .HasFilter("\"IsPersonal\" = true");
            entity.HasIndex(o => o.CreatedByUserId, "IX_Organizations_CreatedByUserId");
        });

        builder.Entity<OrganizationMember>(entity =>
        {
            entity.ToTable("OrganizationMembers", schema);
            entity.HasKey(m => new { m.OrganizationId, m.UserId });
            entity.HasOne(m => m.Organization)
                .WithMany(o => o.Members)
                .HasForeignKey(m => m.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(m => m.User)
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(m => m.UserId);
        });

        builder.Entity<TeamInvite>(entity =>
        {
            entity.ToTable("TeamInvites", schema);
            entity.HasKey(i => i.Id);
            entity.HasOne(i => i.Organization)
                .WithMany()
                .HasForeignKey(i => i.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(i => i.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(i => i.AcceptedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(i => i.TokenHash).IsUnique();
            entity.HasIndex(i => i.OrganizationId);
        });
    }
}

public sealed class IdentityModuleDbContext : DbContext
{
    public const string DefaultSchema = IdentityModelConfiguration.DefaultSchemaName;
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public IdentityModuleDbContext(DbContextOptions<IdentityModuleDbContext> options) : base(options)
    {
    }

    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();
    public DbSet<TeamInvite> TeamInvites => Set<TeamInvite>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var isRelational = Database.IsRelational();
        var schema = isRelational && Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
            ? DefaultSchema
            : null;

        if (schema is not null)
            modelBuilder.HasDefaultSchema(schema);

        IdentityModelConfiguration.Configure(modelBuilder, schema);

        // D-2'ye kadar: tablolar public'te ve DDL Auth zincirinde; bu bağlam kullanılmıyor ama yanlış şemaya eşli kalmasın.
        NonOwnerContext.MapToActualSchema(modelBuilder, schema is null ? null : "public");
    }
}
