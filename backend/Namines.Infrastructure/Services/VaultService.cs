using System;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Infrastructure.Data;
using Namines.Vault.Abstractions;
using Namines.Vault.Storage;

namespace Namines.Infrastructure.Services;

/// <summary>Bir Vault işleminin kullanıcıya dönen sonucu.</summary>
public sealed record VaultResult(bool Ok, string? Error = null, string? BackupId = null);

/// <summary>
/// Vault'un iş akışı: yetki → bağlantı çözme → dump → şifreleme → depo → kayıt.
///
/// <b>Neden Infrastructure'da, Namines.Vault içinde değil:</b> Vault modülü
/// veritabanını ve kimliği bilmiyor, bilmemeli de (bkz. <c>Namines.Vault.csproj</c>).
/// Yetki, bağlantı sırrı ve denetim kaydı bu katmanın işi; Vault yalnızca
/// "şu bağlantıdan şu akışa dump al" diyor.
/// </summary>
public class VaultService
{
    private readonly AuthDbContext _context;
    private readonly IConnectionSecretProtector _protector;
    private readonly IDbHostAccessPolicy _hostPolicy;
    private readonly IBackupProvider _provider;
    private readonly IBackupStore _store;
    private readonly BackupCipher _cipher;
    private readonly ILogger<VaultService> _logger;

    public VaultService(
        AuthDbContext context,
        IConnectionSecretProtector protector,
        IDbHostAccessPolicy hostPolicy,
        IBackupProvider provider,
        IBackupStore store,
        BackupCipher cipher,
        ILogger<VaultService> logger)
    {
        _context = context;
        _protector = protector;
        _hostPolicy = hostPolicy;
        _provider = provider;
        _store = store;
        _cipher = cipher;
        _logger = logger;
    }

    public string StoreDescription => _store.Description;

    /// <summary>Hangi motorun yedeklenebildiği ("PostgreSQL").</summary>
    public string Engine => _provider.Engine;

    /// <summary>Yedeklemenin şu an çalışabilir durumda olup olmadığı; engel varsa açıklaması.</summary>
    public Task<string?> ProbeAsync(CancellationToken ct) => _provider.ProbeAsync(ct);

    /// <summary>
    /// Yedek alır.
    ///
    /// Kayıt iş BAŞLAMADAN yazılıyor: yarıda çöken bir yedek hiçbir iz
    /// bırakmasaydı, kullanıcı hiç denenmemiş bir yedeği "alındı" sanırdı.
    /// </summary>
    public async Task<VaultResult> BackupAsync(
        CloudProject project, string userId, VaultBackupKind kind, CancellationToken ct)
    {
        if (!TryResolveConnection(project, out var connectionString, out var error))
            return new VaultResult(false, error);

        var record = new VaultBackup
        {
            ProjectId = project.Id,
            OrganizationId = project.OrganizationId,
            CreatedByUserId = userId,
            DatabaseName = project.Name,
            Engine = _provider.Engine,
            Kind = kind,
            StoreDescription = _store.Description,
            // Anahtarda proje ve zaman var: depodaki dosya, kayda bakmadan da
            // kime ait olduğu anlaşılabilsin.
            StorageKey = $"{project.Id}/{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.nvlt",
        };

        _context.VaultBackups.Add(record);
        await _context.SaveChangesAsync(ct);

        try
        {
            var spec = new BackupSpec(connectionString!, project.Name);
            record.SizeBytes = await RunPipelineAsync(
                produce: stream => _provider.BackupAsync(spec, stream, ct),
                transform: (input, output) => _cipher.EncryptAsync(input, output, ct),
                consume: stream => _store.PutAsync(record.StorageKey, stream, ct));

            record.Status = VaultBackupStatus.Succeeded;
        }
        catch (Exception ex)
        {
            record.Status = VaultBackupStatus.Failed;
            record.ErrorMessage = Shorten(ex.Message);
            _logger.LogError(ex, "Vault: {ProjectId} projesinin yedegi alinamadi.", project.Id);

            // Yarım yazılmış dosya bırakma: geçerli GÖRÜNEN bir yedek,
            // hiç olmayan bir yedekten daha tehlikeli.
            await TryDeleteAsync(record.StorageKey);
        }
        finally
        {
            record.CompletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(CancellationToken.None);
        }

        return record.Status == VaultBackupStatus.Succeeded
            ? new VaultResult(true, BackupId: record.Id)
            : new VaultResult(false, record.ErrorMessage);
    }

