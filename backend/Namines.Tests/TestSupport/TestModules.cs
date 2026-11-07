using Namines.Identity.Infrastructure.Security;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.BuildingBlocks.Security;
using Namines.Gateway.Application;
using Namines.Gateway.Domain;
using Namines.Gateway.Infrastructure.Persistence;
using Namines.Hub.Application;
using Namines.Hub.Domain;
using Namines.Hub.Infrastructure.Persistence;
using Namines.Studio.Application;
using Namines.Studio.Domain;
using Namines.Studio.Infrastructure.Persistence;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Infrastructure.Data;
using Namines.Ops.Application;
using Namines.Ops.Infrastructure.Persistence;
using Namines.Billing.Application;
using Namines.Billing.Infrastructure.Persistence;
using Namines.Governance.Application;
using Namines.Governance.Infrastructure.Persistence;
using Namines.Identity.Application;
using Namines.Identity.Infrastructure.Persistence;
using Namines.Vault.Infrastructure.Persistence;
using Namines.Ground.Infrastructure.Persistence;
using Namines.Flow.Infrastructure.Persistence;
using Namines.Desk.Infrastructure.Persistence;

namespace Namines.Tests.TestSupport;

/// <summary>
/// Testlerde <see cref="AuthDbContext"/> ile AYNI bağlantıyı paylaşan Studio/Hub bağlamları ve
/// servisleri. DI'daki gibi tek örnek: işlem koordinatörü ile servisler aynı bağlamı görmeli, yoksa
/// servis işleme katılmaz.
/// </summary>
internal sealed class ModuleKit
{
    private bool _flushing;

    /// <summary>Üretimdeki gibi: modüller kendi bağlantılarıyla, işlem paylaşımı SetDbConnection yoluyla.</summary>
    public ModuleKit(AuthDbContext db, string connectionString, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        Db = db;
        Studio = new StudioDbContext(ModuleOptions<StudioDbContext>(connectionString, interceptors));
        Hub = new HubDbContext(ModuleOptions<HubDbContext>(connectionString, interceptors));
        Gateway = new GatewayDbContext(ModuleOptions<GatewayDbContext>(connectionString, interceptors));
        Ops = new OpsDbContext(ModuleOptions<OpsDbContext>(connectionString, interceptors));
        Billing = new BillingDbContext(ModuleOptions<BillingDbContext>(connectionString, interceptors));
        Governance = new GovernanceDbContext(ModuleOptions<GovernanceDbContext>(connectionString, interceptors));
        Vault = new VaultDbContext(ModuleOptions<VaultDbContext>(connectionString, interceptors));
        Ground = new GroundDbContext(ModuleOptions<GroundDbContext>(connectionString, interceptors));
        Flow = new FlowDbContext(ModuleOptions<FlowDbContext>(connectionString, interceptors));
        Desk = new DeskDbContext(ModuleOptions<DeskDbContext>(connectionString, interceptors));
        Transaction = new CrossModuleTransaction(Participants(), new SharedTransactionManager());
        HookFlush();
    }

    private static DbContextOptions<T> ModuleOptions<T>(string connectionString, Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors) where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseNpgsql(connectionString).AddInterceptors(interceptors).Options;

    private static DbContextOptions<T> SharedConnectionOptions<T>(AuthDbContext db) where T : DbContext
    {
        var connection = db.Database.GetDbConnection();
        var builder = new DbContextOptionsBuilder<T>();
        return (db.Database.IsNpgsql() ? builder.UseNpgsql(connection) : builder.UseSqlite(connection)).Options;
    }

    public ModuleKit(AuthDbContext db)
    {
        Db = db;
        Studio = new StudioDbContext(SharedConnectionOptions<StudioDbContext>(db));
        Hub = new HubDbContext(SharedConnectionOptions<HubDbContext>(db));
        Gateway = new GatewayDbContext(SharedConnectionOptions<GatewayDbContext>(db));
        Ops = new OpsDbContext(SharedConnectionOptions<OpsDbContext>(db));
        Billing = new BillingDbContext(SharedConnectionOptions<BillingDbContext>(db));
        Governance = new GovernanceDbContext(SharedConnectionOptions<GovernanceDbContext>(db));
        Vault = new VaultDbContext(SharedConnectionOptions<VaultDbContext>(db));
        Ground = new GroundDbContext(SharedConnectionOptions<GroundDbContext>(db));
        Flow = new FlowDbContext(SharedConnectionOptions<FlowDbContext>(db));
        Desk = new DeskDbContext(SharedConnectionOptions<DeskDbContext>(db));
        Transaction = new CrossModuleTransaction(Participants(), new SharedTransactionManager());
        HookFlush();
    }

