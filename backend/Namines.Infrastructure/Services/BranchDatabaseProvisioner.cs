using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Branch başına canlı veritabanı (new-phase/06-DATA-PLANE.md §4).
///
/// <b>docker.sock mount EDİLMEZ.</b> Bu servis host sürecinde çalışır ve Docker
/// API'sine oradan konuşur — CLAUDE.md'nin kesin kuralı ve 30 §5'in "worker kendi
/// host'unda çalıştırır" köprüsü. <see cref="BranchTestRunnerService"/> ile aynı
/// model; oradaki Docker.DotNet/Testcontainers çakışma notu burada da geçerli.
///
/// Test koşucusundan AYRILDIĞI yer ömürdür: orada container DDL çalışır çalışmaz
/// atılır, burada branch kapanana veya süresi dolana kadar yaşar ve bağlanılabilir.
/// Bu, üç yeni sorumluluk getiriyor ve üçü de bilinçli olarak çözülmüş durumda:
///
///  1. <b>Erişilebilirlik.</b> Port yayımlanmalı, ama YALNIZCA 127.0.0.1'e.
///     0.0.0.0'a yayımlamak, bilinen kullanıcı adıyla çalışan bir veritabanını
///     makinenin bağlı olduğu her ağa açardı.
///  2. <b>Kimlik.</b> Test koşucusunun sabit parolası burada kabul edilemez; her
///     branch kendi rastgele parolasını alır. Kısa ömürlü ve atılabilir bir
///     container'da bile sabit parola, yayımlanmış bir portla birleşince gerçek
///     bir açıklıktır.
///  3. <b>Ömür.</b> Zaman aşımı olmadan bu container'lar birikir; kimse açtığı
///     her branch'i kapatmayı hatırlamaz. Her container son kullanma zamanını
///     ETİKETİNDE taşır, böylece süpürme sunucu yeniden başlasa bile çalışır —
///     durum bellekte tutulsaydı restart sonrası container'lar sahipsiz kalırdı.
/// </summary>
public sealed class BranchDatabaseProvisioner : IBranchDatabaseProvisioner, IDisposable
{
    private readonly DockerClient _client;
    private readonly IDdlGeneratorFactory _ddlFactory;
    private readonly ISmartSeedService _seedService;
    private readonly ILogger<BranchDatabaseProvisioner> _logger;

    /// <summary>Container adı branch'i taşır — süpürücü ve "zaten var mı" kontrolü buna bakar.</summary>
    private const string NamePrefix = "namines-branchdb-";

    private const string ExpiresAtLabel = "com.namines.branchdb.expires-at";
    private const string BranchLabel = "com.namines.branchdb.branch-id";
    private const string EngineLabel = "com.namines.branchdb.engine";
    private const string PasswordLabel = "com.namines.branchdb.password";

    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(8);
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(3);

    private const string DatabaseName = "naminesdb";

    public BranchDatabaseProvisioner(
        IDdlGeneratorFactory ddlFactory, ISmartSeedService seedService,
        ILogger<BranchDatabaseProvisioner> logger)
    {
        _ddlFactory = ddlFactory;
        _seedService = seedService;
        _logger = logger;

        var dockerUri = Environment.OSVersion.Platform == PlatformID.Win32NT
            ? "npipe://./pipe/docker_engine"
            : "unix:///var/run/docker.sock";
        _client = new DockerClientConfiguration(new Uri(dockerUri)).CreateClient();
    }

    public static bool IsEngineSupported(DatabaseType engine) =>
        engine is DatabaseType.PostgreSQL or DatabaseType.MySQL or DatabaseType.MSSQL;

