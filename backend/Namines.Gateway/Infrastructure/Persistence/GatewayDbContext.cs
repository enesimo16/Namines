using Namines.Gateway.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;
using Namines.Gateway.Domain;

namespace Namines.Gateway.Infrastructure.Persistence;

/// <summary>
/// <c>gateway</c> şemasının tek bağlamı ve migration zincirinin sahibi. Internal: anahtar özetine ve
/// izin tablolarına modül dışından erişilemez. Projeye (cascade) ve üreticiye (SET NULL) giden FK'ler DB'de
/// duruyor, bu modelde yok: Gateway proje ve kullanıcıyı yalnızca kimlikle tanır.
/// </summary>
internal sealed class GatewayDbContext : DbContext, IGatewayData
{
    public const string SchemaName = "gateway";

    public GatewayDbContext(DbContextOptions<GatewayDbContext> options) : base(options)
    {
    }

    public DbSet<GatewayApiKey> GatewayApiKeys => Set<GatewayApiKey>();
    public DbSet<GatewayTablePermission> GatewayTablePermissions => Set<GatewayTablePermission>();
    public DbSet<GatewayAuditEntry> GatewayAuditEntries => Set<GatewayAuditEntry>();
    public DbSet<GatewayApiKeyAllowedOrigin> AllowedOrigins => Set<GatewayApiKeyAllowedOrigin>();
    public DbSet<GatewayApiKeyAllowedIp> AllowedIps => Set<GatewayApiKeyAllowedIp>();
    public DbSet<GatewayTableMaskedColumn> MaskedColumns => Set<GatewayTableMaskedColumn>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var isPostgres = Database.IsNpgsql();
        var schema = isPostgres ? SchemaName : null;
        if (isPostgres) modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<GatewayApiKey>(entity =>
        {
            entity.ToTable("GatewayApiKeys", schema, t =>
                t.HasCheckConstraint(CheckConstraintSql.Name("GatewayApiKeys", "RateLimitPerMinute"), CheckConstraintSql.AtLeast("RateLimitPerMinute", 1)));
            entity.HasKey(k => k.Id);
            // Doğrulama önce önekle aday bulur; index olmadan her istek tüm tabloyu tarardı.
            entity.HasIndex(k => k.Prefix);
            // Özet tekil: çakışma tek anahtarın iki hesaba ait olması demek olurdu.
            entity.HasIndex(k => k.KeyHash).IsUnique();
            entity.HasIndex(k => k.ProjectId);
            entity.HasIndex(k => k.CreatedByUserId);
            if (isPostgres)
                entity.Property(k => k.Id).HasDefaultValueSql("gen_random_uuid()::text").ValueGeneratedNever();
        });

        modelBuilder.Entity<GatewayTablePermission>(entity =>
        {
            entity.ToTable("GatewayTablePermissions", schema);
            entity.HasKey(p => p.Id);
            // Proje başına tablo adı tekil: iki çelişen izin satırında geçerli olan kayda bağlı kalırdı.
            entity.HasIndex(p => new { p.ProjectId, p.TableName }).IsUnique();
            if (isPostgres)
                entity.Property(p => p.Id).HasDefaultValueSql("gen_random_uuid()::text").ValueGeneratedNever();
        });

        modelBuilder.Entity<GatewayAuditEntry>(entity =>
        {
            entity.ToTable("GatewayAuditEntries", schema, t =>
                t.HasCheckConstraint(CheckConstraintSql.Name("GatewayAuditEntries", "Kind"), CheckConstraintSql.EnumIn<GatewayWriteKind>("Kind")));
            entity.HasKey(a => a.Id);
            // "Bu projenin son kayıtları" ve analitik aralık sorguları; tablonun en hızlı büyüyen parçası.
            entity.HasIndex(a => new { a.ProjectId, a.CreatedAt }).IsDescending(false, true);
        });

        modelBuilder.Entity<GatewayApiKeyAllowedOrigin>(entity =>
        {
            entity.ToTable("api_key_allowed_origins", schema, t =>
            {
                if (isPostgres)
                    t.HasCheckConstraint("CK_api_key_allowed_origins_Origin", $"\"Origin\" ~ '{GatewayAllowlistRules.OriginPattern}'");
            });
            entity.HasKey(o => o.Id);
            entity.Property(o => o.ApiKeyId).IsRequired();
            entity.Property(o => o.Origin).IsRequired().HasMaxLength(GatewayAllowlistRules.MaxOriginLength);
            entity.HasIndex(o => new { o.ApiKeyId, o.Origin }).IsUnique();
            entity.HasOne<GatewayApiKey>()
                .WithMany(k => k.OriginRules)
                .HasForeignKey(o => o.ApiKeyId)
                .OnDelete(DeleteBehavior.Cascade);
            if (isPostgres)
                entity.Property(o => o.Id).HasDefaultValueSql("gen_random_uuid()::text").ValueGeneratedNever();
        });

        modelBuilder.Entity<GatewayApiKeyAllowedIp>(entity =>
        {
            entity.ToTable("api_key_allowed_ips", schema, t =>
            {
                // Metin kolon: SQLite testleri aynı modeli kurabilsin. Doğruluğu PostgreSQL'in kendi
                // cidr ayrıştırıcısı denetler; host biti açık ya da bozuk değer yazılamaz.
                if (isPostgres)
                    t.HasCheckConstraint("CK_api_key_allowed_ips_Cidr", "position('/' in \"Cidr\") > 0 AND (\"Cidr\"::cidr) IS NOT NULL");
            });
            entity.HasKey(i => i.Id);
            entity.Property(i => i.ApiKeyId).IsRequired();
            entity.Property(i => i.Cidr).IsRequired().HasMaxLength(64);
            entity.HasIndex(i => new { i.ApiKeyId, i.Cidr }).IsUnique();
            entity.HasOne<GatewayApiKey>()
                .WithMany(k => k.IpRules)
                .HasForeignKey(i => i.ApiKeyId)
                .OnDelete(DeleteBehavior.Cascade);
            if (isPostgres)
                entity.Property(i => i.Id).HasDefaultValueSql("gen_random_uuid()::text").ValueGeneratedNever();
        });

        modelBuilder.Entity<GatewayTableMaskedColumn>(entity =>
        {
            entity.ToTable("table_masked_columns", schema, t =>
            {
                if (isPostgres)
                    t.HasCheckConstraint("CK_table_masked_columns_ColumnName", "\"ColumnName\" <> ''");
            });
            entity.HasKey(m => m.Id);
            entity.Property(m => m.TablePermissionId).IsRequired();
            entity.Property(m => m.ColumnName).IsRequired().HasMaxLength(GatewayAllowlistRules.MaxMaskedColumnLength);
            entity.HasIndex(m => new { m.TablePermissionId, m.ColumnName }).IsUnique();
            entity.HasOne<GatewayTablePermission>()
                .WithMany(p => p.MaskedColumnRules)
                .HasForeignKey(m => m.TablePermissionId)
                .OnDelete(DeleteBehavior.Cascade);
            if (isPostgres)
                entity.Property(m => m.Id).HasDefaultValueSql("gen_random_uuid()::text").ValueGeneratedNever();
        });
    }
}

internal sealed class GatewayDbContextFactory : IDesignTimeDbContextFactory<GatewayDbContext>
{
    public GatewayDbContext CreateDbContext(string[] args) =>
        new(ModuleDbContextRegistration.MigrationOptions<GatewayDbContext>(
            ModuleDbContextRegistration.DesignTimeConnectionString(), GatewayDbContext.SchemaName));
}
