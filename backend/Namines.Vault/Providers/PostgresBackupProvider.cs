using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Namines.Vault.Abstractions;

namespace Namines.Vault.Providers;

/// <summary>
/// PostgreSQL yedek/geri yükleme — <c>pg_dump</c> / <c>pg_restore</c>.
///
/// <b>Araçlar geçici bir konteynerde çalışıyor</b>, ana süreçte değil: sunucunun
/// üzerinde doğru sürüm <c>pg_dump</c> binary'si bulunmak zorunda kalmıyor ve
/// sürüm uyumu (dump eden araç ≥ sunucu sürümü) imaj etiketiyle yönetiliyor.
///
/// <b><c>docker.sock</c> MOUNT EDİLMİYOR</b> — host'un daemon'una uzaktan
/// konuşuluyor (AGENTS.md'nin kesin kuralı; sock'u bir konteynere vermek
/// host üzerinde root eşdeğeri yetki demek).
///
/// <b>Parola komut satırına YAZILMIYOR.</b> <c>PGPASSWORD</c> ortam değişkeniyle
/// geçiliyor: komut satırı konteyner meta verisinde kalıcı olarak durur ve
/// <c>docker inspect</c> ile okunabilir.
/// </summary>
public sealed class PostgresBackupProvider : IBackupProvider, IDisposable
{
    private readonly DockerClient _docker;
    private readonly ILogger<PostgresBackupProvider> _logger;
    private readonly string _image;

    public PostgresBackupProvider(IConfiguration configuration, ILogger<PostgresBackupProvider> logger)
    {
        _logger = logger;
        // Sunucu sürümü büyüdüğünde bu etiket de büyümeli: eski bir pg_dump
        // yeni bir sunucuyu reddeder (ve bu İYİ — sessizce eksik dump almaktansa).
        _image = configuration["Vault:PostgresImage"] ?? "postgres:17-alpine";
        _docker = new DockerClientConfiguration().CreateClient();
    }

    public string Engine => "PostgreSQL";

    public async Task BackupAsync(BackupSpec spec, Stream destination, CancellationToken ct)
    {
        var conn = PostgresConnectionParts.Parse(spec.ConnectionString);

        // -Fc: custom format. Düz SQL değil çünkü pg_restore'un seçmeli geri
        // yükleme ve paralel çalışma yetenekleri buna bağlı (ve sıkıştırılmış).
        var command = new List<string>
        {
            "pg_dump", "-Fc", "--no-owner", "--no-acl",
            "-h", conn.ContainerVisibleHost, "-p", conn.Port.ToString(), "-U", conn.Username, "-d", conn.Database,
        };

        await RunAsync(command, conn, uploadFromPath: null, stdout: destination, ct);
    }

    /// <summary>Dump'ın konteyner içindeki yolu (bkz. <see cref="RestoreAsync"/>).</summary>
    private const string ContainerDumpPath = "/tmp/namines-restore.dump";

    public async Task RestoreAsync(RestoreSpec spec, Stream source, CancellationToken ct)
    {
        var conn = PostgresConnectionParts.Parse(spec.ConnectionString);

        // --clean --if-exists: hedefteki nesneler önce DÜŞÜRÜLÜR. Bu, geri
        // yüklemenin "üzerine yaz" anlamına geldiği yer — çağıran onayı ve
        // ön yedeği almış olmak zorunda.
        // --exit-on-error: ilk hatada dur. Varsayılan "devam et" davranışı
        // yarım geri yüklenmiş bir veritabanını BAŞARILI gösterirdi.
        var command = new List<string>
        {
            "pg_restore", "--clean", "--if-exists", "--no-owner", "--no-acl", "--exit-on-error",
            "-h", conn.ContainerVisibleHost, "-p", conn.Port.ToString(), "-U", conn.Username, "-d", conn.Database,
            ContainerDumpPath,
        };

        // Dump, stdin'den DEĞİL dosyadan okunuyor.
        //
        // <b>Canlı denemede öğrenildi:</b> stdin yolu Windows'ta kilitleniyor —
        // Docker.DotNet'in <c>CloseWrite()</c>'ı isimli boru (npipe) üzerinden
        // EOF'u iletmiyor, pg_restore girdinin bittiğini hiç anlamıyor ve süresiz
        // bekliyor. Ölçüldü: konteyner dakikalarca %0 CPU'da stdin'de asılı kaldı.
        // Dosyayı konteynere yükleyip yolunu vermek bu belirsizliği tamamen
        // ortadan kaldırıyor.
        var temp = Path.Combine(Path.GetTempPath(), $"namines-restore-{Guid.NewGuid():N}.dump");
        try
        {
            // Geçici dosya ŞİFRESİZ dump içerir; bu yüzden yalnızca işlem boyunca
            // yaşar ve her durumda (hata dâhil) siliniyor.
            await using (var file = File.Create(temp))
            {
                await source.CopyToAsync(file, ct);
            }

            await RunAsync(command, conn, uploadFromPath: temp, stdout: null, ct);
        }
        finally
        {
            TryDeleteTemp(temp);
        }
    }

