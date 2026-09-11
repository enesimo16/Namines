using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;

namespace Namines.API.Controllers;

/// <param name="ConfirmDatabaseName">
/// Kullanıcının ELİYLE yazdığı proje adı. Geri yükleme mevcut veriyi siler;
/// tek tıkla tetiklenebilmesi, yanlış satıra basan biri için geri dönüşü
/// olmayan bir kayıp demekti.
/// </param>
public sealed record RestoreBackupRequest(string ConfirmDatabaseName);

/// <param name="Cadence">"Daily" veya "Weekly".</param>
/// <param name="DayOfWeek">Haftalıkta 0 (Pazar) – 6. Günlükte yok sayılır.</param>
/// <param name="RetainCount">Saklanacak otomatik yedek sayısı.</param>
public sealed record SaveScheduleRequest(
    bool Enabled, string Cadence, int HourUtc, int? DayOfWeek, int RetainCount);

/// <summary>
/// Namines Vault — yedekleme ve geri yükleme uçları.
///
/// Oturum (JWT) ile korunuyor, Gateway API anahtarıyla DEĞİL: bir panel
/// anahtarının veritabanının tamamını indirebilmesi ya da üzerine yazabilmesi,
/// anahtar sınırlamasının tamamını anlamsız kılardı.
/// </summary>
[Authorize]
[ApiController]
[Route("api/vault")]
public class VaultController : ControllerBase
{
    private readonly AuthDbContext _context;
    private readonly VaultService _vault;
    private readonly IVaultJobQueue _jobs;

    public VaultController(AuthDbContext context, VaultService vault, IVaultJobQueue jobs)
    {
        _context = context;
        _vault = vault;
        _jobs = jobs;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>
    /// Vault'un ayakta olduğunu ve yedeklerin NEREDE durduğunu söyler.
    ///
    /// Depo açıklaması bilerek dönülüyor: v1 sunucu diskini kullanıyor ve
    /// kullanıcının bunu ekranda görmeden yedeğe güvenmesi doğru olmaz.
    /// </summary>
    [HttpGet("health")]
    [AllowAnonymous]
    public async Task<IActionResult> Health(CancellationToken ct)
    {
        var problem = await _vault.ProbeAsync(ct);

        // 200 döndürülüyor, 503 değil: uç ÇALIŞIYOR ve doğru cevabı veriyor —
        // cevap "yedekleme şu an çalışamaz". Bunu bir hata koduna çevirmek,
        // sağlık kontrolünü okunamaz kılardı.
        return Ok(new
        {
            ok = problem is null,
            // Desteklenen motorlar LİSTE olarak dönüyor: tek bir "engine" alanı,
            // Vault birden çok motor yedekleyebildiği andan itibaren yanlış
            // bilgi olurdu. Liste kayıtlı sağlayıcılardan türetiliyor.
            engines = _vault.SupportedEngines,
            store = _vault.StoreDescription,
            problem,
        });
    }

    /// <summary>Projenin yedekleri, en yenisi üstte.</summary>
    [HttpGet("{projectId}/backups")]
    public async Task<IActionResult> List(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _context.CanViewAsync(projectId, userId, ct)) return Forbid();

        var backups = await _context.VaultBackups
            .Where(b => b.ProjectId == projectId)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new
            {
                id = b.Id,
                databaseName = b.DatabaseName,
                engine = b.Engine,
                status = b.Status.ToString(),
                kind = b.Kind.ToString(),
                sizeBytes = b.SizeBytes,
                error = b.ErrorMessage,
                createdAt = b.CreatedAt,
                completedAt = b.CompletedAt,
                store = b.StoreDescription,
                verifiedAt = b.VerifiedAt,
                verifyError = b.VerifyError,
            })
            .ToListAsync(ct);

