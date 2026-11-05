using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Modules;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Infrastructure.Data;

namespace Namines.Tests.TestSupport;

/// <summary>
/// Testlerde <see cref="AuthDbContext"/> ile AYNI bağlantıyı paylaşan Studio/Hub bağlamları ve
/// servisleri. DI'daki gibi tek örnek: işlem koordinatörü ile servisler aynı bağlamı görmeli, yoksa
/// servis işleme katılmaz.
/// </summary>
public sealed class ModuleKit
{
    /// <summary>Üretimdeki gibi: Studio/Hub kendi bağlantılarıyla, işlem paylaşımı SetDbConnection yoluyla.</summary>
    public ModuleKit(AuthDbContext db, string connectionString)
    {
        Db = db;
        Studio = new StudioDbContext(new DbContextOptionsBuilder<StudioDbContext>().UseNpgsql(connectionString).Options);
        Hub = new HubDbContext(new DbContextOptionsBuilder<HubDbContext>().UseNpgsql(connectionString).Options);
        StudioService = new StudioApplicationService(Studio);
        HubService = new HubApplicationService(Hub);
        Transaction = new CrossModuleTransaction(db, Studio, Hub, new SharedTransactionManager());
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
        Transaction = new CrossModuleTransaction(db, Studio, Hub, new SharedTransactionManager());
    }

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
        var repository = new ProjectRepository
        {
            ProjectId = projectId,
            OwnerUserId = ownerUserId,
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

public static class TestModules
{
    private static readonly ConditionalWeakTable<AuthDbContext, ModuleKit> Kits = new();

    public static ModuleKit For(AuthDbContext db) => Kits.GetValue(db, static d => new ModuleKit(d));
}