    /// <summary>
    /// Yedeği geri yükler.
    ///
    /// <b>Önce ZORUNLU bir ön yedek alınır.</b> Geri yükleme hedefteki nesneleri
    /// siler; ön yedek alınamıyorsa işlem hiç başlamaz — bu, "yanlış yedeği
    /// seçtim" durumunun tek geri dönüşü.
    /// </summary>
    public async Task<VaultResult> RestoreAsync(
        CloudProject project, VaultBackup backup, string userId, CancellationToken ct)
    {
        if (backup.Status != VaultBackupStatus.Succeeded)
            return new VaultResult(false, "Only a successful backup can be restored.");

        if (!TryResolveConnection(project, out var connectionString, out var error))
            return new VaultResult(false, error);

        var preRestore = await BackupAsync(project, userId, VaultBackupKind.PreRestore, ct);
        if (!preRestore.Ok)
            return new VaultResult(false,
                $"Restore aborted: the mandatory pre-restore backup failed ({preRestore.Error}).");

        var log = new VaultRestore
        {
            ProjectId = project.Id,
            OrganizationId = project.OrganizationId,
            BackupId = backup.Id,
            PreRestoreBackupId = preRestore.BackupId,
            PerformedByUserId = userId,
        };
        _context.VaultRestores.Add(log);
        await _context.SaveChangesAsync(ct);

        try
        {
            await using var encrypted = await _store.OpenAsync(backup.StorageKey, ct)
                ?? throw new InvalidOperationException(
                    "The backup file is missing from storage; it cannot be restored.");

            var spec = new RestoreSpec(connectionString!);
            await RunPipelineAsync(
                produce: stream => encrypted.CopyToAsync(stream, ct),
                transform: (input, output) => _cipher.DecryptAsync(input, output, ct),
                consume: async stream =>
                {
                    await _provider.RestoreAsync(spec, stream, ct);
                    return 0L;
                });

            log.Status = VaultBackupStatus.Succeeded;
        }
        catch (Exception ex)
        {
            log.Status = VaultBackupStatus.Failed;
            log.ErrorMessage = Shorten(ex.Message);
            _logger.LogError(ex, "Vault: {ProjectId} projesine geri yukleme basarisiz.", project.Id);
        }
        finally
        {
            log.CompletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(CancellationToken.None);
        }

        return log.Status == VaultBackupStatus.Succeeded
            ? new VaultResult(true, BackupId: preRestore.BackupId)
            : new VaultResult(false, log.ErrorMessage);
    }