    private List<ITransactionParticipant> Participants() => new DbContext[] { Db }.Concat(ModuleContexts()).Select(c => (ITransactionParticipant)new TransactionParticipant(c)).ToList();

    /// <summary>Şema kurulumu ve test tohumu için bu kitteki tüm modül bağlamları.</summary>
    public IEnumerable<DbContext> ModuleContexts()
    {
        yield return Studio;
        yield return Hub;
        yield return Gateway;
        yield return Ops;
        yield return Billing;
        yield return Governance;
        yield return Vault;
        yield return Ground;
        yield return Flow;
        yield return Desk;
    }

    /// <summary>
    /// Testler tohumu tek <c>db.SaveChanges()</c> ile yazıyor. Modül bağlamlarındaki bekleyen satırlar
    /// da o anda yazılır; üretimde böyle bir köprü yok, üretim yolu sözleşmelerden geçer.
    /// </summary>
    private void HookFlush()
    {
        Db.SavedChanges += (_, _) =>
        {
            if (_flushing) return;
            _flushing = true;
            try
            {
                foreach (var module in ModuleContexts())
                    if (module.ChangeTracker.HasChanges()) module.SaveChanges();
            }
            finally
            {
                _flushing = false;
            }
        };
    }

    internal OpsDbContext Ops { get; }
    internal BillingDbContext Billing { get; }
    internal GovernanceDbContext Governance { get; }
    internal VaultDbContext Vault { get; }
    internal GroundDbContext Ground { get; }
    internal FlowDbContext Flow { get; }
    internal DeskDbContext Desk { get; }
    public IFeedbackService Feedback => new FeedbackService(Ops);
    public ISqlExecutionAuditService SqlAudit => new SqlExecutionAuditService(Ops);
    public IOpsRetentionService OpsRetention => new OpsRetentionService(Ops);

