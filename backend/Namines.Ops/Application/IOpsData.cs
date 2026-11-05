using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;
using Namines.Ops.Domain;

namespace Namines.Ops.Application;

/// <summary>
/// Uygulama katmanının veri kapısı: servisler somut bağlamı ve Infrastructure ad alanını görmez, EF sorgusu yazmaya
/// devam eder. Mimari test Application'dan Infrastructure'a giden başvuruyu bu sayede IL düzeyinde yasaklayabiliyor.
/// </summary>
internal interface IOpsData
{
    DbSet<Feedback> Feedbacks { get; }
    DbSet<SqlExecutionAudit> SqlExecutionAudits { get; }
    DbSet<AdminFeatureFlag> FeatureFlags { get; }
    DbSet<AiToolRoute> AiToolRoutes { get; }
    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
