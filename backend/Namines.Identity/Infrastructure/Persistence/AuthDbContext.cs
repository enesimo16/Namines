using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Identity.Domain;

namespace Namines.Identity.Infrastructure.Persistence
{
    /// <summary>
    /// Identity modülünün bağlamı: kullanıcılar, roller, talepler, dış girişler, jetonlar (ASP.NET Identity) ile
    /// organizasyonlar, üyelikler ve ekip davetleri. Başka hiçbir modülün tablosu bu modelde yok (mimari test,
    /// model üzerinde yansımayla doğrular) ve bağlam INTERNAL: başka hiçbir derleme ona başvuramaz.
    ///
    /// Zincirin geçmişi burada: <c>public.__EFMigrationsHistory</c>. 14.2 öncesi migration'lar diğer modüllerin
    /// tablolarını da kurdu; onlar silinmedi (geçmiş yeniden yazılmaz), ama tablolar artık kendi modül zincirlerinin.
    /// </summary>
    internal class AuthDbContext : IdentityDbContext<ApplicationUser>, Namines.Identity.Application.IIdentityData
    {
        public DbSet<Organization> Organizations { get; set; } = null!;
        public DbSet<OrganizationMember> OrganizationMembers { get; set; } = null!;
        public DbSet<TeamInvite> TeamInvites { get; set; } = null!;

        public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            var isPostgres = Database.IsNpgsql();

            builder.Entity<Organization>()
                .HasOne(o => o.CreatedByUser)
                .WithMany()
                .HasForeignKey(o => o.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

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

            // ── Team davetleri ──────────────────────────────────────────────
            builder.Entity<TeamInvite>()
                .HasOne(i => i.Organization)
                .WithMany()
                .HasForeignKey(i => i.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            // Token özeti UNIQUE: aynı özetten iki davet, hangisinin tüketildiği belirsiz kalırdı ve
            // tek-kullanımlık garantisi çökerdi.
            builder.Entity<TeamInvite>()
                .HasIndex(i => i.TokenHash)
                .IsUnique();

            builder.Entity<TeamInvite>()
                .HasIndex(i => i.OrganizationId);

            // Davet atıfları: kullanıcı silinince davet kaydı kalır, atıf boşalır.
            builder.Entity<TeamInvite>()
                .HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(i => i.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<TeamInvite>()
                .HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(i => i.AcceptedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // ── Benzersizlik (DB-26/27/28/29) ──
            // E-posta: yalnızca uygulamada (FindByEmail sonra Create) korunuyordu; yarışta iki aynı e-postalı hesap
            // oluşup FindByEmailAsync'i kilitleyebiliyordu. Identity'nin varsayılan adıyla ("EmailIndex") UNIQUE.
            builder.Entity<ApplicationUser>()
                .HasIndex(u => u.NormalizedEmail)
                .HasDatabaseName("EmailIndex")
                .IsUnique();

            // Kullanıcı başına EN FAZLA bir kişisel organizasyon. GetOrCreatePersonalOrgAsync'in "yarış-güvenli"
            // catch'i ancak bu kısıtla gerçekten tetiklenir.
            builder.Entity<Organization>()
                .HasIndex(o => o.CreatedByUserId)
                .IsUnique()
                .HasDatabaseName("UX_Organizations_PersonalOwner")
                .HasFilter("\"IsPersonal\" = true");

            // Filtreli unique indeks FK indeksinin yerini tutmaz (kişisel olmayan org'lar için SET NULL / sorgu
            // aramaları): düz indeks ayrıca duruyor.
            builder.Entity<Organization>()
                .HasIndex(o => o.CreatedByUserId, "IX_Organizations_CreatedByUserId");

            ConfigureCheckConstraints(builder);
            if (isPostgres) ConfigureGuidKeyDefaults(builder);
        }

        /// <summary>
        /// Guid-dizgesi birincil anahtarlar (DB-24): uygulama anahtarı hâlâ KENDİ üretir; DB varsayılanı yalnızca elle
        /// SQL / migration ile eklenen satırlar içindir. <c>ValueGeneratedNever</c> BİLİNÇLİ: <c>HasDefaultValueSql</c>
        /// tek başına anahtarı "store-generated" yapar ve EF, navigasyondan bulunan dolu bir varlığı INSERT yerine
        /// UPDATE sanabilir.
        /// </summary>
        private static void ConfigureGuidKeyDefaults(ModelBuilder builder)
        {
            foreach (var type in new[] { typeof(Organization), typeof(TeamInvite) })
            {
                builder.Entity(type).Property("Id")
                    .HasDefaultValueSql("gen_random_uuid()::text")
                    .ValueGeneratedNever();
            }
        }

        /// <summary>
        /// CHECK kısıtları (docs/last-review/05 DB-21/22). İfadeler <see cref="CheckConstraintSql"/>'den gelir: enum'a
        /// yeni üye eklemek migration gerektirir. Migration'larda bu kısıtlar <c>NOT VALID</c> eklenir.
        /// </summary>
        private static void ConfigureCheckConstraints(ModelBuilder builder)
        {
            static string N(string table, string col) => CheckConstraintSql.Name(table, col);

            builder.Entity<ApplicationUser>().ToTable(t =>
                t.HasCheckConstraint(N("AspNetUsers", "Type"), CheckConstraintSql.EnumIn<UserType>("Type")));

            builder.Entity<OrganizationMember>().ToTable(t =>
                t.HasCheckConstraint(N("OrganizationMembers", "Role"), CheckConstraintSql.EnumIn<OrgRole>("Role")));

            builder.Entity<TeamInvite>().ToTable(t =>
                t.HasCheckConstraint(N("TeamInvites", "Role"), CheckConstraintSql.EnumIn<OrgRole>("Role")));
        }
    }
}
