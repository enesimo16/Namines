using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Modules;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Core.Models.Auth;

namespace Namines.Infrastructure.Data
{
    public class AuthDbContext : IdentityDbContext<ApplicationUser>
    {
        public DbSet<UserAIPolicy> UserAIPolicies { get; set; } = null!;
        public DbSet<UserAIQuota> UserAIQuotas { get; set; } = null!;
        public DbSet<GlobalAiUsage> GlobalAiUsages { get; set; } = null!;
        public DbSet<OrgAiUsage> OrgAiUsages { get; set; } = null!;
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
        public DbSet<TeamInvite> TeamInvites { get; set; } = null!;

        /// <summary>İşlenmiş Stripe olayları — tekrar iletimi tanımak için (docs/last-review M-2).</summary>
        public DbSet<StripeWebhookEvent> StripeWebhookEvents { get; set; } = null!;
        public DbSet<PendingSubscriptionCancellation> PendingSubscriptionCancellations { get; set; } = null!;
        /// <summary>
        /// Desk SQL konsolunun sorgu gecmisi (F-01) -- KULLANICIYA OZEL okunur.
        /// Denetim kaydi degil: <see cref="GatewayAuditEntry"/> hesap
        /// verebilirlik icin SQL metnini saklamiyor, bu ise tam olarak onu
        /// saklamak zorunda.
        /// </summary>

        /// <summary>Kullanicinin ada kaydettigi sorgular (F-02).</summary>

        public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            var isPostgres = Database.IsNpgsql();

            // ── Modular Monolith: Flow, Vault, Ground, Desk Module Model Configurations ──
            Namines.Flow.Configurations.FlowModelConfiguration.Configure(builder, isPostgres ? "flow" : null);
            Namines.Vault.Configurations.VaultModelConfiguration.Configure(builder, isPostgres ? "namines_vault" : null);
            Namines.Ground.Configurations.GroundModelConfiguration.Configure(builder, isPostgres ? "ground" : null);
            Namines.Desk.Configurations.DeskModelConfiguration.Configure(builder, isPostgres ? "desk" : null);

            // Gateway tabloları kendi fazına kadar Auth modelinde; allowlist çocukları zaten gateway şemasında.
            GatewayModelConfiguration.ConfigureAllowlistTables(builder, isPostgres ? GatewayModelConfiguration.DefaultSchemaName : null, excludeFromMigrations: false);

            builder.Entity<PendingSubscriptionCancellation>(entity =>
            {
                entity.Property(c => c.SubscriptionId).IsRequired();
                // İşçi yalnızca açık satırlara bakıyor; tamamlananlar indeksi şişirmesin.
                entity.HasIndex(c => c.NextAttemptAt)
                    .HasDatabaseName("IX_PendingSubscriptionCancellations_Open")
                    .HasFilter("\"CompletedAt\" IS NULL");
            });

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

            // Ekip havuzu: organizasyon + gün başına tek satır. Aynı yarış-güvenli
            // desen — bileşik unique index, eşzamanlı iki isteğin ikinci insert'ini
            // düşürüyor ve çağıran güncellemeye dönüyor.
            builder.Entity<OrgAiUsage>()
                .HasIndex(o => new { o.OrganizationId, o.Date })
                .IsUnique();

            // ── G11 — ChangeRequest / ChangeRequestApproval ("Database PR", new-phase/29) ──

