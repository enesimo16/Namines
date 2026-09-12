using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Namines.Vault.Abstractions;

namespace Namines.Vault.Providers;

/// <summary>
/// Dump/restore aracını geçici bir konteynerde çalıştıran sağlayıcılar için
/// ortak taban.
///
/// <b>Neden taban sınıf:</b> PostgreSQL, MySQL ve MariaDB için Docker
/// tarafındaki iş BİREBİR aynı — imajı hazırla, konteyner aç, dosya yükle,
/// çıktıyı akıt, çıkış kodunu kontrol et, temizle. Bunu motor başına
/// kopyalamak, canlı denemede öğrenilen dersleri (stdin kilitlenmesi,
/// host-gateway, çıktının stdin'den ÖNCE okunması) her kopyada ayrı ayrı
/// hatırlamayı gerektirirdi — biri unutulduğunda sessizce kilitlenen bir
/// yedekleme kalırdı.
///
/// <b><c>docker.sock</c> MOUNT EDİLMİYOR</b> — host'un daemon'una uzaktan
/// konuşuluyor (AGENTS.md'nin kesin kuralı; sock'u bir konteynere vermek
/// host üzerinde root eşdeğeri yetki demek).
///
/// <b>Parola komut satırına YAZILMIYOR</b>, ortam değişkeniyle geçiliyor:
/// komut satırı konteyner meta verisinde kalıcı olarak durur ve
/// <c>docker inspect</c> ile okunabilir.
/// </summary>
public abstract class ContainerBackupProvider : IBackupProvider, IDisposable
{
    /// <summary>Dump'ın konteyner içindeki yolu.</summary>
    protected const string ContainerDumpPath = "/tmp/namines-restore.dump";

    /// <summary>
    /// Doğrulama sunucusuna konteynerin İÇİNDEN bağlanılan adres.
    ///
    /// <b>Unix soketi DEĞİL ve bu farkın bedeli canlı denemede görüldü:</b> hem
    /// postgres hem mysql imajı ilk açılışta veritabanını kurmak için geçici bir
    /// sunucu başlatıp onu KAPATIYOR. O geçici sunucu yalnızca unix soketini
    /// dinliyor; soket üzerinden bakan bir hazırlık kontrolü onu "hazır" görüyor
    /// ve hemen ardından geri yükleme "shutting down" / "can't connect to socket"
    /// ile düşüyor. TCP'yi yalnızca ASIL sunucu dinlediği için bekleyiş doğru
    /// yere oturuyor.
    /// </summary>
    protected const string VerifyHost = "127.0.0.1";

    /// <summary>Doğrulama sunucusunun sabitleri — ağa hiç açılmıyor.</summary>
    protected const string VerifyPassword = "namines-verify";
    protected const string VerifyDatabase = "namines_verify";

    /// <summary>
    /// İstemci İLK KULLANIMDA kuruluyor, kurucuda değil.
    ///
    /// Sağlayıcılar DI'da açılışta oluşuyor ve motor başına bir tane var; bir
    /// nesne yaratmanın Docker yapılandırmasını çözmeyi gerektirmesi, daemon'ı
    /// hiç kullanmayacak yollarda (yalnızca komut üreten testler, Vault'un
    /// kapalı olduğu kurulumlar) gereksiz bir bağımlılık olurdu.
    /// </summary>
    private readonly Lazy<DockerClient> _dockerClient =
        new(() => new DockerClientConfiguration().CreateClient());

    protected ContainerBackupProvider(ILogger logger) => Logger = logger;

    private DockerClient Docker => _dockerClient.Value;

    protected ILogger Logger { get; }

    public abstract string Engine { get; }

    /// <summary>Araçların geldiği imaj (ör. <c>postgres:17-alpine</c>).</summary>
    protected abstract string Image { get; }

    /// <summary>Bağlantı dizesinde port yoksa kullanılacak varsayılan.</summary>
    protected abstract int DefaultPort { get; }

    /// <summary>Probe mesajında geçen araç adları (ör. "pg_dump/pg_restore").</summary>
    protected abstract string ToolNames { get; }

