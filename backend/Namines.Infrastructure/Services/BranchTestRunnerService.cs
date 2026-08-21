using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Data.Sqlite;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;

namespace Namines.Infrastructure.Services;

/// <summary>
/// <see cref="IBranchTestRunner"/> — "Run Tests" (new-phase/29-DATABASE-CHANGE-REVIEW.md §4).
/// Impact Analysis bir TAHMİNdir; bu KANITTIR: üretilen DDL gerçek, ephemeral bir motor
/// container'ında çalıştırılır ve motorun HAM hatası (varsa) olduğu gibi geri döner.
///
/// Bilinçli tasarım kararı — Testcontainers KULLANILMIYOR: o paket ailesi (Testcontainers 4.x)
/// kendi "Docker.DotNet.Enhanced" forkunu getiriyor ve bu fork gerçek `Docker.DotNet` paketiyle
/// AYNI derlenmiş dosya adını (`Docker.DotNet.dll`) paylaşıyor — ikisi aynı projede bir arada
/// olunca (bu projede zaten `Docker.DotNet` 3.125.15'e bağımlı olan <see cref="DockerBackupService"/>
/// var) NuGet'in sürüm çakışması çözümü sessizce yanlış DLL'i seçip DockerBackupService.cs'i
/// derleme zamanında bozuyordu (CS0246/CS1061 — ExecCreateContainerAsync, GetArchiveFromContainerAsync
/// gibi imzalar "kayboluyordu"). Çözüm: Testcontainers'a hiç dokunma — bu projede zaten
/// ÇALIŞAN, G1'de sertleştirilmiş (docker.sock hiçbir container'a mount edilmiyor) aynı
/// `Docker.DotNet` istemcisini ve <see cref="Models.ContainerProfiles"/> profillerini
/// (DockerBackupService'in kullandığı) doğrudan yeniden kullan.
///
/// MSSQL/PostgreSQL/MySQL: gerçek ephemeral container. SQLite: dosya tabanlı, container
/// gerekmez. Diğer motorlar (Oracle/MariaDB/Db2/Firebird/Spanner/Redshift): resmi bir profil
/// yok — <see cref="TestRunResult.Supported"/>=false ile dürüstçe işaretlenir (G5 de yalnızca
/// Postgres/MSSQL/MySQL'i gerçek container'a karşı doğrulamıştı, aynı sınır burada da geçerli).
/// </summary>
public class BranchTestRunnerService : IBranchTestRunner, IDisposable
{
    private readonly IDdlGeneratorFactory _ddlGeneratorFactory;
    private readonly DockerClient _client;

    public BranchTestRunnerService(IDdlGeneratorFactory ddlGeneratorFactory)
    {
        _ddlGeneratorFactory = ddlGeneratorFactory;

        var dockerUri = Environment.OSVersion.Platform == PlatformID.Win32NT
            ? "npipe://./pipe/docker_engine"
            : "unix:///var/run/docker.sock";
        _client = new DockerClientConfiguration(new Uri(dockerUri)).CreateClient();
    }

    public void Dispose() => _client?.Dispose();

    public async Task<TestRunResult> RunAsync(DatabaseSchema schema, DatabaseType engine, CancellationToken cancellationToken = default)
    {
        var ddl = _ddlGeneratorFactory.GetGenerator(engine).Generate(schema);
        var stopwatch = Stopwatch.StartNew();

        if (engine == DatabaseType.SQLite)
            return await RunSqliteAsync(ddl, stopwatch, cancellationToken);

        if (engine is not (DatabaseType.MSSQL or DatabaseType.PostgreSQL or DatabaseType.MySQL))
        {
            return new TestRunResult(Supported: false, Success: false,
                EngineMessage: $"Live test execution isn't available for {engine} yet — no container profile wired up for this engine.",
                FailedStatement: null, DurationMs: 0);
        }

        return await RunContainerizedAsync(ddl, engine, stopwatch, cancellationToken);
    }

