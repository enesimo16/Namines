using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;
using Namines.Studio.Domain;

namespace Namines.Studio.Application;

/// <summary>
/// Uygulama katmanının veri kapısı: servisler somut bağlamı ve Infrastructure ad alanını görmez, EF sorgusu yazmaya
/// devam eder. Mimari test Application'dan Infrastructure'a giden başvuruyu bu sayede IL düzeyinde yasaklayabiliyor.
/// </summary>
internal interface IStudioData
{
    DbSet<CloudProject> Projects { get; }
    DbSet<ProjectConnection> ProjectConnections { get; }
    DbSet<ProjectShare> ProjectShares { get; }
    DbSet<Branch> Branches { get; }
    DbSet<SchemaVersion> SchemaVersions { get; }
    DbSet<CrossDatabaseRelation> CrossDatabaseRelations { get; }
    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
