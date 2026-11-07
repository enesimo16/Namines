using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Namines.Governance.Domain;

namespace Namines.Governance.Application;

/// <summary>
/// Uygulama katmanının veri kapısı: servisler somut bağlamı ve Infrastructure ad alanını görmez, EF sorgusu yazmaya
/// devam eder. Mimari test Application'dan Infrastructure'a giden başvuruyu bu sayede IL düzeyinde yasaklayabiliyor.
/// </summary>
internal interface IGovernanceData
{
    DbSet<ChangeRequest> ChangeRequests { get; }
    DbSet<ChangeRequestApproval> ChangeRequestApprovals { get; }
    DbSet<ChangeRequestAuditLog> ChangeRequestAuditLogs { get; }
    DbSet<UserAIPolicy> UserAIPolicies { get; }
    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
