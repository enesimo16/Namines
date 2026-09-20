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
/// <see cref="GroundService.CancelDeleteAsync"/> — bekleme penceresi içindeki
/// bir silmeyi geri almak.
///
/// <b>Bulunan açık:</b> bu metot "CreateAsync idempotan" varsayımıyla
/// sağlayıcıyı YENİDEN çağırıyordu. Yalnızca <c>LocalPostgresProvider</c> için
/// doğruydu; Neon/Supabase her çağrıda KOŞULSUZ yeni bir proje açıyor. Sonuç:
/// kullanıcının orijinal verisi erişilemez kalıyor (bağlantı yeni/boş projeye
/// işaret ediyor) VE <c>ProviderProjectId</c> hiç güncellenmediği için ileride
/// tetiklenecek gerçek silme yanlış (eski, terk edilmiş) projeyi siliyor — yeni
/// proje sonsuza dek faturalanan, kimsenin bilmediği bir kaynak olarak kalıyor.
/// </summary>
public sealed class GroundCancelDeleteTests : IAsyncLifetime
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

    /// <summary>Düz metni olduğu gibi taşıyan sahte koruyucu — testte gerçek şifreleme gerekmiyor.</summary>
    private sealed class PassthroughProtector : IConnectionSecretProtector
    {
        public string Protect(string plaintext) => "enc:" + plaintext;
        public string Unprotect(string ciphertext) => ciphertext.Replace("enc:", "");
    }

    /// <summary>
    /// Her çağrıda YENİ bir "uzak proje" kimliği üreten sahte sağlayıcı —
    /// Neon/Supabase'in gerçek davranışı. `idempotent` false ise (varsayılan)
    /// bu, `CancelDeleteAsync`'in yeni kontrolüyle HİÇ çağrılmamalı.
    /// </summary>
    private sealed class FakeRemoteProvider : IDatabaseProvider
    {
        private int _created;
        public string Name => "FakeRemote";
        public bool CreateWasCalled => _created > 0;

        public ProviderCapabilities Capabilities { get; }

        public FakeRemoteProvider(bool idempotent) => Capabilities = new ProviderCapabilities(
            SupportsBranching: false, SupportsRegionChoice: false, IsLiveVerified: true,
            ResponsibilityNote: "test", CreateIsIdempotentByProjectId: idempotent);

        public Task<ProvisionedDatabase> CreateAsync(ProvisionSpec spec, CancellationToken ct)
        {
            _created++;
            // Her çağrı FARKLI bir kimlik üretiyor — tam olarak Neon/Supabase'in
            // "koşulsuz yeni proje" davranışı.
            return Task.FromResult(new ProvisionedDatabase(
                ProviderProjectId: $"remote-{_created}",
                ProviderBranchId: null, Region: "us-east-1",
                ConnectionString: $"Host=remote-{_created}.example.com;Database=x;Username=u;Password=p"));
        }

        public Task DeleteAsync(ProvisionedDatabase database, CancellationToken ct) => Task.CompletedTask;
        public Task<DatabaseMetrics> GetMetricsAsync(ProvisionedDatabase database, CancellationToken ct) =>
            Task.FromResult(new DatabaseMetrics(null, null));
        public Task<string?> ProbeAsync(CancellationToken ct) => Task.FromResult<string?>(null);
    }

    private async Task<(CloudProject Project, GroundDatabase Record)> SeedPendingDeleteAsync(AuthDbContext db)
    {
        db.Users.Add(new ApplicationUser { Id = "u1", UserName = "u1" });
        var project = new CloudProject
        {
            Id = "proj-1", Name = "p", DbType = "PostgreSQL",
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = "u1",
            // RequestDeleteAsync'in daha önce yaptığı şey: bağlantı temizlendi.
            EncryptedConnectionString = null, ConnectionDbType = null,
        };
        db.CloudProjects.Add(project);

        var record = new GroundDatabase
        {
            ProjectId = project.Id, Provider = "FakeRemote", CreatedByUserId = "u1",
            Status = GroundStatus.PendingDelete, DeleteRequestedAt = DateTime.UtcNow,
            ProviderProjectId = "remote-original",
        };
        db.GroundDatabases.Add(record);

        await db.SaveChangesAsync();
        return (project, record);
    }

    [Fact]
    public async Task Idempotent_OLMAYAN_saglayicida_ikinci_kaynak_ACILMIYOR()
    {
        await using var db = NewContext();
        var (_, record) = await SeedPendingDeleteAsync(db);
        var provider = new FakeRemoteProvider(idempotent: false);
        var service = new GroundService(db, new PassthroughProtector(),
            new[] { (IDatabaseProvider)provider }, NullLogger<GroundService>.Instance);

        var result = await service.CancelDeleteAsync(record, provider, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.False(provider.CreateWasCalled, "CreateAsync hic cagrilmamali — bu, ikinci faturali kaynagin ta kendisi.");
    }

    [Fact]
    public async Task Idempotent_OLMAYAN_saglayicida_ORIJINAL_ProviderProjectId_KORUNUYOR()
    {
        // Reddedilen bir istek kaydı DEĞİŞTİRMEMELİ — aksi hâlde ileride
        // tetiklenecek gerçek silme yine de yanlış kaynağı hedef alabilirdi.
        await using var db = NewContext();
        var (_, record) = await SeedPendingDeleteAsync(db);
        var provider = new FakeRemoteProvider(idempotent: false);
        var service = new GroundService(db, new PassthroughProtector(),
            new[] { (IDatabaseProvider)provider }, NullLogger<GroundService>.Instance);

        await service.CancelDeleteAsync(record, provider, CancellationToken.None);

        await using var verify = NewContext();
        var reloaded = await verify.GroundDatabases.SingleAsync(g => g.ProjectId == "proj-1");
        Assert.Equal("remote-original", reloaded.ProviderProjectId);
        Assert.Equal(GroundStatus.PendingDelete, reloaded.Status);
    }

    [Fact]
    public async Task Idempotent_OLMAYAN_saglayicida_proje_baglantisi_BOS_KALIYOR()
    {
        // Reddedilen bir iptal, kullanıcıyı "iptal oldu ama bağlantı bozuk"
        // durumunda YARIM bırakmamalı — bağlantı zaten null'du, öyle kalıyor.
        await using var db = NewContext();
        var (_, record) = await SeedPendingDeleteAsync(db);
        var provider = new FakeRemoteProvider(idempotent: false);
        var service = new GroundService(db, new PassthroughProtector(),
            new[] { (IDatabaseProvider)provider }, NullLogger<GroundService>.Instance);

        await service.CancelDeleteAsync(record, provider, CancellationToken.None);

        await using var verify = NewContext();
        var reloadedProject = await verify.CloudProjects.SingleAsync(p => p.Id == "proj-1");
        Assert.Null(reloadedProject.EncryptedConnectionString);
    }

    [Fact]
    public async Task Idempotent_saglayicida_ESKI_DAVRANIS_korunuyor()
    {
        // LocalPostgresProvider gibi GERÇEKTEN idempotan bir sağlayıcı için
        // hiçbir şey değişmemeli: CreateAsync çağrılır, bağlantı geri gelir.
        await using var db = NewContext();
        var (_, record) = await SeedPendingDeleteAsync(db);
        var provider = new FakeRemoteProvider(idempotent: true);
        var service = new GroundService(db, new PassthroughProtector(),
            new[] { (IDatabaseProvider)provider }, NullLogger<GroundService>.Instance);

        var result = await service.CancelDeleteAsync(record, provider, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.True(provider.CreateWasCalled);
        Assert.Equal(GroundStatus.Active, result.Database!.Status);

        await using var verify = NewContext();
        var reloadedProject = await verify.CloudProjects.SingleAsync(p => p.Id == "proj-1");
        Assert.NotNull(reloadedProject.EncryptedConnectionString);
    }
}
