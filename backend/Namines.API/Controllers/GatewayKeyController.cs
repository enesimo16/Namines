using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Namines.Core.Analysis;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;


using Namines.Core.Interfaces;
using Namines.Core.Security;
using Namines.BuildingBlocks.Security;
using Namines.Ground.Abstractions;

namespace Namines.API.Controllers;

/// <param name="CanExecuteSql">
/// Ham SQL (<c>/query</c>) yetkisi. <b>Tablo izinlerini atlar</b>, o yüzden
/// <see cref="CanWrite"/>'dan ayrı ve varsayılanı kapalı.
/// </param>
/// <param name="CanExport">
/// Toplu dışa aktarım (<c>/export</c>) yetkisi. <b>Okuma yetkisinden ayrı</b>
/// (F-08): sayfa sayfa okumak ile tablonun tamamını dosya olarak indirmek aynı
/// yetki değil. Varsayılanı kapalı.
/// </param>
public sealed record CreateGatewayKeyRequest(
    string Name,
    bool CanWrite = false,
    DateTime? ExpiresAt = null,
    string? AllowedOrigins = null,
    string? AllowedIps = null,
    int? RateLimitPerMinute = null,
    bool CanExecuteSql = false,
    bool CanExport = false);

/// <param name="CanExport">
/// Bu tablonun toplu indirilmesine izin var mı. <c>CanRead</c> ile birlikte
/// gerekli — "ekranda göster ama indirilmesin" demek mümkün olsun diye ayrı.
/// </param>
public sealed record SetTablePermissionRequest(
    string TableName, bool CanRead, bool CanWrite, string? MaskedColumns = null,
    bool CanExport = false);

/// <summary>
/// Gateway API anahtarları ve tablo izinleri (08 §4.3).
///
/// Bu uçlar oturum (JWT) ile korunur, API anahtarıyla DEĞİL: bir anahtarın kendi
/// yetkisini genişletebilmesi ya da yeni anahtar üretebilmesi, anahtar
/// sınırlamasının tamamını anlamsız kılardı.
/// </summary>
[Authorize]
[ApiController]
[Route("api/gateway/keys")]
public class GatewayKeyController : ControllerBase
{
    private readonly IGatewayKeyService _keys;
    private readonly IGatewayAuditLog _audit;
    private readonly IDbHostAccessPolicy _hostPolicy;
    private readonly IDbIntrospectionService _introspection;
    private readonly IGroundModule? _ground;
    private readonly IProjectConnectionSecretProvider _secretProvider;

    private readonly ITenantProjectAuthority _authority;
    private readonly IStudioProjects _projects;

