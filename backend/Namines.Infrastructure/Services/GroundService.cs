using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Ground.Abstractions;
using Namines.Infrastructure.Data;

namespace Namines.Infrastructure.Services;

/// <summary>Bir Ground işleminin kullanıcıya dönen sonucu.</summary>
public sealed record GroundResult(bool Ok, string? Error = null, GroundDatabase? Database = null);

/// <summary>
/// Namines Ground'un iş akışı: kota → idempotans → sağlayıcı → şifreli bağlantı.
///
/// <b>Neden Infrastructure'da, Namines.Ground içinde değil:</b> Ground modülü
/// veritabanını, planları ve kimliği bilmiyor, bilmemeli de (bkz.
/// <c>Namines.Ground.csproj</c>). Kota, kayıt ve bağlantı sırrı bu katmanın işi;
/// Ground yalnızca "şu proje için bir veritabanı aç" diyor.
/// </summary>
public class GroundService
{
    private readonly AuthDbContext _context;
    private readonly IConnectionSecretProtector _protector;
    private readonly IEnumerable<IDatabaseProvider> _providers;
    private readonly ILogger<GroundService> _logger;

    public GroundService(
        AuthDbContext context,
        IConnectionSecretProtector protector,
        IEnumerable<IDatabaseProvider> providers,
        ILogger<GroundService> logger)
    {
        _context = context;
        _protector = protector;
        _providers = providers;
        _logger = logger;
    }

    /// <summary>Kayıtlı sağlayıcılar — arayüz hangilerinin canlı kanıtlandığını gösteriyor.</summary>
    public IReadOnlyList<IDatabaseProvider> Providers => _providers.ToList();

    public IDatabaseProvider? FindProvider(string name) =>
        _providers.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Projeye yönetilen bir veritabanı açar.
    ///
    /// <b>İdempotan:</b> proje için zaten bir kayıt varsa YENİ KAYNAK AÇILMAZ,
    /// var olan döner. İki kez basılan bir düğmenin ikinci bir veritabanı
    /// açması, kimsenin bilmediği ama faturalanan bir kaynak bırakırdı.
    /// </summary>
    public async Task<GroundResult> ProvisionAsync(
        CloudProject project, string userId, IDatabaseProvider provider, CancellationToken ct)
    {
        var existing = await _context.GroundDatabases
            .FirstOrDefaultAsync(g => g.ProjectId == project.Id, ct);

        if (existing is not null)
        {
            // Silme bekleyen bir kaydı sessizce yeniden kullanmak, kullanıcının
            // "sildim" bildiğini geri getirirdi — geri alma AYRI ve açık bir işlem.
            if (existing.Status == GroundStatus.PendingDelete)
                return new GroundResult(false,
                    "This project's managed database is pending deletion. Cancel the deletion first.");

            return new GroundResult(true, Database: existing);
        }

        if (await IsQuotaExceededAsync(project, userId, ct) is { } quotaError)
            return new GroundResult(false, quotaError);

        // Kayıt ÖNCE yazılıyor: sağlayıcıya gidilirken süreç çökerse, açılmış
        // olabilecek kaynak en azından iz bırakmış olur.
        var record = new GroundDatabase
        {
            ProjectId = project.Id,
            OrganizationId = project.OrganizationId,
            Provider = provider.Name,
            CreatedByUserId = userId,
            Status = GroundStatus.Provisioning,
            ClaimedAt = DateTime.UtcNow,
        };

        _context.GroundDatabases.Add(record);

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Benzersiz indeks: aynı anda gelen ikinci istek buraya düşer.
            // Bu bir hata değil, idempotansın ta kendisi — yarış koşulunda bile
            // tek kaynak açılmasını garanti eden şey bu.
            _context.Entry(record).State = EntityState.Detached;
            var winner = await _context.GroundDatabases
                .FirstOrDefaultAsync(g => g.ProjectId == project.Id, ct);

            return winner is not null
                ? new GroundResult(true, Database: winner)
                : new GroundResult(false, "The managed database record could not be created.");
        }