    private async Task<TestRunResult> RunContainerizedAsync(string ddl, DatabaseType dbType, Stopwatch stopwatch, CancellationToken ct)
    {
        var profile = ContainerProfiles.GetProfile(dbType);
        var containerId = (string?)null;

        try
        {
            var localImages = await _client.Images.ListImagesAsync(new ImagesListParameters { All = true }, ct);
            var imageExists = localImages.Any(img => img.RepoTags != null && img.RepoTags.Contains($"{profile.Image}:{profile.Tag}"));
            if (!imageExists)
            {
                await _client.Images.CreateImageAsync(
                    new ImagesCreateParameters { FromImage = profile.Image, Tag = profile.Tag },
                    new AuthConfig(), new Progress<JSONMessage>(), ct);
            }

            var createResponse = await _client.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Name = $"namines-runtest-{Guid.NewGuid():N}",
                Image = $"{profile.Image}:{profile.Tag}",
                Env = profile.EnvVars.Select(kvp => $"{kvp.Key}={kvp.Value}").ToList(),
                HostConfig = new HostConfig
                {
                    Memory = dbType == DatabaseType.MSSQL ? 2500L * 1024 * 1024 : 2048L * 1024 * 1024,
                    NanoCPUs = 1000000000,
                    AutoRemove = true // temizlik garanti — stop sonrası kendini siler
                }
            }, ct);
            containerId = createResponse.ID;

