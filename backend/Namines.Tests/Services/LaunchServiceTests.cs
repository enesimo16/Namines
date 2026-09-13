using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Ground.Providers;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Security;
using Namines.Infrastructure.Services;
using Namines.Tests.Integration;
using Namines.Vault.Abstractions;
using Namines.Vault.Providers;
using Namines.Vault.Storage;
using Testcontainers.PostgreSql;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// LaunchService'in iki dalını GERÇEK bir PostgreSQL'e karşı kanıtlar: bir
/// konteynerin admin bağlantısı hem AuthDbContext'in kontrol veritabanı hem
/// de LocalPostgresProvider'ın "kendi sunucumuz" hedefi olarak kullanılıyor —
/// tıpkı gerçek geliştirme ortamındaki gibi (bkz. docker-compose.yml,
/// namines_control + namines_db_* aynı sunucuda).
/// </summary>
[Collection("Docker")]
public class LaunchServiceTests : IAsyncLifetime
{
    // DbHostAccessPolicyTests.FakeEnv ile aynı desen — o sınıf private olduğu
    // için burada AYNI 5 satır tekrarlanıyor, paylaşılan bir test double
    // çıkarmak bu tek kullanım için gereksiz bir soyutlama olurdu.
    private sealed class FakeEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    private AuthDbContext? _context;
    private LaunchService? _launch;
    private string _userId = null!;
    private IConfiguration _config = null!;

    public async Task InitializeAsync()
    {
        if (!DockerAvailable.Value) return;

        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        _context = new AuthDbContext(options);
        await _context.Database.MigrateAsync();

        _userId = Guid.NewGuid().ToString();
        await _context.Users.AddAsync(new ApplicationUser { Id = _userId, UserName = "launch-test" });
        await _context.SaveChangesAsync();

        _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ground:LocalPostgres:AdminConnectionString"] = _container.GetConnectionString(),
            ["Security:ConnectionEncryptionKey"] = "test-launch-service-key-at-least-32-chars",
            ["Security:AllowPrivateDbHosts"] = "true",
            ["Vault:BackupEncryptionKey"] = "test-launch-vault-key-at-least-32-chars",
            ["Vault:StoragePath"] = Path.Combine(Path.GetTempPath(), "namines-launch-tests-" + Guid.NewGuid()),
        }).Build();

        var protector = new AesGcmConnectionSecretProtector(_config);
        var hostPolicy = new DbHostAccessPolicy(
            new FakeEnv(), _config, NullLogger<DbHostAccessPolicy>.Instance);

        var ground = new GroundService(
            _context, protector,
            new[] { new LocalPostgresProvider(_config, NullLogger<LocalPostgresProvider>.Instance) },
            NullLogger<GroundService>.Instance);

        var vault = new VaultService(
            _context, protector, hostPolicy,
            new IBackupProvider[] { new PostgresBackupProvider(_config, NullLogger<PostgresBackupProvider>.Instance) },
            new FileSystemBackupStore(_config),
            new BackupCipher(_config),
            NullLogger<VaultService>.Instance);

        _launch = new LaunchService(
            ground,
            new DbIntrospectionService(NullLogger<DbIntrospectionService>.Instance, hostPolicy),
            new DatabaseExecutorService(hostPolicy),
            vault,
            protector,
            NullLogger<LaunchService>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (!DockerAvailable.Value) return;
        if (_context is not null) await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    private async Task<CloudProject> NewBareProjectAsync()
    {
        var project = new CloudProject
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Launch Test " + Guid.NewGuid().ToString("N")[..8],
            DbType = "PostgreSQL",
            SchemaJson = "{}",
            NodePositionsJson = "{}",
            UserId = _userId,
        };
        await _context!.CloudProjects.AddAsync(project);
        await _context.SaveChangesAsync();
        return project;
    }

    private const string OneTableDdl = """
        CREATE TABLE widgets (
            id SERIAL PRIMARY KEY,
            name TEXT NOT NULL
        );
        """;

    [RequiresDockerFact]
    public async Task Empty_target_gets_provisioned_and_ddl_applied_and_backed_up()
    {
        var project = await NewBareProjectAsync();

        var result = await _launch!.LaunchAsync(project, _userId, "LocalPostgres", OneTableDdl, default);

        Assert.Equal(LaunchStatus.Ready, result.Status);
        Assert.True(result.DdlApplied);
        Assert.Null(result.BackupWarning);

        // Bağımsız doğrulama: LaunchService'in ördüğü aynı EF izleme hattından
        // DEĞİL, taze bir DbContext'le tekrar okumak.
        var reloadOptions = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(_container.GetConnectionString()).Options;
        await using var reloaded = new AuthDbContext(reloadOptions);
        var refreshed = await reloaded.CloudProjects.AsNoTracking()
            .FirstAsync(p => p.Id == project.Id);
        Assert.NotNull(refreshed.EncryptedConnectionString);

        var backupCount = await reloaded.VaultBackups.CountAsync(b => b.ProjectId == project.Id);
        Assert.Equal(1, backupCount);
    }

    [RequiresDockerFact]
    public async Task Target_with_existing_tables_needs_review_and_never_touches_ddl()
    {
        var project = await NewBareProjectAsync();

        // Önce sıfırdan bir kez çalıştır (hedefi "dolu" hale getirmek için) —
        // ikinci çağrının davranışını test ediyoruz, birincisini değil.
        var first = await _launch!.LaunchAsync(project, _userId, "LocalPostgres", OneTableDdl, default);
        Assert.Equal(LaunchStatus.Ready, first.Status);

        const string secondDdl = """
            CREATE TABLE gadgets (
                id SERIAL PRIMARY KEY
            );
            """;
        var second = await _launch!.LaunchAsync(project, _userId, "LocalPostgres", secondDdl, default);

        Assert.Equal(LaunchStatus.NeedsReview, second.Status);
        Assert.False(second.DdlApplied);

        // "gadgets" ASLA oluşturulmamalı — DDL'e hiç dokunulmadığının kanıtı.
        var reloadOptions = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(_container.GetConnectionString()).Options;
        await using var reloaded = new AuthDbContext(reloadOptions);
        var refreshed = await reloaded.CloudProjects.AsNoTracking().FirstAsync(p => p.Id == project.Id);
        var connectionString = new AesGcmConnectionSecretProtector(_config)
            .Unprotect(refreshed.EncryptedConnectionString!);

        await using var direct = new Npgsql.NpgsqlConnection(connectionString);
        await direct.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "SELECT 1 FROM information_schema.tables WHERE table_name = 'gadgets'", direct);
        Assert.Null(await cmd.ExecuteScalarAsync());
    }
}
