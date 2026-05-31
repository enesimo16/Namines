using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Formats.Tar;
using System.Collections.Generic;
using Docker.DotNet;
using Docker.DotNet.Models;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.Infrastructure.Services;

public class DockerBackupService : IDockerService
{
    private readonly DockerClient _client;

    public DockerBackupService()
    {
        var dockerUri = Environment.OSVersion.Platform == PlatformID.Win32NT 
            ? "npipe://./pipe/docker_engine" 
            : "unix:///var/run/docker.sock";
            
        _client = new DockerClientConfiguration(new Uri(dockerUri)).CreateClient();
    }

    public async Task RunSandboxAndBackupAsync(string jobId, string sqlContent, DatabaseType dbType, Action<string> onProgress)
    {
        var profile = ContainerProfiles.GetProfile(dbType);
        string? containerId = null;

        try
        {
            onProgress($"Docker engine'e bağlanıldı. İmaj kontrol ediliyor: {profile.Image}:{profile.Tag}");

            // 1. Check if image exists locally to optimize pull
            var localImages = await _client.Images.ListImagesAsync(new ImagesListParameters { All = true });
            bool imageExists = localImages.Any(img => 
                img.RepoTags != null && 
                img.RepoTags.Contains($"{profile.Image}:{profile.Tag}"));

            if (!imageExists)
            {
                onProgress($"İmaj lokalde bulunamadı. Docker Hub'dan çekiliyor: {profile.Image}:{profile.Tag}...");
                await _client.Images.CreateImageAsync(
                    new ImagesCreateParameters { FromImage = profile.Image, Tag = profile.Tag },
                    new AuthConfig(),
                    new Progress<JSONMessage>(msg => {
                        if (!string.IsNullOrEmpty(msg.Status))
                            onProgress($"İmaj çekiliyor... {msg.Status}");
                    }));
            }
            else
            {
                onProgress($"İmaj lokalde hazır: {profile.Image}:{profile.Tag}");
            }

            onProgress("Container oluşturuluyor...");

            // 2. Create Container using DB-specific official images
            var createParams = new CreateContainerParameters
            {
                Name = $"namines-sandbox-{jobId}",
                Image = $"{profile.Image}:{profile.Tag}",
                Env = profile.EnvVars.Select(kvp => $"{kvp.Key}={kvp.Value}").ToList(),
                HostConfig = new HostConfig
                {
                    Memory = 1024 * 1024 * 1024, // 1GB Memory limit
                    NanoCPUs = 1000000000 // 1 CPU limit
                }
            };

            var createResponse = await _client.Containers.CreateContainerAsync(createParams);
            containerId = createResponse.ID;

            onProgress($"Container oluşturuldu (ID: {containerId.Substring(0, 8)}). Başlatılıyor...");

            // 3. Start Container
            await _client.Containers.StartContainerAsync(containerId, new ContainerStartParameters());
            onProgress("Container başlatıldı. Sağlık kontrolü (Health check) bekleniyor...");

            // 4. Wait for DB to be ready (Smart Healthcheck loop)
            bool isReady = false;
            string[] checkCmd = dbType switch
            {
                DatabaseType.PostgreSQL => new[] { "pg_isready", "-U", "postgres" },
                DatabaseType.MySQL => new[] { "mysqladmin", "ping", "-h", "localhost", "-u", "root", "-pNamines_Secure123!" },
                DatabaseType.MSSQL => new[] { "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa", "-P", "Namines_Secure123!", "-C", "-Q", "SELECT 1" },
                _ => new[] { "echo", "ready" }
            };

            for (int i = 0; i < 40; i++)
            {
                try
                {
                    var (exitCode, output) = await ExecuteCommandAsync(containerId, checkCmd);
                    if (exitCode == 0)
                    {
                        isReady = true;
                        break;
                    }
                    onProgress($"Veritabanı henüz hazır değil, tekrar deneniyor ({i + 1}/40)...");
                }
                catch
                {
                    // Ignore transient exec errors during initial startup
                }
                await Task.Delay(1500);
            }

            if (!isReady)
            {
                throw new Exception("Veritabanı başlatılamadı veya sağlık kontrolü zaman aşımına uğradı.");
            }
            onProgress("Veritabanı başarıyla hazır hale geldi.");

            // 5. Copy SQL file into container natively using TAR
            onProgress("DDL (SQL) scripti aktarılıyor...");
            using (var tarStream = CreateTarStream("schema.sql", sqlContent))
            {
                await _client.Containers.ExtractArchiveToContainerAsync(containerId, new ContainerPathStatParameters
                {
                    Path = "/tmp"
                }, tarStream);
            }
            onProgress("Script başarıyla aktarıldı. Tablolar uygulanıyor...");

            // 6. Execute SQL and apply schema
            if (dbType == DatabaseType.MSSQL)
            {
                // Create database 'naminesdb' first if not exists
                var createDbCmd = new[]
                {
                    "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa", "-P", "Namines_Secure123!", "-C",
                    "-Q", "IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'naminesdb') CREATE DATABASE naminesdb;"
                };
                var (dbExit, dbOut) = await ExecuteCommandAsync(containerId, createDbCmd);
                if (dbExit != 0)
                {
                    throw new Exception($"MSSQL veritabanı oluşturulamadı (ExitCode: {dbExit}): {dbOut}");
                }

                // Execute the schema script inside naminesdb
                var execSchemaCmd = new[]
                {
                    "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa", "-P", "Namines_Secure123!", "-C",
                    "-d", "naminesdb", "-i", "/tmp/schema.sql"
                };
                var (schemaExit, schemaOut) = await ExecuteCommandAsync(containerId, execSchemaCmd);
                if (schemaExit != 0)
                {
                    throw new Exception($"MSSQL şema uygulanamadı (ExitCode: {schemaExit}): {schemaOut}");
                }
            }
            else if (dbType == DatabaseType.PostgreSQL)
            {
                var execSchemaCmd = new[]
                {
                    "sh", "-c", "PGPASSWORD=Namines_Secure123! psql -U postgres -d naminesdb -f /tmp/schema.sql"
                };
                var (schemaExit, schemaOut) = await ExecuteCommandAsync(containerId, execSchemaCmd);
                if (schemaExit != 0)
                {
                    throw new Exception($"PostgreSQL şema uygulanamadı (ExitCode: {schemaExit}): {schemaOut}");
                }
            }
            else if (dbType == DatabaseType.MySQL)
            {
                var execSchemaCmd = new[]
                {
                    "sh", "-c", "mysql -u root -p\"Namines_Secure123!\" naminesdb < /tmp/schema.sql"
                };
                var (schemaExit, schemaOut) = await ExecuteCommandAsync(containerId, execSchemaCmd);
                if (schemaExit != 0)
                {
                    throw new Exception($"MySQL şema uygulanamadı (ExitCode: {schemaExit}): {schemaOut}");
                }
            }

            onProgress("SQL Script başarıyla çalıştırıldı ve şema kuruldu.");

            // 7. Backup (Dump)
            onProgress("Veritabanı yedeği (.bak/.sql) alınıyor...");
            string containerBackupPath = "";
            string[] backupCmd;

            if (dbType == DatabaseType.MSSQL)
            {
                containerBackupPath = "/var/opt/mssql/data/naminesdb.bak";
                backupCmd = new[]
                {
                    "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa", "-P", "Namines_Secure123!", "-C",
                    "-Q", $"BACKUP DATABASE naminesdb TO DISK = '{containerBackupPath}' WITH FORMAT, INIT;"
                };
            }
            else if (dbType == DatabaseType.PostgreSQL)
            {
                containerBackupPath = "/tmp/backup.sql";
                backupCmd = new[]
                {
                    "sh", "-c", $"PGPASSWORD=Namines_Secure123! pg_dump -U postgres -d naminesdb -f {containerBackupPath}"
                };
            }
            else if (dbType == DatabaseType.MySQL)
            {
                containerBackupPath = "/tmp/backup.sql";
                backupCmd = new[]
                {
                    "sh", "-c", $"mysqldump -u root -p\"Namines_Secure123!\" --databases naminesdb -r {containerBackupPath}"
                };
            }
            else
            {
                throw new NotSupportedException($"Unsupported database type for backup: {dbType}");
            }

            var (backupExit, backupOut) = await ExecuteCommandAsync(containerId, backupCmd);
            if (backupExit != 0)
            {
                throw new Exception($"Yedekleme işlemi başarısız oldu (ExitCode: {backupExit}): {backupOut}");
            }
            onProgress("Veritabanı yedeği başarıyla alındı.");

            // 8. Extract backup from container & Extract TAR on Host
            onProgress("Yedek host makineye kopyalanıyor...");
            
            var archiveResponse = await _client.Containers.GetArchiveFromContainerAsync(containerId, new GetArchiveFromContainerParameters 
            { 
                Path = containerBackupPath 
            }, false);
            
            using (var archiveStream = archiveResponse.Stream)
            {
                var outputsDir = Path.Combine(Directory.GetCurrentDirectory(), "Outputs");
                if (!Directory.Exists(outputsDir)) Directory.CreateDirectory(outputsDir);

                var tempExtractDir = Path.Combine(outputsDir, $"temp_{jobId}");
                if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true);
                Directory.CreateDirectory(tempExtractDir);

                try
                {
                    using (var ms = new MemoryStream())
                    {
                        await archiveStream.CopyToAsync(ms);
                        ms.Position = 0;
                        
                        // Extract the tar stream directly into the temp folder
                        TarFile.ExtractToDirectory(ms, tempExtractDir, overwriteFiles: true);
                    }

                    // Locate the extracted backup file
                    var extractedFile = Directory.GetFiles(tempExtractDir, "*", SearchOption.AllDirectories).FirstOrDefault();
                    if (extractedFile == null)
                    {
                        throw new FileNotFoundException("Tar arşivi içinden yedek dosyası çıkarılamadı.");
                    }

                    var ext = dbType == DatabaseType.MSSQL ? ".bak" : ".sql";
                    var finalPath = Path.Combine(outputsDir, $"{jobId}{ext}");
                    
                    if (File.Exists(finalPath)) File.Delete(finalPath);
                    File.Move(extractedFile, finalPath);
                    
                    onProgress($"Yedek dosyası başarıyla kaydedildi: backup_{jobId}{ext}");
                }
                finally
                {
                    if (Directory.Exists(tempExtractDir))
                        Directory.Delete(tempExtractDir, true);
                }
            }

            onProgress("İşlem tamamlandı. Container temizleniyor...");
        }
        catch (Exception ex)
        {
            onProgress($"HATA: {ex.Message}");
            throw;
        }
        finally
        {
            // 9. Cleanup
            if (containerId != null)
            {
                onProgress("Container durduruluyor...");
                await _client.Containers.StopContainerAsync(containerId, new ContainerStopParameters { WaitBeforeKillSeconds = 2 });
                onProgress("Container siliniyor...");
                await _client.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters { Force = true });
            }
        }
    }

    public Task<DualSandboxResult> RunDualSandboxAsync(string jobId, string sqlContent, string appPyContent, DatabaseType dbType, Action<string> onProgress, DatabaseSchema schema)
    {
        throw new NotSupportedException("Dual Sandbox is no longer supported in the stabilized backup-only pipeline. Use CoderAIPackager to generate the project ZIP package instead.");
    }

    public Task CleanupSandboxAsync(string jobId)
    {
        return Task.CompletedTask;
    }

    private async Task<(int ExitCode, string Output)> ExecuteCommandAsync(string containerId, string[] cmd, CancellationToken cancellationToken = default)
    {
        var execCreate = await _client.Exec.ExecCreateContainerAsync(containerId, new ContainerExecCreateParameters
        {
            AttachStdout = true,
            AttachStderr = true,
            Cmd = cmd
        }, cancellationToken);

        using var execStream = await _client.Exec.StartAndAttachContainerExecAsync(execCreate.ID, false, cancellationToken);
        var outputTask = execStream.ReadOutputToEndAsync(cancellationToken);
        
        // Wait for execution to finish
        var inspect = await _client.Exec.InspectContainerExecAsync(execCreate.ID, cancellationToken);
        while (inspect.Running)
        {
            await Task.Delay(500, cancellationToken);
            inspect = await _client.Exec.InspectContainerExecAsync(execCreate.ID, cancellationToken);
        }

        var outputResult = await outputTask;
        string stdout = outputResult.stdout ?? "";
        string stderr = outputResult.stderr ?? "";
        
        string combined = (stdout + "\n" + stderr).Trim();
        return ((int)inspect.ExitCode, combined);
    }

    private static Stream CreateTarStream(string fileName, string content)
    {
        var memoryStream = new MemoryStream();
        using (var archive = new TarWriter(memoryStream, TarEntryFormat.Pax, leaveOpen: true))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, fileName)
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content))
            };
            archive.WriteEntry(entry);
        }
        memoryStream.Position = 0;
        return memoryStream;
    }
}
