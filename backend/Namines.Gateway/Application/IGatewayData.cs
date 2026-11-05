using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;
using Namines.Gateway.Domain;

namespace Namines.Gateway.Application;

/// <summary>
/// Uygulama katmanının veri kapısı: servisler somut bağlamı ve Infrastructure ad alanını görmez, EF sorgusu yazmaya
/// devam eder. Mimari test Application'dan Infrastructure'a giden başvuruyu bu sayede IL düzeyinde yasaklayabiliyor.
/// </summary>
internal interface IGatewayData
{
    DbSet<GatewayApiKey> GatewayApiKeys { get; }
    DbSet<GatewayTablePermission> GatewayTablePermissions { get; }
    DbSet<GatewayAuditEntry> GatewayAuditEntries { get; }
    DbSet<GatewayApiKeyAllowedOrigin> AllowedOrigins { get; }
    DbSet<GatewayApiKeyAllowedIp> AllowedIps { get; }
    DbSet<GatewayTableMaskedColumn> MaskedColumns { get; }
    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
