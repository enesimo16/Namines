using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Xunit;
using static Namines.Tests.Services.ProjectDeletionTestKit;

namespace Namines.Tests.Services;

/// <summary>
/// <see cref="Namines.Infrastructure.Services.ProjectDeletionService"/> — Restrict FK'li yedek/Ground
/// kayıtları ve dış kaynaklar (blob, sağlayıcı DB) temizlenmeden proje silinemez (DB-10).
/// </summary>
public sealed class ProjectDeletionServiceTests : IAsyncLifetime
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

    private static async Task SeedAsync(AuthDbContext db, FakeStore store, bool withGround = true,
        VaultBackupStatus backupStatus = VaultBackupStatus.Succeeded)
    {
        db.Users.Add(new ApplicationUser { Id = "u1", UserName = "u1" });
        db.CloudProjects.Add(new CloudProject
        {
            Id = "proj-1", Name = "Secret CRM", DbType = "PostgreSQL",
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = "u1",
        });
        for (var i = 0; i < 2; i++)
        {
            var key = $"proj-1/backup-{i}.bin";
            store.Blobs[key] = new byte[] { 1, 2, 3 };
            db.VaultBackups.Add(new VaultBackup
            {
                ProjectId = "proj-1", CreatedByUserId = "u1", DatabaseName = "crm", Engine = "PostgreSQL",
                StorageKey = key, StoreDescription = "fake", Status = backupStatus,
            });
        }
        if (withGround)
        {
            db.GroundDatabases.Add(new GroundDatabase
            {
                ProjectId = "proj-1", Provider = "FakeRemote", CreatedByUserId = "u1",
                Status = GroundStatus.Active, ProviderProjectId = "remote-1",
            });
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Yedekli_ve_Ground_veritabanli_proje_temiz_silinir_denetim_kaydi_kalir()
    {
        var store = new FakeStore();
        var provider = new FakeProvider();
        await using var db = NewContext();
        await SeedAsync(db, store);
        var project = await db.CloudProjects.SingleAsync();

        var result = await Create(db, store, provider).DeleteAsync(project, "u1", CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Empty(store.Blobs);
        Assert.Equal(new[] { "remote-1" }, provider.Deleted);

        await using var verify = NewContext();
        Assert.False(await verify.CloudProjects.AnyAsync());
        Assert.False(await verify.VaultBackups.AnyAsync());
        Assert.False(await verify.GroundDatabases.AnyAsync());

        var audit = await verify.ChangeRequestAuditLogs.Where(a => a.ProjectId == "proj-1").ToListAsync();
        Assert.Contains(audit, a => a.Action == ChangeRequestAuditAction.ProjectDeletionRequested);
        var deleted = Assert.Single(audit, a => a.Action == ChangeRequestAuditAction.ProjectDeleted);
        Assert.Equal("u1", deleted.ActorUserId);
        Assert.Contains("\"backups\":2", deleted.Details);
        Assert.Contains("\"groundDatabases\":1", deleted.Details);
    }

    [Fact]
    public async Task Saglayici_basarisizsa_proje_yerinde_kalir_503_ve_yeniden_deneme_isaretlenir()
    {
        var store = new FakeStore();
        var provider = new FakeProvider { FailDelete = true };
        await using var db = NewContext();
        await SeedAsync(db, store);
        var project = await db.CloudProjects.SingleAsync();

        var result = await Create(db, store, provider).DeleteAsync(project, "u1", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(503, result.StatusCode);
        Assert.Equal("GROUND_DEPROVISION_FAILED", result.Code);
        // Sağlayıcı hata metni (host adı) istemciye sızmıyor.
        Assert.DoesNotContain("admin-db.internal", result.Error);

        await using var verify = NewContext();
        Assert.True(await verify.CloudProjects.AnyAsync(p => p.Id == "proj-1"));
        var ground = await verify.GroundDatabases.SingleAsync();
        Assert.Equal(GroundStatus.PendingDelete, ground.Status);
        Assert.Null(ground.ClaimedAt);
        Assert.True(ground.DeleteRequestedAt < DateTime.UtcNow.AddDays(-7));

        var audit = await verify.ChangeRequestAuditLogs.ToListAsync();
        Assert.Contains(audit, a => a.Action == ChangeRequestAuditAction.ProjectDeletionFailed);
        Assert.DoesNotContain(audit, a => a.Action == ChangeRequestAuditAction.ProjectDeleted);
    }

    [Fact]
    public async Task Blob_silinemezse_proje_ve_o_yedegin_kaydi_yerinde_kalir()
    {
        var store = new FakeStore { FailDeletes = true };
        var provider = new FakeProvider();
        await using var db = NewContext();
        await SeedAsync(db, store);
        var project = await db.CloudProjects.SingleAsync();

        var result = await Create(db, store, provider).DeleteAsync(project, "u1", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(503, result.StatusCode);
        Assert.Empty(provider.Deleted); // Ground'a hiç geçilmedi

        await using var verify = NewContext();
        Assert.True(await verify.CloudProjects.AnyAsync());
        Assert.Equal(2, await verify.VaultBackups.CountAsync());
        Assert.Equal(2, store.Blobs.Count);
    }

    [Fact]
    public async Task Calisan_yedek_varsa_409_ve_hicbir_sey_silinmez()
    {
        var store = new FakeStore();
        var provider = new FakeProvider();
        await using var db = NewContext();
        await SeedAsync(db, store, backupStatus: VaultBackupStatus.Running);
        var project = await db.CloudProjects.SingleAsync();

        var result = await Create(db, store, provider).DeleteAsync(project, "u1", CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal("BACKUP_IN_PROGRESS", result.Code);
        Assert.Equal(2, store.Blobs.Count);
        Assert.Empty(provider.Deleted);
    }

    [Fact]
    public async Task Yeniden_deneme_saglayici_duzelince_basarili_olur()
    {
        var store = new FakeStore();
        var provider = new FakeProvider { FailDelete = true };
        await using var db = NewContext();
        await SeedAsync(db, store);
        var service = Create(db, store, provider);

        var first = await service.DeleteAsync(await db.CloudProjects.SingleAsync(), "u1", CancellationToken.None);
        Assert.False(first.Ok);

        provider.FailDelete = false;
        var second = await service.DeleteAsync(await db.CloudProjects.SingleAsync(), "u1", CancellationToken.None);

        Assert.True(second.Ok);
        await using var verify = NewContext();
        Assert.False(await verify.CloudProjects.AnyAsync());
        Assert.Equal(new[] { "remote-1" }, provider.Deleted);
    }
}