    public async Task<string?> ProbeAsync(CancellationToken ct)
    {
        try
        {
            await _docker.System.PingAsync(ct);
            return null;
        }
        catch (Exception ex)
        {
            // Mesaj doğrudan kullanıcıya/operatöre gidiyor; "neden" kadar
            // "ne yapmalı" da söylenmeli, yoksa çıplak bir soket hatası kalır.
            return "Docker daemon is not reachable, so backups cannot run. " +
                   "The API needs access to a Docker daemon to launch pg_dump/pg_restore " +
                   $"(see namines-vault/02-ERISIM-VE-DEPLOY.md). Underlying error: {Truncate(ex.Message)}";
        }
    }

    /// <summary>Doğrulama sunucusunun içindeki sabitler — dışarı hiç açılmıyor.</summary>
    private const string VerifyPassword = "namines-verify";
    private const string VerifyDatabase = "namines_verify";

    /// <summary>
    /// Dump'ı, YALNIZCA bu iş için ayağa kaldırılan boş bir PostgreSQL
    /// sunucusuna geri yükleyerek doğrular.
    ///
    /// <b>Ağa hiç açılmıyor:</b> port yayınlanmıyor ve komutlar konteynerin
    /// İÇİNDE çalıştırılıyor (docker exec). Doğrulama sunucusu bu yüzden
    /// dışarıdan erişilebilir bir yüzey değil; sabit parolası da bu sebeple
    /// zararsız — konteynerin dışına çıkmıyor.
    /// </summary>
    public async Task<string?> VerifyAsync(Stream source, CancellationToken ct)
    {
        await EnsureImageAsync(ct);

        var temp = Path.Combine(Path.GetTempPath(), $"namines-verify-{Guid.NewGuid():N}.dump");
        string? containerId = null;

        try
        {
            await using (var file = File.Create(temp))
            {
                await source.CopyToAsync(file, ct);
            }

            var created = await _docker.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Image = _image,
                Env = new List<string>
                {
                    $"POSTGRES_PASSWORD={VerifyPassword}",
                    $"POSTGRES_DB={VerifyDatabase}",
                },
                // HostConfig boş: port yayını, bind ve ekstra host YOK.
                HostConfig = new HostConfig(),
            }, ct);
            containerId = created.ID;

