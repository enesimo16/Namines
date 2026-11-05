using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Namines.Desk.Abstractions;
using Namines.Desk.Application;
using Namines.Desk.Infrastructure.Persistence;
using Namines.Desk.Services;

namespace Namines.Desk.DependencyInjection;

/// <summary>
/// Namines Desk modülünü DI konteynerine kaydeden uzantılar.
/// </summary>
public static class DeskServiceCollectionExtensions
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=namines_control;Username=postgres;Password=postgres";

    public static IServiceCollection AddDeskModule(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDbContext = null)
    {
        if (configureDbContext is not null)
        {
            services.AddDbContext<DeskDbContext>(configureDbContext);
        }
        else
        {
            services.AddDbContext<DeskDbContext>((sp, options) =>
            {
                var config = sp.GetService<IConfiguration>();
                var connStr = config?.GetConnectionString("DefaultConnection") ?? FallbackConnectionString;
                options.UseNpgsql(
                    connStr,
                    npgsql => npgsql.MigrationsHistoryTable(
                        DeskDbContext.MigrationsHistoryTableName,
                        DeskDbContext.SchemaName));
            });
        }

        services.TryAddSingleton<IDeskHandoffTokenValidator, DeskHandoffTokenValidator>();
        services.TryAddScoped<IDeskApplicationService, DeskApplicationService>();
        services.TryAddScoped<IDeskModule, DeskModule>();

        return services;
    }
}