    public GatewayKeyController(
        IGatewayKeyService keys,
        IGatewayAuditLog audit,
        IDbHostAccessPolicy hostPolicy,
        IDbIntrospectionService introspection,
        IProjectConnectionSecretProvider secretProvider,
        ITenantProjectAuthority authority,
        IStudioProjects projects,
        IGroundModule? ground = null)
    {
        _keys = keys;
        _audit = audit;
        _authority = authority;
        _projects = projects;
        _hostPolicy = hostPolicy;
        _introspection = introspection;
        _ground = ground;
        _secretProvider = secretProvider;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>
    /// Anahtar üretmek ve tablo açmak yönetim işidir — 05 §6'ya göre Admin ve üstü.
    /// Editor yetkisi şema yazmaya yeter ama bir tabloyu internete açmaya yetmemeli.
    /// </summary>
    private Task<bool> CanManageAsync(string projectId, string userId) =>
        _keys.CanManageAsync(userId, projectId);

    /// <summary>
    /// Namines Desk v2 §E4.2 / 34-SENDEN-BEKLENENLER.md madde 13 — SQL konsolunu
    /// AÇMAK, Admin'den de daha dar bir yetki: yalnızca Owner. Bir Admin'in bir
    /// tabloyu Gateway'e açabilmesi (<see cref="CanManageAsync"/>) ile bir
    /// projenin veritabanına ham SELECT çalıştırılabilmesini AÇMAK aynı ağırlıkta
    /// değil — ikincisi yalnızca faturalama/org silme yetkisi olan role bırakıldı.
    /// </summary>
    private async Task<bool> IsOwnerAsync(string projectId, string userId) =>
        await _authority.GetRoleAsync(projectId, userId) == OrgRole.Owner;

    [HttpPost("{projectId}")]
    public async Task<IActionResult> Create(string projectId, [FromBody] CreateGatewayKeyRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var outcome = await _keys.CreateAsync(userId, projectId, new CreateKeyCommand(
            request.Name, request.CanWrite, request.CanExecuteSql, request.CanExport, request.ExpiresAt,
            request.AllowedOrigins, request.AllowedIps, request.RateLimitPerMinute), ct);
        if (!outcome.IsOk) return Fail(outcome);

        // Ham anahtar SADECE burada döner. Kayıtta yalnızca özeti var; bu yanıt
        // kaybolursa anahtar geri getirilemez, yenisi üretilir.
        var entity = outcome.Value!;
        return Ok(new
        {
            entity.Id,
            entity.Name,
            entity.Prefix,
            entity.CanWrite,
            entity.CanExecuteSql,
            entity.CanExport,
            entity.ExpiresAt,
            entity.AllowedOrigins,
            entity.AllowedIps,
            entity.RateLimitPerMinute,
            key = entity.Key,
            warning = "This is the only time the key is shown. Store it now.",
        });
    }

    [HttpGet("{projectId}")]
    public async Task<IActionResult> List(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var outcome = await _keys.ListAsync(userId, projectId, ct);
        return outcome.IsOk ? Ok(outcome.Value) : Fail(outcome);
    }

    [HttpGet("{projectId}/audit")]
    public async Task<IActionResult> AuditTrail(
        string projectId,
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null,
        [FromQuery] IReadOnlyList<GatewayWriteKind>? kinds = null,
        [FromQuery] string? tableName = null, [FromQuery] bool? succeeded = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        // 404 yalnızca proje gerçekten görünmezse; görüyor ama Admin değilse 403 + açık sebep.
        var outcome = await _audit.TrailAsync(userId, projectId, new AuditQuery(from, to, kinds, tableName, succeeded, page, pageSize), ct);
        if (!outcome.IsOk) return Fail(outcome);

        var trail = outcome.Value!;
        return Ok(new
        {
            totalCount = trail.TotalCount,
            page,
            pageSize,
            entries = trail.Entries,
        });
    }

    [HttpGet("/api/gateway/analytics/{projectId}")]
    public async Task<IActionResult> Analytics(
        string projectId, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null,
        [FromQuery] string bucket = "day", CancellationToken ct = default)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var effectiveTo = to ?? DateTime.UtcNow;
        var effectiveFrom = from ?? effectiveTo.AddDays(-7);
        var effectiveBucket = bucket == "hour" ? "hour" : "day";

        var analytics = await _audit.AnalyticsAsync(userId, projectId, effectiveFrom, effectiveTo, effectiveBucket, ct);
        if (!analytics.IsOk) return Fail(analytics);
        var result = analytics.Value!;

        return Ok(new
        {
            from = effectiveFrom,
            to = effectiveTo,
            bucket = effectiveBucket,
            buckets = result.Buckets.Select(b => new
            {
                bucketStart = b.BucketStart,
                create = b.Create, update = b.Update, delete = b.Delete,
                import = b.Import, rpc = b.Rpc, sql = b.Sql,
            }),
            totalWrites = result.TotalWrites,
            successRate = result.SuccessRate,
            totalAffectedRows = result.TotalAffectedRows,
            topTables = result.TopTables.Select(t => new { tableName = t.TableName, count = t.Count }),
            sourceBreakdown = new { human = result.HumanCount, application = result.ApplicationCount },
            schemaVersionCount = result.SchemaVersionCount,
        });
    }

    [HttpDelete("{projectId}/{keyId}")]
    public async Task<IActionResult> Revoke(string projectId, string keyId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var outcome = await _keys.RevokeAsync(userId, projectId, keyId, ct);
        return outcome.IsOk ? NoContent() : Fail(outcome);
    }