            await _docker.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), ct);

            if (!await WaitUntilReadyAsync(containerId, ct))
                return "The verification database did not become ready in time.";

            await UploadAsync(containerId, temp, ct);

            // --exit-on-error: ilk hatada dur. Doğrulamanın tamamı bu bayrağa
            // dayanıyor — hataları yutan bir geri yükleme, bozuk bir dump'ı
            // "doğrulandı" diye işaretlerdi.
            var (exitCode, output) = await ExecAsync(containerId, new[]
            {
                "pg_restore", "--no-owner", "--no-acl", "--exit-on-error",
                "-h", VerifyHost, "-U", "postgres", "-d", VerifyDatabase, ContainerDumpPath,
            }, ct);

            return exitCode == 0 ? null : Truncate(output);
        }
        catch (Exception ex)
        {
            return Truncate(ex.Message);
        }
        finally
        {
            TryDeleteTemp(temp);

            if (containerId is not null)
            {
                try
                {
                    await _docker.Containers.RemoveContainerAsync(
                        containerId,
                        // Force: sunucu hâlâ çalışıyor. Volumes: verisi de gitsin,
                        // yoksa her doğrulama diskte bir birim bırakırdı.
                        new ContainerRemoveParameters { Force = true, RemoveVolumes = true },
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Vault: dogrulama konteyneri {Id} silinemedi.", containerId);
                }
            }
        }
    }

    /// <summary>
    /// Hazırlık kontrolünün yapıldığı adres — konteynerin İÇİNDEN, TCP üzerinden.
    ///
    /// <b>Unix soketi DEĞİL ve bu farkın bedeli canlı denemede görüldü:</b> postgres
    /// imajı ilk açılışta veritabanını kurmak için geçici bir sunucu başlatır, sonra
    /// onu KAPATIP asıl sunucuyu açar. O geçici sunucu yalnızca unix soketini
    /// dinler; soket üzerinden bakan bir hazırlık kontrolü onu "hazır" görür ve
    /// hemen ardından <c>pg_restore</c> "the database system is shutting down"
    /// hatasıyla düşer. TCP'yi yalnızca asıl sunucu dinlediği için bu ayrım
    /// bekleyişi doğru yere koyuyor.
    /// </summary>
    private const string VerifyHost = "127.0.0.1";

    /// <summary>
    /// Sunucu bağlantı kabul edene kadar bekler.
    ///
    /// Sabit bir bekleme yerine <c>pg_isready</c> yoklanıyor: sabit süre ya
    /// gereksiz yavaşlatır ya da yavaş bir makinede erken pes eder.
    /// </summary>
    private async Task<bool> WaitUntilReadyAsync(string containerId, CancellationToken ct)
    {
        // 60 × 500ms = 30 sn. Boş bir postgres saniyeler içinde hazır olur;
        // bu tavan yalnızca yüklü bir makinede pes etmemek için.
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var (exitCode, _) = await ExecAsync(
                containerId,
                new[] { "pg_isready", "-h", VerifyHost, "-U", "postgres", "-d", VerifyDatabase },
                ct);

            if (exitCode == 0) return true;
            await Task.Delay(500, ct);
        }

        return false;
    }

    /// <summary>Çalışan konteynerin içinde komut çalıştırır; çıkış kodunu ve çıktısını döndürür.</summary>
    private async Task<(long ExitCode, string Output)> ExecAsync(
        string containerId, IList<string> command, CancellationToken ct)
    {
        var exec = await _docker.Exec.ExecCreateContainerAsync(containerId, new ContainerExecCreateParameters
        {
            Cmd = command,
            AttachStdout = true,
            AttachStderr = true,
        }, ct);

        using (var stream = await _docker.Exec.StartAndAttachContainerExecAsync(exec.ID, tty: false, ct))
        {
            var stdout = new MemoryStream();
            var stderr = new MemoryStream();
            await stream.CopyOutputToAsync(Stream.Null, stdout, stderr, ct);

            var inspect = await _docker.Exec.InspectContainerExecAsync(exec.ID, ct);

            stdout.Position = 0;
            stderr.Position = 0;
            // Hata metni çoğu araçta stderr'de; ikisi birleştiriliyor ki
            // kullanıcıya dönen sebep hangi akışa yazıldığına bağlı olmasın.
            var text = await new StreamReader(stdout).ReadToEndAsync(ct)
                       + await new StreamReader(stderr).ReadToEndAsync(ct);

            return (inspect.ExitCode, text);
        }
    }

    private void TryDeleteTemp(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            // Şifresiz bir dump diskte kalmış olabilir — sessiz geçilmez.
            _logger.LogWarning(ex, "Vault: gecici geri yukleme dosyasi silinemedi: {Path}", path);
        }
    }

    /// <summary>
    /// Komutu geçici bir konteynerde çalıştırır.
    /// </summary>
    /// <param name="uploadFromPath">
    /// Verilirse, konteyner BAŞLAMADAN ÖNCE bu dosya
    /// <see cref="ContainerDumpPath"/> yoluna kopyalanır.
    /// </param>
    /// <param name="stdout">Verilirse komutun çıktısı buraya akıtılır.</param>
    private async Task RunAsync(
        IList<string> command, PostgresConnectionParts conn,
        string? uploadFromPath, Stream? stdout, CancellationToken ct)
    {
        await EnsureImageAsync(ct);

        var created = await _docker.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = _image,
            Cmd = command,
            Env = new List<string> { $"PGPASSWORD={conn.Password}" },
            AttachStdout = true,
            AttachStderr = true,
            HostConfig = new HostConfig
            {
                // Konteynerin host makinesindeki veritabanına ulaşabilmesi icin.
                // NetworkMode="host" DEGIL: Docker Desktop (Windows/macOS) onu
                // desteklemiyor; host-gateway uc platformda da calisiyor.
                // Binds YOK: hicbir host dizini baglanmiyor, docker.sock hic degil.
                ExtraHosts = new List<string> { $"{PostgresConnectionParts.HostGatewayName}:host-gateway" },
                AutoRemove = false,
            },
        }, ct);

        try
        {
            if (uploadFromPath is not null)
                await UploadAsync(created.ID, uploadFromPath, ct);

            using var stream = await _docker.Containers.AttachContainerAsync(
                created.ID, tty: false,
                new ContainerAttachParameters { Stdout = true, Stderr = true, Stream = true }, ct);

            await _docker.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), ct);

            var stderr = new MemoryStream();

            // Docker çoklama protokolünü ayrıştırır: stdout dump'ın kendisi,
            // stderr ise hata metni. İkisini karıştırmak dump'ı bozardı.
            await stream.CopyOutputToAsync(Stream.Null, stdout ?? Stream.Null, stderr, ct);

            var wait = await _docker.Containers.WaitContainerAsync(created.ID, ct);
            if (wait.StatusCode != 0)
            {
                stderr.Position = 0;
                var message = await new StreamReader(stderr).ReadToEndAsync(ct);
                throw new InvalidOperationException(
                    $"{command[0]} failed (exit {wait.StatusCode}): {Truncate(message)}");
            }
        }
        finally
        {
            try
            {
                await _docker.Containers.RemoveContainerAsync(
                    created.ID, new ContainerRemoveParameters { Force = true }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Temizlik hatası ASIL sonucu bastırmamalı — yedek başarılıysa
                // başarılı sayılır, kalan konteyner ayrıca loglanır.
                _logger.LogWarning(ex, "Vault: gecici konteyner {Id} silinemedi.", created.ID);
            }
        }
    }

    /// <summary>
    /// Yerel dosyayı, konteyner başlamadan önce içine kopyalar.
    ///
    /// Docker'ın archive ucu yalnızca tar kabul ediyor; bu yüzden dosya tek
    /// girdilik bir tar'a sarılıp gönderiliyor (bkz. <see cref="SingleFileTar"/>).
    /// </summary>
    private async Task UploadAsync(string containerId, string sourcePath, CancellationToken ct)
    {
        var info = new FileInfo(sourcePath);

        // Boru: tar HİÇ belleğe alınmadan doğrudan Docker'a akıyor.
        var pipe = new Pipe();
        var writing = Task.Run(async () =>
        {
            try
            {
                await using var file = File.OpenRead(sourcePath);
                await SingleFileTar.WriteAsync(
                    pipe.Writer.AsStream(), Path.GetFileName(ContainerDumpPath), file, info.Length, ct);
                await pipe.Writer.CompleteAsync();
            }
            catch (Exception ex)
            {
                // Hatayı okuyucuya ilet, yoksa Docker yarım bir tar'ı okuyup asılırdı.
                await pipe.Writer.CompleteAsync(ex);
                throw;
            }
        }, ct);

        await _docker.Containers.ExtractArchiveToContainerAsync(
            containerId,
            new ContainerPathStatParameters { Path = Path.GetDirectoryName(ContainerDumpPath)!.Replace('\\', '/') },
            pipe.Reader.AsStream(), ct);

        await writing;
    }

    /// <summary>
    /// Imaj yoksa cekilir.
    ///
    /// Ilk yedek isteginde imajin yerelde olup olmadigi bilinemez; olmadiginda
    /// konteyner olusturma "no such image" ile duserdi ve kullanici bunu
    /// yedegin basarisizligi sanirdi.
    /// </summary>
    private async Task EnsureImageAsync(CancellationToken ct)
    {
        try
        {
            await _docker.Images.InspectImageAsync(_image, ct);
            return;
        }
        catch (DockerImageNotFoundException)
        {
            _logger.LogInformation("Vault: {Image} imaji yerelde yok, cekiliyor.", _image);
        }

        var separator = _image.LastIndexOf(':');
        var name = separator > 0 ? _image[..separator] : _image;
        var tag = separator > 0 ? _image[(separator + 1)..] : "latest";

        await _docker.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = name, Tag = tag },
            authConfig: null,
            progress: new Progress<JSONMessage>(),
            ct);
    }

    /// <summary>Hata metni kullanıcıya gidiyor; bağlantı ayrıntısı taşımasın diye kısaltılıyor.</summary>
    private static string Truncate(string message)
    {
        var clean = message.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return clean.Length <= 400 ? clean : clean[..400] + "…";
    }

    public void Dispose() => _docker.Dispose();
}
