using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Ground.Abstractions;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;

namespace Namines.API.Controllers;

/// <param name="Provider">Hangi sağlayıcı ("LocalPostgres", "Neon").</param>
public sealed record ProvisionRequest(string Provider);

/// <param name="ConfirmProjectName">
/// Kullanıcının ELİYLE yazdığı proje adı. Silme, veritabanının tamamını
/// yok eden bir işlem; tek tıkla tetiklenmesi kabul edilemez — Vault'un geri
/// yükleme onayıyla aynı eşik.
/// </param>
public sealed record DeleteDatabaseRequest(string ConfirmProjectName);

/// <summary>
/// Namines Ground — yönetilen veritabanı uçları.
///
/// <b>Ground'un kendi sitesi yok</b>; Vault'la aynı gerekçe: kullanıcıya bakan
/// yüzü Desk'in içindeki görünüm. Ayrıntı: <c>namines-ground/02-V1-KARARLARI.md</c>.
/// </summary>
[Authorize]
[ApiController]
[Route("api/ground")]
public class GroundController : ControllerBase
{
    private readonly AuthDbContext _context;
    private readonly GroundService _ground;
    private readonly IConfiguration _configuration;

    public GroundController(AuthDbContext context, GroundService ground, IConfiguration configuration)
    {
        _context = context;
        _ground = ground;
        _configuration = configuration;
    }

    private int GraceDays => _configuration.GetValue("Ground:DeleteGraceDays", GroundDefaults.DeleteGraceDays);

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>
    /// Kullanılabilir sağlayıcılar ve her birinin durumu.
    ///
    /// <b>Canlı kanıtlanmış olup olmadığı bilerek dönülüyor:</b> denenmemiş bir
    /// sağlayıcıyı denenmiş gibi sunmak, kullanıcının verisini kanıtlanmamış
    /// bir yola koymasına sessizce izin vermek olurdu.
    /// </summary>
    [HttpGet("providers")]
    public async Task<IActionResult> Providers(CancellationToken ct)
    {
        var providers = new List<object>();

        foreach (var provider in _ground.Providers)
        {
            providers.Add(new
            {
                name = provider.Name,
                liveVerified = provider.Capabilities.IsLiveVerified,
                supportsBranching = provider.Capabilities.SupportsBranching,
                supportsRegionChoice = provider.Capabilities.SupportsRegionChoice,
                responsibility = provider.Capabilities.ResponsibilityNote,
                problem = await provider.ProbeAsync(ct),
            });
        }

        return Ok(providers);
    }

    /// <summary>Projenin yönetilen veritabanı kaydı. Yoksa 204.</summary>
    [HttpGet("{projectId}")]
    public async Task<IActionResult> Get(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _context.CanViewAsync(projectId, userId, ct)) return Forbid();

        var record = await _context.GroundDatabases
            .FirstOrDefaultAsync(g => g.ProjectId == projectId, ct);

