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
/// <see cref="GroundService.PurgeExpiredAsync"/> — bekleme penceresi dolmuş
/// kayıtların kalıcı silinmesi.
///
/// <b>Bulunan açık:</b> <c>ProviderProjectId</c> null/boşken (provizyon hiç
/// sağlayıcıya ulaşamadan <c>Failed</c> oldu, ya da kayıt zaten temizlendi)
/// yöntem yine de <c>provider.DeleteAsync</c>'i çağırıyordu. Böyle bir çağrı
/// GARANTİ başarısız olur; kayıt <c>PendingDelete</c>'te sonsuza kadar kalır
/// ve her arka plan turunda aynı başarısız silme denemesi tekrarlanır —
/// kaynak tüketen, log kirleten sonsuz bir döngü. Doğrusu: dışarıda
/// temizlenecek bir kaynak yoksa sağlayıcıyı hiç çağırmadan kaydı doğrudan
/// silinmiş işaretlemek.
/// </summary>
public sealed class GroundPurgeTests : IAsyncLifetime
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

    /// <summary>
    /// <c>DeleteAsync</c> çağrılırsa İSTİSNA fırlatan sahte sağlayıcı —
    /// "ProviderProjectId boşken bile DeleteAsync'e gidiliyor mu" sorusunun
    /// kanıtı. Gerçek Neon/Supabase/LocalPostgres de boş bir kimlikle
    /// çağrılınca aynen böyle başarısız olurdu.
    /// </summary>
    private sealed class ExplodingProvider : IDatabaseProvider
    {
        public int DeleteCallCount { get; private set; }
        public string Name => "FakeRemote";
        public ProviderCapabilities Capabilities { get; } = new(
            SupportsBranching: false, SupportsRegionChoice: false, IsLiveVerified: true,
            ResponsibilityNote: "test", CreateIsIdempotentByProjectId: false);

        public Task<ProvisionedDatabase> CreateAsync(ProvisionSpec spec, CancellationToken ct) =>
            throw new NotSupportedException("not used in this test");

        public Task DeleteAsync(ProvisionedDatabase database, CancellationToken ct)
        {
            DeleteCallCount++;
            throw new InvalidOperationException(
                "connection to admin-db.internal.example:5432 failed: role \"ghost\" does not exist");
        }

        public Task<DatabaseMetrics> GetMetricsAsync(ProvisionedDatabase database, CancellationToken ct) =>
            Task.FromResult(new DatabaseMetrics(null, null));

        public Task<string?> ProbeAsync(CancellationToken ct) => Task.FromResult<string?>(null);
    }

    private async Task<GroundDatabase> SeedAsync(AuthDbContext db, string? providerProjectId)
    {
        db.Users.Add(new ApplicationUser { Id = "u1", UserName = "u1" });
        db.CloudProjects.Add(new CloudProject
        {
            Id = "proj-1", Name = "p", DbType = "PostgreSQL",
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = "u1",
        });

        var record = new GroundDatabase
        {
            ProjectId = "proj-1",
            Provider = "FakeRemote",
            CreatedByUserId = "u1",
            Status = GroundStatus.PendingDelete,
            DeleteRequestedAt = DateTime.UtcNow.AddDays(-30),
            ProviderProjectId = providerProjectId,
        };
        db.GroundDatabases.Add(record);
        await db.SaveChangesAsync();
        return record;
    }

    [Fact]
    public async Task ProviderProjectId_null_ise_DeleteAsync_HIC_CAGRILMAZ_ve_kayit_dogrudan_silinir()
    {
        await using var db = NewContext();
        await SeedAsync(db, providerProjectId: null);
        var provider = new ExplodingProvider();
        var service = new GroundService(db, new PassthroughProtector(),
            new[] { (IDatabaseProvider)provider }, NullLogger<GroundService>.Instance);

        var purged = await service.PurgeExpiredAsync(graceDays: 7, CancellationToken.None);

        Assert.Equal(1, purged);
        Assert.Equal(0, provider.DeleteCallCount);

        await using var verify = NewContext();
        var reloaded = await verify.GroundDatabases.SingleAsync(g => g.ProjectId == "proj-1");
        Assert.Equal(GroundStatus.Deleted, reloaded.Status);
        Assert.NotNull(reloaded.DeletedAt);
        Assert.Null(reloaded.Error);
    }

    [Fact]
    public async Task ProviderProjectId_bos_dize_ise_de_DeleteAsync_CAGRILMAZ()
    {
        await using var db = NewContext();
        await SeedAsync(db, providerProjectId: "   ");
        var provider = new ExplodingProvider();
        var service = new GroundService(db, new PassthroughProtector(),
            new[] { (IDatabaseProvider)provider }, NullLogger<GroundService>.Instance);

        var purged = await service.PurgeExpiredAsync(graceDays: 7, CancellationToken.None);

        Assert.Equal(1, purged);
        Assert.Equal(0, provider.DeleteCallCount);
    }

    [Fact]
    public async Task ProviderProjectId_DOLUYSA_DeleteAsync_cagrilir_ve_basarisizlikta_TEKRAR_DENENIR()
    {
        // Gerçek bir dış kaynak varken davranış DEĞİŞMEMELİ: silme denenir,
        // başarısız olursa kayıt PendingDelete'te kalıp bir sonraki turda
        // tekrar denenir — "Deleted" işaretlemek duran bir kaynağı kaybetmek
        // olurdu.
        await using var db = NewContext();
        await SeedAsync(db, providerProjectId: "remote-123");
        var provider = new ExplodingProvider();
        var service = new GroundService(db, new PassthroughProtector(),
            new[] { (IDatabaseProvider)provider }, NullLogger<GroundService>.Instance);

        var purged = await service.PurgeExpiredAsync(graceDays: 7, CancellationToken.None);

        Assert.Equal(0, purged);
        Assert.Equal(1, provider.DeleteCallCount);

        await using var verify = NewContext();
        var reloaded = await verify.GroundDatabases.SingleAsync(g => g.ProjectId == "proj-1");
        Assert.Equal(GroundStatus.PendingDelete, reloaded.Status);
        Assert.Null(reloaded.DeletedAt);

        // Sağlayıcının/sürücünün HAM istisna metni ("admin-db.internal.example:5432",
        // "role \"ghost\"") kayda yazılıp buradan GroundController.Describe
        // üzerinden istemciye GİTMEMELİ.
        Assert.NotNull(reloaded.Error);
        Assert.DoesNotContain("admin-db.internal.example", reloaded.Error);
        Assert.DoesNotContain("ghost", reloaded.Error);
    }
}
