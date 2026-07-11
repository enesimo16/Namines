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
        }
    }
}
