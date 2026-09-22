using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
using Namines.Vault.Abstractions;
using Namines.Vault.Storage;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// <see cref="VaultService.ApplyRetentionAsync"/> — "en yeni N OTOMATİK yedek
/// kalır" politikası.
///
/// <b>Bulunan açık:</b> sorgu <c>Kind == Scheduled</c> dışında bir durum
/// filtresi uygulamıyordu, yani <c>Failed</c> kayıtlar da "saklanan N"
/// sayısına dahil oluyordu. Art arda birkaç başarısız zamanlanmış deneme,
/// gerçek/kullanılabilir başarılı yedekleri "eski" sayıp silebiliyordu —
/// bir dizi hata, kurtarılabilir tek yedeği kaybettirebilirdi.
/// </summary>
public sealed class VaultRetentionTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<AuthDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(_connection).Options;

        await using var db = new AuthDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private AuthDbContext NewContext() => new(_options);

    private sealed class PassthroughProtector : IConnectionSecretProtector
    {
        public string Protect(string plaintext) => "enc:" + plaintext;
        public string Unprotect(string ciphertext) => ciphertext.Replace("enc:", "");
    }

    private sealed class AllowAllHostPolicy : IDbHostAccessPolicy
    {
        public bool IsHostAllowed(string? host, out string denyReason)
        {
            denyReason = "";
            return true;
        }
    }

    /// <summary>Silinen anahtarları toplayan sahte depo — testin gerçek dosya sistemine dokunması gerekmiyor.</summary>
    private sealed class FakeBackupStore : IBackupStore
    {
        public List<string> Deleted { get; } = new();
        public string Description => "fake";
        public Task<long> PutAsync(string key, System.IO.Stream content, CancellationToken ct) => Task.FromResult(0L);
        public Task<System.IO.Stream?> OpenAsync(string key, CancellationToken ct) => Task.FromResult<System.IO.Stream?>(null);
        public Task DeleteAsync(string key, CancellationToken ct)
        {
            Deleted.Add(key);
            return Task.CompletedTask;
        }
    }

    private static BackupCipher NewCipher()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vault:BackupEncryptionKey"] = new string('k', 40),
            })
            .Build();
        return new BackupCipher(configuration);
    }

    private VaultService NewService(AuthDbContext db, FakeBackupStore store) => new(
        db, new PassthroughProtector(), new AllowAllHostPolicy(),
        Array.Empty<IBackupProvider>(), store, NewCipher(), NullLogger<VaultService>.Instance);

    private static VaultBackup NewBackup(
        string projectId, VaultBackupStatus status, DateTime createdAt, string suffix) => new()
    {
        ProjectId = projectId,
        CreatedByUserId = "u1",
        DatabaseName = "db",
        Engine = "PostgreSQL",
        Kind = VaultBackupKind.Scheduled,
        Status = status,
        StoreDescription = "fake",
        StorageKey = $"{projectId}/{suffix}.nvlt",
        CreatedAt = createdAt,
    };

    [Fact]
    public async Task Basarisiz_yedekler_saklanan_N_sayisina_DAHIL_EDILMIYOR()
    {
        // 5 BAŞARILI yedek + 3 ardışık BAŞARISIZ deneme (en yeniler).
        // retainCount=3: eski davranışta en yeni 3 kayıt (hepsi Failed) tutulur
        // ve 5 başarılı yedeğin ikisi (en eski ikisi) silinirdi — doğrusu,
        // 3 BAŞARILI yedeğin tutulup geri kalan 2 başarılının silinmesi ve
        // Failed kayıtların hiç etkilenmemesi.
        await using var db = NewContext();

        db.Users.Add(new Namines.Core.Models.Auth.ApplicationUser { Id = "u1", UserName = "u1" });
        db.CloudProjects.Add(new CloudProject
        {
            Id = "p1", Name = "proj", DbType = "PostgreSQL",
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = "u1",
        });

        var baseline = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var succeeded = Enumerable.Range(0, 5)
            .Select(i => NewBackup("p1", VaultBackupStatus.Succeeded, baseline.AddHours(i), $"ok-{i}"))
            .ToList();
        var failed = Enumerable.Range(0, 3)
            .Select(i => NewBackup("p1", VaultBackupStatus.Failed, baseline.AddHours(10 + i), $"fail-{i}"))
            .ToList();

        db.VaultBackups.AddRange(succeeded);
        db.VaultBackups.AddRange(failed);
        await db.SaveChangesAsync();

        var store = new FakeBackupStore();
        var service = NewService(db, store);

        var removed = await service.ApplyRetentionAsync("p1", retainCount: 3, CancellationToken.None);

        // Yalnızca en eski 2 BAŞARILI yedek silinmeli.
        Assert.Equal(2, removed);

        await using var verify = NewContext();
        var remaining = await verify.VaultBackups.Where(b => b.ProjectId == "p1").ToListAsync();

        // Tüm Failed kayıtlar hâlâ duruyor — saklama politikası onlara hiç dokunmadı.
        Assert.Equal(3, remaining.Count(b => b.Status == VaultBackupStatus.Failed));
        // En yeni 3 BAŞARILI yedek duruyor.
        Assert.Equal(3, remaining.Count(b => b.Status == VaultBackupStatus.Succeeded));
        Assert.DoesNotContain(remaining, b => b.StorageKey.Contains("ok-0"));
        Assert.DoesNotContain(remaining, b => b.StorageKey.Contains("ok-1"));
        Assert.Contains(remaining, b => b.StorageKey.Contains("ok-2"));
        Assert.Contains(remaining, b => b.StorageKey.Contains("ok-3"));
        Assert.Contains(remaining, b => b.StorageKey.Contains("ok-4"));

        Assert.Equal(2, store.Deleted.Count);
        Assert.All(store.Deleted, key => Assert.Contains("ok-", key));
    }

    [Fact]
    public async Task Basarili_yedek_sayisi_retainCount_ALTINDAYSA_hicbir_sey_silinmez()
    {
        // Yalnızca başarısız denemeler var, hiç başarılı yedek yok: eski
        // davranışta bunlardan retainCount kadarı "tutuluyor" sayılıp
        // fazlası silinirdi — oysa silinecek gerçek bir yedek yok.
        await using var db = NewContext();

        db.Users.Add(new Namines.Core.Models.Auth.ApplicationUser { Id = "u1", UserName = "u1" });
        db.CloudProjects.Add(new CloudProject
        {
            Id = "p1", Name = "proj", DbType = "PostgreSQL",
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = "u1",
        });

        var baseline = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var failed = Enumerable.Range(0, 5)
            .Select(i => NewBackup("p1", VaultBackupStatus.Failed, baseline.AddHours(i), $"fail-{i}"))
            .ToList();
        db.VaultBackups.AddRange(failed);
        await db.SaveChangesAsync();

        var store = new FakeBackupStore();
        var service = NewService(db, store);

        var removed = await service.ApplyRetentionAsync("p1", retainCount: 2, CancellationToken.None);

        Assert.Equal(0, removed);
        await using var verify = NewContext();
        Assert.Equal(5, await verify.VaultBackups.CountAsync(b => b.ProjectId == "p1"));
    }
}