    /// <summary>Kaydı ve dosyayı siler.</summary>
    public async Task DeleteAsync(VaultBackup backup, CancellationToken ct)
    {
        await TryDeleteAsync(backup.StorageKey);
        _context.VaultBackups.Remove(backup);
        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Yedeğin gerçekten geri yüklenebildiğini kanıtlar (V5).
    ///
    /// Kullanıcının veritabanına dokunmaz; sağlayıcı bunu geçici, boş bir
    /// sunucuda yapar. Sonuç yedeğin üzerine yazılır.
    /// </summary>
    public async Task<VaultResult> VerifyAsync(VaultBackup backup, CancellationToken ct)
    {
        if (backup.Status != VaultBackupStatus.Succeeded)
            return new VaultResult(false, "Only a successful backup can be verified.");

        try
        {
            await using var encrypted = await _store.OpenAsync(backup.StorageKey, ct)
                ?? throw new InvalidOperationException("The backup file is missing from storage.");

            // Şifre çözme de doğrulamanın parçası: etiketi tutmayan bir dosya
            // buraya kadar bile gelemez ve bu da geçerli bir "bozuk" cevabıdır.
            var error = await RunVerifyPipelineAsync(encrypted, ct);

            backup.VerifiedAt = error is null ? DateTime.UtcNow : null;
            backup.VerifyError = error;
        }
        catch (Exception ex)
        {
            backup.VerifiedAt = null;
            backup.VerifyError = Shorten(ex.Message);
            _logger.LogWarning(ex, "Vault: {BackupId} dogrulanamadi.", backup.Id);
        }

        await _context.SaveChangesAsync(CancellationToken.None);

        return backup.VerifiedAt is not null
            ? new VaultResult(true, BackupId: backup.Id)
            : new VaultResult(false, backup.VerifyError);
    }

    /// <summary>
    /// Saklama politikası: en yeni <paramref name="retainCount"/> OTOMATİK
    /// yedek kalır, daha eskiler silinir.
    ///
    /// <b>Elle alınan ve geri yükleme öncesi yedeklere dokunulmuyor.</b>
    /// Kullanıcının bilerek aldığı ya da bir geri yüklemenin tek geri dönüşü
    /// olan bir yedeği otomatik bir temizliğin silmesi, temizliğin çözdüğünden
    /// çok daha büyük bir sorun olurdu.
    /// </summary>
    public async Task<int> ApplyRetentionAsync(string projectId, int retainCount, CancellationToken ct)
    {
        if (retainCount < 1) return 0;

        var expired = await _context.VaultBackups
            .Where(b => b.ProjectId == projectId && b.Kind == VaultBackupKind.Scheduled)
            .OrderByDescending(b => b.CreatedAt)
            .Skip(retainCount)
            .ToListAsync(ct);

        foreach (var backup in expired)
        {
            await TryDeleteAsync(backup.StorageKey);
            _context.VaultBackups.Remove(backup);
        }

        if (expired.Count > 0) await _context.SaveChangesAsync(ct);
        return expired.Count;
    }

    /// <summary>Şifreli akışı çözüp sağlayıcının doğrulamasına verir.</summary>
    private async Task<string?> RunVerifyPipelineAsync(Stream encrypted, CancellationToken ct)
    {
        string? error = null;

        await RunPipelineAsync(
            produce: stream => encrypted.CopyToAsync(stream, ct),
            transform: (input, output) => _cipher.DecryptAsync(input, output, ct),
            consume: async stream =>
            {
                error = await _provider.VerifyAsync(stream, ct);
                return 0L;
            });

        return error;
    }

    /// <summary>Şifreli akışı olduğu gibi çağırana verir (indirme).</summary>
    public Task<Stream?> OpenEncryptedAsync(VaultBackup backup, CancellationToken ct) =>
        _store.OpenAsync(backup.StorageKey, ct);

    /// <summary>
    /// Üç adımı belleğe almadan birbirine bağlar: üretici → dönüştürücü → tüketici.
    ///
    /// <b>Neden boru (pipe), geçici dosya değil:</b> birkaç GB'lık bir dump'ı
    /// belleğe almak tek bir yedeğin sunucuyu düşürmesi demekti; diske yazılan
    /// ara dosya ise ŞİFRESİZ düz metin olurdu.
    /// </summary>
    private static async Task<long> RunPipelineAsync(
        Func<Stream, Task> produce,
        Func<Stream, Stream, Task> transform,
        Func<Stream, Task<long>> consume)
    {
        var first = new Pipe();
        var second = new Pipe();

        // Her adım çıkışını HER DURUMDA kapatıyor: bir adım hata alırsa sonraki
        // adım veri bekleyerek sonsuza kadar asılı kalmasın.
        var produceTask = RunStageAsync(() => produce(first.Writer.AsStream()), first.Writer);
        var transformTask = RunStageAsync(
            () => transform(first.Reader.AsStream(), second.Writer.AsStream()), second.Writer);
        var consumeTask = consume(second.Reader.AsStream());

        await Task.WhenAll(produceTask, transformTask, consumeTask);
        return await consumeTask;
    }

    private static async Task RunStageAsync(Func<Task> stage, PipeWriter writer)
    {
        try
        {
            await stage();
            await writer.CompleteAsync();
        }
        catch (Exception ex)
        {
            // Hatayı okuyucuya da ilet: aksi hâlde okuyucu "akış bitti" sanıp
            // YARIM veriyi başarılı sayardı.
            await writer.CompleteAsync(ex);
            throw;
        }
    }

    /// <summary>Bağlantıyı çözer ve SSRF politikasından geçirir.</summary>
    private bool TryResolveConnection(CloudProject project, out string? connectionString, out string? error)
    {
        connectionString = null;
        error = null;

        if (string.IsNullOrWhiteSpace(project.EncryptedConnectionString))
        {
            error = "This project has no live database connection.";
            return false;
        }

        // v1 yalnızca PostgreSQL: yazılıp canlı doğrulanmamış bir motoru
        // "destekleniyor" göstermek, kullanıcıya olmayan bir yedek vaat etmektir.
        if (!string.Equals(project.ConnectionDbType, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            error = $"Vault v1 supports PostgreSQL only (this project uses {project.ConnectionDbType}).";
            return false;
        }

        connectionString = _protector.Unprotect(project.EncryptedConnectionString);

        var host = DbIntrospectionService.ExtractHost(connectionString, project.ConnectionDbType!);
        if (!_hostPolicy.IsHostAllowed(host, out var denyReason))
        {
            connectionString = null;
            error = denyReason;
            return false;
        }

        return true;
    }

    private async Task TryDeleteAsync(string key)
    {
        try
        {
            await _store.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vault: {Key} depodan silinemedi.", key);
        }
    }

    /// <summary>Hata metni kullanıcıya gidiyor; bağlantı ayrıntısı taşımasın diye kısaltılıyor.</summary>
    private static string Shorten(string message)
    {
        var clean = message.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return clean.Length <= 400 ? clean : clean[..400] + "…";
    }
}