    public AuthDbContext Db { get; }
    internal StudioDbContext Studio { get; }
    internal HubDbContext Hub { get; }
    internal GatewayDbContext Gateway { get; }
    internal IGatewayAccess GatewayAccess => new GatewayAccessService(Gateway, DefaultAuthority);
    public IGatewayAuditLog GatewayAudit => new GatewayAuditLog(Gateway, DefaultAuthority, Branches);
    public IGatewayRequestGate GatewayGate(Microsoft.Extensions.Configuration.IConfiguration configuration) =>
        new GatewayRequestGate(GatewayAccess, GatewayAudit, Usage, DefaultAuthority, StudioService, new NoGatewayMetrics(), configuration);
    public IGatewayKeyService GatewayKeys => new GatewayKeyService(Gateway, DefaultAuthority, BillingAccounts);
    public IGatewayMaintenance GatewayMaintenance => new GatewayKeyService(Gateway, DefaultAuthority, BillingAccounts);
    public IUsageMeter Usage => new UsageMeterService(Billing, Identity);
    internal AiQuotaService AiQuota(Microsoft.Extensions.Configuration.IConfiguration? configuration = null) =>
        new(Billing, configuration ?? new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), Identity);
    internal BillingAccountService BillingAccounts => new(Billing, Identity, AiQuota(),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<BillingAccountService>.Instance, null, new ISubscriptionCancellationObserver[] { GovernanceAudit });
    internal GovernanceAuditService GovernanceAudit => new(Governance);
    internal AiPolicyService Policies => new(Governance);

    public IStudioApplicationService StudioService => new StudioApplicationService(Studio);
    public IHubApplicationService HubService => new HubApplicationService(Hub);
    public ICrossModuleTransaction Transaction { get; }

    public IIdentityDirectory Identity => new IdentityDirectoryService(Db);
    public IStudioProjects Projects => new StudioProjectService(Studio);
    public IStudioAdminQueries StudioAdmin => new StudioProjectService(Studio);
    public ITenantProjectAuthority DefaultAuthority => Authority(new ThrowingProtector());
    public IStudioBranches Branches => new StudioBranchService(Studio, DefaultAuthority);
    public IBranchService BranchService => new StudioBranchService(Studio, DefaultAuthority);
    public IStudioRelations Relations => new CrossDatabaseRelationService(Studio, DefaultAuthority);
    public ICrossDatabaseRelationService RelationService => new CrossDatabaseRelationService(Studio, DefaultAuthority);
    public IProjectWorkspaceService Workspace => new ProjectWorkspaceService(Studio, DefaultAuthority, Identity, Projects);
    public IShareService Shares => new ShareService(DefaultAuthority, StudioService);
    public IProjectLifecycleService Lifecycle => new ProjectLifecycleService(Projects, DefaultAuthority, Identity, new HubApplicationService(Hub), Transaction);
    internal HubIssueService HubIssues => new HubIssueService(Hub, HubService, Projects, DefaultAuthority, Identity);

    public IHubRepositoryService HubRepositories(IProjectDeletion deletion) =>
        new HubRepositoryService(HubService, HubIssues, Projects, Branches, DefaultAuthority, Identity, deletion, GovernanceAudit, Transaction);

    internal TenantProjectAuthorityService Authority(IConnectionSecretProtector protector, IDbHostAccessPolicy? hostPolicy = null) =>
        new(Studio, Identity, BillingAccounts, protector, hostPolicy is null ? null : new ConnectionHostInspector(hostPolicy));

    /// <summary>Hub verisini doğrudan yazar: proje oluşturma akışını sınamayan testlerin kurulumu.</summary>
    public async Task<ProjectRepository> SeedRepositoryAsync(
        string projectId,
        string ownerUserId,
        string? slug = null,
        ProjectVisibility visibility = ProjectVisibility.Private,
        string? description = null,
        string category = "General",
        string? tags = null,
        int starCount = 0,
        int forkCount = 0,
        string? forkedFromProjectId = null,
        string? linkedGithubRepo = null,
        DateTime? createdAt = null)
    {
        // Okuma modeli kopyası: üretimde proje yazımıyla aynı işlemde gelir, burada projeden okunur.
        var project = await Studio.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.Name, p.DbType })
            .FirstOrDefaultAsync();
        var repository = new ProjectRepository
        {
            ProjectId = projectId,
            OwnerUserId = ownerUserId,
            ProjectName = project?.Name ?? projectId,
            ProjectDbType = project?.DbType ?? "PostgreSQL",
            Slug = slug,
            Visibility = visibility,
            Description = description,
            Category = category,
            StarCount = starCount,
            ForkCount = forkCount,
            ForkedFromProjectId = forkedFromProjectId,
            LinkedGithubRepo = linkedGithubRepo,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            UpdatedAt = createdAt ?? DateTime.UtcNow,
        };
        await HubService.CreateRepositoryAsync(repository, tags);
        return repository;
    }

    /// <summary>Şifreli metni olduğu gibi yazar (ör. testin kendi ürettiği ciphertext).</summary>
    public async Task SeedConnectionAsync(
        string projectId, string? encrypted, string? dbType = "PostgreSQL", bool allowDeskSql = false)
    {
        Studio.ChangeTracker.Clear();
        Studio.ProjectConnections.Add(new ProjectConnection
        {
            ProjectId = projectId,
            EncryptedConnectionString = encrypted,
            ConnectionDbType = dbType,
            AllowDeskSql = allowDeskSql,
        });
        await Studio.SaveChangesAsync();
        Studio.ChangeTracker.Clear();
    }

    public async Task<string> SeedShareAsync(string projectId, string rawToken, DateTime? revokedAt = null)
    {
        Studio.ChangeTracker.Clear();
        Studio.ProjectShares.Add(new ProjectShare
        {
            ProjectId = projectId,
            ShareTokenHash = ShareTokenHasher.Hash(rawToken),
            RevokedAt = revokedAt,
        });
        await Studio.SaveChangesAsync();
        Studio.ChangeTracker.Clear();
        return rawToken;
    }
}