    private IActionResult Fail<T>(ModuleOutcome<T> outcome) => outcome.Kind switch
    {
        ModuleOutcomeKind.NotFound => NotFound(new { error = outcome.Error }),
        ModuleOutcomeKind.Invalid => BadRequest(new { error = outcome.Error }),
        ModuleOutcomeKind.Forbidden => StatusCode(403, new { error = outcome.Error }),
        ModuleOutcomeKind.Conflict => Conflict(new { error = outcome.Error }),
        _ => StatusCode(500),
    };

    // ── Tablo izinleri ───────────────────────────────────────────────────────

    [HttpGet("{projectId}/tables")]
    public async Task<IActionResult> ListTablePermissions(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var outcome = await _keys.ListPermissionsAsync(userId, projectId, ct);
        return outcome.IsOk ? Ok(outcome.Value) : Fail(outcome);
    }

    [HttpPut("{projectId}/tables")]
    public async Task<IActionResult> SetTablePermission(string projectId, [FromBody] SetTablePermissionRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var outcome = await _keys.SetPermissionAsync(userId, projectId, new PermissionCommand(
            request.TableName, request.CanRead, request.CanWrite, request.CanExport, request.MaskedColumns), ct);
        if (!outcome.IsOk) return Fail(outcome);
        return outcome.Value is null ? NoContent() : Ok(outcome.Value);
    }

    // ── Namines Desk: projeye bağlı CANLI veritabanı bağlantısı ──────────────
    //
    // Desk barındırılan bir panel. Bağlantı saklanmasaydı tarayıcının her istekte
    // veritabanı parolasını göndermesi gerekirdi — parola istemcide yaşardı.
    // Burada şifreli olarak sunucuda tutuluyor (bkz. IConnectionSecretProtector),
    // Gateway istek anında çözüyor, tarayıcı hiç görmüyor.

    /// <param name="ConnectionString">Düz metin — YALNIZCA burada, TLS üstünde, bir kez.</param>
    public sealed record SetProjectConnectionRequest(string ConnectionString, string DbType);

    [HttpPut("project/{projectId}/connection")]
    public async Task<IActionResult> SetProjectConnection(
        string projectId, [FromBody] SetProjectConnectionRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await CanManageAsync(projectId, userId))
            return NotFound(new { error = "Project not found." });

        if (string.IsNullOrWhiteSpace(request.ConnectionString) || string.IsNullOrWhiteSpace(request.DbType))
            return BadRequest(new { error = "A connection string and engine type are required." });

        if (!await _projects.ExistsAsync(projectId, ct)) return NotFound(new { error = "Project not found." });

        // SSRF: kaydetmeden ÖNCE. Reddedilecek bir hedefi şifreleyip saklamak,
        // sonra her istekte reddetmek; hatayı kullanıcıdan bir adım uzaklaştırırdı.
        // Tek host değil, TÜM adaylar doğrulanıyor (bkz. FindDisallowedHost
        // class yorumu — yinelenen anahtar/çoklu-host allowlist atlatması).
        if (DbIntrospectionService.FindDisallowedHost(_hostPolicy, request.ConnectionString, request.DbType) is { } denyReason)
            return BadRequest(new { error = denyReason });