        try
        {
            var provisioned = await provider.CreateAsync(
                new ProvisionSpec(project.Id, project.Name), ct);

            record.ProviderProjectId = provisioned.ProviderProjectId;
            record.ProviderBranchId = provisioned.ProviderBranchId;
            record.Region = provisioned.Region;
            record.Status = GroundStatus.Active;
            record.ClaimedAt = null;

            // Bağlantı, Desk/Vault ile AYNI mekanizmayla ve AYNI yere yazılıyor.
            // İkinci bir kopya tutmak, sızdırılabilecek yüzeyi ikiye katlardı.
            project.EncryptedConnectionString = _protector.Protect(provisioned.ConnectionString);
            project.ConnectionDbType = "PostgreSQL";
            project.UpdatedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "Ground: {ProjectId} icin {Provider} uzerinde veritabani acildi.",
                project.Id, provider.Name);
        }
        catch (Exception ex)
        {
            // record.Error, GroundController.Describe uzerinden GET
            // /api/ground/{projectId} yanitina ve bu metodun donus degeri
            // uzerinden Provision uc noktasinin BadRequest gövdesine gidiyor.
            // Ham istisna metni (saglayici API'sinin ham HTTP govdesi ya da
            // surucu mesaji) İSTEMCİYE degil, yalnizca loga yazilir.
            record.Status = GroundStatus.Failed;
            record.ClaimedAt = null;
            record.Error = "Provisioning failed. Contact support if this persists.";
            _logger.LogError(ex, "Ground: {ProjectId} icin provizyon basarisiz.", project.Id);
        }
        finally
        {
            await _context.SaveChangesAsync(CancellationToken.None);
        }

        return record.Status == GroundStatus.Active
            ? new GroundResult(true, Database: record)
            : new GroundResult(false, record.Error);
    }

    /// <summary>
    /// Silmeyi İSTER — kaynak hemen silinmez, bekleme penceresi başlar.
    ///
    /// <b>Gecikme bilinçli:</b> bir veritabanının yanlışlıkla silindiği çoğu
    /// zaman ancak birileri onu kullanmayı denediğinde anlaşılır. Anında silme,
    /// tek bir yanlış tıklamayı geri dönüşü olmayan veri kaybına çevirirdi.
    /// </summary>
    public async Task<GroundResult> RequestDeleteAsync(GroundDatabase record, CancellationToken ct)
    {
        if (record.Status == GroundStatus.Deleted)
            return new GroundResult(false, "This database has already been deleted.");

        record.Status = GroundStatus.PendingDelete;
        record.DeleteRequestedAt = DateTime.UtcNow;

        // Bağlantı HEMEN kaldırılıyor: kaynak henüz duruyor ama kullanıcı onu
        // "silinmiş" saydı; Desk'in ona yazmaya devam etmesi tutarsız olurdu.
        var project = await _context.CloudProjects
            .FirstOrDefaultAsync(p => p.Id == record.ProjectId, ct);

        if (project is not null)
        {
            project.EncryptedConnectionString = null;
            project.ConnectionDbType = null;
            project.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(ct);
        return new GroundResult(true, Database: record);
    }

    /// <summary>
    /// Bekleme penceresi içindeki silmeyi geri alır.
    ///
    /// Bağlantı dizesi geri getirilemiyor (şifreli metin silindi), bu yüzden
    /// sağlayıcıdan yeni bir kimlik bilgisi alınıyor — parola tazeleniyor.
    ///
    /// <b>YALNIZCA <see cref="ProviderCapabilities.CreateIsIdempotentByProjectId"/>
    /// olan sağlayıcılarda.</b> Bu kontrol OLMADAN önce, Neon/Supabase'de
    /// <c>CreateAsync</c>'i tekrar çağırmak KOŞULSUZ ikinci bir uzak proje
    /// açıyordu: kullanıcının orijinal verisi erişilemez kalıyor (bağlantı artık
    /// yeni/boş projeye işaret ediyor) ve <c>record.ProviderProjectId</c> hiç
    /// güncellenmediği için ileride tetiklenecek gerçek silme YANLIŞ (eski, terk
    /// edilmiş) projeyi siliyor — yeni proje sonsuza dek faturalanan, kimsenin
    /// bilmediği bir kaynak olarak kalıyordu. Desteklenmeyen bir sağlayıcıda
    /// yanlış ama "başarılı görünen" bir kurtarma yerine dürüst bir ret.
    /// </summary>
    public async Task<GroundResult> CancelDeleteAsync(
        GroundDatabase record, IDatabaseProvider provider, CancellationToken ct)
    {
        if (record.Status != GroundStatus.PendingDelete)
            return new GroundResult(false, "There is no pending deletion for this database.");

        if (!provider.Capabilities.CreateIsIdempotentByProjectId)
        {
            return new GroundResult(false,
                $"Canceling deletion is not supported for {provider.Name} yet: recovering the " +
                "connection would require opening a second database, abandoning the original " +
                "and billing both. Let the deletion proceed, or contact support before the " +
                "grace window ends if you need the original data back.");
        }

        var project = await _context.CloudProjects
            .FirstOrDefaultAsync(p => p.Id == record.ProjectId, ct);

        if (project is null) return new GroundResult(false, "Project not found.");

        // DURUM GEÇİŞİ ATOMİK OLARAK SAHİPLENİLİYOR — ve bu, TÜM ret kapılarından
        // SONRA yapılıyor.
        //
        // <b>Bulunan açık (docs/last-review M-7):</b> iptal ile arka plandaki
        // <see cref="PurgeExpiredAsync"/> aynı kayıt üzerinde yarışıyordu ve hiçbir
        // eşzamanlılık kontrolü yoktu. Bekleme penceresinin son saniyesinde iptal
        // durumu <c>Active</c> yaparken purge zaten <c>DROP DATABASE</c>'i çağırıyor
        // olabiliyordu: ekranda "Active" görünen, veritabanı ise düşürülmüş bir kayıt.
        //
        // <b>SIRA ÖNEMLİ ve bunu bir test yakaladı.</b> Sahiplenme ilk yazdığımda
        // metodun BAŞINDAYDI, yani yetenek kontrolünden (<c>CreateIsIdempotentByProjectId</c>)
        // ve proje aramasından ÖNCE. O dallardan biri erken döndüğünde sahiplenme
        // geri alınmıyor ve kayıt <c>Provisioning</c>'de ASILI kalıyordu — ne purge
        // ne de ikinci bir iptal onu ele alabilirdi. Yani eşzamanlılık düzeltmesi,
        // düzeltmeye çalıştığı şeyin daha kötü bir sürümünü üretmişti. Reddedilecek
        // bir istek artık hiçbir şeye dokunmuyor.
        var claimed = await _context.GroundDatabases
            .Where(g => g.Id == record.Id && g.Status == GroundStatus.PendingDelete)
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.Status, GroundStatus.Provisioning)
                .SetProperty(g => g.ClaimedAt, (DateTime?)DateTime.UtcNow)
                .SetProperty(g => g.DeleteRequestedAt, (DateTime?)null), ct);

        if (claimed == 0)
            return new GroundResult(false,
                "This deletion is already being processed and can no longer be canceled.");

        // ExecuteUpdate degisiklik izleyicisini atlar; elimizdeki nesne bayat.
        await _context.Entry(record).ReloadAsync(ct);

        try
        {
            // CreateAsync idempotan: var olan veritabanına dokunmuyor, yalnızca
            // rolün parolasını tazeleyip yeni bağlantıyı döndürüyor. (Yukarıdaki
            // kontrol bunu YALNIZCA bunu garanti eden sağlayıcılar için sağlıyor.)
            var provisioned = await provider.CreateAsync(
                new ProvisionSpec(
                    project.Id, project.Name, record.Region,
                    ExistingProviderProjectId: record.ProviderProjectId,
                    ExistingProviderBranchId: record.ProviderBranchId),
                ct);

            project.EncryptedConnectionString = _protector.Protect(provisioned.ConnectionString);
            project.ConnectionDbType = "PostgreSQL";
            project.UpdatedAt = DateTime.UtcNow;

            // Sahiplenme Provisioning'e cekmisti; is bitti, Active'e geciyor.
            record.Status = GroundStatus.Active;
            record.ClaimedAt = null;
            record.DeleteRequestedAt = null;
            record.Error = null;
        }
        catch (Exception ex)
        {
            // Ayni sebep: ham istisna metni CancelDelete uc noktasinin
            // BadRequest govdesine ve Describe uzerinden GET ucuna gider.
            // SAHIPLENME GERI ALINIYOR: kayit Provisioning'de asili kalirsa ne
            // purge ne de ikinci bir iptal denemesi onu ele alabilir — kullanici
            // ne verisini geri alabilir ne de silinmesini bekleyebilir.
            record.Status = GroundStatus.PendingDelete;
            record.ClaimedAt = null;
            record.DeleteRequestedAt = DateTime.UtcNow;
            record.Error = "Canceling deletion failed. Contact support if this persists.";
            _logger.LogError(ex, "Ground: {ProjectId} icin silme geri alinamadi.", record.ProjectId);
            await _context.SaveChangesAsync(CancellationToken.None);
            return new GroundResult(false, record.Error);
        }

        await _context.SaveChangesAsync(ct);
        return new GroundResult(true, Database: record);
    }

    /// <summary>
    /// Bir sahiplenmenin "terk edilmiş" sayılması için geçmesi gereken süre.
    /// Cömert tutuldu: yavaş bir sağlayıcı çağrısının ortasındaki canlı bir işi
    /// yanlışlıkla geri almak, bayat kaydı biraz geç kurtarmaktan pahalı.
    /// </summary>
    public static readonly TimeSpan StaleClaimLease = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Süreç ölünce <c>Deleting</c>/<c>Provisioning</c>'de asılı kalan kayıtları
    /// kurtarır. Her geçiş, durum VE ClaimedAt üzerinde koşullu tek bir UPDATE:
    /// iş o sırada gerçekten bittiyse (durum değiştiyse) 0 satır döner.
    ///
    /// <b>Hiçbiri veri kaybına doğru düşmüyor:</b>
    /// <list type="bullet">
    /// <item><c>Deleting</c> → <c>PendingDelete</c>: normal purge yeniden dener
    /// (silme zaten istenmişti, DeleteRequestedAt purge sahiplenmesinde korunur).</item>
    /// <item><c>Provisioning</c> + ProviderProjectId var → iptal sahiplenmesiydi
    /// (kaynak mevcut); <c>PendingDelete</c>'e dönüyor ve bekleme penceresi
    /// BAŞTAN başlıyor — orijinal istek zamanı iptalde silinmişti, kullanıcıya
    /// kısa değil tam süre tanımak güvenli taraf.</item>
    /// <item><c>Provisioning</c> + ProviderProjectId yok → ilk açılış yarıda
    /// kaldı; mevcut "başarısız provizyon" durumu <c>Failed</c>'e alınıyor.</item>
    /// </list>
    /// ClaimedAt'i null olan (migration öncesi) asılı kayıtlara DOKUNULMUYOR: canlı
    /// bir işi geri almamak için yalnızca uyarı loglanıyor.
    /// </summary>
    public async Task<int> ReclaimStaleClaimsAsync(TimeSpan lease, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var cutoff = now - lease;

        var deleting = await _context.GroundDatabases
            .Where(g => g.Status == GroundStatus.Deleting && g.ClaimedAt != null && g.ClaimedAt < cutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.Status, GroundStatus.PendingDelete)
                .SetProperty(g => g.ClaimedAt, (DateTime?)null), ct);

        var cancelClaims = await _context.GroundDatabases
            .Where(g => g.Status == GroundStatus.Provisioning && g.ClaimedAt != null && g.ClaimedAt < cutoff
                        && g.ProviderProjectId != null && g.ProviderProjectId != "")
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.Status, GroundStatus.PendingDelete)
                .SetProperty(g => g.DeleteRequestedAt, (DateTime?)now)
                .SetProperty(g => g.ClaimedAt, (DateTime?)null), ct);

        var failedProvisions = await _context.GroundDatabases
            .Where(g => g.Status == GroundStatus.Provisioning && g.ClaimedAt != null && g.ClaimedAt < cutoff
                        && (g.ProviderProjectId == null || g.ProviderProjectId == ""))
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.Status, GroundStatus.Failed)
                .SetProperty(g => g.Error, "Provisioning was interrupted. Contact support if this persists.")
                .SetProperty(g => g.ClaimedAt, (DateTime?)null), ct);

        var total = deleting + cancelClaims + failedProvisions;
        if (total > 0)
            _logger.LogWarning(
                "Ground: {Total} asili kayit kurtarildi (Deleting->PendingDelete: {Deleting}, " +
                "iptal->PendingDelete: {Cancel}, Provisioning->Failed: {Failed}).",
                total, deleting, cancelClaims, failedProvisions);

        var legacy = await _context.GroundDatabases.CountAsync(
            g => (g.Status == GroundStatus.Deleting || g.Status == GroundStatus.Provisioning)
                 && g.ClaimedAt == null && g.CreatedAt < cutoff, ct);
        if (legacy > 0)
            _logger.LogWarning(
                "Ground: {Count} kayit ClaimedAt olmadan Deleting/Provisioning'de; migration oncesi " +
                "asili kalmis olabilir, otomatik dokunulmadi — elle incelenmeli.", legacy);

        return total;
    }

    /// <summary>
    /// Proje silinirken sağlayıcıdaki kaynağı BEKLEME PENCERESİ OLMADAN kaldırır.
    ///
    /// Proje gidince kaydı da gider (FK <c>Restrict</c>: çağıran satırı kaldırmak
    /// zorunda) ve geriye "silinecek bir kayıt" kalmaz; bekleme penceresi bu yüzden
    /// anlamsız ve kaynak kimsenin bilmediği, faturalanan bir veritabanı olarak kalırdı.
    ///
    /// Purge ile AYNI sahiplenme kapısı kullanılıyor: kayıt yalnızca hâlâ sahiplenilebilir
    /// durumdaysa <c>Deleting</c>'e alınıyor; taze bir <c>Deleting</c>/<c>Provisioning</c>
    /// başka bir işlem demek → "meşgul" döner. Sağlayıcı çağrısı başarısızsa kayıt
    /// <c>PendingDelete</c> + geçmiş tarihle işaretlenir ki arka plan purge'ü yeniden
    /// denesin; çağıran projeyi SİLMEMELİ.
    /// </summary>
    public async Task<GroundResult> DeprovisionNowAsync(GroundDatabase record, CancellationToken ct)
    {
        if (record.Status == GroundStatus.Deleted)
            return new GroundResult(true, Database: record);

        var leaseCutoff = DateTime.UtcNow - StaleClaimLease;
        var busy = (record.Status == GroundStatus.Deleting || record.Status == GroundStatus.Provisioning)
                   && record.ClaimedAt != null && record.ClaimedAt >= leaseCutoff;
        if (busy)
            return new GroundResult(false, "The managed database is being created or deleted right now. Try again in a few minutes.");

        // Dış kaynak yoksa (provizyon sağlayıcıya hiç ulaşmadı) temizlenecek bir şey de yok.
        if (string.IsNullOrWhiteSpace(record.ProviderProjectId))
            return new GroundResult(true, Database: record);

        var provider = FindProvider(record.Provider);
        if (provider is null)
        {
            _logger.LogError("Ground: {Provider} saglayicisi kayitli degil, {ProjectId} silinemedi.",
                record.Provider, record.ProjectId);
            return new GroundResult(false, "The managed database provider is not available, so the database could not be removed.");
        }

        var previousStatus = record.Status;
        var claimed = await _context.GroundDatabases
            .Where(g => g.Id == record.Id && g.Status == previousStatus)
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.Status, GroundStatus.Deleting)
                .SetProperty(g => g.ClaimedAt, (DateTime?)DateTime.UtcNow), ct);
        if (claimed == 0)
            return new GroundResult(false, "The managed database changed while it was being removed. Try again.");

        // ExecuteUpdate izlenen varlığı güncellemez; purge'daki gibi yeniden yükle ki
        // sonraki durum atamaları (özellikle PendingDelete -> PendingDelete) kaydedilsin.
        await _context.Entry(record).ReloadAsync(ct);

        try
        {
            await provider.DeleteAsync(
                new ProvisionedDatabase(
                    record.ProviderProjectId,
                    record.ProviderBranchId,
                    record.Region ?? string.Empty,
                    ConnectionString: string.Empty),
                ct);

            record.Status = GroundStatus.Deleted;
            record.ClaimedAt = null;
            record.DeletedAt = DateTime.UtcNow;
            record.Error = null;
            await _context.SaveChangesAsync(CancellationToken.None);
            return new GroundResult(true, Database: record);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ground: {ProjectId} proje silinirken saglayicidan kaldirilamadi.", record.ProjectId);

            // Geçmiş tarih: purge bekleme penceresini beklemeden bir sonraki turda yeniden dener.
            record.Status = GroundStatus.PendingDelete;
            record.ClaimedAt = null;
            record.DeleteRequestedAt = DateTime.UtcNow.AddDays(-(GroundDefaults.DeleteGraceDays + 1));
            record.Error = "Deletion failed and will be retried automatically.";
            await _context.SaveChangesAsync(CancellationToken.None);
            return new GroundResult(false, "The managed database could not be removed from its provider. The project was not deleted; it will be retried automatically, try again shortly.");
        }
    }

    /// <summary>
    /// Bekleme penceresi dolmuş kayıtları KALICI olarak siler.
    ///
    /// Arka plan işinden çağrılıyor ve tek tek hata yakalıyor: bir kaydın
    /// silinememesi, sıradakilerin de silinmemesine yol açmamalı.
    /// </summary>
    public async Task<int> PurgeExpiredAsync(int graceDays, CancellationToken ct)
    {
        await ReclaimStaleClaimsAsync(StaleClaimLease, ct);

        var cutoff = DateTime.UtcNow.AddDays(-graceDays);

        var expired = await _context.GroundDatabases
            .Where(g => g.Status == GroundStatus.PendingDelete
                        && g.DeleteRequestedAt != null
                        && g.DeleteRequestedAt < cutoff)
            .ToListAsync(ct);

        var purged = 0;
        foreach (var record in expired)
        {
            // AYNI KAPI (docs/last-review M-7): kayit yalnizca HALA PendingDelete
            // ise sahipleniliyor. Kullanici bu arada silmeyi geri aldiysa (durum
            // Provisioning/Active) burada 0 satir doner ve sağlayıcıda DROP
            // CAGRILMAZ — yarisin kaybeden tarafi hicbir sey yapmiyor.
            var claimed = await _context.GroundDatabases
                .Where(g => g.Id == record.Id && g.Status == GroundStatus.PendingDelete)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(g => g.Status, GroundStatus.Deleting)
                    .SetProperty(g => g.ClaimedAt, (DateTime?)DateTime.UtcNow), ct);

            if (claimed == 0)
            {
                _logger.LogInformation(
                    "Ground: {ProjectId} silinmedi — kayit bu arada baska bir islem tarafindan alindi.",
                    record.ProjectId);
                continue;
            }

            await _context.Entry(record).ReloadAsync(ct);

            var provider = FindProvider(record.Provider);
            if (provider is null)
            {
                // Sağlayıcı artık kayıtlı değil. Kaydı "silindi" işaretlemek,
                // gerçekte duran bir kaynağı görünmez yapardı. Sahiplenme geri
                // alınıyor: Deleting'de bırakmak kaydı hiç işlenemez kılardı.
                record.Status = GroundStatus.PendingDelete;
                record.ClaimedAt = null;
                await _context.SaveChangesAsync(CancellationToken.None);
                _logger.LogError(
                    "Ground: {Provider} saglayicisi kayitli degil, {ProjectId} silinemedi.",
                    record.Provider, record.ProjectId);
                continue;
            }

            // ProviderProjectId yoksa (provizyon hiç sağlayıcıya ulaşamadan
            // Failed oldu, ya da zaten temizlendi) silinecek DIŞ bir kaynak
            // yok. provider.DeleteAsync'i yine de çağırmak GARANTİ başarısız
            // olurdu (sağlayıcılar bunu boş/geçersiz bir kimlikle çağırınca
            // hata döner) — kayıt sonsuza kadar PendingDelete'te kalır ve her
            // turda aynı başarısız çağrı tekrarlanırdı. Temizlenecek dış
            // kaynak olmadığından kaydı doğrudan silinmiş işaretlemek doğru.
            if (string.IsNullOrWhiteSpace(record.ProviderProjectId))
            {
                record.Status = GroundStatus.Deleted;
                record.ClaimedAt = null;
                record.DeletedAt = DateTime.UtcNow;
                record.Error = null;
                purged++;
                continue;
            }

            try
            {
                await provider.DeleteAsync(
                    new ProvisionedDatabase(
                        record.ProviderProjectId,
                        record.ProviderBranchId,
                        record.Region ?? string.Empty,
                        ConnectionString: string.Empty),
                    ct);

                record.Status = GroundStatus.Deleted;
                record.ClaimedAt = null;
                record.DeletedAt = DateTime.UtcNow;
                record.Error = null;
                purged++;
            }
            catch (Exception ex)
            {
                // Kayıt PendingDelete kalıyor: bir sonraki turda tekrar denenir.
                // "Deleted" işaretlemek, duran bir kaynağı kaybetmek olurdu.
                // Ham istisna metni burada da GET /api/ground/{projectId}
                // (Describe.error) üzerinden istemciye gidiyordu.
                // Sahiplenme geri aliniyor ki bir sonraki turda yeniden denenebilsin;
                // Deleting'de asili kalan kayit hic islenmezdi.
                record.Status = GroundStatus.PendingDelete;
                record.ClaimedAt = null;
                record.Error = "Deletion failed and will be retried automatically.";
                _logger.LogError(ex, "Ground: {ProjectId} kalici olarak silinemedi.", record.ProjectId);
            }
        }

        if (expired.Count > 0) await _context.SaveChangesAsync(CancellationToken.None);
        return purged;
    }

    /// <summary>
    /// Kaynağın kullanım ölçümleri.
    ///
    /// <b>Sayılar sağlayıcıdan OKUNUR, tahmin edilmez</b>
    /// (<c>00-GENEL-BAKIS.md</c> §9 madde 4). Sağlayıcı bir değeri vermiyorsa
    /// alan <c>null</c> kalır ve arayüz onu "—" gösterir; sıfır yazmak
    /// "ölçüldü ve sıfır çıktı" demek olurdu.
    /// </summary>
    public async Task<DatabaseMetrics> GetMetricsAsync(GroundDatabase record, CancellationToken ct)
    {
        // Silinmiş bir kaynağı ölçmeye çalışmak, sağlayıcıda kaçınılmaz bir
        // hataya koşmak demek — durum zaten cevabı veriyor.
        if (record.Status is GroundStatus.Deleted or GroundStatus.Failed)
            return new DatabaseMetrics(null, null);

        var provider = FindProvider(record.Provider);
        if (provider is null) return new DatabaseMetrics(null, null);

        try
        {
            return await provider.GetMetricsAsync(
                new ProvisionedDatabase(
                    record.ProviderProjectId ?? string.Empty,
                    record.ProviderBranchId,
                    record.Region ?? string.Empty,
                    ConnectionString: string.Empty),
                ct);
        }
        catch (Exception ex)
        {
            // Ölçüm alınamaması kaynağı geçersiz kılmaz; "bilinmiyor" dönülüyor.
            _logger.LogWarning(ex, "Ground: {ProjectId} olcumleri okunamadi.", record.ProjectId);
            return new DatabaseMetrics(null, null);
        }
    }

    /// <summary>
    /// Boyut planın uyarı eşiğini aşmış mı — aşmışsa kullanıcıya gösterilecek
    /// mesaj, aşmamışsa (ya da plan uyarı tanımlamıyorsa) null.
    ///
    /// <b>Yalnızca UYARI, kısıtlama değil</b> (<c>PlanLimits.ManagedDatabaseStorageWarningBytes</c>'in
    /// kendi yorumu): PostgreSQL'de bir veritabanının yazmasını disk boyutuna
    /// göre durdurmanın standart bir yolu yok; olan yol kullanıcının
    /// uygulamasını kırardı. Bu yüzden burada hiçbir şey KAPATILMIYOR — yalnızca
    /// log'a ve arayüze bir mesaj düşüyor.
    /// </summary>
    public async Task<string?> GetStorageWarningAsync(
        GroundDatabase record, DatabaseMetrics metrics, CancellationToken ct)
    {
        if (metrics.StorageBytes is not { } bytes) return null;

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == record.CreatedByUserId, ct);
        if (user is null) return null;

        var tier = PlanQuotas.Resolve(user.SubscriptionStatus, user.PlanCode, user.IsDev);
        var threshold = PlanQuotas.For(tier).ManagedDatabaseStorageWarningBytes;

        if (threshold <= 0 || bytes < threshold) return null;

        // Operatör tarafında da görünsün: kullanıcı ekranı hiç açmazsa bile
        // bu, sunucu diskinin kimin yüzünden büyüdüğünü gösteren tek iz olur.
        _logger.LogWarning(
            "Ground: {ProjectId} {Bytes} bayta ulaşti, plan esigi {Threshold} bayt.",
            record.ProjectId, bytes, threshold);

        return $"This database has grown past the {threshold / 1024 / 1024} MB guideline for your plan. " +
               "It keeps working — nothing is restricted — but consider upgrading or trimming data.";
    }

    /// <summary>
    /// Plan kotası. Aşılmışsa kullanıcıya gösterilecek mesaj, aşılmamışsa null.
    /// </summary>
    private async Task<string?> IsQuotaExceededAsync(
        CloudProject project, string userId, CancellationToken ct)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return "User not found.";

        var tier = PlanQuotas.Resolve(user.SubscriptionStatus, user.PlanCode, user.IsDev);
        var limit = PlanQuotas.For(tier).ManagedDatabases;

        // Silinmiş kayıtlar sayılmıyor: silinen bir kaynak kotayı işgal etmemeli.
        // PendingDelete SAYILIYOR — kaynak hâlâ duruyor ve hâlâ maliyeti var.
        var current = await _context.GroundDatabases
            .CountAsync(g => g.CreatedByUserId == userId && g.Status != GroundStatus.Deleted, ct);

        if (!PlanQuotas.IsExceeded(limit, current)) return null;

        return limit == 0
            ? "Managed databases are not included in the free plan. You can connect your own " +
              "PostgreSQL server, or upgrade your plan."
            : PlanQuotas.LimitMessage(tier, "managed database", limit);
    }

    private static string Shorten(string message)
    {
        var clean = message.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return clean.Length <= 400 ? clean : clean[..400] + "…";
    }
}
