using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models;
using Namines.Core.Models.Auth;

namespace Namines.Infrastructure.Data
{
    public class AuthDbContext : IdentityDbContext<ApplicationUser>
    {
        public DbSet<CloudProject> CloudProjects { get; set; } = null!;
        public DbSet<UserAIPolicy> UserAIPolicies { get; set; } = null!;
        public DbSet<UserAIQuota> UserAIQuotas { get; set; } = null!;
        public DbSet<Feedback> Feedbacks { get; set; } = null!;
        public DbSet<GlobalAiUsage> GlobalAiUsages { get; set; } = null!;
        public DbSet<Branch> Branches { get; set; } = null!;
        public DbSet<SchemaVersion> SchemaVersions { get; set; } = null!;
        public DbSet<ChangeRequest> ChangeRequests { get; set; } = null!;
        public DbSet<ChangeRequestApproval> ChangeRequestApprovals { get; set; } = null!;
        public DbSet<ChangeRequestAuditLog> ChangeRequestAuditLogs { get; set; } = null!;
        public DbSet<Organization> Organizations { get; set; } = null!;
        public DbSet<OrganizationMember> OrganizationMembers { get; set; } = null!;
        public DbSet<GatewayApiKey> GatewayApiKeys { get; set; } = null!;
        public DbSet<GatewayTablePermission> GatewayTablePermissions { get; set; } = null!;
        public DbSet<GatewayAuditEntry> GatewayAuditEntries { get; set; } = null!;
        public DbSet<UsageEvent> UsageEvents { get; set; } = null!;
        public DbSet<UserBillingSettings> UserBillingSettings { get; set; } = null!;

