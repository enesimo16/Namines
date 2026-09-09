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
                    "Bu projenin yönetilen veritabanı silinmeyi bekliyor. Önce silme işlemini geri alın.");

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
                : new GroundResult(false, "Yönetilen veritabanı kaydı oluşturulamadı.");
        }

        try
        {
            var provisioned = await provider.CreateAsync(
                new ProvisionSpec(project.Id, project.Name), ct);

            record.ProviderProjectId = provisioned.ProviderProjectId;
            record.ProviderBranchId = provisioned.ProviderBranchId;
            record.Region = provisioned.Region;
            record.Status = GroundStatus.Active;

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
            record.Status = GroundStatus.Failed;
            record.Error = Shorten(ex.Message);
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
            return new GroundResult(false, "Bu veritabanı zaten silinmiş.");

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
    /// </summary>
    public async Task<GroundResult> CancelDeleteAsync(
        GroundDatabase record, IDatabaseProvider provider, CancellationToken ct)
    {
        if (record.Status != GroundStatus.PendingDelete)
            return new GroundResult(false, "Bu veritabanı için bekleyen bir silme yok.");

        var project = await _context.CloudProjects
            .FirstOrDefaultAsync(p => p.Id == record.ProjectId, ct);

        if (project is null) return new GroundResult(false, "Proje bulunamadı.");

        try
        {
            // CreateAsync idempotan: var olan veritabanına dokunmuyor, yalnızca
            // rolün parolasını tazeleyip yeni bağlantıyı döndürüyor.
            var provisioned = await provider.CreateAsync(
                new ProvisionSpec(project.Id, project.Name), ct);

            project.EncryptedConnectionString = _protector.Protect(provisioned.ConnectionString);
            project.ConnectionDbType = "PostgreSQL";
            project.UpdatedAt = DateTime.UtcNow;

            record.Status = GroundStatus.Active;
            record.DeleteRequestedAt = null;
            record.Error = null;
        }
        catch (Exception ex)
        {
            record.Error = Shorten(ex.Message);
            _logger.LogError(ex, "Ground: {ProjectId} icin silme geri alinamadi.", record.ProjectId);
            await _context.SaveChangesAsync(CancellationToken.None);
            return new GroundResult(false, record.Error);
        }

        await _context.SaveChangesAsync(ct);
        return new GroundResult(true, Database: record);
    }

    /// <summary>
    /// Bekleme penceresi dolmuş kayıtları KALICI olarak siler.
    ///
    /// Arka plan işinden çağrılıyor ve tek tek hata yakalıyor: bir kaydın
    /// silinememesi, sıradakilerin de silinmemesine yol açmamalı.
    /// </summary>
    public async Task<int> PurgeExpiredAsync(int graceDays, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-graceDays);

        var expired = await _context.GroundDatabases
            .Where(g => g.Status == GroundStatus.PendingDelete
                        && g.DeleteRequestedAt != null
                        && g.DeleteRequestedAt < cutoff)
            .ToListAsync(ct);

        var purged = 0;
        foreach (var record in expired)
        {
            var provider = FindProvider(record.Provider);
            if (provider is null)
            {
                // Sağlayıcı artık kayıtlı değil. Kaydı "silindi" işaretlemek,
                // gerçekte duran bir kaynağı görünmez yapardı.
                _logger.LogError(
                    "Ground: {Provider} saglayicisi kayitli degil, {ProjectId} silinemedi.",
                    record.Provider, record.ProjectId);
                continue;
            }

            try
            {
                await provider.DeleteAsync(
                    new ProvisionedDatabase(
                        record.ProviderProjectId ?? string.Empty,
                        record.ProviderBranchId,
                        record.Region ?? string.Empty,
                        ConnectionString: string.Empty),
                    ct);

                record.Status = GroundStatus.Deleted;
                record.DeletedAt = DateTime.UtcNow;
                record.Error = null;
                purged++;
            }
            catch (Exception ex)
            {
                // Kayıt PendingDelete kalıyor: bir sonraki turda tekrar denenir.
                // "Deleted" işaretlemek, duran bir kaynağı kaybetmek olurdu.
                record.Error = Shorten(ex.Message);
                _logger.LogError(ex, "Ground: {ProjectId} kalici olarak silinemedi.", record.ProjectId);
            }
        }

        if (expired.Count > 0) await _context.SaveChangesAsync(CancellationToken.None);
        return purged;
    }

    /// <summary>
    /// Plan kotası. Aşılmışsa kullanıcıya gösterilecek mesaj, aşılmamışsa null.
    /// </summary>
    private async Task<string?> IsQuotaExceededAsync(
        CloudProject project, string userId, CancellationToken ct)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return "Kullanıcı bulunamadı.";

        var tier = PlanQuotas.Resolve(user.SubscriptionStatus, user.PlanCode, user.IsDev);
        var limit = PlanQuotas.For(tier).ManagedDatabases;

        // Silinmiş kayıtlar sayılmıyor: silinen bir kaynak kotayı işgal etmemeli.
        // PendingDelete SAYILIYOR — kaynak hâlâ duruyor ve hâlâ maliyeti var.
        var current = await _context.GroundDatabases
            .CountAsync(g => g.CreatedByUserId == userId && g.Status != GroundStatus.Deleted, ct);

        if (!PlanQuotas.IsExceeded(limit, current)) return null;

        return limit == 0
            ? "Yönetilen veritabanı ücretsiz planda bulunmuyor. Kendi PostgreSQL sunucunuzu " +
              "bağlayabilir ya da planınızı yükseltebilirsiniz."
            : PlanQuotas.LimitMessage(tier, "yönetilen veritabanı", limit);
    }

    private static string Shorten(string message)
    {
        var clean = message.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return clean.Length <= 400 ? clean : clean[..400] + "…";
    }
}
