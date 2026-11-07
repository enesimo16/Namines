using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Namines.Core.Models.Auth;

namespace Namines.Vault.Application;

/// <summary>
/// Uygulama katmanının veri kapısı: servisler somut bağlamı ve Infrastructure ad alanını görmez, EF sorgusu yazmaya
/// devam eder. Mimari test Application'dan Infrastructure'a giden başvuruyu bu sayede IL düzeyinde yasaklayabiliyor.
/// </summary>
internal interface IVaultData
{
    DbSet<VaultBackup> VaultBackups { get; }
    DbSet<VaultRestore> VaultRestores { get; }
    DbSet<VaultSchedule> VaultSchedules { get; }
    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
