using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Namines.BuildingBlocks.Persistence;

/// <summary>Bir modül <c>DbContext</c>'i için üretim seçenekleri; her modül aynı kuralları kullanır.</summary>
public static class ModuleDbContextRegistration
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=namines_control;Username=postgres;Password=postgres";

    public const string HistoryTable = "__EFMigrationsHistory";

    /// <summary>
    /// Bağlantı dizesi AuthDbContext ile AYNI olmalı (aynı havuz): ayrı dizge ayrı 100'lük havuz demek,
    /// anonim yükte "too many clients" görülmüştü. Geçici hata yeniden denemesi de Auth ile aynı:
    /// bu tablolar Auth'tan taşınırken dayanıklılığı kaybetmemeli. Çok adımlı işlem ICrossModuleTransaction'da.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services,
        string schema,
        Func<IConfiguration, string>? connectionStringFactory = null,
        Action<DbContextOptionsBuilder>? configureDbContext = null)
        where TContext : DbContext
    {
        if (configureDbContext is not null)
        {
            services.AddDbContext<TContext>(configureDbContext);
            return services;
        }

        services.AddDbContext<TContext>((sp, options) =>
        {
            var config = sp.GetService<IConfiguration>();
            var connectionString = config is not null && connectionStringFactory is not null
                ? connectionStringFactory(config)
                : config?.GetConnectionString("DefaultConnection") ?? FallbackConnectionString;
            var timeout = new Npgsql.NpgsqlConnectionStringBuilder(connectionString).CommandTimeout;
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable(HistoryTable, schema);
                npgsql.CommandTimeout(timeout);
                npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
            });
        });
        return services;
    }

    /// <summary>Migration ve araçlar için: geçici hata yeniden denemesi yok, geçmiş tablosu kendi şemasında.</summary>
    public static DbContextOptions<TContext> MigrationOptions<TContext>(string connectionString, string schema)
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(HistoryTable, schema))
            .Options;

    /// <summary><c>dotnet ef</c> için ortak dizge. Migration üretimi bağlanmaz; <c>database update</c> bağlanır.</summary>
    public static string DesignTimeConnectionString() =>
        Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
        ?? "Host=localhost;Port=5432;Database=namines_control;Username=namines;Password=namines_dev_only_change_in_prod";
}