        return record is null ? NoContent() : Ok(Describe(record));
    }

    /// <summary>
    /// Kaynağın kullanım ölçümleri.
    ///
    /// <b>Ayrı bir uç</b>, kaydın içinde değil: ölçüm sağlayıcıya gidip
    /// gerçek bir sorgu çalıştırıyor. Kayıt okunan her yerde bunu yapmak,
    /// proje listesini açmayı sağlayıcı çağrısına bağımlı kılardı.
    /// </summary>
    [HttpGet("{projectId}/metrics")]
    public async Task<IActionResult> Metrics(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _context.CanViewAsync(projectId, userId, ct)) return Forbid();

        var record = await _context.GroundDatabases
            .FirstOrDefaultAsync(g => g.ProjectId == projectId, ct);
        if (record is null) return NotFound();

        var metrics = await _ground.GetMetricsAsync(record, ct);

        // null "bilinmiyor" demek ve arayuz onu "—" gosteriyor; sifira
        // cevirmek "olculdu ve sifir cikti" olurdu.
        return Ok(new
        {
            storageBytes = metrics.StorageBytes,
            activeConnections = metrics.ActiveConnections,
            // Yalnizca UYARI -- veritabani KAPATILMIYOR/kisitlanmiyor. Bkz.
            // GroundService.GetStorageWarningAsync'in kendi yorumu.
            storageWarning = await _ground.GetStorageWarningAsync(record, metrics, ct),
        });
    }

    /// <summary>
    /// Projeye yönetilen bir veritabanı açar.
    ///
    /// <b>Owner şartı:</b> barındırılan ve faturalanan bir kaynak açmak, Desk'in
    /// SQL konsolunu açmakla aynı eşik — en yüksek yetki seviyesi.
    /// </summary>
    [HttpPost("{projectId}/provision")]
    public async Task<IActionResult> Provision(
        string projectId, [FromBody] ProvisionRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (await _context.GetRoleAsync(projectId, userId, ct) != OrgRole.Owner) return Forbid();

        var provider = _ground.FindProvider(request?.Provider ?? string.Empty);
        if (provider is null)
            return BadRequest(new { error = "No such provider." });

        if (await provider.ProbeAsync(ct) is { } problem)
            return BadRequest(new { error = problem });

        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFound();

        // Var olan bir bağlantının üzerine sessizce yazmak, kullanıcının kendi
        // sunucusuna giden bağlantısını kaybetmesi demek olurdu.
        var alreadyManaged = await _context.GroundDatabases.AnyAsync(g => g.ProjectId == projectId, ct);
        if (!alreadyManaged && !string.IsNullOrWhiteSpace(project.EncryptedConnectionString))
            return BadRequest(new
            {
                error = "This project already has a database connection. Provisioning a managed " +
                        "database would replace it; remove the existing connection first.",
            });

        var result = await _ground.ProvisionAsync(project, userId, provider, ct);
        return result.Ok
            ? Ok(Describe(result.Database!))
            : BadRequest(new { error = result.Error });
    }

    /// <summary>
    /// Silmeyi ister. Kaynak HEMEN silinmez — bekleme penceresi başlar.
    /// </summary>
    [HttpPost("{projectId}/delete")]
    public async Task<IActionResult> RequestDelete(
        string projectId, [FromBody] DeleteDatabaseRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (await _context.GetRoleAsync(projectId, userId, ct) != OrgRole.Owner) return Forbid();

        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFound();

        if (!string.Equals(request?.ConfirmProjectName?.Trim(), project.Name, StringComparison.Ordinal))
            return BadRequest(new { error = $"Type the project name exactly ({project.Name}) to confirm." });

        var record = await _context.GroundDatabases
            .FirstOrDefaultAsync(g => g.ProjectId == projectId, ct);
        if (record is null) return NotFound();

        var result = await _ground.RequestDeleteAsync(record, ct);
        return result.Ok
            ? Ok(Describe(result.Database!))
            : BadRequest(new { error = result.Error });
    }

    /// <summary>Bekleme penceresi içindeki silmeyi geri alır.</summary>
    [HttpPost("{projectId}/cancel-delete")]
    public async Task<IActionResult> CancelDelete(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (await _context.GetRoleAsync(projectId, userId, ct) != OrgRole.Owner) return Forbid();

        var record = await _context.GroundDatabases
            .FirstOrDefaultAsync(g => g.ProjectId == projectId, ct);
        if (record is null) return NotFound();

        var provider = _ground.FindProvider(record.Provider);
        if (provider is null)
            return BadRequest(new { error = $"The '{record.Provider}' provider is no longer registered." });

        var result = await _ground.CancelDeleteAsync(record, provider, ct);
        return result.Ok
            ? Ok(Describe(result.Database!))
            : BadRequest(new { error = result.Error });
    }

    /// <summary>
    /// Kaydı dışa açarken sağlayıcı kimliklerini de veriyor: kullanıcının
    /// kaynağı sağlayıcının kendi panelinde bulabilmesi gerekir.
    /// </summary>
    private object Describe(GroundDatabase record) => new
    {
        id = record.Id,
        projectId = record.ProjectId,
        provider = record.Provider,
        providerProjectId = record.ProviderProjectId,
        region = record.Region,
        status = record.Status.ToString(),
        error = record.Error,
        createdAt = record.CreatedAt,
        deleteRequestedAt = record.DeleteRequestedAt,
        deletedAt = record.DeletedAt,
        // Arayuzun geri sayimi gosterebilmesi icin: kullanici silmeye kac gun
        // kaldigini gormeden geri alma firsatini degerlendiremezdi.
        graceDays = GraceDays,
    };
}