/// <summary>Şifre çözmeye ulaşılmaması gereken testler için: ulaşılırsa yüksek sesle patlar.</summary>
public sealed class ThrowingProtector : IConnectionSecretProtector
{
    public string Protect(string plaintext) => throw new InvalidOperationException("Protect should not be reached.");
    public string Unprotect(string ciphertext) => throw new InvalidOperationException("Unprotect should not be reached.");
}

internal static partial class TestModules
{
    private static readonly ConditionalWeakTable<AuthDbContext, ModuleKit> Kits = new();

    internal static BillingDbContext Billing(this AuthDbContext db) => For(db).Billing;
    internal static GovernanceDbContext Governance(this AuthDbContext db) => For(db).Governance;

    public static ModuleKit For(AuthDbContext db) => Kits.GetValue(db, static d => new ModuleKit(d));

    internal static OpsDbContext Ops(this AuthDbContext db) => For(db).Ops;
    internal static StudioDbContext Studio(this AuthDbContext db) => For(db).Studio;
    internal static HubDbContext Hub(this AuthDbContext db) => For(db).Hub;
    internal static GatewayDbContext Gateway(this AuthDbContext db) => For(db).Gateway;

    // Gateway'in eski yardımcı biçimleri: testler aynı kuralları sözleşmeden sınar.
    public static Task<GatewayApiKey?> AuthenticateAsync(this AuthDbContext db, string? rawKey, CancellationToken ct = default) =>
        For(db).GatewayAccess.AuthenticateAsync(rawKey, ct);
    public static Task<bool> IsTableAllowedAsync(this AuthDbContext db, GatewayApiKey key, string table, bool forWrite, CancellationToken ct = default) =>
        For(db).GatewayAccess.IsTableAllowedAsync(key, table, forWrite, ct);
    public static Task<bool> IsTableExportAllowedAsync(this AuthDbContext db, GatewayApiKey key, string table, CancellationToken ct = default) =>
        For(db).GatewayAccess.IsTableExportAllowedAsync(key, table, ct);
    public static Task<IReadOnlyList<string>> MaskedColumnsAsync(this AuthDbContext db, string projectId, string table, CancellationToken ct = default) =>
        For(db).GatewayAccess.MaskedColumnsAsync(projectId, table, ct);
    public static Task<IReadOnlyList<GatewayTablePermission>> ReadableTablesAsync(this AuthDbContext db, string projectId, CancellationToken ct = default) =>
        For(db).GatewayAccess.ReadableTablesAsync(projectId, ct);
    public static Task RecordAuditAsync(this AuthDbContext db, GatewayApiKey? key, string? actorUserId, GatewayWriteKind kind, string? tableName, string? rowKey, IEnumerable<string>? columns, int affectedRows, bool succeeded, CancellationToken ct = default, string? projectId = null) =>
        For(db).GatewayAudit.RecordAsync(key, actorUserId, kind, tableName, rowKey, columns, affectedRows, succeeded, ct, projectId);
    public static Task<(List<GatewayAuditEntry> Entries, int TotalCount)> AuditTrailAsync(this AuthDbContext db, string projectId, DateTime? from = null, DateTime? to = null, IReadOnlyList<GatewayWriteKind>? kinds = null, string? tableName = null, bool? succeeded = null, int page = 1, int pageSize = 100, CancellationToken ct = default) =>
        new GatewayAuditLog(For(db).Gateway, For(db).DefaultAuthority, For(db).Branches).QueryTrailAsync(projectId, new AuditQuery(from, to, kinds, tableName, succeeded, page, pageSize), ct);
    public static Task<AnalyticsResult> AnalyticsAsync(this AuthDbContext db, string projectId, DateTime from, DateTime to, string bucket, CancellationToken ct = default) =>
        new GatewayAuditLog(For(db).Gateway, For(db).DefaultAuthority, For(db).Branches).ComputeAnalyticsAsync(projectId, from, to, bucket, ct);