    /// <summary>Parolayı taşıyan ortam değişkenleri.</summary>
    internal abstract IList<string> BuildEnvironment(DbConnectionParts conn);

    /// <summary>Kullanıcının veritabanından dump alan komut; çıktı stdout'a yazmalı.</summary>
    internal abstract IList<string> BuildDumpCommand(DbConnectionParts conn);

    /// <summary>
    /// <see cref="ContainerDumpPath"/>'teki dump'ı kullanıcının veritabanına
    /// uygulayan komut. <b>Hedefin mevcut nesnelerini SİLER</b> (tabloları
    /// SEÇİLMİŞSE yalnızca o tabloların nesnelerini).
    /// </summary>
    /// <param name="tables">
    /// KISMİ geri yükleme (B-43): boş/null ise tam geri yükleme. Sağlayıcı
    /// bunu desteklemiyorsa (ör. düz SQL dökümü) <see cref="NotSupportedException"/>
    /// fırlatmalı — sessizce TAM geri yükleme yapmak, kullanıcının "yalnızca
    /// şu tabloyu" seçimini görmezden gelip veri kaybına yol açardı.
    /// </param>
    internal abstract IList<string> BuildRestoreCommand(DbConnectionParts conn, IReadOnlyList<string>? tables);

    /// <summary>Doğrulama sunucusunu ayağa kaldıran ortam değişkenleri.</summary>
    protected abstract IList<string> BuildVerifyServerEnvironment();

    /// <summary>Doğrulama sunucusu bağlantı kabul ediyor mu — çıkış kodu 0 ise evet.</summary>
    protected abstract IList<string> BuildVerifyReadinessCommand();

    /// <summary>Dump'ı doğrulama sunucusuna uygulayan komut.</summary>
    protected abstract IList<string> BuildVerifyRestoreCommand();

    public async Task BackupAsync(BackupSpec spec, Stream destination, CancellationToken ct)
    {
        var conn = DbConnectionParts.Parse(spec.ConnectionString, DefaultPort);
        await RunAsync(BuildDumpCommand(conn), conn, uploadFromPath: null, stdout: destination, ct);
    }

    public async Task RestoreAsync(RestoreSpec spec, Stream source, CancellationToken ct)
    {
        var conn = DbConnectionParts.Parse(spec.ConnectionString, DefaultPort);
        // Komutu ERKEN üret: sağlayıcı kısmi geri yüklemeyi desteklemiyorsa
        // (NotSupportedException) hiç dosya yazmadan/indirmeden hemen patlasın.
        var restoreCommand = BuildRestoreCommand(conn, spec.Tables);

        // Dump, stdin'den DEĞİL dosyadan okunuyor.
        //
        // <b>Canlı denemede öğrenildi:</b> stdin yolu Windows'ta kilitleniyor —
        // Docker.DotNet'in CloseWrite()'ı isimli boru (npipe) üzerinden EOF'u
        // iletmiyor, araç girdinin bittiğini hiç anlamıyor ve süresiz bekliyor.
        // Ölçüldü: konteyner dakikalarca %0 CPU'da stdin'de asılı kaldı. Dosyayı
        // konteynere yükleyip yolunu vermek bu belirsizliği tamamen kaldırıyor.
        var temp = Path.Combine(Path.GetTempPath(), $"namines-restore-{Guid.NewGuid():N}.dump");
        try
        {
            // Geçici dosya ŞİFRESİZ dump içerir; yalnızca işlem boyunca yaşar
            // ve her durumda (hata dâhil) siliniyor.
            await using (var file = File.Create(temp))
            {
                await source.CopyToAsync(file, ct);
            }

            await RunAsync(restoreCommand, conn, uploadFromPath: temp, stdout: null, ct);
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
            await Docker.System.PingAsync(ct);
            return null;
        }
        catch (Exception ex)
        {
            // Mesaj doğrudan kullanıcıya/operatöre gidiyor; "neden" kadar
            // "ne yapmalı" da söylenmeli, yoksa çıplak bir soket hatası kalır.
            return "Docker daemon is not reachable, so backups cannot run. " +
                   $"The API needs access to a Docker daemon to launch {ToolNames} " +
                   $"(see namines-vault/02-ERISIM-VE-DEPLOY.md). Underlying error: {Truncate(ex.Message)}";
        }
    }

