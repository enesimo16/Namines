using System;
using System.Collections.Generic;
using System.IO;
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
/// <see cref="VaultService"/> — sürücü/araç hatalarının istemciye nasıl
/// yansıdığı.
///
/// <b>Bulunan açık:</b> yedekleme/geri yükleme/doğrulama sırasında oluşan
/// istisnaların HAM <c>ex.Message</c>'ı (kısaltılmış olsa da) doğrudan
/// <c>VaultBackup.ErrorMessage</c>/<c>VerifyError</c>/<c>VaultRestore.ErrorMessage</c>
/// alanlarına yazılıyordu — bu alanlar <c>VaultController</c>'ın GetBackup/List/
/// Restores/Verify uçlarından AYNEN istemciye dönüyor. Npgsql/MySqlConnector
/// gibi sürücüler bağlantı hatalarında hedef host/port'u mesaja gömdüğü için
/// bu, Gateway'in "ham sürücü metni asla istemciye" kuralını Vault'ta ihlal
/// ediyordu. Düzeltme: <see cref="DbConnectionFailure.Classify"/> ile aynı
/// güvenli kategoriler kullanılıyor; ham metin yalnızca loga gidiyor.
/// </summary>
public sealed class VaultErrorSanitizationTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<AuthDbContext> _options = null!;

    private const string SensitiveHost = "internal-db-07.corp.example:5432";

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

    private sealed class InertBackupStore : IBackupStore
    {
        private readonly byte[] _encryptedContent;

        public InertBackupStore(byte[]? encryptedContent = null) =>
            _encryptedContent = encryptedContent ?? new byte[] { 1, 2, 3 };

        public string Description => "fake";
        public Task<long> PutAsync(string key, Stream content, CancellationToken ct) => Task.FromResult(0L);
        public Task<Stream?> OpenAsync(string key, CancellationToken ct) =>
            Task.FromResult<Stream?>(new MemoryStream(_encryptedContent));
        public Task DeleteAsync(string key, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>Gerçek Npgsql/mysqldump hatalarının taşıdığı gibi HEDEF ADRESİ içeren bir mesajla patlar.</summary>
    private sealed class FailingBackupProvider : IBackupProvider
    {
        public string Engine => "PostgreSQL";

        public Task BackupAsync(BackupSpec spec, Stream destination, CancellationToken ct) =>
            throw new InvalidOperationException($"Failed to connect to {SensitiveHost}");

        public Task RestoreAsync(RestoreSpec spec, Stream source, CancellationToken ct) =>
            throw new InvalidOperationException($"Failed to connect to {SensitiveHost}");

        public Task<string?> VerifyAsync(Stream source, CancellationToken ct) =>
            throw new InvalidOperationException($"Failed to connect to {SensitiveHost}");

        public Task<string?> ProbeAsync(CancellationToken ct) => Task.FromResult<string?>(null);
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

    private VaultService NewService(AuthDbContext db, IBackupStore store) => new(
        db, new PassthroughProtector(), new AllowAllHostPolicy(),
        new IBackupProvider[] { new FailingBackupProvider() }, store, NewCipher(),
        NullLogger<VaultService>.Instance);

    private async Task<CloudProject> SeedProjectAsync(AuthDbContext db)
    {
        db.Users.Add(new ApplicationUser { Id = "u1", UserName = "u1" });
        var project = new CloudProject
        {
            Id = "p1", Name = "proj", DbType = "PostgreSQL",
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = "u1",
            EncryptedConnectionString = "enc:Host=db.example.com;Database=x;Username=u;Password=p",
            ConnectionDbType = "PostgreSQL",
        };
        db.CloudProjects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    [Fact]
    public async Task Basarisiz_yedekte_ham_surucu_metni_ErrorMessage_a_YAZILMAZ()
    {
        await using var db = NewContext();
        var project = await SeedProjectAsync(db);
        var service = NewService(db, new InertBackupStore());

        var result = await service.BackupAsync(project, "u1", VaultBackupKind.Manual, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.DoesNotContain(SensitiveHost, result.Error);
        Assert.DoesNotContain("internal-db-07", result.Error);

        await using var verify = NewContext();
        var record = await verify.VaultBackups.SingleAsync(b => b.ProjectId == "p1");
        Assert.Equal(VaultBackupStatus.Failed, record.Status);
        Assert.DoesNotContain(SensitiveHost, record.ErrorMessage);
    }

    [Fact]
    public async Task Dogrulama_basarisizliginda_ham_surucu_metni_VerifyError_a_YAZILMAZ()
    {
        await using var db = NewContext();
        await SeedProjectAsync(db);

        var backup = new VaultBackup
        {
            ProjectId = "p1",
            CreatedByUserId = "u1",
            DatabaseName = "db",
            Engine = "PostgreSQL",
            Kind = VaultBackupKind.Manual,
            Status = VaultBackupStatus.Succeeded,
            StoreDescription = "fake",
            StorageKey = "p1/existing.nvlt",
        };
        db.VaultBackups.Add(backup);
        await db.SaveChangesAsync();

        // Doğrulama sağlayıcıya (FailingBackupProvider.VerifyAsync) ULAŞSIN
        // diye depo GEÇERLİ şekilde şifrelenmiş bir içerik döndürüyor —
        // aksi hâlde çözme aşaması (bozuk/rastgele bayt) kendi jenerik
        // hatasıyla erken çıkar ve asıl senaryo (sağlayıcının ham mesajı)
        // hiç denenmemiş olurdu.
        await using var encrypted = new MemoryStream();
        await NewCipher().EncryptAsync(new MemoryStream(new byte[] { 9, 9, 9 }), encrypted, CancellationToken.None);
        var service = NewService(db, new InertBackupStore(encrypted.ToArray()));

        var result = await service.VerifyAsync(backup, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.DoesNotContain(SensitiveHost, result.Error);
        Assert.DoesNotContain("internal-db-07", result.Error);
    }
}
