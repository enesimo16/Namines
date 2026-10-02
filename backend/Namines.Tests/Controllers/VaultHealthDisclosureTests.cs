using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Controllers;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
using Namines.Vault.Abstractions;
using Namines.Vault.Storage;
using Xunit;

namespace Namines.Tests.Controllers;

/// <summary>
/// <c>GET /api/vault/health</c> — anonim çağırana ne söylendiği. docs/last-review M-9.
///
/// <b>Bulunan açık:</b> uç <c>[AllowAnonymous]</c> ve ayrıntıları HERKESE dönüyordu.
/// <c>store</c> alanı <c>FileSystemBackupStore.Description</c>'dan geliyor ve içinde
/// yedeğin durduğu <b>mutlak sunucu dosya yolu</b> var; <c>problem</c> ise
/// sağlayıcının ham hata metnini taşıyabiliyor (Docker socket yolu, iç host adları).
/// İkisi de kimliği doğrulanmamış bir çağıranın işine yaramaz, saldırganın keşif
/// işine yarar.
/// </summary>
public sealed class VaultHealthDisclosureTests : IAsyncLifetime
{
    private const string SecretPath = @"C:\srv\namines\vault-backups-SECRET";
    private const string ProbeProblem = "unix:///var/run/docker.sock erisilemedi";

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

    /// <summary>Açıklamasında sunucu yolu taşıyan sahte depo — asıl sızıntı alanı.</summary>
    private sealed class PathLeakingStore : IBackupStore
    {
        public string Description => $"Local disk ({SecretPath})";
        public Task<long> PutAsync(string key, Stream content, CancellationToken ct) => Task.FromResult(0L);
        public Task<Stream?> OpenAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string key, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>Probe'unda ham altyapı metni dönen sahte sağlayıcı.</summary>
    private sealed class FailingProvider : IBackupProvider
    {
        public string Engine => "PostgreSQL";
        public Task BackupAsync(BackupSpec spec, Stream destination, CancellationToken ct) => Task.CompletedTask;
        public Task RestoreAsync(RestoreSpec spec, Stream source, CancellationToken ct) => Task.CompletedTask;
        public Task<string?> VerifyAsync(Stream source, CancellationToken ct) => Task.FromResult<string?>(null);
        public Task<string?> ProbeAsync(CancellationToken ct) => Task.FromResult<string?>(ProbeProblem);
    }

    private VaultController NewController(bool authenticated)
    {
        var db = new AuthDbContext(_options);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vault:BackupEncryptionKey"] = "test-key-at-least-32-characters-long!!",
            })
            .Build();

        var vault = new VaultService(
            db,
            protector: null!,
            hostPolicy: null!,
            providers: new IBackupProvider[] { new FailingProvider() },
            store: new PathLeakingStore(),
            cipher: new BackupCipher(config),
            logger: NullLogger<VaultService>.Instance);

        var identity = authenticated
            ? new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "u1") }, "TestAuth")
            : new ClaimsIdentity();

        return new VaultController(db, vault, jobs: null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }

    /// <summary>Anonim yanıt JSON'a çevrilerek aranıyor — alan adına değil İÇERİĞE bakıyoruz.</summary>
    private static string Serialize(IActionResult result) =>
        System.Text.Json.JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(result).Value);

    [Fact]
    public async Task Anonim_cagiran_sunucu_dosya_yolunu_GORMUYOR()
    {
        var body = Serialize(await NewController(authenticated: false).Health(default));

        Assert.DoesNotContain(SecretPath, body);
        Assert.DoesNotContain("vault-backups", body);
    }

    [Fact]
    public async Task Anonim_cagiran_ham_altyapi_hata_metnini_GORMUYOR()
    {
        var body = Serialize(await NewController(authenticated: false).Health(default));

        Assert.DoesNotContain(ProbeProblem, body);
        Assert.DoesNotContain("docker.sock", body);
    }

    [Fact]
    public async Task Anonim_cagiran_YINE_DE_calisiyor_mu_sorusunu_alabiliyor()
    {
        // Uç anonim KALIYOR: bir sağlık kontrolü oturum istememeli. Cevabı
        // tamamen kesmek, düzeltmeyi bir kırılmaya çevirirdi.
        var body = Serialize(await NewController(authenticated: false).Health(default));

        Assert.Contains("\"ok\":false", body);
    }

    [Fact]
    public async Task Kimligi_dogrulanmis_cagiran_AYRINTIYI_aliyor()
    {
        // Uçun kendi gerekçesi hâlâ geçerli: kullanıcı yedeğinin nerede
        // durduğunu bilmeden ona güvenmemeli.
        var body = Serialize(await NewController(authenticated: true).Health(default));

        Assert.Contains("vault-backups", body);
        Assert.Contains("PostgreSQL", body);
        Assert.Contains("docker.sock", body);
    }
}
