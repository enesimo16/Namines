using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Namines.BuildingBlocks.Persistence;

/// <summary>
/// Bir modülün kendi `DbContext` ve şeması için sunduğu migration sözleşmesi
/// (Kritik Kontrol #5: EF Core Migration Isolation).
/// </summary>
public interface IModuleDbContextMigrator
{
    /// <summary>Deterministik çalıştırma sırası (küçükten büyüğe: kök şemalar önce).</summary>
    int Order { get; }

    /// <summary>Modül adı (ör: "Vault", "Ground", "Flow", "Desk", "Hub").</summary>
    string ModuleName { get; }

    /// <summary>PostgreSQL şema adı (ör: "namines_vault", "ground", "flow", "desk", "hub").</summary>
    string SchemaName { get; }

    /// <summary>Modülün bekleyen EF Core migration listesini döndürür.</summary>
    Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken ct = default);

    /// <summary>Modülün migration'larını uygular.</summary>
    Task MigrateAsync(CancellationToken ct = default);
}

/// <summary>
/// Belirli bir <typeparamref name="TDbContext"/> için standart <see cref="IModuleDbContextMigrator"/> uygulaması.
/// </summary>
public sealed class ModuleDbContextMigrator<TDbContext> : IModuleDbContextMigrator
    where TDbContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;

    public int Order { get; }
    public string ModuleName { get; }
    public string SchemaName { get; }

    public ModuleDbContextMigrator(
        IServiceScopeFactory scopeFactory,
        int order,
        string moduleName,
        string schemaName)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        Order = order;
        ModuleName = moduleName ?? throw new ArgumentNullException(nameof(moduleName));
        SchemaName = schemaName ?? throw new ArgumentNullException(nameof(schemaName));
    }

    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var pending = await db.Database.GetPendingMigrationsAsync(ct);
        return pending.ToList();
    }

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}

/// <summary>
/// Tüm modül `DbContext` migration'larını deterministik sırada (`Order`) yürüten ve doğrulayan orkestratör.
/// </summary>
public sealed class ModuleMigrationOrchestrator
{
    private readonly IReadOnlyList<IModuleDbContextMigrator> _migrators;
    private readonly ILogger<ModuleMigrationOrchestrator> _logger;

    public ModuleMigrationOrchestrator(
        IEnumerable<IModuleDbContextMigrator> migrators,
        ILogger<ModuleMigrationOrchestrator> logger)
    {
        _migrators = (migrators ?? throw new ArgumentNullException(nameof(migrators)))
            .OrderBy(m => m.Order)
            .ThenBy(m => m.ModuleName, StringComparer.Ordinal)
            .ToList();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<IModuleDbContextMigrator> Migrators => _migrators;

    public async Task MigrateAllAsync(CancellationToken ct = default)
    {
        foreach (var migrator in _migrators)
        {
            _logger.LogInformation(
                "Modül migration başlatılıyor: {ModuleName} (şema: {SchemaName}, sıra: {Order})",
                migrator.ModuleName, migrator.SchemaName, migrator.Order);

            await migrator.MigrateAsync(ct);

            _logger.LogInformation(
                "Modül migration tamamlandı: {ModuleName} (şema: {SchemaName})",
                migrator.ModuleName, migrator.SchemaName);
        }
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetAllPendingMigrationsAsync(
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var migrator in _migrators)
        {
            var pending = await migrator.GetPendingMigrationsAsync(ct);
            if (pending.Count > 0)
            {
                result[migrator.ModuleName] = pending;
            }
        }
        return result;
    }
}
