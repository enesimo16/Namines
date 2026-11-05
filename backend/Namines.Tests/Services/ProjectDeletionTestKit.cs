using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Desk.Infrastructure.Persistence;
using Namines.Ground.Abstractions;
using Namines.Ground.Application;
using Namines.Ground.Infrastructure.Persistence;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
using Namines.Vault.Abstractions;
using Namines.Vault.Application;
using Namines.Vault.Infrastructure.Persistence;
using Namines.Vault.Storage;
using Namines.Tests.TestSupport;

namespace Namines.Tests.Services;

/// <summary>Proje/hesap silme testleri için ortak sahte bağımlılıklar.</summary>
internal static class ProjectDeletionTestKit
{
    public sealed class FakeStore : IBackupStore
    {
        public Dictionary<string, byte[]> Blobs { get; } = new();
        public bool FailDeletes { get; set; }
        public string Description => "fake";

        public Task<long> PutAsync(string key, Stream content, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            content.CopyTo(ms);
            Blobs[key] = ms.ToArray();
            return Task.FromResult((long)ms.Length);
        }

        public Task<Stream?> OpenAsync(string key, CancellationToken ct) =>
            Task.FromResult<Stream?>(Blobs.TryGetValue(key, out var b) ? new MemoryStream(b) : null);

        public Task DeleteAsync(string key, CancellationToken ct)
        {
            if (FailDeletes) throw new IOException("disk /var/backups unavailable");
            Blobs.Remove(key);
            return Task.CompletedTask;
        }
    }

    public sealed class FakeProvider : IDatabaseProvider
    {
        public bool FailDelete { get; set; }
        public List<string> Deleted { get; } = new();
        public string Name => "FakeRemote";
        public ProviderCapabilities Capabilities { get; } = new(
            SupportsBranching: false, SupportsRegionChoice: false, IsLiveVerified: true,
            ResponsibilityNote: "test", CreateIsIdempotentByProjectId: false);

        public Task<ProvisionedDatabase> CreateAsync(ProvisionSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task DeleteAsync(ProvisionedDatabase database, CancellationToken ct)
        {
            if (FailDelete) throw new InvalidOperationException("provider admin-db.internal:5432 refused");
            Deleted.Add(database.ProviderProjectId);
            return Task.CompletedTask;
        }

        public Task<DatabaseMetrics> GetMetricsAsync(ProvisionedDatabase database, CancellationToken ct) =>
            Task.FromResult(new DatabaseMetrics(null, null));

        public Task<string?> ProbeAsync(CancellationToken ct) => Task.FromResult<string?>(null);
    }

    private sealed class PassthroughProtector : IConnectionSecretProtector
    {
        public string Protect(string plaintext) => "enc:" + plaintext;
        public string Unprotect(string ciphertext) => ciphertext.Replace("enc:", "");
    }

    public static VaultDbContext NewVaultDb(AuthDbContext db) =>
        db.Database.IsNpgsql()
            ? new(new DbContextOptionsBuilder<VaultDbContext>().UseNpgsql(db.Database.GetConnectionString()).Options)
            : new(new DbContextOptionsBuilder<VaultDbContext>().UseSqlite(db.Database.GetDbConnection()).Options);

    public static GroundDbContext NewGroundDb(AuthDbContext db) =>
        db.Database.IsNpgsql()
            ? new(new DbContextOptionsBuilder<GroundDbContext>().UseNpgsql(db.Database.GetConnectionString()).Options)
            : new(new DbContextOptionsBuilder<GroundDbContext>().UseSqlite(db.Database.GetDbConnection()).Options);

    public static DeskDbContext NewDeskDb(AuthDbContext db) =>
        new(new DbContextOptionsBuilder<DeskDbContext>().UseSqlite(db.Database.GetDbConnection()).Options);

    public static ProjectDeletionService Create(AuthDbContext db, ModuleKit kit) =>
        Create(db, new FakeStore(), new FakeProvider(), kit);

    public static ProjectDeletionService Create(AuthDbContext db, FakeStore store, FakeProvider provider) =>
        Create(db, store, provider, TestModules.For(db));

    private static ProjectDeletionService Create(AuthDbContext db, FakeStore store, FakeProvider provider, ModuleKit kit)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vault:BackupEncryptionKey"] = "test-key-at-least-32-characters-long!!",
            }).Build();

        var authority = kit.Authority(new PassthroughProtector());
        var vault = new VaultApplicationService(
            NewVaultDb(db),
            authority,
            authority,
            providers: Array.Empty<IBackupProvider>(),
            store,
            new BackupCipher(config),
            NullLogger<VaultApplicationService>.Instance);
        var ground = new GroundApplicationService(
            NewGroundDb(db),
            authority,
            authority,
            new IDatabaseProvider[] { provider },
            NullLogger<GroundApplicationService>.Instance);
        return new ProjectDeletionService(db, vault, ground, NullLogger<ProjectDeletionService>.Instance, kit.HubService, kit.Transaction);
    }

    public static ProjectDeletionService Create(AuthDbContext db) => Create(db, new FakeStore(), new FakeProvider());
}
