using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;
using Namines.Hub.Domain;

namespace Namines.Hub.Application;

/// <summary>
/// Uygulama katmanının veri kapısı: servisler somut bağlamı ve Infrastructure ad alanını görmez, EF sorgusu yazmaya
/// devam eder. Mimari test Application'dan Infrastructure'a giden başvuruyu bu sayede IL düzeyinde yasaklayabiliyor.
/// </summary>
internal interface IHubData
{
    DbSet<ProjectRepository> Repositories { get; }
    DbSet<ProjectTag> ProjectTags { get; }
    DbSet<RepoStar> RepoStars { get; }
    DbSet<RepoIssue> RepoIssues { get; }
    DbSet<RepoIssueComment> RepoIssueComments { get; }
    DbSet<IssueLabel> IssueLabels { get; }
    DbSet<RepoIssueLabel> RepoIssueLabels { get; }
    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
