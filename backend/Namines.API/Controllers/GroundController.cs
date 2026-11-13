using Namines.API.Filters;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Namines.BuildingBlocks.Security;
using Namines.Core.Models.Auth;
using Namines.Ground.Abstractions;
using Namines.Ground.Application;
using Namines.Ground.Domain;

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
/// <b>Modül sınırı:</b> Bu denetleyici <c>AuthDbContext</c>'e DOĞRUDAN DOKUNMAZ;
/// yalnızca <see cref="ITenantProjectAuthority"/>, <see cref="IProjectConnectionSecretProvider"/>
/// ve <see cref="IGroundModule"/> üzerinden konuşur.
/// </summary>
[Authorize]
[ApiController]
[Route("api/ground")]
[FeatureGate("ground")]
public class GroundController : ControllerBase
{
    private readonly ITenantProjectAuthority _authority;
    private readonly IProjectConnectionSecretProvider _secrets;
    private readonly IGroundModule _groundModule;
    private readonly IConfiguration _configuration;

    public GroundController(
        ITenantProjectAuthority authority,
        IProjectConnectionSecretProvider secrets,
        IGroundModule groundModule,
        IConfiguration configuration)
    {
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _groundModule = groundModule ?? throw new ArgumentNullException(nameof(groundModule));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    private IGroundApplicationService Ground => _groundModule.Databases;

    private int GraceDays => _configuration.GetValue("Ground:DeleteGraceDays", GroundDefaults.DeleteGraceDays);

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet("providers")]
    public async Task<IActionResult> Providers(CancellationToken ct)
    {
        var providers = new List<object>();

        foreach (var provider in Ground.Providers)
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

    [HttpGet("{projectId}")]
    public async Task<IActionResult> Get(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _authority.CanViewAsync(projectId, userId, ct)) return Forbid();

        var record = await Ground.GetByProjectIdAsync(projectId, ct);

        return record is null ? NoContent() : Ok(Describe(record));
    }

    [HttpGet("{projectId}/metrics")]
    public async Task<IActionResult> Metrics(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _authority.CanViewAsync(projectId, userId, ct)) return Forbid();

        var record = await Ground.GetByProjectIdAsync(projectId, ct);
        if (record is null) return NotFound();

        var metrics = await Ground.GetMetricsAsync(record, ct);

        return Ok(new
        {
            storageBytes = metrics.StorageBytes,
            activeConnections = metrics.ActiveConnections,
            storageWarning = await Ground.GetStorageWarningAsync(record, metrics, ct),
        });
    }

    [HttpPost("{projectId}/provision")]
    public async Task<IActionResult> Provision(
        string projectId, [FromBody] ProvisionRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _authority.IsOwnerAsync(projectId, userId, ct)) return Forbid();

        var provider = Ground.FindProvider(request?.Provider ?? string.Empty);
        if (provider is null)
            return BadRequest(new { error = "No such provider." });

        if (await provider.ProbeAsync(ct) is { } problem)
            return BadRequest(new { error = problem });

        var snapshot = await _authority.GetProjectSnapshotAsync(projectId, ct);
        if (snapshot is null) return NotFound();

        var alreadyManaged = await Ground.HasManagedDatabaseAsync(projectId, ct);
        var existingConnection = await _secrets.ResolveAsync(projectId, ct);
        if (!alreadyManaged && existingConnection is not null)
            return BadRequest(new
            {
                error = "This project already has a database connection. Provisioning a managed " +
                        "database would replace it; remove the existing connection first.",
            });

        var result = await Ground.ProvisionAsync(projectId, userId, provider, ct);
        return result.Ok
            ? Ok(Describe(result.Database!))
            : BadRequest(new { error = result.Error });
    }

    [HttpPost("{projectId}/delete")]
    public async Task<IActionResult> RequestDelete(
        string projectId, [FromBody] DeleteDatabaseRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _authority.IsOwnerAsync(projectId, userId, ct)) return Forbid();

        var snapshot = await _authority.GetProjectSnapshotAsync(projectId, ct);
        if (snapshot is null) return NotFound();

        if (!string.Equals(request?.ConfirmProjectName?.Trim(), snapshot.ProjectName, StringComparison.Ordinal))
            return BadRequest(new { error = $"Type the project name exactly ({snapshot.ProjectName}) to confirm." });

        var record = await Ground.GetByProjectIdAsync(projectId, ct);
        if (record is null) return NotFound();

        var result = await Ground.RequestDeleteAsync(record, ct);
        return result.Ok
            ? Ok(Describe(result.Database!))
            : BadRequest(new { error = result.Error });
    }

    [HttpPost("{projectId}/cancel-delete")]
    public async Task<IActionResult> CancelDelete(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();
        if (!await _authority.IsOwnerAsync(projectId, userId, ct)) return Forbid();

        var record = await Ground.GetByProjectIdAsync(projectId, ct);
        if (record is null) return NotFound();

        var provider = Ground.FindProvider(record.Provider);
        if (provider is null)
            return BadRequest(new { error = $"The '{record.Provider}' provider is no longer registered." });

        var result = await Ground.CancelDeleteAsync(record, provider, ct);
        return result.Ok
            ? Ok(Describe(result.Database!))
            : BadRequest(new { error = result.Error });
    }

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
        graceDays = GraceDays,
    };
}
