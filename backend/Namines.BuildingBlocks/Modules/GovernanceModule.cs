using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;

namespace Namines.BuildingBlocks.Modules;

/// <summary>
/// Governance (Review &amp; ChangeRequest) modülünün dışa açtığı resmi sözleşme (Order: 40, Şema: <c>governance</c>).
/// </summary>
public interface IGovernanceModule
{
    GovernanceDbContext DbContext { get; }
}

public sealed class GovernanceModule : IGovernanceModule
{
    public GovernanceModule(GovernanceDbContext dbContext)
    {
        DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public GovernanceDbContext DbContext { get; }
}

public static class GovernanceModelConfiguration
{
    public const string DefaultSchemaName = "governance";

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
            builder.Ignore<Branch>();
            builder.Ignore<SchemaVersion>();
        }

        builder.Entity<ChangeRequest>(entity =>
        {
            entity.ToTable("ChangeRequests", schema);
            entity.HasKey(c => c.Id);
            entity.HasIndex(c => new { c.ProjectId, c.CreatedAt });

            if (configureCrossModuleForeignKeys)
            {
                entity.HasOne(c => c.Project)
                    .WithMany()
                    .HasForeignKey(c => c.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(c => c.Branch)
                    .WithMany()
                    .HasForeignKey(c => c.BranchId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(c => c.HeadVersion)
                    .WithMany()
                    .HasForeignKey(c => c.HeadVersionId)
                    .OnDelete(DeleteBehavior.NoAction);
                entity.HasOne(c => c.BaseVersion)
                    .WithMany()
                    .HasForeignKey(c => c.BaseVersionId)
                    .OnDelete(DeleteBehavior.NoAction);
                entity.HasOne(c => c.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(c => c.CreatedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);
            }
            else
            {
                entity.Ignore(c => c.Project);
                entity.Ignore(c => c.Branch);
                entity.Ignore(c => c.HeadVersion);
                entity.Ignore(c => c.BaseVersion);
                entity.Ignore(c => c.CreatedByUser);
            }
        });

        builder.Entity<ChangeRequestApproval>(entity =>
        {
            entity.ToTable("ChangeRequestApprovals", schema);
            entity.HasKey(a => a.Id);
            entity.HasOne(a => a.ChangeRequest)
                .WithMany(c => c.Approvals)
                .HasForeignKey(a => a.ChangeRequestId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(a => new { a.ChangeRequestId, a.UserId }).IsUnique();

            if (configureCrossModuleForeignKeys)
            {
                entity.HasOne(a => a.User)
                    .WithMany()
                    .HasForeignKey(a => a.UserId)
                    .OnDelete(DeleteBehavior.SetNull);
            }
            else
            {
                entity.Ignore(a => a.User);
            }
        });

        builder.Entity<ChangeRequestAuditLog>(entity =>
        {
            entity.ToTable("ChangeRequestAuditLogs", schema);
            entity.HasKey(a => a.Id);
            entity.HasIndex(a => new { a.ProjectId, a.CreatedAt });
            entity.HasIndex(a => new { a.ChangeRequestId, a.CreatedAt });

            if (configureCrossModuleForeignKeys)
            {
                entity.HasOne(a => a.ActorUser)
                    .WithMany()
                    .HasForeignKey(a => a.ActorUserId)
                    .OnDelete(DeleteBehavior.SetNull);
            }
            else
            {
                entity.Ignore(a => a.ActorUser);
            }
        });

        builder.Entity<SqlExecutionAudit>(entity =>
        {
            entity.ToTable("SqlExecutionAudits", schema);
            entity.HasKey(a => a.Id);
            entity.HasIndex(a => new { a.UserId, a.CreatedAt });
            entity.HasIndex(a => a.CreatedAt);
            entity.HasIndex(a => a.ProjectId);
        });
    }
}

public sealed class GovernanceDbContext : DbContext
{
    public const string DefaultSchema = GovernanceModelConfiguration.DefaultSchemaName;
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public GovernanceDbContext(DbContextOptions<GovernanceDbContext> options) : base(options)
    {
    }

    public DbSet<ChangeRequest> ChangeRequests => Set<ChangeRequest>();
    public DbSet<ChangeRequestApproval> ChangeRequestApprovals => Set<ChangeRequestApproval>();
    public DbSet<ChangeRequestAuditLog> ChangeRequestAuditLogs => Set<ChangeRequestAuditLog>();
    public DbSet<SqlExecutionAudit> SqlExecutionAudits => Set<SqlExecutionAudit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var isRelational = Database.IsRelational();
        var schema = isRelational && Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
            ? DefaultSchema
            : null;

        if (schema is not null)
            modelBuilder.HasDefaultSchema(schema);

        GovernanceModelConfiguration.Configure(modelBuilder, schema, configureCrossModuleForeignKeys: false);
    }
}