            await _client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), ct);

            var isReady = await WaitForReadyAsync(containerId, dbType, ct);
            if (!isReady)
            {
                stopwatch.Stop();
                return new TestRunResult(true, false, "Database container failed to become ready within the timeout.", null, stopwatch.ElapsedMilliseconds);
            }

            using var tarStream = DockerTarFile.SingleFile("schema.sql", ddl);
            await _client.Containers.ExtractArchiveToContainerAsync(containerId, new ContainerPathStatParameters { Path = "/tmp" }, tarStream, ct);

            var (createDbExit, createDbOut) = dbType switch
            {
                DatabaseType.MSSQL => await ExecuteCommandAsync(containerId,
                    new[] { "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa", "-P", "Namines_Secure123!", "-C", "-b", "-Q",
                        "IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'naminesdb') CREATE DATABASE naminesdb;" }, ct),
                DatabaseType.MySQL => (0, ""), // MYSQL_DATABASE env değişkeni ile zaten oluşturuluyor
                _ => (0, "") // POSTGRES_DB env değişkeni ile zaten oluşturuluyor
            };
            if (createDbExit != 0)
            {
                stopwatch.Stop();
                return new TestRunResult(true, false, $"Could not create the test database (exit {createDbExit}): {createDbOut}", null, stopwatch.ElapsedMilliseconds);
            }

            var schemaCmd = dbType switch
            {
                DatabaseType.MSSQL => new[] { "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa", "-P", "Namines_Secure123!", "-C", "-b", "-d", "naminesdb", "-i", "/tmp/schema.sql" },
                DatabaseType.PostgreSQL => new[] { "sh", "-c", "PGPASSWORD=Namines_Secure123! psql -U postgres -d naminesdb -f /tmp/schema.sql -v ON_ERROR_STOP=1" },
                DatabaseType.MySQL => new[] { "sh", "-c", "mysql -u root -p\"Namines_Secure123!\" naminesdb < /tmp/schema.sql" },
                _ => throw new NotSupportedException()
            };
            var (schemaExit, schemaOut) = await ExecuteCommandAsync(containerId, schemaCmd, ct);

            stopwatch.Stop();
            if (schemaExit != 0)
            {
                // Motorun HAM hatasını olduğu gibi döndür — süslemeden (bkz. doc §4, G5 dersi).
                return new TestRunResult(true, false, schemaOut.Trim(), ddl, stopwatch.ElapsedMilliseconds);
            }

            return new TestRunResult(true, true, null, null, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new TestRunResult(true, false, $"Could not run the test container: {ex.Message}", null, stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            if (containerId is not null)
            {
                try
                {
                    await _client.Containers.StopContainerAsync(containerId, new ContainerStopParameters { WaitBeforeKillSeconds = 2 }, CancellationToken.None);
                    // AutoRemove=true zaten silecek; RemoveContainerAsync ek güvenlik (ör. stop sırasında zaten kaldıysa hatayı yut).
                    try { await _client.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters { Force = true }, CancellationToken.None); } catch { /* AutoRemove zaten halletmiş olabilir */ }
                }
                catch { /* temizlik hatası ana sonucu maskelemesin */ }
            }
        }
    }

    private async Task<bool> WaitForReadyAsync(string containerId, DatabaseType dbType, CancellationToken ct)
    {
        string[] checkCmd = dbType switch
        {
            DatabaseType.PostgreSQL => new[] { "pg_isready", "-U", "postgres" },
            // mysqladmin ping döner 0 döner ama MySQL 8'in iki-aşamalı başlangıcındaki (init server ->
            // restart -> gerçek server) GEÇİCİ sunucuya karşı da başarılı olabiliyor — gerçek kimlik
            // doğrulamalı bir sorgu çalıştırmak asıl sunucunun hazır olduğunu KANITLAR (ampirik olarak
            // doğrulandı: ping "hazır" derken mysql client "Access denied" veriyordu).
            DatabaseType.MySQL => new[] { "sh", "-c", "mysql -u root -p\"Namines_Secure123!\" -e 'SELECT 1;'" },
            DatabaseType.MSSQL => new[] { "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa", "-P", "Namines_Secure123!", "-C", "-b", "-Q", "SELECT 1" },
            _ => new[] { "echo", "ready" }
        };

        for (var i = 0; i < 40; i++)
        {
            try
            {
                var (exitCode, _) = await ExecuteCommandAsync(containerId, checkCmd, ct);
                if (exitCode == 0) return true;
            }
            catch { /* başlangıçta geçici exec hataları beklenir */ }
            await Task.Delay(1500, ct);
        }
        return false;
    }

    private async Task<(int ExitCode, string Output)> ExecuteCommandAsync(string containerId, string[] cmd, CancellationToken ct)
    {
        var execCreate = await _client.Exec.ExecCreateContainerAsync(containerId, new ContainerExecCreateParameters
        {
            AttachStdout = true,
            AttachStderr = true,
            Cmd = cmd
        }, ct);

        using var execStream = await _client.Exec.StartAndAttachContainerExecAsync(execCreate.ID, false, ct);
        var outputTask = execStream.ReadOutputToEndAsync(ct);

        var inspect = await _client.Exec.InspectContainerExecAsync(execCreate.ID, ct);
        while (inspect.Running)
        {
            await Task.Delay(500, ct);
            inspect = await _client.Exec.InspectContainerExecAsync(execCreate.ID, ct);
        }

        var outputResult = await outputTask;
        var combined = ((outputResult.stdout ?? "") + "\n" + (outputResult.stderr ?? "")).Trim();
        return ((int)inspect.ExitCode, combined);
    }


    private static async Task<TestRunResult> RunSqliteAsync(string ddl, Stopwatch stopwatch, CancellationToken ct)
    {
        // SQLite dosya-tabanlı — container gerekmez, geçici dosya yeterli ve saniyeler içinde biter.
        var tempFile = Path.Combine(Path.GetTempPath(), $"namines_run_test_{Guid.NewGuid():N}.db");
        try
        {
            await using var conn = new SqliteConnection($"Data Source={tempFile}");
            await conn.OpenAsync(ct);

            foreach (var statement in ddl.Split(';', StringSplitOptions.RemoveEmptyEntries)
                         .Select(s => s.Trim()).Where(s => s.Length > 0 && !s.StartsWith("--")))
            {
                try
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = statement;
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                catch (SqliteException ex)
                {
                    stopwatch.Stop();
                    return new TestRunResult(true, false, $"{ex.SqliteErrorCode}: {ex.Message}", statement, stopwatch.ElapsedMilliseconds);
                }
            }

            stopwatch.Stop();
            return new TestRunResult(true, true, null, null, stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* geçici dosya, temizlik başarısız olsa da kritik değil */ }
        }
    }
}