        return Ok(new { store = _vault.StoreDescription, backups });
    }

    /// <summary>Geri yükleme geçmişi — "bu veritabanına en son kim ne yazdı".</summary>
    [HttpGet("{projectId}/restores")]
    public async Task<IActionResult> Restores(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _context.CanViewAsync(projectId, userId, ct)) return Forbid();

        var restores = await _context.VaultRestores
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => new
            {
                id = r.Id,
                backupId = r.BackupId,
                preRestoreBackupId = r.PreRestoreBackupId,
                status = r.Status.ToString(),
                error = r.ErrorMessage,
                startedAt = r.StartedAt,
                completedAt = r.CompletedAt,
            })
            .ToListAsync(ct);

        return Ok(restores);
    }

    /// <summary>
    /// Yedeklemeyi BAŞLATIR — bitmesini beklemez (PERF-003 / B-35).
    ///
    /// <b>Editor yetkisi yetiyor</b> (<see cref="OrgAccess.CanEditAsync"/>):
    /// yedek almak veriyi DEĞİŞTİRMEYEN, kaybı önleyen bir işlem — onu dar bir
    /// role kilitlemek, riski azaltmak yerine artırırdı.
    ///
    /// <b>Neden 202 ve neden 200 değil:</b> Yedekleme süresi veritabanı
    /// boyutuyla doğrusal büyüyor (bu oturumda küçük bir MariaDB için ~2,8 sn
    /// ölçüldü). 50 GB'lık bir veritabanında istek dakikalarca açık kalır;
    /// araya giren her proxy/load balancer zaman aşımı bunu keser ve kullanıcı
    /// "yedek başarısız" görür — oysa yedek sunucuda devam etmektedir.
    /// Yanlış bilgi, yavaşlıktan daha kötü.
    ///
    /// 202, "kabul ettim, henüz bitmedi" demenin standart yolu ve istemciye
    /// yoklaması gereken adresi veriyor. 200 dönmek "bitti" demek olurdu.
    ///
    /// <b>Doğrulama hemen yapılıyor</b> (bağlantı çözülebiliyor mu, motor
    /// destekli mi): bunları arka plana bırakmak, kullanıcının anında
    /// duyabileceği bir hatayı yoklamayla öğrenmesi demek olurdu.
    /// </summary>
    [HttpPost("{projectId}/backups")]
    public async Task<IActionResult> Create(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _context.CanEditAsync(projectId, userId, ct)) return Forbid();

        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFound();

        var result = await _vault.QueueBackupAsync(project, userId, VaultBackupKind.Manual, ct);
        if (!result.Ok) return BadRequest(new { error = result.Error });

        if (!_jobs.TryEnqueue(new VaultBackupJob(result.BackupId!, projectId)))
        {
            // Kuyruk dolu. Kaydı SİLMİYORUZ ama Failed olarak kapatıyoruz:
            // Running kalan bir kayıt, arayüzde sonsuza kadar "sürüyor"
            // gösterilirdi ve hiç kimse onu çalıştırmayacaktı.
            var orphan = await _context.VaultBackups
                .FirstOrDefaultAsync(b => b.Id == result.BackupId, ct);
            if (orphan is not null)
            {
                orphan.Status = VaultBackupStatus.Failed;
                orphan.ErrorMessage = "The backup queue is full. Try again in a few minutes.";
                orphan.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);
            }

            return StatusCode(503, new
            {
                error = "Too many backups are already queued. Try again in a few minutes.",
                queued = _jobs.PendingCount,
            });
        }

        return Accepted(
            Url.Action(nameof(GetBackup), new { projectId, backupId = result.BackupId }),
            new
            {
                backupId = result.BackupId,
                status = VaultBackupStatus.Running.ToString(),
                queued = _jobs.PendingCount,
            });
    }

    /// <summary>
    /// Tek bir yedeğin durumu — 202 sonrası istemcinin yoklayacağı uç.
    ///
    /// <b>Okuma yetkisi yetiyor:</b> burada verinin kendisi değil, işin durumu
    /// dönüyor. İndirme ayrı ve daha dar bir yetkiye bağlı (bkz. <c>Download</c>).
    /// </summary>
    [HttpGet("{projectId}/backups/{backupId}")]
    public async Task<IActionResult> GetBackup(string projectId, string backupId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (await _context.GetRoleAsync(projectId, userId, ct) is null) return NotFound();

        var backup = await FindBackupAsync(projectId, backupId, ct);
        if (backup is null) return NotFound();

        return Ok(new
        {
            backup.Id,
            status = backup.Status.ToString(),
            backup.SizeBytes,
            backup.ErrorMessage,
            backup.CreatedAt,
            backup.CompletedAt,
            // İstemcinin yoklamayı ne zaman bırakacağını bilmesi için: durum
            // artık Running değilse iş bitmiştir (başarılı ya da başarısız).
            done = backup.Status != VaultBackupStatus.Running,
        });
    }

    /// <summary>
    /// Şifreli yedeği indirir.
    ///
    /// <b>Dosya ŞİFRELİ iniyor</b> ve bu bilinçli: içerik veritabanının tamamı,
    /// çözülmüş hâlini tarayıcıya göndermek onu tarayıcı önbelleğine ve indirilenler
    /// klasörüne düz metin olarak bırakırdı. Çözmek yalnızca geri yükleme yolunda.
    /// </summary>
    [HttpGet("{projectId}/backups/{backupId}/download")]
    public async Task<IActionResult> Download(string projectId, string backupId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        // Yedek indirmek verinin TAMAMINI almaktir — okuma degil, yonetim yetkisi.
        if (!await _context.CanManageMembersAsync(projectId, userId, ct)) return Forbid();

        var backup = await FindBackupAsync(projectId, backupId, ct);
        if (backup is null) return NotFound();

        var stream = await _vault.OpenEncryptedAsync(backup, ct);
        if (stream is null) return NotFound(new { error = "The backup file is missing from storage." });

        return File(stream, "application/octet-stream", $"{backup.DatabaseName}-{backup.CreatedAt:yyyyMMdd-HHmmss}.nvlt");
    }

    /// <summary>
    /// Geri yükler. <b>Hedefteki veriyi SİLER.</b>
    ///
    /// Owner şartı: bir Admin bir tabloyu Gateway'e açabiliyor, ama veritabanının
    /// tamamının üzerine yazmak faturalama/org silme ile aynı ağırlıkta.
    /// </summary>
    [HttpPost("{projectId}/backups/{backupId}/restore")]
    public async Task<IActionResult> Restore(
        string projectId, string backupId, [FromBody] RestoreBackupRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (await _context.GetRoleAsync(projectId, userId, ct) != OrgRole.Owner) return Forbid();

        var backup = await FindBackupAsync(projectId, backupId, ct);
        if (backup is null) return NotFound();

        if (!string.Equals(request?.ConfirmDatabaseName?.Trim(), backup.DatabaseName, StringComparison.Ordinal))
            return BadRequest(new { error = $"Type the database name exactly ({backup.DatabaseName}) to confirm." });

        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFound();

        var result = await _vault.RestoreAsync(project, backup, userId, ct);
        return result.Ok
            ? Ok(new { restored = true, preRestoreBackupId = result.BackupId })
            : BadRequest(new { error = result.Error });
    }

    /// <summary>Yedeği siler — kaydı ve dosyayı birlikte.</summary>
    [HttpDelete("{projectId}/backups/{backupId}")]
    public async Task<IActionResult> Delete(string projectId, string backupId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _context.CanManageMembersAsync(projectId, userId, ct)) return Forbid();

        var backup = await FindBackupAsync(projectId, backupId, ct);
        if (backup is null) return NotFound();

        await _vault.DeleteAsync(backup, ct);
        return NoContent();
    }

    /// <summary>
    /// Yedeğin gerçekten geri yüklenebildiğini kanıtlar (V5).
    ///
    /// Doğrulama geçici ve boş bir sunucuda yapılır; projenin veritabanına
    /// dokunulmaz. Bu yüzden Owner değil, Editor yetkisi yetiyor.
    /// </summary>
    [HttpPost("{projectId}/backups/{backupId}/verify")]
    public async Task<IActionResult> Verify(string projectId, string backupId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _context.CanEditAsync(projectId, userId, ct)) return Forbid();

        var backup = await FindBackupAsync(projectId, backupId, ct);
        if (backup is null) return NotFound();

        var result = await _vault.VerifyAsync(backup, ct);
        return result.Ok
            ? Ok(new { verified = true, verifiedAt = backup.VerifiedAt })
            : BadRequest(new { error = result.Error });
    }

    /// <summary>Projenin otomatik yedek ayarı. Ayar yoksa kapalı varsayılan döner.</summary>
    [HttpGet("{projectId}/schedule")]
    public async Task<IActionResult> GetSchedule(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _context.CanViewAsync(projectId, userId, ct)) return Forbid();

        var schedule = await _context.VaultSchedules.FirstOrDefaultAsync(s => s.ProjectId == projectId, ct);

        // Ayar yoksa 404 DEĞİL: "bu projede otomatik yedek kapalı" geçerli bir
        // durum ve arayüzün iki ayrı hâl (yok / kapalı) yönetmesi gereksiz.
        return Ok(new
        {
            enabled = schedule?.Enabled ?? false,
            cadence = (schedule?.Cadence ?? VaultCadence.Daily).ToString(),
            hourUtc = schedule?.HourUtc ?? 3,
            dayOfWeek = schedule?.DayOfWeek,
            retainCount = schedule?.RetainCount ?? 7,
            lastRunAt = schedule?.LastRunAt,
        });
    }

    /// <summary>
    /// Otomatik yedek ayarını kaydeder.
    ///
    /// Yönetim yetkisi (Admin+): zamanlama, projenin diskini ve yedek geçmişini
    /// kalıcı olarak etkileyen bir ayar, tekil bir veri düzenlemesi değil.
    /// </summary>
    [HttpPut("{projectId}/schedule")]
    public async Task<IActionResult> SaveSchedule(
        string projectId, [FromBody] SaveScheduleRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _context.CanManageMembersAsync(projectId, userId, ct)) return Forbid();

        if (!Enum.TryParse<VaultCadence>(request.Cadence, ignoreCase: true, out var cadence))
            return BadRequest(new { error = "Cadence must be 'Daily' or 'Weekly'." });

        if (request.HourUtc is < 0 or > 23)
            return BadRequest(new { error = "HourUtc must be between 0 and 23." });

        if (cadence == VaultCadence.Weekly && request.DayOfWeek is not (>= 0 and <= 6))
            return BadRequest(new { error = "A weekly schedule needs DayOfWeek between 0 (Sunday) and 6." });

        // Üst sınır bilinçli: saklama sayısı doğrudan disk kullanımıdır ve v1
        // yedekleri sunucu diskinde tutuyor. Sınırsız bir sayı, tek bir projenin
        // diski doldurmasına izin vermek olurdu.
        if (request.RetainCount is < 1 or > 60)
            return BadRequest(new { error = "RetainCount must be between 1 and 60." });

        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFound();

        var schedule = await _context.VaultSchedules.FirstOrDefaultAsync(s => s.ProjectId == projectId, ct);
        if (schedule is null)
        {
            schedule = new VaultSchedule { ProjectId = projectId, OrganizationId = project.OrganizationId };
            _context.VaultSchedules.Add(schedule);
        }

        schedule.Enabled = request.Enabled;
        schedule.Cadence = cadence;
        schedule.HourUtc = request.HourUtc;
        schedule.DayOfWeek = cadence == VaultCadence.Weekly ? request.DayOfWeek : null;
        schedule.RetainCount = request.RetainCount;
        schedule.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return Ok(new
        {
            enabled = schedule.Enabled,
            cadence = schedule.Cadence.ToString(),
            hourUtc = schedule.HourUtc,
            dayOfWeek = schedule.DayOfWeek,
            retainCount = schedule.RetainCount,
            lastRunAt = schedule.LastRunAt,
        });
    }

    /// <summary>
    /// Yedeği hem kimliğiyle hem PROJESİYLE arar.
    ///
    /// Yalnızca kimlikle aramak, yetkisi olan bir projenin kimliğiyle BAŞKA bir
    /// projenin yedeğine ulaşmayı mümkün kılardı.
    /// </summary>
    private Task<VaultBackup?> FindBackupAsync(string projectId, string backupId, CancellationToken ct) =>
        _context.VaultBackups.FirstOrDefaultAsync(b => b.Id == backupId && b.ProjectId == projectId, ct);
}