    // Yetki kuralı artık Studio'da; testlerin eski kısa yolları aynı sözleşmeye gider.
    public static Task<OrgRole?> GetRoleAsync(this AuthDbContext db, string projectId, string userId, CancellationToken ct = default) =>
        For(db).DefaultAuthority.GetRoleAsync(projectId, userId, ct);
    public static Task<bool> CanViewAsync(this AuthDbContext db, string projectId, string userId, CancellationToken ct = default) =>
        For(db).DefaultAuthority.CanViewAsync(projectId, userId, ct);
    public static Task<bool> CanEditAsync(this AuthDbContext db, string projectId, string userId, CancellationToken ct = default) =>
        For(db).DefaultAuthority.CanEditAsync(projectId, userId, ct);
    public static Task<bool> CanManageMembersAsync(this AuthDbContext db, string projectId, string userId, CancellationToken ct = default) =>
        For(db).DefaultAuthority.CanManageMembersAsync(projectId, userId, ct);
    public static Task<int> CountVotingMembersAsync(this AuthDbContext db, string projectId, CancellationToken ct = default) =>
        For(db).DefaultAuthority.CountVotingMembersAsync(projectId, ct);
    public static async Task<StudioBranch> GetOrCreateDefaultBranchAsync(this AuthDbContext db, string projectId, string userId, CancellationToken ct = default) =>
        await For(db).Branches.GetOrCreateDefaultAsync(projectId, userId, ct);

    /// <summary>Tüm bağlamların şemasını aynı bağlantıda kurar. EnsureCreated tek bağlam içindir: ikinci bağlam "tablo var" deyip çıkardı.</summary>
    public static async Task EnsureModulesCreatedAsync(this AuthDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        await CreateMissingModuleSchemasAsync(db);
    }

    public static void EnsureModulesCreated(this AuthDbContext db)
    {
        db.Database.EnsureCreated();
        CreateMissingModuleSchemasAsync(db).GetAwaiter().GetResult();
    }

    private static async Task CreateMissingModuleSchemasAsync(AuthDbContext db)
    {
        foreach (var module in For(db).ModuleContexts())
        {
            var firstTable = module.Model.GetEntityTypes().Select(e => e.GetTableName()).First(n => n is not null)!;
            var schema = module.Model.GetEntityTypes().First(e => e.GetTableName() == firstTable).GetSchema();
            var exists = db.Database.IsNpgsql()
                ? await module.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_tables WHERE schemaname = {0} AND tablename = {1}", schema ?? "public", firstTable).SingleAsync()
                : await module.Database.SqlQueryRaw<int>("SELECT count(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = {0}", firstTable).SingleAsync();
            if (exists == 0)
                await module.Database.ExecuteSqlRawAsync(module.Database.GenerateCreateScript());
        }
    }

    /// <summary>Üretimdeki tek yol: Auth, sonra modül zincirleri.</summary>
    public static Task MigrateAllAsync(string connectionString) =>
        ModuleMigrationOrchestrator.MigrateAllAsync(Descriptors, connectionString, TimeSpan.FromMinutes(5));

    public static IReadOnlyList<ModuleMigrationDescriptor> Descriptors { get; } = new[]
    {
        Namines.Identity.DependencyInjection.IdentityServiceCollectionExtensions.MigrationDescriptor,
        Namines.Studio.DependencyInjection.StudioServiceCollectionExtensions.MigrationDescriptor,
        Namines.Hub.DependencyInjection.HubServiceCollectionExtensions.MigrationDescriptor,
        Namines.Gateway.DependencyInjection.GatewayServiceCollectionExtensions.MigrationDescriptor,
        Namines.Ops.DependencyInjection.OpsServiceCollectionExtensions.MigrationDescriptor,
        Namines.Billing.DependencyInjection.BillingServiceCollectionExtensions.MigrationDescriptor,
        Namines.Governance.DependencyInjection.GovernanceServiceCollectionExtensions.MigrationDescriptor,
        Namines.Vault.DependencyInjection.VaultServiceCollectionExtensions.MigrationDescriptor,
        Namines.Ground.DependencyInjection.GroundServiceCollectionExtensions.MigrationDescriptor,
        Namines.Flow.DependencyInjection.FlowServiceCollectionExtensions.MigrationDescriptor,
        Namines.Desk.DependencyInjection.DeskServiceCollectionExtensions.MigrationDescriptor,
    };

    public static async Task MigrateAllModulesAsync(this AuthDbContext db) =>
        await MigrateAllAsync(db.Database.GetConnectionString() ?? db.Database.GetDbConnection().ConnectionString);
}
