using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Namines.Vault.Abstractions;
using Namines.Vault.Application;
using Namines.Vault.Infrastructure.Persistence;
using Namines.Vault.Infrastructure.Workers;
using Namines.Vault.Providers;
using Namines.Vault.Storage;

namespace Namines.Vault.DependencyInjection;

/// <summary>
/// Vault modülünün DI kayıt noktası (Domain + Application + Infrastructure + Workers + DbContext).
/// </summary>
public static class VaultServiceCollectionExtensions
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=namines_control;Username=postgres;Password=postgres";

    public static IServiceCollection AddVaultModule(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDbContext = null,
        bool registerWorkers = true)
        => services.AddNaminesVault(configureDbContext, registerWorkers);

    public static IServiceCollection AddNaminesVault(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDbContext = null,
        bool registerWorkers = true)
    {
        if (configureDbContext is not null)
        {
            services.AddDbContext<VaultDbContext>(configureDbContext);
        }
        else
        {
            services.AddDbContext<VaultDbContext>((sp, options) =>
            {
                var config = sp.GetService<IConfiguration>();
                var connStr = config?.GetConnectionString("DefaultConnection") ?? FallbackConnectionString;
                options.UseNpgsql(
                    connStr,
                    npgsql => npgsql.MigrationsHistoryTable(
                        VaultDbContext.MigrationsHistoryTableName,
                        VaultDbContext.SchemaName));
            });
        }

        services.TryAddSingleton<BackupCipher>();

        services.TryAddSingleton<IBackupStore>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();

            return string.IsNullOrWhiteSpace(configuration["Vault:S3:Bucket"])
                ? new FileSystemBackupStore(configuration)
                : new S3BackupStore(configuration);
        });

        services.AddSingleton<IBackupProvider>(sp => new PostgresBackupProvider(
            sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<ILogger<PostgresBackupProvider>>())
        { HostPolicy = sp.GetService<Namines.Core.Security.IDbHostAccessPolicy>() });

        services.AddSingleton<IBackupProvider>(sp => new MySqlFamilyBackupProvider(
            engine: "MySQL",
            image: sp.GetRequiredService<IConfiguration>()["Vault:MySqlImage"] ?? "mysql:8.0",
            hasColumnStatisticsFlag: true,
            logger: sp.GetRequiredService<ILogger<MySqlFamilyBackupProvider>>())
        { HostPolicy = sp.GetService<Namines.Core.Security.IDbHostAccessPolicy>() });

        services.AddSingleton<IBackupProvider>(sp => new MySqlFamilyBackupProvider(
            engine: "MariaDB",
            image: sp.GetRequiredService<IConfiguration>()["Vault:MariaDbImage"] ?? "mariadb:10.6",
            hasColumnStatisticsFlag: false,
            logger: sp.GetRequiredService<ILogger<MySqlFamilyBackupProvider>>())
        { HostPolicy = sp.GetService<Namines.Core.Security.IDbHostAccessPolicy>() });

        services.TryAddSingleton<IVaultJobQueue, VaultJobQueue>();
        services.TryAddScoped<IVaultApplicationService, VaultApplicationService>();
        services.TryAddScoped<IVaultModule>(sp => new VaultModule(
            sp.GetRequiredService<IBackupStore>(),
            sp.GetRequiredService<BackupCipher>(),
            sp.GetServices<IBackupProvider>(),
            sp.GetRequiredService<IVaultJobQueue>(),
            sp));

        if (registerWorkers)
        {
            services.AddHostedService<VaultBackupWorker>();
            services.AddHostedService<VaultScheduleBackgroundService>();
        }

        return services;
    }
}
