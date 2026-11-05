using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Modules;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Infrastructure.Data;
using Namines.Ops.Application;
using Namines.Ops.Infrastructure.Persistence;

namespace Namines.Tests.TestSupport;

/// <summary>
/// Testlerde <see cref="AuthDbContext"/> ile AYNI bağlantıyı paylaşan Studio/Hub bağlamları ve
/// servisleri. DI'daki gibi tek örnek: işlem koordinatörü ile servisler aynı bağlamı görmeli, yoksa
/// servis işleme katılmaz.
/// </summary>
public sealed class ModuleKit
{
    private bool _flushing;

    /// <summary>Üretimdeki gibi: Studio/Hub kendi bağlantılarıyla, işlem paylaşımı SetDbConnection yoluyla.</summary>
    public ModuleKit(AuthDbContext db, string connectionString, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        Db = db;
        Studio = new StudioDbContext(new DbContextOptionsBuilder<StudioDbContext>().UseNpgsql(connectionString).AddInterceptors(interceptors).Options);
        Hub = new HubDbContext(new DbContextOptionsBuilder<HubDbContext>().UseNpgsql(connectionString).AddInterceptors(interceptors).Options);
        StudioService = new StudioApplicationService(Studio);
        HubService = new HubApplicationService(Hub);
        Ops = new OpsDbContext(ModuleOptions<OpsDbContext>(connectionString, interceptors));
        Transaction = new CrossModuleTransaction(db, Participants(), new SharedTransactionManager());
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
        var connection = db.Database.GetDbConnection();
        if (db.Database.IsNpgsql())
        {
            Studio = new StudioDbContext(new DbContextOptionsBuilder<StudioDbContext>().UseNpgsql(connection).Options);
            Hub = new HubDbContext(new DbContextOptionsBuilder<HubDbContext>().UseNpgsql(connection).Options);
        }
        else
        {
            Studio = new StudioDbContext(new DbContextOptionsBuilder<StudioDbContext>().UseSqlite(connection).Options);
            Hub = new HubDbContext(new DbContextOptionsBuilder<HubDbContext>().UseSqlite(connection).Options);
        }

        StudioService = new StudioApplicationService(Studio);
        HubService = new HubApplicationService(Hub);
        Ops = new OpsDbContext(SharedConnectionOptions<OpsDbContext>(db));
        Transaction = new CrossModuleTransaction(db, Participants(), new SharedTransactionManager());
        HookFlush();
    }

    private List<ITransactionParticipant> Participants() => ModuleContexts().Select(c => (ITransactionParticipant)new TransactionParticipant(c)).ToList();

    /// <summary>Şema kurulumu ve test tohumu için bu kitteki tüm modül bağlamları.</summary>
    public IEnumerable<DbContext> ModuleContexts()
    {
        yield return Studio;
        yield return Hub;
        yield return Ops;
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
    public IFeedbackService Feedback => new FeedbackService(Ops);
    public ISqlExecutionAuditService SqlAudit => new SqlExecutionAuditService(Ops);
    public IOpsRetentionService OpsRetention => new OpsRetentionService(Ops);

    public AuthDbContext Db { get; }
    public StudioDbContext Studio { get; }
    public HubDbContext Hub { get; }
    public IStudioApplicationService StudioService { get; }
    public IHubApplicationService HubService { get; }
    public ICrossModuleTransaction Transaction { get; }

    public TenantProjectAuthorityService Authority(IConnectionSecretProtector protector, IDbHostAccessPolicy? hostPolicy = null) =>
        new(Db, Studio, protector, hostPolicy);

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

public static partial class TestModules
{
    private static readonly ConditionalWeakTable<AuthDbContext, ModuleKit> Kits = new();

    public static ModuleKit For(AuthDbContext db) => Kits.GetValue(db, static d => new ModuleKit(d));

    internal static OpsDbContext Ops(this AuthDbContext db) => For(db).Ops;

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
        AuthMigrations.Descriptor,
        Namines.Ops.DependencyInjection.OpsServiceCollectionExtensions.MigrationDescriptor,
    };

    public static async Task MigrateAllModulesAsync(this AuthDbContext db) =>
        await MigrateAllAsync(db.Database.GetConnectionString() ?? db.Database.GetDbConnection().ConnectionString);
}
