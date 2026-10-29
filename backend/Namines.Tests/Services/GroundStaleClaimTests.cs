using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Ground.Abstractions;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// Süreç ölünce <c>Deleting</c>/<c>Provisioning</c>'de asılı kalan Ground
/// kayıtlarının (ClaimedAt) kurtarılması ve slug benzersizlik indeksi.
/// </summary>
public sealed class GroundStaleClaimTests : IAsyncLifetime
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

    private sealed class OkProvider : IDatabaseProvider
    {
        public int DeleteCallCount { get; private set; }
        public string Name => "FakeRemote";
        public ProviderCapabilities Capabilities { get; } = new(
            SupportsBranching: false, SupportsRegionChoice: false, IsLiveVerified: true,
            ResponsibilityNote: "test", CreateIsIdempotentByProjectId: false);

        public Task<ProvisionedDatabase> CreateAsync(ProvisionSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task DeleteAsync(ProvisionedDatabase database, CancellationToken ct)
        {
            DeleteCallCount++;
            return Task.CompletedTask;
        }

        public Task<DatabaseMetrics> GetMetricsAsync(ProvisionedDatabase database, CancellationToken ct) =>
            Task.FromResult(new DatabaseMetrics(null, null));

        public Task<string?> ProbeAsync(CancellationToken ct) => Task.FromResult<string?>(null);
    }

    private async Task SeedAsync(
        GroundStatus status, DateTime? claimedAt, string? providerProjectId,
        DateTime? deleteRequestedAt = null, DateTime? createdAt = null)
    {
        await using var db = NewContext();
        db.Users.Add(new ApplicationUser { Id = "u1", UserName = "u1" });
        db.CloudProjects.Add(new CloudProject
        {
            Id = "proj-1", Name = "p", DbType = "PostgreSQL",
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = "u1",
        });
        db.GroundDatabases.Add(new GroundDatabase
        {
            ProjectId = "proj-1", Provider = "FakeRemote", CreatedByUserId = "u1",
            Status = status, ClaimedAt = claimedAt, ProviderProjectId = providerProjectId,
            DeleteRequestedAt = deleteRequestedAt,
            CreatedAt = createdAt ?? DateTime.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();
    }

    private GroundService NewService(AuthDbContext db, OkProvider provider) =>
        new(db, new PassthroughProtector(), new[] { (IDatabaseProvider)provider },
            NullLogger<GroundService>.Instance);

    private async Task<GroundDatabase> LoadAsync()
    {
        await using var verify = NewContext();
        return await verify.GroundDatabases.SingleAsync();
    }

    [Fact]
    public async Task Deleting_bayat_sahiplenme_PendingDelete_olur_ve_purge_tekrar_dener()
    {
        await SeedAsync(GroundStatus.Deleting, DateTime.UtcNow.AddHours(-2), "remote-1",
            deleteRequestedAt: DateTime.UtcNow.AddDays(-30));
        var provider = new OkProvider();
        await using var db = NewContext();

        var purged = await NewService(db, provider).PurgeExpiredAsync(7, CancellationToken.None);

        Assert.Equal(1, purged);
        Assert.Equal(1, provider.DeleteCallCount);
        var row = await LoadAsync();
        Assert.Equal(GroundStatus.Deleted, row.Status);
        Assert.Null(row.ClaimedAt);
    }

    [Fact]
    public async Task Deleting_taze_sahiplenmeye_DOKUNULMAZ()
    {
        await SeedAsync(GroundStatus.Deleting, DateTime.UtcNow.AddMinutes(-5), "remote-1",
            deleteRequestedAt: DateTime.UtcNow.AddDays(-30));
        await using var db = NewContext();

        var reclaimed = await NewService(db, new OkProvider())
            .ReclaimStaleClaimsAsync(TimeSpan.FromMinutes(30), CancellationToken.None);

        Assert.Equal(0, reclaimed);
        Assert.Equal(GroundStatus.Deleting, (await LoadAsync()).Status);
    }

    [Fact]
    public async Task Provisioning_bayat_iptal_sahiplenmesi_PendingDelete_olur_ve_pencere_bastan_baslar()
    {
        await SeedAsync(GroundStatus.Provisioning, DateTime.UtcNow.AddHours(-2), "remote-1");
        await using var db = NewContext();

        var reclaimed = await NewService(db, new OkProvider())
            .ReclaimStaleClaimsAsync(TimeSpan.FromMinutes(30), CancellationToken.None);

        Assert.Equal(1, reclaimed);
        var row = await LoadAsync();
        Assert.Equal(GroundStatus.PendingDelete, row.Status);
        Assert.Null(row.ClaimedAt);
        Assert.NotNull(row.DeleteRequestedAt);
        Assert.True(row.DeleteRequestedAt > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Provisioning_bayat_ilk_acilis_Failed_olur()
    {
        await SeedAsync(GroundStatus.Provisioning, DateTime.UtcNow.AddHours(-2), providerProjectId: null);
        await using var db = NewContext();

        await NewService(db, new OkProvider())
            .ReclaimStaleClaimsAsync(TimeSpan.FromMinutes(30), CancellationToken.None);

        var row = await LoadAsync();
        Assert.Equal(GroundStatus.Failed, row.Status);
        Assert.NotNull(row.Error);
        Assert.Null(row.ClaimedAt);
    }

    [Fact]
    public async Task ClaimedAt_null_olan_eski_asili_kayda_DOKUNULMAZ()
    {
        await SeedAsync(GroundStatus.Deleting, claimedAt: null, providerProjectId: "remote-1",
            deleteRequestedAt: DateTime.UtcNow.AddDays(-30), createdAt: DateTime.UtcNow.AddDays(-40));
        await using var db = NewContext();

        var reclaimed = await NewService(db, new OkProvider())
            .ReclaimStaleClaimsAsync(TimeSpan.FromMinutes(30), CancellationToken.None);

        Assert.Equal(0, reclaimed);
        Assert.Equal(GroundStatus.Deleting, (await LoadAsync()).Status);
    }

    [Fact]
    public async Task Basarili_purge_ClaimedAt_temizler()
    {
        await SeedAsync(GroundStatus.PendingDelete, null, "remote-1",
            deleteRequestedAt: DateTime.UtcNow.AddDays(-30));
        await using var db = NewContext();

        await NewService(db, new OkProvider()).PurgeExpiredAsync(7, CancellationToken.None);

        var row = await LoadAsync();
        Assert.Equal(GroundStatus.Deleted, row.Status);
        Assert.Null(row.ClaimedAt);
    }

    // ── Hub slug benzersizliği ──────────────────────────────────────────

    private static CloudProject Project(string id, string userId, string? slug) => new()
    {
        Id = id, Name = id, DbType = "PostgreSQL", SchemaJson = "{}", NodePositionsJson = "{}",
        UserId = userId, Slug = slug,
    };

    [Fact]
    public async Task Ayni_sahipte_ayni_slug_veritabani_tarafindan_reddedilir()
    {
        await using var db = NewContext();
        db.Users.AddRange(new ApplicationUser { Id = "a", UserName = "a" }, new ApplicationUser { Id = "b", UserName = "b" });
        db.CloudProjects.AddRange(Project("p1", "a", "shop"), Project("p3", "b", "shop"));
        await db.SaveChangesAsync(); // farklı sahipler aynı slug'ı kullanabilir

        db.CloudProjects.Add(Project("p2", "a", "shop"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Bos_ve_null_slug_benzersizlik_disinda()
    {
        await using var db = NewContext();
        db.Users.Add(new ApplicationUser { Id = "a", UserName = "a" });
        db.CloudProjects.AddRange(
            Project("p1", "a", null), Project("p2", "a", null),
            Project("p3", "a", ""), Project("p4", "a", ""));

        await db.SaveChangesAsync();
    }
}
