using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;

namespace Namines.BuildingBlocks.Modules;

/// <summary>
/// Gateway (REST/Query API Anahtarları, Tablo İzinleri ve Denetim İzi) modülünün
/// dışa açtığı resmi sözleşme (Order: 100, Şema: <c>gateway</c>).
/// </summary>
public interface IGatewayModule
{
    GatewayDbContext DbContext { get; }
}

public sealed class GatewayModule : IGatewayModule
{
    public GatewayModule(GatewayDbContext dbContext)
    {
        DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public GatewayDbContext DbContext { get; }
}

public static class GatewayModelConfiguration
{
    public const string DefaultSchemaName = "gateway";

    /// <summary>
    /// Anahtar ve izin tablolarının bugünkü gerçek şeması. D-1 onları <c>gateway</c>'e taşıyana kadar
    /// <c>public</c>'te duruyorlar; çocuk tablolar zaten <c>gateway</c>'de ve FK şemayı izler.
    /// </summary>
    private const string ParentTablesSchema = "public";

    /// <summary>
    /// Kaynak kısıtı ve maskeleme çocuk tabloları. DDL'i AuthDbContext zinciri kurar, Gateway bağlamı
    /// aynı tabloları <paramref name="excludeFromMigrations"/> ile eşler.
    /// </summary>
    public static void ConfigureAllowlistTables(ModelBuilder builder, string? schema, bool excludeFromMigrations)
    {
        var isPostgres = schema is not null;

        builder.Entity<GatewayApiKeyAllowedOrigin>(entity =>
        {
            entity.ToTable("api_key_allowed_origins", schema, t =>
            {
                if (excludeFromMigrations) t.ExcludeFromMigrations();
                if (isPostgres)
                    t.HasCheckConstraint(
                        "CK_api_key_allowed_origins_Origin",
                        $"\"Origin\" ~ '{GatewayAllowlistRules.OriginPattern}'");
            });
            entity.HasKey(o => o.Id);
            entity.Property(o => o.ApiKeyId).IsRequired();
            entity.Property(o => o.Origin).IsRequired().HasMaxLength(GatewayAllowlistRules.MaxOriginLength);
            entity.HasIndex(o => new { o.ApiKeyId, o.Origin }).IsUnique();
            entity.HasOne<GatewayApiKey>()
                .WithMany(k => k.OriginRules)
                .HasForeignKey(o => o.ApiKeyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<GatewayApiKeyAllowedIp>(entity =>
        {
            entity.ToTable("api_key_allowed_ips", schema, t =>
            {
                if (excludeFromMigrations) t.ExcludeFromMigrations();
                // Metin kolon: SQLite testleri aynı modeli kurabilsin. Doğruluğu PostgreSQL'in kendi
                // cidr ayrıştırıcısı denetler; host biti açık ya da bozuk değer yazılamaz.
                if (isPostgres)
                    t.HasCheckConstraint(
                        "CK_api_key_allowed_ips_Cidr",
                        "position('/' in \"Cidr\") > 0 AND (\"Cidr\"::cidr) IS NOT NULL");
            });
            entity.HasKey(i => i.Id);
            entity.Property(i => i.ApiKeyId).IsRequired();
            entity.Property(i => i.Cidr).IsRequired().HasMaxLength(64);
            entity.HasIndex(i => new { i.ApiKeyId, i.Cidr }).IsUnique();
            entity.HasOne<GatewayApiKey>()
                .WithMany(k => k.IpRules)
                .HasForeignKey(i => i.ApiKeyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<GatewayTableMaskedColumn>(entity =>
        {
            entity.ToTable("table_masked_columns", schema, t =>
            {
                if (excludeFromMigrations) t.ExcludeFromMigrations();
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
        });
    }

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
        }

        builder.Entity<GatewayApiKey>(entity =>
        {
            entity.ToTable("GatewayApiKeys", schema is null ? null : ParentTablesSchema, t => t.ExcludeFromMigrations());
            entity.HasKey(k => k.Id);
            entity.HasIndex(k => k.Prefix);
            entity.HasIndex(k => k.KeyHash).IsUnique();
            if (!configureCrossModuleForeignKeys) entity.Ignore(k => k.CreatedByUser);
        });

        builder.Entity<GatewayTablePermission>(entity =>
        {
            entity.ToTable("GatewayTablePermissions", schema is null ? null : ParentTablesSchema, t => t.ExcludeFromMigrations());
            entity.HasKey(p => p.Id);
            entity.HasIndex(p => new { p.ProjectId, p.TableName }).IsUnique();
        });

        builder.Entity<GatewayAuditEntry>(entity =>
        {
            entity.ToTable("GatewayAuditEntries", schema is null ? null : ParentTablesSchema, t => t.ExcludeFromMigrations());
            entity.HasKey(a => a.Id);
            entity.HasIndex(a => new { a.ProjectId, a.CreatedAt }).IsDescending(false, true);
        });

        ConfigureAllowlistTables(builder, schema, excludeFromMigrations: true);
    }
}

public sealed class GatewayDbContext : DbContext
{
    public const string DefaultSchema = GatewayModelConfiguration.DefaultSchemaName;
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public GatewayDbContext(DbContextOptions<GatewayDbContext> options) : base(options)
    {
    }

    public DbSet<GatewayApiKey> GatewayApiKeys => Set<GatewayApiKey>();
    public DbSet<GatewayTablePermission> GatewayTablePermissions => Set<GatewayTablePermission>();
    public DbSet<GatewayAuditEntry> GatewayAuditEntries => Set<GatewayAuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var isRelational = Database.IsRelational();
        var schema = isRelational && Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
            ? DefaultSchema
            : null;

        if (schema is not null)
            modelBuilder.HasDefaultSchema(schema);

        GatewayModelConfiguration.Configure(modelBuilder, schema, configureCrossModuleForeignKeys: false);
    }
}