    /// <summary>
    /// Dump'ı, YALNIZCA bu iş için ayağa kaldırılan boş bir sunucuya geri
    /// yükleyerek doğrular.
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

            var created = await Docker.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Image = Image,
                Env = BuildVerifyServerEnvironment(),
                // HostConfig boş: port yayını, bind ve ekstra host YOK.
                HostConfig = new HostConfig(),
            }, ct);
            containerId = created.ID;

            await Docker.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), ct);

            if (!await WaitUntilReadyAsync(containerId, ct))
                return "The verification database did not become ready in time.";

            await UploadAsync(containerId, temp, ct);

            var (exitCode, output) = await ExecAsync(containerId, BuildVerifyRestoreCommand(), ct);
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
                    await Docker.Containers.RemoveContainerAsync(
                        containerId,
                        // Force: sunucu hâlâ çalışıyor. Volumes: verisi de gitsin,
                        // yoksa her doğrulama diskte bir birim bırakırdı.
                        new ContainerRemoveParameters { Force = true, RemoveVolumes = true },
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Vault: dogrulama konteyneri {Id} silinemedi.", containerId);
                }
            }
        }
    }

    /// <summary>
    /// Sunucu bağlantı kabul edene kadar bekler.
    ///
    /// Sabit bir bekleme yerine sunucunun kendi hazırlık komutu yoklanıyor:
    /// sabit süre ya gereksiz yavaşlatır ya da yavaş bir makinede erken pes eder.
    /// </summary>
    private async Task<bool> WaitUntilReadyAsync(string containerId, CancellationToken ct)
    {
        // 60 × 500ms = 30 sn. Boş bir sunucu saniyeler içinde hazır olur;
        // bu tavan yalnızca yüklü bir makinede pes etmemek için.
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var (exitCode, _) = await ExecAsync(containerId, BuildVerifyReadinessCommand(), ct);
            if (exitCode == 0) return true;
            await Task.Delay(500, ct);
        }

        return false;
    }

    /// <summary>Çalışan konteynerin içinde komut çalıştırır; çıkış kodunu ve çıktısını döndürür.</summary>
    protected async Task<(long ExitCode, string Output)> ExecAsync(
        string containerId, IList<string> command, CancellationToken ct)
    {
        var exec = await Docker.Exec.ExecCreateContainerAsync(containerId, new ContainerExecCreateParameters
        {
            Cmd = command,
            AttachStdout = true,
            AttachStderr = true,
        }, ct);

        using var stream = await Docker.Exec.StartAndAttachContainerExecAsync(exec.ID, tty: false, ct);

        var stdout = new MemoryStream();
        var stderr = new MemoryStream();
        await stream.CopyOutputToAsync(Stream.Null, stdout, stderr, ct);

        var inspect = await Docker.Exec.InspectContainerExecAsync(exec.ID, ct);

        stdout.Position = 0;
        stderr.Position = 0;
        // Hata metni çoğu araçta stderr'de; ikisi birleştiriliyor ki kullanıcıya
        // dönen sebep hangi akışa yazıldığına bağlı olmasın.
        var text = await new StreamReader(stdout).ReadToEndAsync(ct)
                   + await new StreamReader(stderr).ReadToEndAsync(ct);

        return (inspect.ExitCode, text);
    }

    /// <summary>Komutu geçici bir konteynerde çalıştırır.</summary>
    /// <param name="uploadFromPath">
    /// Verilirse, konteyner BAŞLAMADAN ÖNCE bu dosya
    /// <see cref="ContainerDumpPath"/> yoluna kopyalanır.
    /// </param>
    /// <param name="stdout">Verilirse komutun çıktısı buraya akıtılır.</param>
    private async Task RunAsync(
        IList<string> command, DbConnectionParts conn,
        string? uploadFromPath, Stream? stdout, CancellationToken ct)
    {
        await EnsureImageAsync(ct);

        var created = await Docker.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = Image,
            Cmd = command,
            Env = BuildEnvironment(conn),
            AttachStdout = true,
            AttachStderr = true,
            HostConfig = new HostConfig
            {
                // Konteynerin host makinesindeki veritabanına ulaşabilmesi icin.
                // NetworkMode="host" DEGIL: Docker Desktop (Windows/macOS) onu
                // desteklemiyor; host-gateway uc platformda da calisiyor.
                // Binds YOK: hicbir host dizini baglanmiyor, docker.sock hic degil.
                ExtraHosts = new List<string> { $"{DbConnectionParts.HostGatewayName}:host-gateway" },
                AutoRemove = false,
            },
        }, ct);

        try
        {
            if (uploadFromPath is not null)
                await UploadAsync(created.ID, uploadFromPath, ct);

            using var stream = await Docker.Containers.AttachContainerAsync(
                created.ID, tty: false,
                new ContainerAttachParameters { Stdout = true, Stderr = true, Stream = true }, ct);

            await Docker.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), ct);

            var stderr = new MemoryStream();

            // Docker çoklama protokolünü ayrıştırır: stdout dump'ın kendisi,
            // stderr ise hata metni. İkisini karıştırmak dump'ı bozardı.
            await stream.CopyOutputToAsync(Stream.Null, stdout ?? Stream.Null, stderr, ct);

            var wait = await Docker.Containers.WaitContainerAsync(created.ID, ct);
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
                await Docker.Containers.RemoveContainerAsync(
                    created.ID, new ContainerRemoveParameters { Force = true }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Temizlik hatası ASIL sonucu bastırmamalı — yedek başarılıysa
                // başarılı sayılır, kalan konteyner ayrıca loglanır.
                Logger.LogWarning(ex, "Vault: gecici konteyner {Id} silinemedi.", created.ID);
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

        await Docker.Containers.ExtractArchiveToContainerAsync(
            containerId,
            new ContainerPathStatParameters { Path = Path.GetDirectoryName(ContainerDumpPath)!.Replace('\\', '/') },
            pipe.Reader.AsStream(), ct);

        await writing;
    }

    /// <summary>
    /// İmaj yoksa çekilir.
    ///
    /// İlk yedek isteğinde imajın yerelde olup olmadığı bilinemez; olmadığında
    /// konteyner oluşturma "no such image" ile düşerdi ve kullanıcı bunu
    /// yedeğin başarısızlığı sanırdı.
    /// </summary>
    private async Task EnsureImageAsync(CancellationToken ct)
    {
        try
        {
            await Docker.Images.InspectImageAsync(Image, ct);
            return;
        }
        catch (DockerImageNotFoundException)
        {
            Logger.LogInformation("Vault: {Image} imaji yerelde yok, cekiliyor.", Image);
        }

        var separator = Image.LastIndexOf(':');
        var name = separator > 0 ? Image[..separator] : Image;
        var tag = separator > 0 ? Image[(separator + 1)..] : "latest";

        await Docker.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = name, Tag = tag },
            authConfig: null,
            progress: new Progress<JSONMessage>(),
            ct);
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
            Logger.LogWarning(ex, "Vault: gecici geri yukleme dosyasi silinemedi: {Path}", path);
        }
    }

    /// <summary>Hata metni kullanıcıya gidiyor; bağlantı ayrıntısı taşımasın diye kısaltılıyor.</summary>
    protected static string Truncate(string message)
    {
        var clean = message.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return clean.Length <= 400 ? clean : clean[..400] + "…";
    }

    public void Dispose()
    {
        // Hiç kurulmadıysa kurup atmanın anlamı yok — üstelik Docker
        // yapılandırılmamışsa Dispose'un kendisi patlardı.
        if (_dockerClient.IsValueCreated) _dockerClient.Value.Dispose();
        GC.SuppressFinalize(this);
    }
}