            builder.Entity<ChangeRequest>()
                .HasOne(c => c.CreatedByUser)
                .WithMany()
                .HasForeignKey(c => c.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<ChangeRequest>()
                .HasIndex(c => new { c.ProjectId, c.CreatedAt });

            // Studio'ya giden FK'ler DB'de duruyor (cascade/no action), modelde yok: indeksleri açıkça tutulur.
            builder.Entity<ChangeRequest>().HasIndex(c => c.BranchId);
            builder.Entity<ChangeRequest>().HasIndex(c => c.HeadVersionId);
            builder.Entity<ChangeRequest>().HasIndex(c => c.BaseVersionId);

            builder.Entity<ChangeRequestApproval>()
                .HasOne(a => a.ChangeRequest)
                .WithMany(c => c.Approvals)
                .HasForeignKey(a => a.ChangeRequestId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ChangeRequestApproval>()
                .HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Bir kullanıcı aynı CR'a yalnızca bir kez oy verebilir.
            builder.Entity<ChangeRequestApproval>()
                .HasIndex(a => new { a.ChangeRequestId, a.UserId })
                .IsUnique();

            // DENETİM İZİ YAŞAMALI (DB-10): ChangeRequestId'ye bilinçli olarak FK YOK.
            // Eskiden CR → audit log CASCADE'iydi; proje/branch silinince "migration'ı
            // kim onayladı" kanıtı da sessizce giderdi. Artık CR kimliği tarihsel
            // referans olarak kalır; ProjectId / ChangeRequestTitle anlık kopyadır.
            builder.Entity<ChangeRequestAuditLog>()
                .HasIndex(a => new { a.ProjectId, a.CreatedAt });

            builder.Entity<ChangeRequestAuditLog>()
                .HasOne(a => a.ActorUser)
                .WithMany()
                .HasForeignKey(a => a.ActorUserId)
                .OnDelete(DeleteBehavior.SetNull);

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
                // Faturalama kaydı kullanıcıdan uzun yaşamalı (vergi/mutabakat saklama
                // süresi): hesap silinince anonimleşir (UserId NULL), satır gitmez.
                .OnDelete(DeleteBehavior.SetNull);

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

            builder.Entity<GatewayApiKey>().HasIndex(k => k.ProjectId);

            builder.Entity<GatewayApiKey>()
                .HasOne(k => k.CreatedByUser)
                .WithMany()
                .HasForeignKey(k => k.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Proje başına tablo adı tekil: aynı tabloya iki çelişen izin satırı
            // olsaydı hangisinin geçerli olduğu kayda bağlı hale gelirdi.
            builder.Entity<GatewayTablePermission>()
                .HasIndex(p => new { p.ProjectId, p.TableName })
                .IsUnique();

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

            // Token özeti UNIQUE: aynı özetten iki davet, hangisinin tüketildiği
            // belirsiz kalırdı ve tek-kullanımlık garantisi çökerdi.
            builder.Entity<TeamInvite>()
                .HasIndex(i => i.TokenHash)
                .IsUnique();

            builder.Entity<TeamInvite>()
                .HasIndex(i => i.OrganizationId);

            // Gateway denetimi: "bu projenin son kayıtları" + analitik aralık sorguları (DB-12).
            // Tablonun en hızlı büyüyen parçası; indeks migration'da CONCURRENTLY kurulur.
            builder.Entity<GatewayAuditEntry>()
                .HasIndex(a => new { a.ProjectId, a.CreatedAt })
                .IsDescending(false, true);

            // Admin kullanım sorguları (Resource == X && OccurredAt aralığı) — DB-16.
            builder.Entity<UsageEvent>()
                .HasIndex(e => new { e.Resource, e.OccurredAt });

            // Stripe olay kimliği BIRINCIL ANAHTAR: ikinci ekleme denemesi
            // veritabanı seviyesinde çakışır, yani iki webhook isteği aynı anda
            // gelse bile yalnızca biri işleyebiliyor. Uygulama içi bir "var mı?"
            // kontrolü bu yarışı çözmezdi (bkz. StripeWebhookEvent.Id notu).
            builder.Entity<StripeWebhookEvent>()
                .HasKey(e => e.Id);

            // Sıra kontrolü müşteri başına ve olayın ÜRETİLME zamanına göre
            // okunuyor: "bu müşteri için gördüğüm en yeni olay hangisi?"
            builder.Entity<StripeWebhookEvent>()
                .HasIndex(e => new { e.StripeCustomerId, e.EventCreatedAt });

            // ── Referans bütünlüğü (docs/last-review/05 DB-05/06) ──
            builder.Entity<OrgAiUsage>()
                .HasOne<Organization>()
                .WithMany()
                .HasForeignKey(o => o.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

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
            // E-posta: yalnızca uygulamada (FindByEmail sonra Create) korunuyordu; yarışta iki aynı
            // e-postalı hesap oluşup FindByEmailAsync'i kilitleyebiliyordu. Identity'nin varsayılan
            // adıyla ("EmailIndex") UNIQUE yapılıyor; NULL e-postalar PG'de çakışmaz.
            builder.Entity<ApplicationUser>()
                .HasIndex(u => u.NormalizedEmail)
                .HasDatabaseName("EmailIndex")
                .IsUnique();

            // Kullanıcı başına EN FAZLA bir kişisel organizasyon. GetOrCreatePersonalOrgAsync'in
            // "yarış-güvenli" catch'i ancak bu kısıtla gerçekten tetiklenir.
            builder.Entity<Organization>()
                .HasIndex(o => o.CreatedByUserId)
                .IsUnique()
                .HasDatabaseName("UX_Organizations_PersonalOwner")
                .HasFilter("\"IsPersonal\" = true");

            // Filtreli unique indeks FK indeksinin yerini tutmaz (kişisel olmayan org'lar için
            // SET NULL / sorgu aramaları): düz indeks ayrıca duruyor.
            builder.Entity<Organization>()
                .HasIndex(o => o.CreatedByUserId, "IX_Organizations_CreatedByUserId");

            // API anahtarı özeti tekil: doğrulama özetle doğrudan arayabilsin, çakışma tek
            // anahtarın iki hesaba ait olması demek olurdu.
            builder.Entity<GatewayApiKey>()
                .HasIndex(k => k.KeyHash)
                .IsUnique();

            ConfigureCheckConstraints(builder, isPostgres);
            if (isPostgres) ConfigureGuidKeyDefaults(builder);
        }

        /// <summary>
        /// Guid-dizgesi birincil anahtarlar (DB-24): uygulama anahtarı hâlâ KENDİ üretir
        /// (<c>Guid.NewGuid().ToString()</c>); DB varsayılanı yalnızca elle SQL / migration ile
        /// eklenen satırlar içindir (<c>gen_random_uuid()::text</c>, PG13+ çekirdek).
        ///
        /// <c>ValueGeneratedNever</c> BİLİNÇLİ: <c>HasDefaultValueSql</c> tek başına anahtarı
        /// "store-generated" yapar ve EF, navigasyon üzerinden bulunan değeri-dolu yeni bir
        /// varlığı INSERT yerine UPDATE sanabilir; Never ile EF davranışı bugünküyle aynı kalır
        /// (Id her zaman INSERT'e dahil), sadece DDL'e DEFAULT yazılır.
        /// Stripe olay kimliği gibi doğal anahtarlar ve Identity tabloları listede YOK.
        /// </summary>
        private static void ConfigureGuidKeyDefaults(ModelBuilder builder)
        {
            var guidKeyed = new[]
            {
                typeof(PendingSubscriptionCancellation), typeof(ChangeRequest),
                typeof(ChangeRequestApproval), typeof(ChangeRequestAuditLog), typeof(Organization),
                typeof(TeamInvite), typeof(GatewayApiKey), typeof(GatewayTablePermission),
                typeof(DeskHandoffToken), typeof(SqlQueryHistoryEntry),
                typeof(SavedQuery), typeof(VaultBackup), typeof(VaultRestore), typeof(VaultSchedule),
                typeof(GroundDatabase), typeof(AutomationRule), typeof(AutomationAction),
                typeof(AutomationRunLog), typeof(GatewayApiKeyAllowedOrigin), typeof(GatewayApiKeyAllowedIp),
                typeof(GatewayTableMaskedColumn),
            };

            foreach (var type in guidKeyed)
            {
                builder.Entity(type).Property("Id")
                    .HasDefaultValueSql("gen_random_uuid()::text")
                    .ValueGeneratedNever();
            }
        }

        /// <summary>
        /// CHECK kısıtları (docs/last-review/05 DB-21/22). İfadeler <see cref="CheckConstraintSql"/>'den
        /// gelir: enum'a yeni üye eklemek migration gerektirir. Migration'larda bu kısıtlar
        /// <c>NOT VALID</c> eklenir (yeni yazımlar zorunlu, eski satırlar bozulmaz) ve yalnızca
        /// ihlal eden satır yoksa VALIDATE edilir.
        ///
        /// Metin enum'lar (automation_run_logs.Status/TriggerType, DbType, Provider, Engine) bilinçli
        /// olarak DIŞARIDA: C#'ta bir enum'a değil serbest dizgelere bağlılar, kapalı bir küme yok.
        /// </summary>
        private static void ConfigureCheckConstraints(ModelBuilder builder, bool isPostgres)
        {
            static string N(string table, string col, string? suffix = null) => CheckConstraintSql.Name(table, col, suffix);

            builder.Entity<ApplicationUser>().ToTable(t =>
                t.HasCheckConstraint(N("AspNetUsers", "Type"), CheckConstraintSql.EnumIn<UserType>("Type")));

            builder.Entity<GatewayAuditEntry>().ToTable(t =>
                t.HasCheckConstraint(N("GatewayAuditEntries", "Kind"), CheckConstraintSql.EnumIn<GatewayWriteKind>("Kind")));

            builder.Entity<OrganizationMember>().ToTable(t =>
                t.HasCheckConstraint(N("OrganizationMembers", "Role"), CheckConstraintSql.EnumIn<OrgRole>("Role")));

            builder.Entity<TeamInvite>().ToTable(t =>
                t.HasCheckConstraint(N("TeamInvites", "Role"), CheckConstraintSql.EnumIn<OrgRole>("Role")));

            builder.Entity<UsageEvent>().ToTable(t =>
            {
                t.HasCheckConstraint(N("UsageEvents", "Resource"), CheckConstraintSql.EnumIn<UsageResource>("Resource"));
                t.HasCheckConstraint(N("UsageEvents", "Quantity"), CheckConstraintSql.AtLeast("Quantity", 0));
            });

            builder.Entity<ChangeRequest>().ToTable(t =>
            {
                t.HasCheckConstraint(N("ChangeRequests", "Status"), CheckConstraintSql.EnumIn<ChangeRequestStatus>("Status"));
                t.HasCheckConstraint(N("ChangeRequests", "RiskLevel"), CheckConstraintSql.EnumIn<RiskLevel>("RiskLevel"));
            });

            builder.Entity<ChangeRequestApproval>().ToTable(t =>
                t.HasCheckConstraint(N("ChangeRequestApprovals", "Decision"), CheckConstraintSql.EnumIn<ApprovalDecision>("Decision")));

            builder.Entity<ChangeRequestAuditLog>().ToTable(t =>
                t.HasCheckConstraint(N("ChangeRequestAuditLogs", "Action"), CheckConstraintSql.EnumIn<ChangeRequestAuditAction>("Action")));

            builder.Entity<GatewayApiKey>().ToTable(t =>
                t.HasCheckConstraint(N("GatewayApiKeys", "RateLimitPerMinute"), CheckConstraintSql.AtLeast("RateLimitPerMinute", 1)));

            builder.Entity<UserAIPolicy>().ToTable(t =>
            {
                foreach (var col in new[] { "SmartSeed", "Documentation", "Scaffolding", "SchemaGeneration", "SchemaRevision", "DbaAnalysis", "Migration", "Voice" })
                    t.HasCheckConstraint(N("UserAIPolicies", col), CheckConstraintSql.EnumIn<AIMode>(col));
            });

        }
    }
}
