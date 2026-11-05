using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Namines.Ground.Abstractions;
using Namines.Ground.Application;
using Namines.Ground.Infrastructure.Persistence;
using Namines.Ground.Infrastructure.Workers;
using Namines.Ground.Neon;
using Namines.Ground.Providers;
using Namines.Ground.Supabase;

namespace Namines.Ground.DependencyInjection;

/// <summary>
/// Ground modülünün DI kayıt noktası (Domain + Application + Infrastructure + Workers + DbContext).
/// </summary>
public static class GroundServiceCollectionExtensions
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=namines_control;Username=postgres;Password=postgres";

    public static IServiceCollection AddGroundModule(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDbContext = null,
        bool registerWorkers = true)
        => services.AddNaminesGround(configureDbContext, registerWorkers);

    public static IServiceCollection AddNaminesGround(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDbContext = null,
        bool registerWorkers = true)
    {
        if (configureDbContext is not null)
        {
            services.AddDbContext<GroundDbContext>(configureDbContext);
        }
        else
        {
            services.AddDbContext<GroundDbContext>((sp, options) =>
            {
                var config = sp.GetService<IConfiguration>();
                var connStr = config?.GetConnectionString("DefaultConnection") ?? FallbackConnectionString;
                options.UseNpgsql(
                    connStr,
                    npgsql => npgsql.MigrationsHistoryTable(
                        GroundDbContext.MigrationsHistoryTableName,
                        GroundDbContext.SchemaName));
            });
        }

        services.AddSingleton<IDatabaseProvider, LocalPostgresProvider>();
        services.AddHttpClient<INeonClient, NeonClient>();
        services.AddSingleton<IDatabaseProvider>(sp => new NeonProvider(
            sp.GetRequiredService<INeonClient>(),
            sp.GetRequiredService<ILogger<NeonProvider>>()));

        services.AddHttpClient<ISupabaseClient, SupabaseClient>();
        services.AddSingleton<IDatabaseProvider>(sp => new SupabaseProvider(
            sp.GetRequiredService<ISupabaseClient>(),
            sp.GetRequiredService<ILogger<SupabaseProvider>>()));

        services.AddSingleton(Namines.BuildingBlocks.Persistence.NonOwnerContext.Descriptor<Namines.Ground.Infrastructure.Persistence.GroundDbContext>("Ground", "ground", "Tablolar kendi şemasında; DDL AuthDbContext zincirinde kalıyor. Bu fazda tablo taşınmadı, bağlam yalnızca okur/yazar."));

        services.TryAddScoped<IGroundApplicationService, GroundApplicationService>();
        services.TryAddScoped<IGroundModule>(sp => new GroundModule(
            sp.GetServices<IDatabaseProvider>(),
            sp));

        if (registerWorkers)
        {
            services.AddHostedService<GroundPurgeBackgroundService>();
        }

        return services;
    }
}