        // Namines Desk (02-PROJECTS.md §3 kabul kriteri 4): yanlış bir bağlantı
        // dizesi KAYDEDİLMEZ, sebep gösterilir. Kaydedip ilk veri ekranında
        // patlamasını beklemek yerine, kaydetmeden ÖNCE gerçekten bağlanılıp
        // şema okunabildiği doğrulanır — aynı zamanda "bağlı" rozetinin
        // arkasında gerçekten okunabilir bir şema olduğunu garantiler.
        try
        {
            await _introspection.IntrospectAsync(request.ConnectionString, request.DbType, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not allowed"))
        {
            return BadRequest(new { error = "Connection target is not allowed (private or reserved address)." });
        }
        catch (Exception ex)
        {
            // Ham sürücü mesajı (ex.Message) İSTEMCİYE ASLA döndürülmez: Npgsql/SqlClient
            // gibi sürücüler bağlantı hatalarında hedef host/port'u, hatta bazen bozuk
            // bağlantı dizesinin bir kısmını mesaja gömer — bu uç dış (Admin/Owner
            // olmayan bir saldırgan için bile) rastgele hostlara karşı bir bağlantı
            // kâşifine dönerdi. GatewayController'ın kendi genel `catch(Exception)`
            // bloğu da aynı sebeple ex.Message'ı hiç yazdırmıyor; burası ondan sapıyordu.
            //
            // Yine de kapsam dışı KALMASIN diye (02-PROJECTS.md §4: "sunucunun HAM
            // mesajı" değil ama "bir hata oluştu" da değil) birkaç GÜVENLİ kategoriye
            // ayrıştırılıyor — sürücü metninin kendisi asla geri yansıtılmadan.
            return BadRequest(new { error = DbConnectionFailure.Classify(ex) });
        }

        try
        {
            await _secretProvider.SetConnectionAsync(projectId, request.ConnectionString, request.DbType, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict.Result();
        }

        // Bağlantı dizesi ASLA geri döndürülmez — maskelenmiş hâli bile değil.
        return Ok(new { projectId, dbType = request.DbType, connected = true });
    }


    [HttpDelete("project/{projectId}/connection")]
    public async Task<IActionResult> ClearProjectConnection(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await CanManageAsync(projectId, userId))
            return NotFound(new { error = "Project not found." });

        if (!await _projects.ExistsAsync(projectId, ct)) return NotFound(new { error = "Project not found." });

        // Namines Ground'un YÖNETTİĞİ bir veritabanının bağlantısı buradan
        // koparılamaz.
        //
        // <b>İki ayrı sebep, ikisi de tek başına yeterli:</b>
        // (1) Kaynak silinmez, yalnızca görünmez olur — sunucuda (Neon'da ise
        //     faturalanarak) durmaya devam eder ve Ground kaydı hâlâ "Active"
        //     der. Kimsenin ulaşamadığı ama yaşayan bir kaynak kalır.
        // (2) Ground'un silme akışındaki iki güvenlik önlemini atlar: proje
        //     adını elle yazma onayı ve 7 günlük geri alma penceresi. Yıkıcı
        //     bir işlemin daha zayıf bir kapıdan yapılabilmesi, güçlü kapıyı
        //     anlamsız kılar.
        var managed = _ground is not null ? await _ground.Databases.GetByProjectIdAsync(projectId, ct) : null;
        // Ground modulu uzerinden sorgulanir (dogrudan GroundDatabases erisimi yok).

        if (managed is not null &&
            managed.Status is GroundStatus.Provisioning or GroundStatus.Active or GroundStatus.PendingDelete or GroundStatus.Deleting)
        {
            return BadRequest(new
            {
                error = "This project's database is managed by Namines Ground. Remove it from the " +
                        "Hosting screen instead — that path deletes the actual database and gives " +
                        "you a recovery window.",
            });
        }

        try
        {
            await _secretProvider.ClearConnectionAsync(projectId, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict.Result();
        }
        return Ok(new { projectId, connected = false });
    }

    public sealed record SetDeskSqlEnabledRequest(bool Enabled);

    /// <summary>
    /// Namines Desk v2 §E4.2 — SQL konsolunu bir proje için açar/kapatır.
    /// <b>Owner-only</b> (bkz. <see cref="IsOwnerAsync"/>'ın sınıf yorumu) —
    /// bu depoda Admin'den de dar bir yetki gerektiren ilk uç.
    /// </summary>
    [HttpPut("project/{projectId}/desk-sql")]
    public async Task<IActionResult> SetDeskSqlEnabled(
        string projectId, [FromBody] SetDeskSqlEnabledRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        // 404: proje hiç görünmüyor (Viewer bile değilsin) — var olan projectId'leri
        // sızdırmama gerekçesi diğer uçlarla aynı. Görüyor ama Owner değilsen 403.
        var role = await _authority.GetRoleAsync(projectId, userId, ct);
        if (role is null) return NotFound(new { error = "Project not found." });
        if (role != OrgRole.Owner)
            return StatusCode(403, new { error = "Only the project owner can enable or disable the SQL console." });

        if (!await _projects.ExistsAsync(projectId, ct)) return NotFound(new { error = "Project not found." });

        try
        {
            await _secretProvider.SetDeskSqlEnabledAsync(projectId, request.Enabled, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict.Result();
        }

        return Ok(new { projectId, allowDeskSql = request.Enabled });
    }
}