        public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<CloudProject>()
                .HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<UserAIPolicy>()
                .HasOne(p => p.User)
                .WithOne()
                .HasForeignKey<UserAIPolicy>(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<UserAIQuota>()
                .HasOne(p => p.User)
                .WithOne()
                .HasForeignKey<UserAIQuota>(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Stripe webhook her çağrıda StripeCustomerId ile kullanıcı arıyor → index ile full table scan önlenir.
            builder.Entity<ApplicationUser>()
                .HasIndex(u => u.StripeCustomerId);

            // Global token havuzu: gün başına tek satır (yarışta ikinci insert unique index ile düşer).
            builder.Entity<GlobalAiUsage>()
                .HasIndex(g => g.Date)
                .IsUnique();

            // ── G10 — Branch / SchemaVersion (new-phase/30-SERVER-SIDE-BRANCHING.md §3 Adım 1) ──

            builder.Entity<Branch>()
                .HasOne(b => b.Project)
                .WithMany()
                .HasForeignKey(b => b.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Branch>()
                .HasOne(b => b.ParentBranch)
                .WithMany()
                .HasForeignKey(b => b.ParentBranchId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Branch>()
                .HasOne(b => b.CreatedByUser)
                .WithMany()
                .HasForeignKey(b => b.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Proje içinde branch adı benzersiz olmalı — aynı projede iki "main" olamaz.
            builder.Entity<Branch>()
                .HasIndex(b => new { b.ProjectId, b.Name })
                .IsUnique();

            // Kısmi unique index: bir projede en fazla bir IsDefault=true branch olabilir.
            // 18-CONTROL-PLANE-DDL.md'deki `ux_branches_default`'ın EF karşılığı.
            builder.Entity<Branch>()
                .HasIndex(b => b.ProjectId)
                .IsUnique()
                .HasFilter("\"IsDefault\" = true");

            builder.Entity<SchemaVersion>()
                .HasOne(v => v.Project)
                .WithMany()
                .HasForeignKey(v => v.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<SchemaVersion>()
                .HasOne(v => v.Branch)
                .WithMany()
                .HasForeignKey(v => v.BranchId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<SchemaVersion>()
                .HasOne(v => v.AuthorUser)
                .WithMany()
                .HasForeignKey(v => v.AuthorUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Branch içinde versiyon numarası benzersiz — commit sırasının garantisi.
            builder.Entity<SchemaVersion>()
                .HasIndex(v => new { v.BranchId, v.Version })
                .IsUnique();

            builder.Entity<SchemaVersion>()
                .HasIndex(v => new { v.ProjectId, v.CreatedAt });

            // ── G11 — ChangeRequest / ChangeRequestApproval ("Database PR", new-phase/29) ──

            builder.Entity<ChangeRequest>()
                .HasOne(c => c.Project)
                .WithMany()
                .HasForeignKey(c => c.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ChangeRequest>()
                .HasOne(c => c.Branch)
                .WithMany()
                .HasForeignKey(c => c.BranchId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ChangeRequest>()
                .HasOne(c => c.HeadVersion)
                .WithMany()
                .HasForeignKey(c => c.HeadVersionId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ChangeRequest>()
                .HasOne(c => c.BaseVersion)
                .WithMany()
                .HasForeignKey(c => c.BaseVersionId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ChangeRequest>()
                .HasOne(c => c.CreatedByUser)
                .WithMany()
                .HasForeignKey(c => c.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ChangeRequest>()
                .HasIndex(c => new { c.ProjectId, c.CreatedAt });

            builder.Entity<ChangeRequestApproval>()
                .HasOne(a => a.ChangeRequest)
                .WithMany(c => c.Approvals)
                .HasForeignKey(a => a.ChangeRequestId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ChangeRequestApproval>()
                .HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Bir kullanıcı aynı CR'a yalnızca bir kez oy verebilir.
            builder.Entity<ChangeRequestApproval>()
                .HasIndex(a => new { a.ChangeRequestId, a.UserId })
                .IsUnique();

            builder.Entity<ChangeRequestAuditLog>()
                .HasOne(a => a.ChangeRequest)
                .WithMany()
                .HasForeignKey(a => a.ChangeRequestId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ChangeRequestAuditLog>()
                .HasOne(a => a.ActorUser)
                .WithMany()
                .HasForeignKey(a => a.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ChangeRequestAuditLog>()
                .HasIndex(a => new { a.ChangeRequestId, a.CreatedAt });

            // ── 05 §6 — Organizasyon / üyelik (RBAC sınırı) ──────────────────
            // Kullanım sorguları her zaman (kullanıcı, dönem, kaynak) üçlüsüyle
            // filtreleniyor; index olmadan her fatura hesabı tüm tabloyu tarardı
            // ve bu tablo en hızlı büyüyen tablo olacak.
            builder.Entity<UsageEvent>()
                .HasIndex(e => new { e.UserId, e.BillingPeriod, e.Resource });

            builder.Entity<UsageEvent>()
                .HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Parasal alan: double kullanmak kuruş kayıplarına yol açar.
            builder.Entity<UsageEvent>()
                .Property(e => e.Quantity)
                .HasPrecision(18, 4);

            builder.Entity<UserBillingSettings>()
                .HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<UserBillingSettings>()
                .Property(s => s.MonthlyCapUsd)
                .HasPrecision(18, 2);

            // Anahtar doğrulaması önce önekle aday bulur; index olmadan her istek
            // tüm anahtar tablosunu tarardı.
            builder.Entity<GatewayApiKey>()
                .HasIndex(k => k.Prefix);

            builder.Entity<GatewayApiKey>()
                .HasOne(k => k.Project)
                .WithMany()
                .HasForeignKey(k => k.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<GatewayApiKey>()
                .HasOne(k => k.CreatedByUser)
                .WithMany()
                .HasForeignKey(k => k.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Proje başına tablo adı tekil: aynı tabloya iki çelişen izin satırı
            // olsaydı hangisinin geçerli olduğu kayda bağlı hale gelirdi.
            builder.Entity<GatewayTablePermission>()
                .HasIndex(p => new { p.ProjectId, p.TableName })
                .IsUnique();

            builder.Entity<GatewayTablePermission>()
                .HasOne(p => p.Project)
                .WithMany()
                .HasForeignKey(p => p.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Organization>()
                .HasOne(o => o.CreatedByUser)
                .WithMany()
                .HasForeignKey(o => o.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Bileşik PK — 18-CONTROL-PLANE-DDL.md'deki org_members ile aynı.
            builder.Entity<OrganizationMember>()
                .HasKey(m => new { m.OrganizationId, m.UserId });

            builder.Entity<OrganizationMember>()
                .HasOne(m => m.Organization)
                .WithMany(o => o.Members)
                .HasForeignKey(m => m.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<OrganizationMember>()
                .HasOne(m => m.User)
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<OrganizationMember>()
                .HasIndex(m => m.UserId);

            // Org silinince projeleri de gitmeli (18'deki ON DELETE CASCADE).
            builder.Entity<CloudProject>()
                .HasOne(p => p.Organization)
                .WithMany()
                .HasForeignKey(p => p.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<CloudProject>()
                .HasIndex(p => p.OrganizationId);
        }
    }
}