    public async Task<BranchDatabase> ProvisionAsync(
        string branchId, DatabaseSchema schema, DatabaseType engine,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentNullException.ThrowIfNull(schema);

        if (!IsEngineSupported(engine))
            throw new NotSupportedException(
                $"Branch databases aren't available for {engine} yet. " +
                "Supported: PostgreSQL, MySQL, SQL Server.");

        // Aynı branch için ikinci bir container açmak, her sayfa yenilemesinde host'ta
        // bir veritabanı daha bırakırdı. Var olanı döndürmek doğru davranış.
        var existing = await GetAsync(branchId, cancellationToken);
        if (existing is not null) return existing;

        var password = GeneratePassword();
        var expiresAt = DateTime.UtcNow.Add(DefaultTtl);
        var profile = BuildProfile(engine, password);

        await EnsureImageAsync(profile, cancellationToken);

        var create = await _client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Name = NamePrefix + Sanitize(branchId),
            Image = $"{profile.Image}:{profile.Tag}",
            Env = profile.EnvVars.Select(kv => $"{kv.Key}={kv.Value}").ToList(),
            Labels = new Dictionary<string, string>
            {
                [BranchLabel] = branchId,
                [EngineLabel] = engine.ToString(),
                [ExpiresAtLabel] = expiresAt.ToString("O", CultureInfo.InvariantCulture),
                // Parola etikette: süreç yeniden başladığında bağlantı dizesini
                // yeniden üretebilmenin tek yolu bu. Etiketleri okuyabilen biri zaten
                // Docker API'sine sahip demektir — yani host'ta zaten tam yetkilidir;
                // burada saklamak yeni bir yetki sınırı geçmiyor.
                [PasswordLabel] = password,
            },
            HostConfig = new HostConfig
            {
                Memory = engine == DatabaseType.MSSQL ? 2500L * 1024 * 1024 : 2048L * 1024 * 1024,
                NanoCPUs = 1_000_000_000,
                // AutoRemove YOK: bu container yaşamaya devam etmeli.
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    [$"{profile.ContainerPort}/tcp"] = new List<PortBinding>
                    {
                        // HostPort boş → Docker boş bir port seçer. HostIP sabit
                        // 127.0.0.1: dışarıya açılmaması bilinçli (sınıf yorumu §1).
                        new() { HostIP = "127.0.0.1", HostPort = string.Empty },
                    },
                },
            },
            ExposedPorts = new Dictionary<string, EmptyStruct>
            {
                [$"{profile.ContainerPort}/tcp"] = default,
            },
        }, cancellationToken);

        try
        {
            await _client.Containers.StartContainerAsync(create.ID, new ContainerStartParameters(), cancellationToken);

            var port = await ResolveHostPortAsync(create.ID, profile.ContainerPort, cancellationToken);
            var database = new BranchDatabase(
                branchId, engine, "127.0.0.1", port, DatabaseName, profile.Username, password, expiresAt);

            if (!await WaitForReadyAsync(database, cancellationToken))
                throw new InvalidOperationException("The branch database did not become ready in time.");

            await ApplySchemaAsync(create.ID, schema, engine, password, cancellationToken);

            _logger.LogInformation(
                "Branch database provisioned for {BranchId} ({Engine}) on port {Port}, expires {ExpiresAt:O}.",
                branchId, engine, port, expiresAt);

            return database;
        }
        catch
        {
            // Yarım kalmış container host'ta kalmasın: hazır olamayan ya da şeması
            // uygulanamayan bir veritabanı kullanıcıya hiçbir işe yaramaz, ama
            // belleği tutmaya devam eder.
            await TryRemoveAsync(create.ID, CancellationToken.None);
            throw;
        }
    }

    public async Task<BranchDatabase?> GetAsync(string branchId, CancellationToken cancellationToken = default)
    {
        var container = await FindContainerAsync(branchId, cancellationToken);
        if (container is null) return null;

        // Durum container'ın ETİKETLERİNDEN okunur, bellekten değil — sunucu yeniden
        // başladığında da branch veritabanı bulunabilir olmalı.
        if (!container.Labels.TryGetValue(EngineLabel, out var engineName) ||
            !Enum.TryParse<DatabaseType>(engineName, out var engine))
            return null;

        container.Labels.TryGetValue(PasswordLabel, out var password);
        container.Labels.TryGetValue(ExpiresAtLabel, out var expiresRaw);

        var expiresAt = DateTime.TryParse(
            expiresRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTime.UtcNow;

        var containerPort = ProfilePort(engine);
        var port = container.Ports?
            .FirstOrDefault(p => p.PrivatePort == containerPort && p.PublicPort != 0)?.PublicPort ?? 0;

        if (port == 0) return null;

        return new BranchDatabase(
            branchId, engine, "127.0.0.1", port, DatabaseName,
            UsernameFor(engine), password ?? string.Empty, expiresAt);
    }

    public async Task<int> SeedAsync(
        string branchId, DatabaseSchema schema, int rowsPerTable = 25,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var database = await GetAsync(branchId, cancellationToken)
            ?? throw new InvalidOperationException(
                "This branch has no live database yet. Provision one before seeding.");

        var container = await FindContainerAsync(branchId, cancellationToken)
            ?? throw new InvalidOperationException("The branch database container disappeared.");

        // forceDeterministic: yapay zekâ çağrısı YOK — bkz. arayüz yorumu.
        var seed = await _seedService.GenerateSmartSeedAsync(
            schema, database.Engine, domainHint: null, rowCount: rowsPerTable, forceDeterministic: true);

        if (string.IsNullOrWhiteSpace(seed.SqlScript)) return 0;

        using var tar = DockerTarFile.SingleFile("seed.sql", seed.SqlScript);
        await _client.Containers.ExtractArchiveToContainerAsync(
            container.ID, new ContainerPathStatParameters { Path = "/tmp" }, tar, cancellationToken);

        var command = database.Engine switch
        {
            DatabaseType.PostgreSQL => new[]
            {
                "sh", "-c",
                $"PGPASSWORD={database.Password} psql -U {database.Username} -d {DatabaseName} " +
                "-f /tmp/seed.sql -v ON_ERROR_STOP=1",
            },
            DatabaseType.MySQL => new[]
            {
                "sh", "-c", $"mysql -u {database.Username} -p\"{database.Password}\" {DatabaseName} < /tmp/seed.sql",
            },
            DatabaseType.MSSQL => new[]
            {
                "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", database.Username,
                "-P", database.Password, "-C", "-b", "-d", DatabaseName, "-i", "/tmp/seed.sql",
            },
            _ => throw new NotSupportedException(),
        };

        var (exit, output) = await ExecAsync(container.ID, command, cancellationToken);

        // Yarım uygulanmış tohum verisi sessizce "başarılı" sayılmamalı: kullanıcı
        // eksik veriyle çalışıp yanlış sonuca varır. ON_ERROR_STOP / -b bunu garanti eder.
        if (exit != 0)
            throw new InvalidOperationException($"The seed data could not be applied: {output}");

        return seed.TableRowCounts.Values.Sum();
    }

    public async Task DestroyAsync(string branchId, CancellationToken cancellationToken = default)
    {
        var container = await FindContainerAsync(branchId, cancellationToken);
        if (container is null) return;
        await TryRemoveAsync(container.ID, cancellationToken);
    }

    public async Task<int> SweepExpiredAsync(CancellationToken cancellationToken = default)
    {
        var containers = await _client.Containers.ListContainersAsync(
            new ContainersListParameters { All = true }, cancellationToken);

        var removed = 0;
        foreach (var container in containers)
        {
            if (container.Labels is null || !container.Labels.ContainsKey(BranchLabel)) continue;
            if (!container.Labels.TryGetValue(ExpiresAtLabel, out var raw)) continue;

            if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt))
                continue;

            if (expiresAt > DateTime.UtcNow) continue;

            await TryRemoveAsync(container.ID, cancellationToken);
            removed++;
            _logger.LogInformation(
                "Expired branch database removed: {Branch}.",
                container.Labels.TryGetValue(BranchLabel, out var b) ? b : container.ID);
        }
        return removed;
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────

    private async Task<ContainerListResponse?> FindContainerAsync(string branchId, CancellationToken ct)
    {
        var expected = "/" + NamePrefix + Sanitize(branchId);
        var containers = await _client.Containers.ListContainersAsync(
            new ContainersListParameters { All = true }, ct);

        return containers.FirstOrDefault(c =>
            c.Names is not null &&
            c.Names.Any(n => string.Equals(n, expected, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task TryRemoveAsync(string containerId, CancellationToken ct)
    {
        try
        {
            await _client.Containers.RemoveContainerAsync(
                containerId, new ContainerRemoveParameters { Force = true, RemoveVolumes = true }, ct);
        }
        catch (DockerApiException ex)
        {
            // "Zaten yok" bir hata değil; başka bir sebep varsa görünür olmalı.
            _logger.LogWarning(ex, "Branch database container {Id} could not be removed.", containerId);
        }
    }

    private async Task EnsureImageAsync(BranchProfile profile, CancellationToken ct)
    {
        var images = await _client.Images.ListImagesAsync(new ImagesListParameters { All = true }, ct);
        var tag = $"{profile.Image}:{profile.Tag}";
        if (images.Any(i => i.RepoTags is not null && i.RepoTags.Contains(tag))) return;

        await _client.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = profile.Image, Tag = profile.Tag },
            new AuthConfig(), new Progress<JSONMessage>(), ct);
    }

    private async Task<int> ResolveHostPortAsync(string containerId, int containerPort, CancellationToken ct)
    {
        var inspect = await _client.Containers.InspectContainerAsync(containerId, ct);
        var key = $"{containerPort}/tcp";

        if (inspect.NetworkSettings?.Ports is not null &&
            inspect.NetworkSettings.Ports.TryGetValue(key, out var bindings) &&
            bindings is { Count: > 0 } &&
            int.TryParse(bindings[0].HostPort, out var port))
            return port;

        throw new InvalidOperationException("Docker did not publish a host port for the branch database.");
    }

    /// <summary>
    /// Hazır olma kontrolü: container İÇİNDEN bir komut değil, HOST'tan gerçek bir
    /// bağlantı.
    ///
    /// Container içi problar bu imajlarda yanıltıcı. Postgres imajı önce yalnızca
    /// unix soketinde dinleyen GEÇİCİ bir sunucu başlatıp init'i çalıştırıyor, sonra
    /// onu kapatıp gerçek sunucuyu açıyor; <c>pg_isready</c> o geçici sunucuya
    /// "hazır" diyor ve hemen ardından gelen şema uygulaması düşüyor. MySQL 8'de
    /// aynı tuzağa <c>mysqladmin ping</c> ile düşülmüştü (G-ekstra).
    ///
    /// Host'tan bağlanmak bu sınıfın tamamını çözüyor, çünkü geçici sunucular TCP'de
    /// dinlemiyor. Üstelik doğru soruyu soruyor: "kullanıcıya vereceğimiz bağlantı
    /// dizesi ŞU AN çalışıyor mu?" — hazır saydığımız şeyle teslim ettiğimiz şey
    /// aynı olmalı.
    /// </summary>
    private static async Task<bool> WaitForReadyAsync(BranchDatabase database, CancellationToken ct)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < ReadyTimeout)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using DbConnection conn = database.Engine switch
                {
                    DatabaseType.PostgreSQL => new NpgsqlConnection(database.ConnectionString),
                    DatabaseType.MySQL => new MySqlConnection(database.ConnectionString),
                    DatabaseType.MSSQL => new SqlConnection(database.ConnectionString),
                    _ => throw new NotSupportedException(),
                };

                await conn.OpenAsync(ct);

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1";
                await cmd.ExecuteScalarAsync(ct);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Henüz ayakta değil. Sebebi burada ayıklamıyoruz: başlangıç sırasında
                // "bağlantı reddedildi", "kimlik doğrulanamadı" ve "veritabanı yok"
                // hepsi geçici olabiliyor.
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
        return false;
    }

    private async Task ApplySchemaAsync(
        string containerId, DatabaseSchema schema, DatabaseType engine, string password, CancellationToken ct)
    {
        var ddl = _ddlFactory.GetGenerator(engine).Generate(schema);
        if (string.IsNullOrWhiteSpace(ddl)) return;

        using var tar = DockerTarFile.SingleFile("schema.sql", ddl);
        await _client.Containers.ExtractArchiveToContainerAsync(
            containerId, new ContainerPathStatParameters { Path = "/tmp" }, tar, ct);

        if (engine == DatabaseType.MSSQL)
        {
            var (createExit, createOut) = await ExecAsync(containerId, new[]
            {
                "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa", "-P", password, "-C", "-b", "-Q",
                $"IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = '{DatabaseName}') CREATE DATABASE {DatabaseName};",
            }, ct);

            if (createExit != 0)
                throw new InvalidOperationException($"Could not create the branch database: {createOut}");
        }

        // -b / ON_ERROR_STOP: bunlar olmadan istemciler hatalı DDL'de bile 0 döner
        // ve şeması yarım uygulanmış bir veritabanı "hazır" görünür (G12 dersi).
        var apply = engine switch
        {
            DatabaseType.PostgreSQL => new[]
            {
                "sh", "-c",
                $"PGPASSWORD={password} psql -U postgres -d {DatabaseName} -f /tmp/schema.sql -v ON_ERROR_STOP=1",
            },
            DatabaseType.MySQL => new[]
            {
                "sh", "-c", $"mysql -u root -p\"{password}\" {DatabaseName} < /tmp/schema.sql",
            },
            DatabaseType.MSSQL => new[]
            {
                "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa", "-P", password,
                "-C", "-b", "-d", DatabaseName, "-i", "/tmp/schema.sql",
            },
            _ => throw new NotSupportedException(),
        };

        var (exit, output) = await ExecAsync(containerId, apply, ct);
        if (exit != 0)
            throw new InvalidOperationException($"The schema could not be applied to the branch database: {output}");
    }

    private async Task<(int ExitCode, string Output)> ExecAsync(string containerId, string[] command, CancellationToken ct)
    {
        var exec = await _client.Exec.ExecCreateContainerAsync(containerId, new ContainerExecCreateParameters
        {
            AttachStdout = true,
            AttachStderr = true,
            Cmd = command,
        }, ct);

        using var stream = await _client.Exec.StartAndAttachContainerExecAsync(exec.ID, false, ct);
        var (stdout, stderr) = await stream.ReadOutputToEndAsync(ct);
        var inspect = await _client.Exec.InspectContainerExecAsync(exec.ID, ct);

        return ((int)inspect.ExitCode, string.IsNullOrWhiteSpace(stderr) ? stdout : stdout + stderr);
    }

    // ── Profil ───────────────────────────────────────────────────────────────

    private sealed record BranchProfile(
        string Image, string Tag, string Username, int ContainerPort, Dictionary<string, string> EnvVars);

    private static int ProfilePort(DatabaseType engine) => engine switch
    {
        DatabaseType.PostgreSQL => 5432,
        DatabaseType.MySQL => 3306,
        DatabaseType.MSSQL => 1433,
        _ => throw new NotSupportedException(),
    };

    private static string UsernameFor(DatabaseType engine) => engine switch
    {
        DatabaseType.PostgreSQL => "postgres",
        DatabaseType.MySQL => "root",
        DatabaseType.MSSQL => "sa",
        _ => throw new NotSupportedException(),
    };

    /// <summary>
    /// <see cref="ContainerProfiles"/> YENİDEN KULLANILMIYOR: oradaki profiller sabit
    /// parola taşıyor ve atılabilir test container'ları için yazıldı. Burada parola
    /// branch'e özel üretiliyor ve port yayımlandığı için sabit parola gerçek bir
    /// açıklık olurdu.
    /// </summary>
    private static BranchProfile BuildProfile(DatabaseType engine, string password) => engine switch
    {
        DatabaseType.PostgreSQL => new("postgres", "15-alpine", "postgres", 5432, new()
        {
            ["POSTGRES_PASSWORD"] = password,
            ["POSTGRES_USER"] = "postgres",
            ["POSTGRES_DB"] = DatabaseName,
        }),
        DatabaseType.MySQL => new("mysql", "8.0", "root", 3306, new()
        {
            ["MYSQL_ROOT_PASSWORD"] = password,
            ["MYSQL_DATABASE"] = DatabaseName,
        }),
        DatabaseType.MSSQL => new("mcr.microsoft.com/mssql/server", "2022-latest", "sa", 1433, new()
        {
            ["ACCEPT_EULA"] = "Y",
            ["MSSQL_SA_PASSWORD"] = password,
            ["MSSQL_PID"] = "Developer",
        }),
        _ => throw new NotSupportedException($"No branch database profile for {engine}."),
    };

    /// <summary>
    /// Rastgele parola. SQL Server'ın karmaşıklık kuralını (büyük/küçük/rakam/simge)
    /// karşılaması ŞART — karşılamazsa container sessizce başlamaz ve hata
    /// "veritabanı hazır olmadı" gibi görünür, ki bu yanlış yere baktırır.
    /// </summary>
    internal static string GeneratePassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "_-.!";

        var chars = new List<char>
        {
            Pick(upper), Pick(lower), Pick(digits), Pick(symbols),
        };
        const string all = upper + lower + digits;
        for (var i = 0; i < 20; i++) chars.Add(Pick(all));

        // Zorunlu karakterler baştaki sabit konumlarında kalmasın.
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars.ToArray());

        static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
    }

    /// <summary>Docker container adları sınırlı bir karakter kümesi kabul eder.</summary>
    internal static string Sanitize(string value) =>
        new(value.Select(c => char.IsLetterOrDigit(c) || c is '_' or '.' or '-' ? c : '-').ToArray());

    public void Dispose() => _client.Dispose();
}
