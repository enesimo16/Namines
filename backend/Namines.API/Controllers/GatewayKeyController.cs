using System;
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
using Namines.Core.Security;
using Namines.Core.Interfaces;

namespace Namines.API.Controllers;

/// <param name="CanExecuteSql">
/// Ham SQL (<c>/query</c>) yetkisi. <b>Tablo izinlerini atlar</b>, o yüzden
/// <see cref="CanWrite"/>'dan ayrı ve varsayılanı kapalı.
/// </param>
public sealed record CreateGatewayKeyRequest(
    string Name,
    bool CanWrite = false,
    DateTime? ExpiresAt = null,
    string? AllowedOrigins = null,
    string? AllowedIps = null,
    int? RateLimitPerMinute = null,
    bool CanExecuteSql = false);
public sealed record SetTablePermissionRequest(
    string TableName, bool CanRead, bool CanWrite, string? MaskedColumns = null);

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
    private readonly AuthDbContext _context;
    private readonly IConnectionSecretProtector _protector;
    private readonly IDbHostAccessPolicy _hostPolicy;
    private readonly IDbIntrospectionService _introspection;

    public GatewayKeyController(
        AuthDbContext context,
        IConnectionSecretProtector protector,
        IDbHostAccessPolicy hostPolicy,
        IDbIntrospectionService introspection)
    {
        _context = context;
        _protector = protector;
        _hostPolicy = hostPolicy;
        _introspection = introspection;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>
    /// Anahtar üretmek ve tablo açmak yönetim işidir — 05 §6'ya göre Admin ve üstü.
    /// Editor yetkisi şema yazmaya yeter ama bir tabloyu internete açmaya yetmemeli.
    /// </summary>
    private async Task<bool> CanManageAsync(string projectId, string userId) =>
        await _context.CanManageMembersAsync(projectId, userId);

    /// <summary>
    /// Namines Desk v2 §E4.2 / 34-SENDEN-BEKLENENLER.md madde 13 — SQL konsolunu
    /// AÇMAK, Admin'den de daha dar bir yetki: yalnızca Owner. Bir Admin'in bir
    /// tabloyu Gateway'e açabilmesi (<see cref="CanManageAsync"/>) ile bir
    /// projenin veritabanına ham SELECT çalıştırılabilmesini AÇMAK aynı ağırlıkta
    /// değil — ikincisi yalnızca faturalama/org silme yetkisi olan role bırakıldı.
    /// </summary>
    private async Task<bool> IsOwnerAsync(string projectId, string userId) =>
        await _context.GetRoleAsync(projectId, userId) == OrgRole.Owner;

    [HttpPost("{projectId}")]
    public async Task<IActionResult> Create(string projectId, [FromBody] CreateGatewayKeyRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await CanManageAsync(projectId, userId))
            return NotFound(new { error = "Proje bulunamadı." });

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Anahtar için bir ad gerekli." });

        // Sıfır ya da negatif bir limit anahtarı tamamen kullanılamaz kılardı; bunu
        // "limit yok" saymak da yanlış olurdu, o yüzden açıkça reddediliyor.
        // Doğrulama anahtar üretilmeden ÖNCE: reddedilecek bir istek için entropi
        // harcamak ve yarı kurulmuş bir nesne bırakmak gereksiz.
        if (request.RateLimitPerMinute is <= 0)
            return BadRequest(new { error = "Rate limit must be greater than zero." });

        // İstek hakkı PLANIN tavanını aşamaz. Aksi hâlde ücretsiz bir hesap kendine
        // 100.000 rpm'lik bir anahtar üretip planı anlamsız kılardı — ve bunu
        // fark etmenin tek yolu faturaya bakmak olurdu.
        var account = await _context.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.SubscriptionStatus, u.PlanCode, u.IsDev })
            .FirstOrDefaultAsync(ct);

        var tier = PlanQuotas.Resolve(account?.SubscriptionStatus, account?.PlanCode, account?.IsDev ?? false);
        var planCeiling = PlanQuotas.For(tier).GatewayRequestsPerMinute;

        if (request.RateLimitPerMinute > planCeiling)
            return BadRequest(new
            {
                error = $"The {tier} plan allows at most {planCeiling} requests per minute per key.",
            });

        var (entity, rawKey) = GatewayAccess.CreateKey(
            projectId, request.Name.Trim(), userId, request.CanWrite, request.ExpiresAt);

        entity.CanExecuteSql = request.CanExecuteSql;
        entity.AllowedOrigins = Normalize(request.AllowedOrigins);
        entity.AllowedIps = Normalize(request.AllowedIps);
        // Belirtilmemişse planın hakkı: kullanıcıyı bir sayı uydurmaya zorlamak,
        // çoğu kişinin ya çok düşük ya çok yüksek seçmesi demek.
        entity.RateLimitPerMinute = request.RateLimitPerMinute ?? planCeiling;

        _context.GatewayApiKeys.Add(entity);
        await _context.SaveChangesAsync(ct);

        // Ham anahtar SADECE BURADA döner. Kayıtta yalnızca özeti var; bu yanıt
        // kaybolursa anahtar geri getirilemez, yenisi üretilir.
        return Ok(new
        {
            entity.Id,
            entity.Name,
            entity.Prefix,
            entity.CanWrite,
            entity.CanExecuteSql,
            entity.ExpiresAt,
            entity.AllowedOrigins,
            entity.AllowedIps,
            entity.RateLimitPerMinute,
            key = rawKey,
            warning = "This is the only time the key is shown. Store it now.",
        });
    }

    [HttpGet("{projectId}")]
    public async Task<IActionResult> List(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await CanManageAsync(projectId, userId))
            return NotFound(new { error = "Proje bulunamadı." });

        var keys = await _context.GatewayApiKeys
            .Where(k => k.ProjectId == projectId)
            .OrderByDescending(k => k.CreatedAt)
            // KeyHash bilinçli olarak dışarıda: gösterilecek bir değer değil.
            .Select(k => new
            {
                k.Id, k.Name, k.Prefix, k.CanWrite, k.CanExecuteSql,
                k.AllowedOrigins, k.AllowedIps, k.RateLimitPerMinute,
                k.CreatedAt, k.ExpiresAt, k.RevokedAt, k.LastUsedAt,
            })
            .ToListAsync(ct);

        return Ok(keys);
    }

    /// <summary>
    /// Projenin denetim kaydı (07 §5, Namines Desk D6 06-LOGS.md §4).
    ///
    /// <b>Anahtar yönetimiyle aynı yetki isteniyor (Admin ve üstü).</b> Denetim
    /// kaydı kimin neye dokunduğunu gösterir; onu okuyabilmek, projenin veri
    /// hareketlerinin tamamını görebilmek demektir ve bu bir yönetim yetkisidir.
    /// Desk bu kuralı GEVŞETMİYOR (06-LOGS.md §4 yetki notu) — Viewer/Editor bu
    /// ekranı görür ama açıklayıcı bir 403 alır, boş liste değil.
    ///
    /// Kayıt <b>yalnızca okunabilir</b> — silme ya da düzenleme ucu YOK. Silinebilen
    /// bir denetim kaydı, tam olarak lazım olduğu anda kaybolur.
    /// </summary>
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

        // 404 yalnızca proje gerçekten görünmezse (Viewer bile değilse) — hangi
        // projelerin var olduğunu sızdırmamak için (AuthorizeAsync'teki aynı
        // gerekçe). Görüyor ama Admin değilse 403 + AÇIK sebep: kabul kriteri 5
        // "yetkisiz kullanıcı açıklayıcı mesaj görür, boş liste değil".
        if (!await _context.CanViewAsync(projectId, userId, ct))
            return NotFound(new { error = "Proje bulunamadı." });
        if (!await CanManageAsync(projectId, userId))
            return StatusCode(403, new { error = "Bu bölüm için yönetici (Admin) yetkisi gerekiyor." });

        var (entries, totalCount) = await _context.AuditTrailAsync(
            projectId, from, to, kinds, tableName, succeeded, page, pageSize, ct);

        return Ok(new
        {
            totalCount,
            page,
            pageSize,
            entries = entries.Select(e => new
            {
                e.Id,
                kind = e.Kind.ToString().ToLowerInvariant(),
                e.TableName,
                e.RowKey,
                e.Columns,
                e.AffectedRows,
                e.Succeeded,
                e.ApiKeyPrefix,
                e.ActorUserId,
                e.CreatedAt,
            }),
        });
    }

    /// <summary>
    /// Namines Desk — Analytics (D7, 07-ANALYTICS.md §3). Rota, doc'un tarif
    /// ettiği <c>/api/gateway/analytics/{projectId}</c> ile birebir eşleşsin
    /// diye bu controller'ın <c>api/gateway/keys</c> önekini AÇIKÇA aşıyor
    /// (baştaki <c>/</c>) — mantıksal olarak Logs'la (bu dosyadaki `/audit`)
    /// aynı yetki/veri katmanına ait olduğu için burada duruyor, ayrı bir
    /// controller açmak gereksiz bölünme olurdu.
    ///
    /// <b>Yetki Logs ile aynı (Admin ve üstü):</b> toplamlar da projenin veri
    /// hareketinin tamamını açığa vuruyor.
    /// </summary>
    [HttpGet("/api/gateway/analytics/{projectId}")]
    public async Task<IActionResult> Analytics(
        string projectId, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null,
        [FromQuery] string bucket = "day", CancellationToken ct = default)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!await _context.CanViewAsync(projectId, userId, ct))
            return NotFound(new { error = "Proje bulunamadı." });
        if (!await CanManageAsync(projectId, userId))
            return StatusCode(403, new { error = "Bu bölüm için yönetici (Admin) yetkisi gerekiyor." });

        var effectiveTo = to ?? DateTime.UtcNow;
        var effectiveFrom = from ?? effectiveTo.AddDays(-7);
        var effectiveBucket = bucket == "hour" ? "hour" : "day";

        var result = await _context.AnalyticsAsync(projectId, effectiveFrom, effectiveTo, effectiveBucket, ct);

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
        if (!await CanManageAsync(projectId, userId))
            return NotFound(new { error = "Proje bulunamadı." });

        var key = await _context.GatewayApiKeys
            .FirstOrDefaultAsync(k => k.Id == keyId && k.ProjectId == projectId, ct);
        if (key is null) return NotFound(new { error = "Anahtar bulunamadı." });

        // Silinmez, işaretlenir: "ne zaman iptal edildi" sorusu cevapsız kalmasın.
        key.RevokedAt ??= DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>Boş/whitespace listeyi null'a indirger: "" ile null aynı anlama gelmeli.</summary>
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ── Tablo izinleri ───────────────────────────────────────────────────────

    [HttpGet("{projectId}/tables")]
    public async Task<IActionResult> ListTablePermissions(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await CanManageAsync(projectId, userId))
            return NotFound(new { error = "Proje bulunamadı." });

        var permissions = await _context.GatewayTablePermissions
            .Where(p => p.ProjectId == projectId)
            .OrderBy(p => p.TableName)
            .ToListAsync(ct);

        return Ok(permissions.Select(p => new
        {
            p.TableName, p.CanRead, p.CanWrite, p.MaskedColumns, p.UpdatedAt,
        }));
    }

    /// <summary>
    /// Bir tablonun API anahtarlarına açıklığını belirler.
    ///
    /// Kayıt yokluğu = erişim yok (08 §1). Bu yüzden hem <c>canRead</c> hem
    /// <c>canWrite</c> false verildiğinde satır SİLİNİR: "her ikisi de kapalı" ile
    /// "hiç kayıt yok" aynı anlama gelir, iki farklı temsil tutmak listeyi
    /// yanıltıcı kılardı.
    /// </summary>
    [HttpPut("{projectId}/tables")]
    public async Task<IActionResult> SetTablePermission(string projectId, [FromBody] SetTablePermissionRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await CanManageAsync(projectId, userId))
            return NotFound(new { error = "Proje bulunamadı." });

        if (string.IsNullOrWhiteSpace(request.TableName))
            return BadRequest(new { error = "Tablo adı gerekli." });

        var existing = await _context.GatewayTablePermissions
            .FirstOrDefaultAsync(p => p.ProjectId == projectId && p.TableName == request.TableName, ct);

        if (!request.CanRead && !request.CanWrite)
        {
            if (existing is not null) _context.GatewayTablePermissions.Remove(existing);
            await _context.SaveChangesAsync(ct);
            return NoContent();
        }

        if (existing is null)
        {
            existing = new GatewayTablePermission
            {
                ProjectId = projectId,
                TableName = request.TableName,
            };
            _context.GatewayTablePermissions.Add(existing);
        }

        existing.CanRead = request.CanRead;
        // Yazma okumayı ima eder: yazabilen ama okuyamayan bir istemci, yazdığını
        // doğrulayamaz ve bu neredeyse her zaman istenmeyen bir yapılandırmadır.
        if (request.CanWrite) existing.CanRead = true;
        existing.CanWrite = request.CanWrite;
        existing.MaskedColumns = Normalize(request.MaskedColumns);
        existing.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
        return Ok(new { existing.TableName, existing.CanRead, existing.CanWrite, existing.MaskedColumns });
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
            return NotFound(new { error = "Proje bulunamadı." });

        if (string.IsNullOrWhiteSpace(request.ConnectionString) || string.IsNullOrWhiteSpace(request.DbType))
            return BadRequest(new { error = "Bağlantı dizesi ve motor türü gerekli." });

        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFound(new { error = "Proje bulunamadı." });

        // SSRF: kaydetmeden ÖNCE. Reddedilecek bir hedefi şifreleyip saklamak,
        // sonra her istekte reddetmek; hatayı kullanıcıdan bir adım uzaklaştırırdı.
        var host = DbIntrospectionService.ExtractHost(request.ConnectionString, request.DbType);
        if (!_hostPolicy.IsHostAllowed(host, out var denyReason))
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
            return BadRequest(new { error = ClassifyConnectionFailure(ex) });
        }

        project.EncryptedConnectionString = _protector.Protect(request.ConnectionString);
        project.ConnectionDbType = request.DbType;
        project.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        // Bağlantı dizesi ASLA geri döndürülmez — maskelenmiş hâli bile değil.
        return Ok(new { projectId, dbType = request.DbType, connected = true });
    }

    /// <summary>
    /// Bir bağlantı denemesi neden başarısız oldu — sürücünün HAM mesajını hiç
    /// okuyucuya yansıtmadan birkaç güvenli kategoriye ayırır. Yalnızca ex.Message'ın
    /// KENDİSİNİ okur (dahili karar için), asla geri döndürmez.
    /// </summary>
    private static string ClassifyConnectionFailure(Exception ex)
    {
        var m = ex.Message;
        if (m.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("login failed", StringComparison.OrdinalIgnoreCase))
            return "Authentication failed. Check the username and password in the connection string.";

        if (m.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            ex is TimeoutException)
            return "Connection timed out. Check the host, port, and firewall rules.";

        if (m.Contains("database", StringComparison.OrdinalIgnoreCase) &&
            (m.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
             m.Contains("cannot open", StringComparison.OrdinalIgnoreCase)))
            return "The database name in the connection string was not found on the server.";

        return "Could not connect to the database. Check the connection string and network access.";
    }

    [HttpDelete("project/{projectId}/connection")]
    public async Task<IActionResult> ClearProjectConnection(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await CanManageAsync(projectId, userId))
            return NotFound(new { error = "Proje bulunamadı." });

        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFound(new { error = "Proje bulunamadı." });

        project.EncryptedConnectionString = null;
        project.ConnectionDbType = null;
        project.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
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
        var role = await _context.GetRoleAsync(projectId, userId, ct);
        if (role is null) return NotFound(new { error = "Proje bulunamadı." });
        if (role != OrgRole.Owner)
            return StatusCode(403, new { error = "Yalnızca proje sahibi (Owner) SQL konsolunu açıp kapatabilir." });

        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFound(new { error = "Proje bulunamadı." });

        project.AllowDeskSql = request.Enabled;
        project.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return Ok(new { projectId, allowDeskSql = project.AllowDeskSql });
    }
}
