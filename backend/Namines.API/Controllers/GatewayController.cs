using System;
using Microsoft.Extensions.Logging;
using Namines.Infrastructure.AI;
using System.Security.Claims;
using System.Data.Common;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Namines.BuildingBlocks.Security;
using Namines.BuildingBlocks.Application;
using Microsoft.AspNetCore.RateLimiting;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System.Text;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Security;
using System.Diagnostics;
using Namines.Infrastructure.Services;

namespace Namines.API.Controllers;

public sealed record GatewayListRequest(
    string ConnectionString, string DbType, string TableName,
    int Page = 1, int PageSize = 25,
    string? OrderByColumn = null, bool IncludeTotalCount = true,
    GatewaySortDirection SortDirection = GatewaySortDirection.Asc,
    IReadOnlyList<GatewayFilter>? Filters = null,
    IReadOnlyList<GatewayFilterGroup>? OrGroups = null,
    IReadOnlyList<string>? Select = null,
    IReadOnlyList<GatewayExpand>? Expand = null,
    string? ProjectId = null);

public sealed record GatewayExportRequest(
    string ConnectionString, string DbType, string TableName,
    string Format = "csv",
    int MaxRows = 10_000,
    string? OrderByColumn = null,
    GatewaySortDirection SortDirection = GatewaySortDirection.Asc,
    IReadOnlyList<GatewayFilter>? Filters = null,
    IReadOnlyList<GatewayFilterGroup>? OrGroups = null,
    IReadOnlyList<string>? Select = null,
    string? ProjectId = null);
public sealed record GatewayDetailRequest(string ConnectionString, string DbType, string TableName, string PkColumn, string PkValue, string? ProjectId = null);

public sealed record GatewayCreateRequest(
    string ConnectionString, string DbType, string TableName,
    Dictionary<string, string?> Values, string? ProjectId = null);

public sealed record GatewayUpdateRequest(
    string ConnectionString, string DbType, string TableName,
    string PkColumn, string PkValue,
    Dictionary<string, string?> Values, string? ProjectId = null);

public sealed record GatewayDeleteRequest(
    string ConnectionString, string DbType, string TableName,
    string PkColumn, string PkValue, string? ProjectId = null);

/// <summary>Namines Desk v2 §E4.1 — toplu silme.</summary>
public sealed record GatewayBulkDeleteRequest(
    string ConnectionString, string DbType, string TableName,
    string PkColumn, List<string> PkValues, string? ProjectId = null);

/// <summary>
/// Namines Desk v2 §E4.2 — SQL konsolu. Bilinçli olarak <c>ConnectionString</c>/
/// <c>DbType</c> ALMAZ: bu uç yalnızca oturum + <c>ProjectId</c> yoluyla çalışır,
/// API anahtarı yolu hiç yok (aşağıdaki <c>DeskSql</c> action'ının yorumuna bkz.).
/// </summary>
public sealed record GatewayDeskSqlRequest(string ProjectId, string Sql, int MaxRows = 500);

/// <param name="Name">Sorgunun adı. Aynı ad ikinci kez gelirse GÜNCELLEME olur.</param>
public sealed record SaveQueryRequest(string ProjectId, string Name, string Sql);

/// <param name="Rows">
/// Yazılacak satırlar. Hepsi AYNI kolonları taşımalı — satır başına farklı kolon
/// kümesine izin vermek, eksik kolonu olan satıra sessizce varsayılan yazardı.
/// </param>
public sealed record GatewayImportRequest(
    string ConnectionString, string DbType, string TableName,
    List<Dictionary<string, string?>> Rows);

public sealed record GatewayRpcRequest(
    string ConnectionString, string DbType, string Function,
    List<string?>? Arguments = null);

/// <param name="ReadOnly">
/// Varsayılan true. Yazma niyeti AÇIKÇA belirtilmeli: bir raporlama sorgusunun
/// yanlışlıkla veri değiştirmesi, sessizce olan ve geri alınamayan bir hatadır.
/// </param>
public sealed record GatewayQueryRequest(
    string ConnectionString, string DbType, string Sql,
    bool ReadOnly = true, string? ProjectId = null);

/// <param name="Execute">
/// Üretilen SQL çalıştırılsın mı? Varsayılan <b>false</b>: doğal dilden üretilen
/// bir sorguyu görmeden çalıştırmak, "geçen ayki siparişleri göster" diyen birinin
/// isteğinin başka bir şeye dönüşme ihtimalini kabul etmek olur. true verilse bile
/// yalnızca OKUMA sorguları çalışır.
/// </param>
public sealed record GatewayNlQueryRequest(
    string ConnectionString, string DbType, string Question,
    bool Execute = false, string? ProjectId = null);

/// <summary>
/// G14 — Minimal Gateway. Şemadan otomatik salt-okunur REST (liste + detay).
/// DatabaseExecutorController/DbIntrospectController ile aynı güvenlik modeli:
/// login zorunlu, rate-limit'li, connection string hiçbir yerde saklanmaz.
/// </summary>
// [Authorize] DEĞİL: iki kimlik yolu var (oturum ve API anahtarı) ve ikincisi
// JWT taşımaz. Kimlik doğrulama her uçta AuthorizeAsync ile YAPILIR — anonim
// erişim yoktur, yalnızca kontrol middleware'den controller'a taşınmıştır.
[AllowAnonymous]
// "sensitive" DEĞİL: o politika dakikada 5 istekle sınırlı ve Gateway'i normal bir
// uygulama için kullanılamaz kılıyordu. Asıl sınır anahtar başına uygulanıyor
// (GatewayRateLimiter, 08 §5); buradaki yalnızca son çare. Bkz. Program.cs.
[EnableRateLimiting("gateway")]
[ApiController]
[Route("api/gateway")]
public class GatewayController : ControllerBase
{
    private readonly IGatewayService _gateway;
    private readonly IGatewayRequestGate _gate;
    private readonly IGatewayAuditLog _audit;
    private readonly IUsageMeter _usage;
    private readonly IConfiguration _configuration;
    private readonly GroqAIService _groq;
    private readonly ILogger<GatewayController> _logger;
    private readonly AiQuotaService _quota;
    private readonly IProjectConnectionSecretProvider _secrets;
    private readonly IStudioApplicationService _studio;
    private readonly IDbIntrospectionService _introspection;

    /// <summary>
    /// Bir doğal dil çevirisinin tahmini token maliyeti.
    ///
    /// <c>AIQuotaMiddleware</c>'deki diğer özelliklerle aynı büyüklük sırasında
    /// (2000-6000). Şema istemin içine giriyor, yani maliyet tablo sayısıyla
    /// birlikte artıyor; sabit bir tahmin bunu tam yakalamaz ama sayaç birimini
    /// doğru tutmak, birimin kendisini yanlış almaktan çok daha önemli.
    /// </summary>
    private const int NlQueryTokenEstimate = 2500;

    private readonly ITenantProjectAuthority _authority;
    private readonly IStudioProjects _projects;
    private readonly IStudioBranches _branches;

    public GatewayController(
        IGatewayService gateway, IGatewayRequestGate gate, IGatewayAuditLog audit, IUsageMeter usage, IConfiguration configuration,
        GroqAIService groq, ILogger<GatewayController> logger, AiQuotaService quota,
        IProjectConnectionSecretProvider secrets, IStudioApplicationService studio, IDbIntrospectionService introspection,
        ISqlWorkbenchService workbench,
        ITenantProjectAuthority authority,
        IStudioProjects projects,
        IStudioBranches branches)
    {
        _authority = authority;
        _projects = projects;
        _branches = branches;
        _workbench = workbench;
        _gateway = gateway;
        _gate = gate;
        _audit = audit;
        _usage = usage;
        _configuration = configuration;
        _groq = groq;
        _logger = logger;
        _quota = quota;
        _secrets = secrets;
        _studio = studio;
        _introspection = introspection;
    }

    private readonly ISqlWorkbenchService _workbench;

    /// <summary>
    /// İstemci adresine güvenilebilir mi?
    ///
    /// Program.cs, <c>ForwardedHeaders:KnownNetworks</c> tanımlı DEĞİLSE
    /// <c>X-Forwarded-For</c>'ı doğrulamadan kabul ediyor (PaaS'te proxy IP'si
    /// dinamik olabildiği için bilinçli bir taviz). O hâlde istemci kendi adresini
    /// istediği gibi yazabilir; IP kısıtı böyle bir ortamda hiçbir şey doğrulamaz.
    /// Aynı yapılandırma anahtarını okuyoruz ki iki yer ayrışmasın.
    /// </summary>
    private bool ClientAddressIsTrustworthy =>
        _configuration.GetSection("ForwardedHeaders:KnownNetworks").GetChildren().Any();

    /// <summary>API anahtarının taşındığı başlık.</summary>
    public const string ApiKeyHeader = "X-Namines-Key";

    // Frontend enum'ları JSON'a string yazıyor; dönüştürücü olmadan
    // Deserialize<DatabaseSchema> patlar (BranchController ile aynı gerekçe).
    private static readonly JsonSerializerOptions SchemaJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// İki kimlik yolu var ve yetkileri farklı: oturum (Studio, tablo izni yok) ve API anahtarı (hiçbir tablo
    /// varsayılan açık değil). Karar Gateway modülünde (<see cref="IGatewayRequestGate"/>); burada yalnızca istek
    /// bilgisi toplanır ve karar HTTP'ye çevrilir.
    /// </summary>
    private GatewayCaller Caller()
    {
        var hasHeader = Request.Headers.TryGetValue(ApiKeyHeader, out var header);
        return new GatewayCaller(
            hasHeader ? header.ToString() : null,
            User?.Identity?.IsAuthenticated == true,
            User?.FindFirstValue(ClaimTypes.NameIdentifier),
            Request.Headers.Origin.ToString(),
            HttpContext.Connection.RemoteIpAddress,
            ClientAddressIsTrustworthy);
    }

    private IActionResult Deny(GatewayDecision decision) => decision.Denial switch
    {
        GatewayDenial.Unauthenticated => Unauthorized(),
        GatewayDenial.InvalidCredentials => Unauthorized(new { message = decision.Message }),
        GatewayDenial.NotFound => NotFound(new { message = decision.Message }),
        GatewayDenial.RateLimited => StatusCode(429, new { message = decision.Message }),
        GatewayDenial.BadRequest => BadRequest(new { message = decision.Message }),
        _ => StatusCode(403, new { message = decision.Message }),
    };

    private async Task<(bool Allowed, IActionResult? Failure, GatewayApiKey? Key)> AuthorizeAsync(
        string tableName, bool forWrite, CancellationToken ct, IReadOnlyList<GatewayExpand>? expands = null, bool export = false)
    {
        var decision = await _gate.AuthorizeTableAsync(Caller(), tableName, forWrite, expands, export, ct);
        return decision.Allowed ? (true, null, decision.Key) : (false, Deny(decision), null);
    }

    private Task<IReadOnlyList<GatewayRow>> MaskAsync(
        GatewayApiKey? key, string tableName, IReadOnlyList<GatewayRow> rows, CancellationToken ct) =>
        _gate.MaskAsync(key, User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, tableName, rows, ct);


    private Task<IReadOnlyList<GatewayRow>> MaskExpandedAsync(
        GatewayApiKey? key, IReadOnlyList<GatewayExpand>? expands, IReadOnlyList<GatewayRow> rows, CancellationToken ct) =>
        _gate.MaskExpandedAsync(key, User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, expands, rows, ct);


    private async Task<(bool Allowed, string? UserId, IActionResult? Failure)> ResolveSessionAccessAsync(
        string projectId, bool forWrite, CancellationToken ct)
    {
        var caller = Caller();
        var decision = await _gate.AuthorizeSessionProjectAsync(caller, projectId, forWrite, ct);
        return decision.Allowed ? (true, caller.SessionUserId, null) : (false, null, Deny(decision));
    }

    /// <summary>
    /// Denetim kaydına yazılacak proje kimliği — yalnızca DOĞRULANMIŞ olanı.
    ///
    /// <b>Bulunan açık (docs/last-review H-5):</b> yazma uçları
    /// <c>projectId: apiKey?.ProjectId ?? request.ProjectId</c> yazıyordu.
    /// <see cref="ResolveConnectionAsync"/> ise istek KENDİ bağlantı dizesini
    /// taşıyorsa <b>hemen dönüyor</b> — proje erişim kontrolü (
    /// <c>ResolveSessionAccessAsync</c>) o daldan hiç çalışmıyor. Yani kendi
    /// veritabanına yazan bir çağıran, `ProjectId` alanına BAŞKASININ proje
    /// kimliğini yazarak o projenin Logs/Analytics ekranını kendi uydurduğu
    /// satırlarla doldurabiliyordu: denetim kaydı sahibinin güvenemediği bir kayıt
    /// hâline geliyordu.
    ///
    /// Kural: proje kimliği ya ANAHTARDAN gelir (anahtarın kendi projesi, taklit
    /// edilemez) ya da isteğin bağlantı dizesi YOKKEN gelir — çünkü yalnızca o
    /// dalda erişim gerçekten doğrulandı. Kendi dizesini gönderen çağrı (Studio'nun
    /// canvas çağrıları) <c>null</c> alacak ve <c>RecordAuditAsync</c> "session"
    /// placeholder'ına düşecek — Desk'ten ÖNCEKİ davranışın ta kendisi.
    /// </summary>
    private static string? AuditProjectId(
        GatewayApiKey? apiKey, string? requestConnectionString, string? requestProjectId)
    {
        if (apiKey is not null) return apiKey.ProjectId;

        return string.IsNullOrWhiteSpace(requestConnectionString) ? requestProjectId : null;
    }

    private async Task<(string? Connection, string DbType, IActionResult? Failure)> ResolveConnectionAsync(
        string? requested, string? requestedDbType, string? requestedProjectId,
        GatewayApiKey? apiKey, bool forWrite, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return (requested, requestedDbType ?? string.Empty, null);

        string? projectId = apiKey?.ProjectId;

        if (projectId is null)
        {
            if (string.IsNullOrWhiteSpace(requestedProjectId))
                return (null, string.Empty, null);

            var (allowed, _, failure) = await ResolveSessionAccessAsync(requestedProjectId, forWrite, ct);
            if (!allowed) return (null, string.Empty, failure);

            projectId = requestedProjectId;
        }

        var stored = await _secrets.GetStoredConnectionAsync(projectId, ct);
        if (stored is null)
            return (null, string.Empty, null);

        // Çözülemezse (anahtar döndü / kayıt bozuldu) İSTİSNA fırlar ve üstteki
        // catch 500 döner. Bilinçli: sessizce "bağlantı yok" demek, kullanıcıyı
        // yanlış yere (bağlantıyı yeniden gir) yönlendirirdi.
        //
        // Motor türü İSTEKTEKİNE DEĞİL saklanana güvenir: istemci boş ya da
        // yanlış gönderebilir, ama bağlantının hangi motora ait olduğunu
        // kaydeden taraf sunucudur.
        var dbType = !string.IsNullOrWhiteSpace(stored.ConnectionDbType)
            ? stored.ConnectionDbType
            : (requestedDbType ?? string.Empty);

        return (stored.ConnectionString, dbType, null);
    }

    [HttpPost("list")]
    public async Task<IActionResult> List([FromBody] GatewayListRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TableName))
            return BadRequest(new { message = "Table name is required." });

        // expand ayrı tablo okur, ayrı izin ister (docs/last-review H-2): karar kapıda, ana tabloyla birlikte.
        var (allowed, failure, apiKey) = await AuthorizeAsync(request.TableName, forWrite: false, cancellationToken, expands: request.Expand);
        if (!allowed) return failure!;

        var (connectionString, effectiveDbType, connectionFailure) = await ResolveConnectionAsync(
            request.ConnectionString, request.DbType, request.ProjectId, apiKey, forWrite: false, cancellationToken);
        if (connectionFailure is not null) return connectionFailure;
        if (string.IsNullOrWhiteSpace(connectionString))
            return BadRequest(new { message = "No connection: send a connection string, or store one for the project first." });

        try
        {
            var result = await _gateway.ListAsync(
                connectionString, effectiveDbType, request.TableName,
                request.Page, request.PageSize, request.OrderByColumn,
                request.IncludeTotalCount, request.SortDirection, request.Filters,
                request.OrGroups, request.Select, request.Expand, cancellationToken);

            var rows = await MaskAsync(apiKey, request.TableName, result.Rows, cancellationToken);
            rows = await MaskExpandedAsync(apiKey, request.Expand, rows, cancellationToken);
            return Ok(new GatewayListResult(rows, result.Page, result.PageSize, result.TotalCount));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not allowed"))
        {
            return BadRequest(new { message = "Connection target is not allowed (private or reserved address)." });
        }
        catch (Exception)
        {
            return StatusCode(500, new { message = "Could not connect or query the database. Check credentials and network access." });
        }
    }

    [HttpPost("detail")]
    public async Task<IActionResult> Detail([FromBody] GatewayDetailRequest request, CancellationToken cancellationToken)
    {
        // Baglanti ARTIK zorunlu degil (anahtardan cozulebilir) ama anahtar
        // kolonu/degeri hala zorunlu: onlarsiz hangi satirin hedeflendigi belirsiz.
        if (string.IsNullOrWhiteSpace(request.TableName) ||
            string.IsNullOrWhiteSpace(request.PkColumn) ||
            string.IsNullOrWhiteSpace(request.PkValue))
            return BadRequest(new { message = "Table name, PK column and PK value are required." });

        var (allowed, failure, apiKey) = await AuthorizeAsync(request.TableName, forWrite: false, cancellationToken);
        if (!allowed) return failure!;

        // Baglanti istekte yoksa API anahtarindan ya da oturum + ProjectId'den cozulur (Namines Desk).
        var (connectionString, effectiveDbType, connectionFailure) = await ResolveConnectionAsync(
            request.ConnectionString, request.DbType, request.ProjectId, apiKey, forWrite: false, cancellationToken);
        if (connectionFailure is not null) return connectionFailure;
        if (string.IsNullOrWhiteSpace(connectionString))
            return BadRequest(new { message = "No connection: send a connection string, or store one for the project first." });

        try
        {
            var row = await _gateway.DetailAsync(
                connectionString, effectiveDbType, request.TableName,
                request.PkColumn, request.PkValue, cancellationToken);

            if (row is null) return NotFound(new { message = "No row found for the given key." });

            var masked = await MaskAsync(apiKey, request.TableName, new[] { row }, cancellationToken);
            return Ok(masked[0]);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not allowed"))
        {
            return BadRequest(new { message = "Connection target is not allowed (private or reserved address)." });
        }
        catch (Exception)
        {
            return StatusCode(500, new { message = "Could not connect or query the database. Check credentials and network access." });
        }
    }

    // ── Yazma uçları (Faz B/08) ──────────────────────────────────────────────
    //
    // Bunlar kullanıcının KENDİ canlı veritabanına yazar. Güven modeli liste/detay
    // ile aynıdır (çağıran zaten bağlantı dizesine sahip), dolayısıyla buradaki asıl
    // risk yetkisiz erişim değil, BİZİM ürettiğimiz SQL'in yanlış olması. Servis
    // katmanı buna karşı üç şey yapıyor: koşulsuz UPDATE/DELETE üretilemez, her yazma
    // işlem içinde çalışır, ve beklenenden fazla satır etkilenirse geri alınır.

    [HttpPost("create")]
    public async Task<IActionResult> Create([FromBody] GatewayCreateRequest request, CancellationToken cancellationToken)
    {
        
        if (request.Values is null || request.Values.Count == 0)
            return BadRequest(new { message = "At least one column value is required." });

        var (allowed, failure, apiKey) = await AuthorizeAsync(request.TableName, forWrite: true, cancellationToken);
        if (!allowed) return failure!;

        // Baglanti istekte yoksa API anahtarindan ya da oturum + ProjectId'den cozulur (Namines Desk).
        var (connectionString, effectiveDbType, connectionFailure) = await ResolveConnectionAsync(
            request.ConnectionString, request.DbType, request.ProjectId, apiKey, forWrite: true, cancellationToken);
        if (connectionFailure is not null) return connectionFailure;
        if (string.IsNullOrWhiteSpace(connectionString))
            return BadRequest(new { message = "No connection: send a connection string, or store one for the project first." });

        return await AuditedAsync(apiKey, GatewayWriteKind.Create, request.TableName, null,
            request.Values.Keys, cancellationToken, async () =>
            {
                var result = await _gateway.CreateAsync(
                    connectionString, effectiveDbType, request.TableName,
                    request.Values, cancellationToken);
                return (Ok(result), result.AffectedRows);
            }, projectId: AuditProjectId(apiKey, request.ConnectionString, request.ProjectId));
    }

    [HttpPost("update")]
    public async Task<IActionResult> Update([FromBody] GatewayUpdateRequest request, CancellationToken cancellationToken)
    {
        // Baglanti ARTIK zorunlu degil (anahtardan cozulebilir) ama anahtar
        // kolonu/degeri hala zorunlu: onlarsiz hangi satirin hedeflendigi belirsiz.
        if (string.IsNullOrWhiteSpace(request.TableName) ||
            string.IsNullOrWhiteSpace(request.PkColumn) ||
            string.IsNullOrWhiteSpace(request.PkValue))
            return BadRequest(new { message = "Table name, PK column and PK value are required." });

        if (request.Values is null || request.Values.Count == 0)
            return BadRequest(new { message = "At least one column value is required." });

        var (allowed, failure, apiKey) = await AuthorizeAsync(request.TableName, forWrite: true, cancellationToken);
        if (!allowed) return failure!;

        // Baglanti istekte yoksa API anahtarindan ya da oturum + ProjectId'den cozulur (Namines Desk).
        var (connectionString, effectiveDbType, connectionFailure) = await ResolveConnectionAsync(
            request.ConnectionString, request.DbType, request.ProjectId, apiKey, forWrite: true, cancellationToken);
        if (connectionFailure is not null) return connectionFailure;
        if (string.IsNullOrWhiteSpace(connectionString))
            return BadRequest(new { message = "No connection: send a connection string, or store one for the project first." });

        return await AuditedAsync(apiKey, GatewayWriteKind.Update, request.TableName, request.PkValue,
            request.Values.Keys, cancellationToken, async () =>
            {
                var result = await _gateway.UpdateAsync(
                    connectionString, effectiveDbType, request.TableName,
                    request.PkColumn, request.PkValue, request.Values, cancellationToken);

                return (result.AffectedRows == 0
                    ? NotFound(new { message = "No row found for the given key." })
                    : Ok(result), result.AffectedRows);
            }, projectId: AuditProjectId(apiKey, request.ConnectionString, request.ProjectId));
    }

    [HttpPost("delete")]
    public async Task<IActionResult> Delete([FromBody] GatewayDeleteRequest request, CancellationToken cancellationToken)
    {
        // Baglanti ARTIK zorunlu degil (anahtardan cozulebilir) ama anahtar
        // kolonu/degeri hala zorunlu: onlarsiz hangi satirin hedeflendigi belirsiz.
        if (string.IsNullOrWhiteSpace(request.TableName) ||
            string.IsNullOrWhiteSpace(request.PkColumn) ||
            string.IsNullOrWhiteSpace(request.PkValue))
            return BadRequest(new { message = "Table name, PK column and PK value are required." });

        var (allowed, failure, apiKey) = await AuthorizeAsync(request.TableName, forWrite: true, cancellationToken);
        if (!allowed) return failure!;

        // Baglanti istekte yoksa API anahtarindan ya da oturum + ProjectId'den cozulur (Namines Desk).
        var (connectionString, effectiveDbType, connectionFailure) = await ResolveConnectionAsync(
            request.ConnectionString, request.DbType, request.ProjectId, apiKey, forWrite: true, cancellationToken);
        if (connectionFailure is not null) return connectionFailure;
        if (string.IsNullOrWhiteSpace(connectionString))
            return BadRequest(new { message = "No connection: send a connection string, or store one for the project first." });

        return await AuditedAsync(apiKey, GatewayWriteKind.Delete, request.TableName, request.PkValue,
            null, cancellationToken, async () =>
            {
                var result = await _gateway.DeleteAsync(
                    connectionString, effectiveDbType, request.TableName,
                    request.PkColumn, request.PkValue, cancellationToken);

                return (result.AffectedRows == 0
                    ? NotFound(new { message = "No row found for the given key." })
                    : Ok(result), result.AffectedRows);
            }, projectId: AuditProjectId(apiKey, request.ConnectionString, request.ProjectId));
    }

    /// <summary>
    /// Namines Desk v2 §E4.1 — toplu silme. <see cref="Delete"/>'in çoğul hâli;
    /// aynı yetki modeli (yazma izni), aynı bağlantı çözümü, tek fark
    /// <c>GatewayService.BulkDeleteAsync</c>'in TEK işlemde çalışması.
    ///
    /// <b>Onay eşiği Desk'in kendisinde (arayüz), burada DEĞİL:</b> "10+ satırda
    /// yaz: SİL" kuralı bir kullanıcı deneyimi kararı — sunucu kaç satır
    /// istendiğini umursamaz, yalnızca <see cref="GatewayService.MaxBulkDeleteRows"/>
    /// tavanını uygular (34-SENDEN-BEKLENENLER.md madde 12).
    /// </summary>
    [HttpPost("bulk-delete")]
    public async Task<IActionResult> BulkDelete([FromBody] GatewayBulkDeleteRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TableName) || string.IsNullOrWhiteSpace(request.PkColumn))
            return BadRequest(new { message = "Table name and PK column are required." });
        if (request.PkValues is null || request.PkValues.Count == 0)
            return BadRequest(new { message = "At least one row must be selected." });

        var (allowed, failure, apiKey) = await AuthorizeAsync(request.TableName, forWrite: true, cancellationToken);
        if (!allowed) return failure!;

        var (connectionString, effectiveDbType, connectionFailure) = await ResolveConnectionAsync(
            request.ConnectionString, request.DbType, request.ProjectId, apiKey, forWrite: true, cancellationToken);
        if (connectionFailure is not null) return connectionFailure;
        if (string.IsNullOrWhiteSpace(connectionString))
            return BadRequest(new { message = "No connection: send a connection string, or store one for the project first." });

        return await AuditedAsync(apiKey, GatewayWriteKind.Delete, request.TableName,
            $"{request.PkValues.Count} rows", null, cancellationToken, async () =>
            {
                var result = await _gateway.BulkDeleteAsync(
                    connectionString, effectiveDbType, request.TableName,
                    request.PkColumn, request.PkValues, cancellationToken);
                return (Ok(result), result.AffectedRows);
            }, projectId: AuditProjectId(apiKey, request.ConnectionString, request.ProjectId));
    }

    /// <summary>
    /// Namines Desk v2 §E4.2 — salt-okunur SQL konsolu.
    ///
    /// <b>Bilinçli olarak GENEL <see cref="Query"/> ucundan AYRI:</b> o uç API
    /// anahtarı + <c>CanExecuteSql</c> için tasarlandı ve oturum yolunda hiçbir
    /// ek kısıtlama uygulamıyordu (JWT çağıranı zaten kendi bağlantı dizesini
    /// getiriyor sayıyordu — Desk'in modeli bu değil). Bu ikisini TEK bir action'a
    /// sığdırmak (özel durumları genel yola yamamak), gelecekte "hangi koşulda
    /// hangi kural işliyor" sorusunu karmaşıklaştırırdı; ayrı bir action ile her
    /// kuralın YERİ tek satırda okunabilir kalıyor.
    ///
    /// Üç ayrı, birbirinden bağımsız kapı — hepsi geçmeli:
    /// 1. Çağıran bu projenin <b>Owner</b>'ı mı (Admin bile yetmez — 34-SENDEN-
    ///    BEKLENENLER.md madde 13'ün kararı: "tüm güvenlik önlemleri").
    /// 2. Proje sahibi SQL konsolunu bu proje için AÇIKÇA açmış mı
    ///    (<c>ProjectConnection.AllowDeskSql</c>, varsayılan kapalı).
    /// 3. SQL'in kendisi tek bir SELECT/WITH/EXPLAIN/SHOW ifadesi mi
    ///    (<c>GatewayService.EnsureReadOnlySelectStatement</c> — DB'nin kendi
    ///    salt-okunur oturumunun SQL Server/Oracle'da hiç var olmamasını telafi
    ///    eden katman, bkz. o metodun sınıf yorumu).
    ///
    /// Her çağrı — başarılı ya da başarısız — denetim kaydına yazılır
    /// (<see cref="GatewayWriteKind.Sql"/>), D6/D7'nin Logs/Analytics ekranlarında
    /// görünür olsun diye; salt-okunur olması onu "önemsiz" yapmıyor.
    /// </summary>
    // Kontrolcü seviyesindeki "gateway" politikasını (dakikada 1200) BİLEREK
    // ezip daha sıkı "sensitive" politikasına (dakikada 5, bkz. Program.cs)
    // düşürüyor — DatabaseExecutorController'ın ham SQL ucuyla AYNI gerekçe:
    // bu, normal bir CRUD isteği değil, ek bir güvenlik katmanı hak ediyor.
    [EnableRateLimiting("sensitive")]
    [HttpPost("desk-sql")]
    public async Task<IActionResult> DeskSql([FromBody] GatewayDeskSqlRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId) || string.IsNullOrWhiteSpace(request.Sql))
            return BadRequest(new { message = "ProjectId and Sql are required." });

        // Owner, kayıtlı bağlantı ve konsol izni kapıda (denetim R2-m1); reddedilecek istek için sır çözülmez.
        var caller = Caller();
        var console = await _gate.AuthorizeDeskConsoleAsync(caller, request.ProjectId, requireStoredConnection: true, cancellationToken);
        if (!console.Allowed) return Deny(console);
        var userId = caller.SessionUserId!;

        var stored = await _secrets.GetStoredConnectionAsync(request.ProjectId, cancellationToken);
        if (stored is null)
            return BadRequest(new { message = "This project has no stored database connection." });

        var connectionString = stored.ConnectionString;
        var dbType = string.IsNullOrWhiteSpace(stored.ConnectionDbType) ? "PostgreSQL" : stored.ConnectionDbType;
        var maxRows = Math.Clamp(request.MaxRows, 1, 2000);

        // ArgumentException (tek-ifade/salt-okunur ihlali) ve "not allowed" (SSRF)
        // burada YAKALANMIYOR bilerek — ExecuteAsync'in (AuditedAsync'in içinde
        // çağrılan) kendi catch merdiveni bunları zaten doğru mesajlarla ele
        // alıyor; Create/Update/Delete ile AYNI desen, burada tekrar etmiyoruz.
        // Geçmiş kaydı, denetim kaydından AYRI ve ikisi birbirinin yerine geçmez:
        // denetim "kim ne zaman çalıştırdı"yı hesap verebilirlik için tutar ve SQL
        // metnini bilerek SAKLAMAZ; geçmiş ise tam olarak o metni, yalnızca
        // çalıştıran kullanıcının okuyabileceği şekilde tutar (bkz.
        // SqlQueryHistoryEntry sınıf notu).
        //
        // BAŞARISIZ çalıştırma da kaydediliyor: kullanıcının aradığı sorgu çoğu
        // zaman tam da hata verendir ("düzeltip tekrar deneyeceğim").
        var started = Stopwatch.GetTimestamp();
        var response = await AuditedAsync(null, GatewayWriteKind.Sql, null, null, null, cancellationToken, async () =>
        {
            var result = await _gateway.DeskSqlQueryAsync(connectionString, dbType, request.Sql, maxRows, cancellationToken);
            return (Ok(result), result.Rows.Count);
        }, projectId: request.ProjectId);

        await RecordHistoryAsync(request, userId, response, started, cancellationToken);
        return response;
    }

    /// <summary>
    /// Geçmiş kaydını yazar. <b>Yazma hatası isteği BOZMAZ:</b> kullanıcının
    /// sorgusu çalıştı; yan defterin tutulamaması ona bir hata göstermeyi haklı
    /// çıkarmaz. Sessizce yutulmuyor ama — log'a düşüyor.
    /// </summary>
    private async Task RecordHistoryAsync(
        GatewayDeskSqlRequest request, string userId, IActionResult response,
        long startedTimestamp, CancellationToken cancellationToken)
    {
        var elapsed = (int)Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds;

        var (succeeded, rowCount, error) = response switch
        {
            OkObjectResult { Value: GatewayQueryResult r } => (true, r.Rows.Count, (string?)null),
            ObjectResult { StatusCode: >= 400 } bad => (false, 0, MessageOf(bad.Value)),
            _ => (false, 0, (string?)null),
        };

        try
        {
            await _workbench.RecordAsync(
                request.ProjectId, userId, request.Sql, succeeded, rowCount,
                error, elapsed, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record SQL console history for project {ProjectId}.", request.ProjectId);
        }
    }

    /// <summary>Hata gövdesindeki <c>message</c> alanını okur (anonim tip olabilir).</summary>
    private static string? MessageOf(object? body) =>
        body?.GetType().GetProperty("message")?.GetValue(body) as string;

    /// <summary>
    /// SQL konsolunun geçmişi (F-01) — <b>YALNIZCA çağıranın kendi geçmişi</b>.
    ///
    /// Proje Owner'ı bile başkasının geçmişini göremez: bir sorgu metni
    /// <c>WHERE email = '…'</c> içerebilir ve bir meslektaşın hangi müşteriyi
    /// aradığı, projeye sahip olmakla kazanılan bir bilgi değil.
    /// </summary>
    [HttpGet("desk-sql/history")]
    public async Task<IActionResult> DeskSqlHistory(
        [FromQuery] string projectId, [FromQuery] int limit, CancellationToken cancellationToken)
    {
        var gate = await RequireDeskSqlOwnerAsync(projectId, cancellationToken);
        if (gate.Failure is not null) return gate.Failure;

        var items = await _workbench.GetHistoryAsync(
            projectId, gate.UserId!, limit <= 0 ? 50 : limit, cancellationToken);

        return Ok(new { items });
    }

    /// <summary>
    /// Kullanıcının kendi geçmişini silmesi.
    ///
    /// <b>Denetim kaydı silinmiyor</b> — <see cref="GatewayAuditEntry"/> yerinde
    /// duruyor ve "kim ne zaman çalıştırdı" kaydı korunuyor. Silinen yalnızca
    /// kolaylık amaçlı tutulan SQL METNİ; zorunlu olmayan hassas veriyi tutmakta
    /// ısrar etmek gereksiz risk.
    /// </summary>
    [HttpDelete("desk-sql/history")]
    public async Task<IActionResult> ClearDeskSqlHistory(
        [FromQuery] string projectId, CancellationToken cancellationToken)
    {
        var gate = await RequireDeskSqlOwnerAsync(projectId, cancellationToken);
        if (gate.Failure is not null) return gate.Failure;

        var deleted = await _workbench.ClearHistoryAsync(projectId, gate.UserId!, cancellationToken);
        return Ok(new { deleted });
    }

    /// <summary>Kaydedilmiş sorgular (F-02) — yine yalnızca çağıranın kendi kayıtları.</summary>
    [HttpGet("desk-sql/saved")]
    public async Task<IActionResult> SavedQueries(
        [FromQuery] string projectId, CancellationToken cancellationToken)
    {
        var gate = await RequireDeskSqlOwnerAsync(projectId, cancellationToken);
        if (gate.Failure is not null) return gate.Failure;

        var items = await _workbench.GetSavedAsync(projectId, gate.UserId!, cancellationToken);
        return Ok(new { items });
    }

    [HttpPost("desk-sql/saved")]
    public async Task<IActionResult> SaveQuery(
        [FromBody] SaveQueryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Sql))
            return BadRequest(new { message = "Sql is required." });

        var gate = await RequireDeskSqlOwnerAsync(request.ProjectId, cancellationToken);
        if (gate.Failure is not null) return gate.Failure;

        try
        {
            var item = await _workbench.SaveAsync(
                request.ProjectId, gate.UserId!, request.Name ?? string.Empty, request.Sql, cancellationToken);
            return Ok(item);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpDelete("desk-sql/saved/{id}")]
    public async Task<IActionResult> DeleteSavedQuery(
        string id, [FromQuery] string projectId, CancellationToken cancellationToken)
    {
        var gate = await RequireDeskSqlOwnerAsync(projectId, cancellationToken);
        if (gate.Failure is not null) return gate.Failure;

        return await _workbench.DeleteSavedAsync(projectId, gate.UserId!, id, cancellationToken)
            ? NoContent()
            : NotFound(new { message = "Saved query not found." });
    }

    /// <summary>
    /// SQL konsolunun yan defterleri için ortak kapı.
    ///
    /// <b><see cref="DeskSql"/> ile AYNI kapılar, bilerek:</b> konsolu
    /// kullanamayan biri onun geçmişini de görmemeli. Ayrı bir (daha gevşek)
    /// kural koymak, konsolu kapatmanın aslında hiçbir şeyi kapatmadığı bir
    /// arka kapı açardı. <c>AllowDeskSql</c> kontrolü de dahil.
    /// </summary>
    private async Task<(IActionResult? Failure, string? UserId)> RequireDeskSqlOwnerAsync(
        string projectId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return (BadRequest(new { message = "ProjectId is required." }), null);

        var caller = Caller();
        var console = await _gate.AuthorizeDeskConsoleAsync(caller, projectId, requireStoredConnection: false, cancellationToken);
        return console.Allowed ? (null, caller.SessionUserId) : (Deny(console), null);
    }

    /// <summary>
    /// Liste/detay uçlarındaki catch merdiveninin tek kopyası.
    ///
    /// "Birden fazla satır etkilenecekti, geri alındı" mesajı KULLANICIYA AYNEN
    /// aktarılır: genel bir 500 gövdesine gömmek, kullanıcının verisinin neden
    /// değişmediğini — ve neyin yanlış olduğunu — gizlerdi.
    /// </summary>
    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not allowed"))
        {
            return BadRequest(new { message = "Connection target is not allowed (private or reserved address)." });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Refusing to modify"))
        {
            return Conflict(new { message = ex.Message });
        }
        catch (DbException ex) when (IsCallerFault(ex))
        {
            // Kısıt ihlali ve sözdizimi hatası çağıranın VERİSİYLE ilgilidir, sunucu
            // arızasıyla değil. 500 dönmek, "geçersiz e-posta gönderdin" ile
            // "veritabanı çöktü"yü aynı kutuya koyar ve çağıran neyi düzelteceğini
            // bilemez. Mesaj olduğu gibi geçiriliyor: bağlantı dizesi zaten
            // çağıranın kendisinden geldi, yani veritabanı ONUN.
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(500, new { message = "Could not connect or query the database. Check credentials and network access." });
        }
    }

    /// <summary>
    /// Yazma işlemini çalıştırır ve <b>sonucu ne olursa olsun</b> denetim kaydına
    /// yazar (07 §5).
    ///
    /// Yalnızca başarılı yazmaları kaydetmek, denetim kaydını "ne oldu"nun değil
    /// "ne işe yaradı"nın listesi yapar; reddedilen bir yazma girişimi çoğu zaman
    /// başarılı olandan daha ilgi çekicidir.
    ///
    /// Kayıt <see cref="ExecuteAsync"/>'in DIŞINDA değil, sonucuna bakılarak
    /// yazılıyor: içeride yazmak, hata yolunu atlamak demek olurdu.
    /// </summary>
    private async Task<IActionResult> AuditedAsync(
        GatewayApiKey? key,
        GatewayWriteKind kind,
        string? tableName,
        string? rowKey,
        IEnumerable<string>? columns,
        CancellationToken cancellationToken,
        Func<Task<(IActionResult Result, int AffectedRows)>> action,
        string? projectId = null)
    {
        var affected = 0;
        var succeeded = false;

        var response = await ExecuteAsync(async () =>
        {
            var (result, rows) = await action();
            affected = rows;
            // 2xx dışındaki her şey başarısızdır: "satır bulunamadı" da bir
            // yazma denemesidir ve kaydedilmelidir.
            succeeded = result is ObjectResult { StatusCode: >= 200 and < 300 } or OkObjectResult;
            return result;
        });

        await _audit.RecordAsync(
            key, User?.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            kind, tableName, rowKey, columns, affected, succeeded, cancellationToken, projectId);

        return response;
    }

    /// <summary>
    /// Hata çağıranın verisinden mi kaynaklanıyor, altyapıdan mı?
    ///
    /// SQLSTATE sınıfları taşınabilir: <c>22</c> veri hatası (aralık dışı, geçersiz
    /// biçim), <c>23</c> bütünlük ihlali (NOT NULL, UNIQUE, FOREIGN KEY),
    /// <c>42</c> sözdizimi/erişim hatası. Bunları 400 saymak, çağıranın kendi
    /// isteğini düzeltebilmesi demektir.
    ///
    /// <c>25</c> (geçersiz işlem durumu) de burada: salt-okunur bir istekte yazma
    /// denemesi tam olarak buraya düşer ve çağıranın duyması gereken şey
    /// "veritabanına bağlanılamadı" değil, "bu istek salt-okunur".
    ///
    /// Bağlantı ve yetkilendirme hataları (<c>08</c>, <c>28</c>) bilerek DIŞARIDA:
    /// onlar kurulumla ilgilidir ve ayrıntısı çağırana bilgi sızdırır.
    /// </summary>
    private static bool IsCallerFault(DbException ex)
    {
        var state = ex.SqlState;
        if (string.IsNullOrEmpty(state) || state.Length < 2) return false;

        return state[..2] is "22" or "23" or "25" or "42";
    }

    // ── Dışa aktarım (08 §2) ─────────────────────────────────────────────────

    /// <summary>
    /// Filtrelenmiş satırları CSV ya da JSON olarak indirir.
    ///
    /// Sunucu tarafında bir TAVAN var (08 §5): aşan sorgu sessizce KIRPILMAZ,
    /// reddedilir. Kırpılmış bir dosya, eksik olduğunu söylemez — kullanıcı onu
    /// tam sanıp üzerine rapor kurar.
    /// </summary>
    [HttpPost("export")]
    public async Task<IActionResult> Export([FromBody] GatewayExportRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TableName))
            return BadRequest(new { message = "Table name is required." });

        // Dışa aktarım ayrı izin (F-08), okuma yetmez; karar kapıda.
        var (allowed, failure, apiKey) = await AuthorizeAsync(request.TableName, forWrite: false, cancellationToken, export: true);
        if (!allowed) return failure!;

        // Namines Desk (D4, 04-DATA-CRUD.md §2.3): bağlantı istekte yoksa API
        // anahtarından ya da oturum + ProjectId'den çözülür — diğer beş uçla
        // (list/detail/create/update/delete) aynı yol. Önceden burası yalnızca
        // ham bir bağlantı dizesi kabul ediyordu; Desk'in oturum yolunu hiç
        // desteklemiyordu.
        var (connectionString, effectiveDbType, connectionFailure) = await ResolveConnectionAsync(
            request.ConnectionString, request.DbType, request.ProjectId, apiKey, forWrite: false, cancellationToken);
        if (connectionFailure is not null) return connectionFailure;
        if (string.IsNullOrWhiteSpace(connectionString))
            return BadRequest(new { message = "No connection: send a connection string, or store one for the project first." });

        var format = (request.Format ?? "csv").Trim().ToLowerInvariant();
        if (format is not ("csv" or "json"))
            return BadRequest(new { message = "Format must be 'csv' or 'json'." });

        try
        {
            IReadOnlyList<GatewayRow> rows = await _gateway.ExportAsync(
                connectionString, effectiveDbType, request.TableName,
                request.MaxRows, request.OrderByColumn, request.SortDirection,
                request.Filters, request.OrGroups, request.Select, cancellationToken);

            // Dışa aktarım da maskeleniyor: maskelenmiş bir listeden sonra ham bir
            // CSV indirilebilseydi koruma tamamen anlamsız olurdu.
            rows = await MaskAsync(apiKey, request.TableName, rows, cancellationToken);

            var fileName = $"{request.TableName}.{format}";

            if (format == "csv")
            {
                var csv = CsvWriter.Write(rows.Select(r => r.Values).ToList());
                // UTF-8 BOM: Excel BOM'suz bir CSV'yi ANSI sanıp Türkçe karakterleri
                // bozuk gösteriyor. Dosyayı açan çoğu kişi Excel kullanıyor.
                var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
                return File(bytes, "text/csv; charset=utf-8", fileName);
            }

            var json = JsonSerializer.Serialize(rows.Select(r => r.Values));
            return File(Encoding.UTF8.GetBytes(json), "application/json; charset=utf-8", fileName);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not allowed"))
        {
            return BadRequest(new { message = "Connection target is not allowed (private or reserved address)." });
        }
        catch (Exception)
        {
            return StatusCode(500, new { message = "Could not connect or query the database. Check credentials and network access." });
        }
    }

    // ── Metadata ve OpenAPI (08 §2) ──────────────────────────────────────────

    /// <summary>
    /// Bu API anahtarının erişebildiği tablolar.
    ///
    /// Yalnızca ANAHTAR yolunda anlamlı: oturum yolunda kullanıcı zaten bağlantı
    /// dizesini kendisi giriyor ve tablo listesi introspection'dan geliyor.
    /// </summary>
    /// <summary>
    /// Toplu içe aktarım (08 §2 <c>/import</c>).
    ///
    /// <c>export</c>'un karşılığı. Tek işlemde çalışır: yarım kalan bir içe
    /// aktarımdan sonra çağıran hangi satırların yazıldığını bilemez ve aynı
    /// dosyayı tekrar denediğinde yinelenen kayıt üretir.
    /// </summary>
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] GatewayImportRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString) || string.IsNullOrWhiteSpace(request.TableName))
            return BadRequest(new { message = "Connection string and table name are required." });

        if (request.Rows is null || request.Rows.Count == 0)
            return BadRequest(new { message = "At least one row is required." });

        var (allowed, failure, apiKey) = await AuthorizeAsync(request.TableName, forWrite: true, cancellationToken);
        if (!allowed) return failure!;

        return await AuditedAsync(apiKey, GatewayWriteKind.Import, request.TableName, null,
            request.Rows[0].Keys, cancellationToken, async () =>
            {
                var result = await _gateway.ImportAsync(
                    request.ConnectionString, request.DbType, request.TableName,
                    request.Rows.Cast<IReadOnlyDictionary<string, string?>>().ToList(),
                    cancellationToken);

                return (Ok(result), result.InsertedRows);
            });
    }

    /// <summary>
    /// Veritabanında tanımlı bir fonksiyonu çağırır (08 §2 <c>/rpc</c>).
    ///
    /// <b>Yazma izni isteniyor,</b> çünkü bir fonksiyonun ne yaptığını dışarıdan
    /// bilemeyiz: adı <c>get_total</c> olan bir fonksiyon pekâlâ tablo
    /// güncelleyebilir. Salt-okunur sayıp yazmasına izin vermek, izin modelinde
    /// sessiz bir delik açardı.
    ///
    /// İzin, fonksiyon ADI üzerinden veriliyor: tablo izinleri tablolar içindir ve
    /// bir fonksiyonun hangi tablolara dokunduğu şemadan görülmez.
    /// </summary>
    [HttpPost("rpc")]
    public async Task<IActionResult> Rpc([FromBody] GatewayRpcRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString) || string.IsNullOrWhiteSpace(request.Function))
            return BadRequest(new { message = "Connection string and function name are required." });

        var (allowed, failure, apiKey) = await AuthorizeAsync(request.Function, forWrite: true, cancellationToken);
        if (!allowed) return failure!;

        return await AuditedAsync(apiKey, GatewayWriteKind.Rpc, request.Function, null,
            null, cancellationToken, async () =>
            {
                var result = await _gateway.RpcAsync(
                    request.ConnectionString, request.DbType, request.Function,
                    request.Arguments ?? new List<string?>(), cancellationToken);

                return (Ok(result), result.AffectedRows);
            });
    }

    /// <summary>
    /// Ham SQL (08 §2 <c>/query</c>).
    ///
    /// <b>Bu uç tablo izinlerini ATLAR</b> — bu yüzden anahtarda ayrı bir yetki
    /// (<c>CanExecuteSql</c>) arıyor ve <see cref="AuthorizeAsync"/>'ın tablo
    /// kontrolünü kullanmıyor. Aynı bayrağı <c>CanWrite</c> ile birleştirmek,
    /// "orders'a yazabilsin" demek isteyen birine bütün veritabanını vermek olurdu.
    ///
    /// Oturum yolunda (JWT) bayrak aranmaz: kullanıcı bağlantı dizesini zaten
    /// kendisi giriyor, yani veritabanına erişimi ham SQL'den bağımsız olarak var.
    ///
    /// <b>DÜZELTME (bkz. AuthorizeSessionRawSqlAsync):</b> yukarıdaki varsayım
    /// yalnızca çağıran KENDİ bağlantı dizesini gönderdiğinde doğru. Bu uç,
    /// gövdede dize yoksa <paramref name="request"/>'teki <c>ProjectId</c> ile
    /// PROJEYE KAYITLI şifreli bağlantıyı da çözecek şekilde genişletildiğinde
    /// ("list/detail gibi" — aşağıdaki yorum) bu öncül sessizce çürüdü: artık
    /// kullanıcının bağlantıya erişimi bu uçtan BAĞIMSIZ değil, tam olarak bu
    /// uç ONA veriyor. O zamandan beri herhangi bir Editor, Desk SQL konsolunun
    /// (yalnızca Owner + projede açıkça <c>AllowDeskSql</c>) uyguladığı iki
    /// kısıtı da atlayıp saklı bağlantı üzerinden keyfi DDL/DML çalıştırabiliyordu.
    /// </summary>
    [HttpPost("query")]
    public async Task<IActionResult> Query([FromBody] GatewayQueryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Sql))
            return BadRequest(new { message = "SQL is required." });

        // Yetki kontrolü bağlantı kontrolünden ÖNCE: ham SQL hakkı olmayan bir
        // anahtar, "bağlantı dizesi eksik" (400) cevabı alıyordu. Yanlış cevap —
        // bağlantıyı düzeltmek yetkiyi vermez ve arayan yanlış yeri arar.
        var (allowed, failure, key) = await AuthorizeForSqlAsync(cancellationToken, readOnly: request.ReadOnly);
        if (!allowed) return failure!;

        // Projeye kayıtlı bağlantıda ham SQL: oturumda Owner ve proje izni, anahtarda proje izni. Kendi dizesini
        // gönderen çağrı kapıdan geçmez, zaten kendi veritabanına erişiyor. Karar kapıda.
        var stored = await _gate.AuthorizeStoredConnectionSqlAsync(Caller(), key, request.ConnectionString, request.ProjectId, cancellationToken);
        if (!stored.Allowed) return Deny(stored);

        // list/detail gibi projeye kayıtlı şifreli bağlantıyı da çözer. Önceden
        // yalnızca gövdedeki dizeyi kabul ediyordu: bağlantısını Namines'e emanet
        // etmiş bir kullanıcı, ham SQL için onu tekrar ağa çıkarmak zorundaydı.
        var (connectionString, effectiveDbType, connectionFailure) = await ResolveConnectionAsync(
            request.ConnectionString, request.DbType, request.ProjectId, key,
            forWrite: !request.ReadOnly, cancellationToken);
        if (connectionFailure is not null) return connectionFailure;
        if (string.IsNullOrWhiteSpace(connectionString))
            return BadRequest(new { message = "No connection: send a connection string, or store one for the project first." });

        // Salt-okunur sorgular kaydedilmiyor: bir raporlama panelinin her
        // yenilenmesini denetim kaydına yazmak, gerçek değişiklikleri gürültünün
        // içinde kaybederdi. Yazma niyetiyle çalışan ham SQL ise kaydediliyor.
        if (request.ReadOnly)
        {
            return await ExecuteAsync(async () =>
            {
                // `readOnly: true` TEK BAŞINA veritabanı bağlantısının salt-okunur
                // oturum moduna güveniyordu — <see cref="UserDbConnection.AppliesReadOnlySession"/>
                // bu modu yalnızca PostgreSQL/MySQL/MariaDB'de uyguluyor; MSSQL/
                // Oracle/SQLite'ta hiçbir şey yapmıyor. Yani o motorlarda
                // "readOnly: true" iddiası hiç doğrulanmadan kabul ediliyordu ve
                // bir DELETE/DROP tam olarak çalışıyordu. `DeskSqlQueryAsync`
                // metnin GERÇEKTEN SELECT/WITH/EXPLAIN/SHOW olduğunu ayrıca
                // doğruluyor — motordan bağımsız, metin seviyesinde.
                var result = await _gateway.DeskSqlQueryAsync(
                    connectionString, effectiveDbType, request.Sql, MaxReadOnlyQueryRows, cancellationToken);
                return Ok(result);
            });
        }

        return await AuditedAsync(key, GatewayWriteKind.Sql, null, null, null, cancellationToken, async () =>
        {
            var result = await _gateway.QueryAsync(
                connectionString, effectiveDbType, request.Sql, false, cancellationToken);

            return (Ok(result), result.AffectedRows);
        });
    }

    /// <summary>Desk SQL konsoluyla aynı sayfa boyutu tavanı — bkz. <see cref="DeskSql"/>.</summary>
    private const int MaxReadOnlyQueryRows = 2000;


    /// <summary>
    /// Doğal dil sorgusu (08 §2 <c>/query/nl</c>).
    ///
    /// <b>Varsayılan olarak SQL'i döndürür, ÇALIŞTIRMAZ.</b> Bir dil modelinin
    /// ürettiği sorguyu görmeden çalıştırmak, sonucun doğruluğunu kullanıcının
    /// kontrol edemeyeceği bir yere taşır. <c>execute: true</c> verildiğinde bile
    /// yalnızca OKUMA sorguları çalışır — sınıflandırmayı geçemeyen her şey
    /// çalıştırılmadan geri döner.
    ///
    /// Şema, anahtarın projesinden okunuyor: modelin var olmayan tablo adları
    /// uydurmasının önündeki tek engel, ona gerçek şemayı vermek.
    /// </summary>
    [HttpPost("query/nl")]
    public async Task<IActionResult> NaturalLanguageQuery(
        [FromBody] GatewayNlQueryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { message = "A question is required." });

        var (allowed, failure, key) = await AuthorizeForSqlAsync(cancellationToken);
        if (!allowed) return failure!;

        // Oturum yolunda proje bilinmiyor; şema olmadan model tablo adı uydurur.
        if (key is null)
            return BadRequest(new { message = "Natural language queries need an API key, because the schema is read from the key's project." });

        // Saklanan bağlantıda ham SQL çalışacaksa projenin anahtarı açık olmalı —
        // /query ile aynı kapı. Model ürettiği için daha gevşek olması için bir
        // sebep yok; tersine, metni insan bile yazmıyor.
        var stored = await _gate.AuthorizeStoredConnectionSqlAsync(Caller(), key, request.ConnectionString, requestProjectId: null, cancellationToken);
        if (!stored.Allowed) return Deny(stored);

        var project = (await _projects.GetAsync(key.ProjectId, cancellationToken))?.SchemaJson;

        if (string.IsNullOrWhiteSpace(project))
            return BadRequest(new { message = "This project has no saved schema, so there is nothing to translate the question against." });

        DatabaseSchema schema;
        try
        {
            schema = JsonSerializer.Deserialize<DatabaseSchema>(project, SchemaJsonOptions) ?? new DatabaseSchema();
        }
        catch (JsonException)
        {
            return BadRequest(new { message = "The saved schema could not be read." });
        }

        // Tanınmayan motorda SESSİZCE PostgreSQL'e düşmek, modele yanlış lehçeyi
        // söylemek ve o motorda hiç çalışmayacak bir SQL üretmek olurdu — üstelik
        // hata, kullanıcı sorguyu çalıştırana kadar görünmezdi.
        if (!Enum.TryParse<Namines.Core.Enums.DatabaseType>(request.DbType, ignoreCase: true, out var engine))
            return BadRequest(new { message = $"Unknown database engine '{request.DbType}'." });

        // AI bütçesi, Studio'nunkiyle AYNI kaptan harcanıyor: kural
        // AiQuotaService'te, burada tekrarlanmıyor. Kendi kopyasını yazdığında üç
        // şey birden yanlıştı — kota token sayarken çağrı başına 1 artırılıyordu,
        // paylaşılan havuza hiç dokunulmuyordu ve gün sınırı middleware'inkinden
        // farklıydı. Fatura anahtarı ÜRETEN kullanıcıya yazılıyor: isteği yapan
        // taraf anonim bir uygulama, ödeyen hesap sahibi.
        //
        // Kontrol + sonradan düşme DEĞİL, atomik rezervasyon: Check→Consume arasındaki
        // pencerede eşzamanlı N istek aynı bütçeyi birlikte harcayabiliyordu.
        var decision = await _quota.TryReserveAsync(key.CreatedByUserId!, NlQueryTokenEstimate, cancellationToken);
        if (decision != AiQuotaDecision.Allowed)
        {
            return StatusCode(429, new
            {
                // Uc sebep uc ayri mesaj: kullanici hangisinin doldugunu bilmeden
                // ne yapacagini (yarin dene / ekip arkadasina sor / plani yukselt)
                // secemiyor.
                message = decision switch
                {
                    AiQuotaDecision.PoolExhausted =>
                        "The shared daily AI budget is exhausted. Try again tomorrow.",
                    AiQuotaDecision.TeamExhausted =>
                        "Your team's shared daily AI budget is used up. It resets tomorrow.",
                    _ =>
                        "The daily AI limit for the account that owns this key has been reached.",
                },
            });
        }

        string sql;
        try
        {
            sql = await _groq.GenerateSqlFromQuestionAsync(schema, engine, request.Question);
        }
        catch (Exception ex)
        {
            // Model çalışmadı: peşin rezerve edilen bütçe tamamen iade ediliyor.
            // Sağlayıcı arızasını kullanıcının günlük hakkından kesmek yanlış olurdu.
            // İade, istemci bağlantıyı kopardıysa da yapılsın diye iptal belirteci
            // GEÇİRİLMİYOR.
            await _quota.RefundAsync(key.CreatedByUserId!, NlQueryTokenEstimate);

            // Upstream'in hata GÖVDESİ geçirilmiyor: sağlayıcı mesajları uç adresi,
            // model adı ve kota ayrıntısı taşıyabiliyor ve bunlar çağıranın işine
            // yaramaz, saldırganın işine yarar. Ayrıntı log'a gidiyor.
            _logger.LogWarning(ex, "Natural language query could not be translated.");
            return StatusCode(502, new { message = "The language model could not be reached." });
        }

        // Model gerçekten cevap verdi: bütçe zaten rezerve edildi, faturalama ölçümü işleniyor.
        await _usage.RecordAsync(key.CreatedByUserId!, UsageResource.AiCall, 1, "query/nl", cancellationToken);

        // Model "cevaplayamıyorum" diyebilmeli: uyduran bir sorgu, boş dönenden
        // çok daha kötüdür çünkü kullanıcı sonucun doğru olduğunu sanır.
        if (string.IsNullOrWhiteSpace(sql) || sql.Trim().Equals("UNANSWERABLE", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new
            {
                sql = (string?)null,
                executed = false,
                message = "The question could not be answered from this project's schema.",
            });
        }

        var kind = SqlStatementKind.Classify(sql);

        if (!request.Execute)
            return Ok(new { sql, kind = kind.ToString().ToLowerInvariant(), executed = false });

        if (kind != SqlKind.Read)
        {
            // Sınıflandırma bir güvenlik kapısı: modelin talimata uyacağına
            // güvenmek bir güvenlik kararı olamaz.
            return Ok(new
            {
                sql,
                kind = kind.ToString().ToLowerInvariant(),
                executed = false,
                message = "This statement is not read-only, so it was not run. Review it and send it to /query if you meant it.",
            });
        }

        // Bağlantı ancak burada gerekiyor: SQL'i yalnızca GÖRMEK isteyen çağıran
        // (Execute=false, varsayılan) bağlantı dizesi vermek zorunda kalmasın.
        var (connectionString, effectiveDbType, connectionFailure) = await ResolveConnectionAsync(
            request.ConnectionString, request.DbType, request.ProjectId, key,
            forWrite: false, cancellationToken);
        if (connectionFailure is not null) return connectionFailure;
        if (string.IsNullOrWhiteSpace(connectionString))
            return BadRequest(new { message = "No connection: send a connection string, or store one for the project first." });

        // `QueryAsync(readOnly: true)` DEĞİL `DeskSqlQueryAsync`.
        //
        // Bulunan açık (docs/last-review H-1): buradaki TEK kapı
        // `SqlStatementKind.Classify` idi ve `readOnly: true`, oturum seviyesi
        // salt-okunur mod olan motorlarda (PostgreSQL/MySQL/MariaDB) bir şey
        // yapıyordu — MSSQL/Oracle/SQLite'ta hiçbir şey. `/query`'nin salt-okunur
        // dalı `DeskSqlQueryAsync`'ten (baş anahtar kelime + yasak kelime taraması,
        // `INTO` dahil) geçerken bu uç geçmiyordu: iki yol ayrışmıştı. Artık
        // ikisi aynı kapıdan geçiyor ve satır tavanı da aynı.
        return await AuditedAsync(
            key, GatewayWriteKind.Sql, null, null, null, cancellationToken, async () =>
            {
                var result = await _gateway.DeskSqlQueryAsync(
                    connectionString, effectiveDbType, sql, MaxReadOnlyQueryRows, cancellationToken);

                // Denetim kaydı BURADA gerekli, `/query`'nin salt-okunur dalında
                // olmadığı hâlde: orada SQL'i insan yazıyor ve `/query` tablo
                // izinlerine tabi uçlarla aynı projede. Burada metni MODEL üretiyor
                // ve ham SQL tablo izinlerini tamamen atlıyor — hangi soruya
                // karşılık hangi sorgunun çalıştığının izi hiçbir yerde yoktu.
                return (Ok(new { sql, kind = "read", executed = true, result.Rows, result.Truncated }),
                        result.Rows.Count);
            });
    }


    private async Task<(bool Allowed, IActionResult? Failure, GatewayApiKey? Key)> AuthorizeForSqlAsync(
        CancellationToken ct, bool? readOnly = null)
    {
        var decision = await _gate.AuthorizeSqlAsync(Caller(), readOnly, ct);
        return decision.Allowed ? (true, null, decision.Key) : (false, Deny(decision), null);
    }

    /// <summary>
    /// Namines Desk için ŞEMA: izinli tabloların kolon meta verisi.
    ///
    /// <b>Neden `dbintrospect` değil:</b> orası JWT istiyor (Studio kullanıcısı) ve
    /// bağlantı dizesini istekte bekliyor. Desk'in ikisi de yok — yalnızca bir API
    /// anahtarı taşıyor.
    ///
    /// <b>Neden kayıtlı `SchemaJson` değil:</b> o, kullanıcının canvas'ta ÇİZDİĞİ
    /// tasarım. Gerçek veritabanı ondan sapmış olabilir (elle ALTER, başka bir
    /// araç). Desk formu gerçek kolonlara göre üretmek zorunda, yoksa var olmayan
    /// bir kolona yazmaya çalışır. Bu yüzden CANLI introspection.
    ///
    /// Anahtarın okuyamadığı tablolar listeden düşürülür — Desk'in var olduğunu
    /// bile bilmesine gerek yok.
    /// </summary>
    [HttpGet("schema")]
    public async Task<IActionResult> Schema([FromQuery] string? projectId, CancellationToken cancellationToken)
    {
        var (key, keyFailure) = await ResolveKeyAsync(cancellationToken);
        if (keyFailure is not null) return keyFailure;

        // key varsa: dış uygulama yolu, tablo izinleri key'e göre uygulanır (mevcut davranış).
        // key yoksa: oturum (JWT) yolu — Namines Desk. Tablo izinleri uygulanmaz (01 §2),
        // ama `projectId`'ye erişim DOĞRULANIR — aksi hâlde herhangi bir giriş yapmış
        // kullanıcı başkasının projesinin şemasını okuyabilirdi.
        string resolvedProjectId;
        Dictionary<string, bool>? keyPermissions = null;
        bool sessionCanWrite = false;

        if (key is not null)
        {
            resolvedProjectId = key.ProjectId;

            var grants = await _gate.TableGrantsAsync(key, readableOnly: true, cancellationToken);
            keyPermissions = grants
                .Where(g => g.CanRead)
                .ToDictionary(g => g.TableName, g => g.CanWrite, StringComparer.OrdinalIgnoreCase);

            if (keyPermissions.Count == 0)
                return Ok(new { tables = Array.Empty<object>() });
        }
        else
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return Unauthorized(new { message = "A valid API key or an authenticated session with a projectId is required." });

            var (allowed, userId, failure) = await ResolveSessionAccessAsync(projectId, forWrite: false, cancellationToken);
            if (!allowed) return failure!;

            resolvedProjectId = projectId;
            // Erişim (view) yeterliyken canWrite AYRI bir bayrak — şemayı okumak
            // için Editor olmak gerekmez, ama Desk formunun düzenlenebilir mi
            // salt-okunur mu göstereceğini bu belirler.
            sessionCanWrite = await _authority.CanEditAsync(projectId, userId!, cancellationToken);
        }

        var connectionMeta = await _studio.GetConnectionMetadataAsync(resolvedProjectId, cancellationToken);
        if (!connectionMeta.HasConnection)
            return BadRequest(new { message = "This project has no stored database connection." });

        try
        {
            var stored = await _secrets.GetStoredConnectionAsync(resolvedProjectId, cancellationToken)
                ?? throw new InvalidOperationException("The stored connection disappeared while it was being read.");
            var schema = await _introspection.IntrospectAsync(
                stored.ConnectionString,
                string.IsNullOrWhiteSpace(stored.ConnectionDbType) ? "PostgreSQL" : stored.ConnectionDbType,
                cancellationToken);

            // İlişkiler Id üzerinden geliyor; Desk'in isimle çalışması daha basit.
            var tableById  = schema.Tables.ToDictionary(t => t.Id);
            var columnById = schema.Tables
                .SelectMany(t => t.Columns.Select(c => new { t, c }))
                .ToDictionary(x => x.c.Id, x => x);

            var fkTargets = new Dictionary<string, object>();
            foreach (var r in schema.Relations)
            {
                if (!columnById.TryGetValue(r.SourceColumnId, out var src)) continue;
                if (!tableById.TryGetValue(r.TargetTableId, out var tgtTable)) continue;
                if (!columnById.TryGetValue(r.TargetColumnId, out var tgt)) continue;
                fkTargets[$"{src.t.Name}.{src.c.Name}"] = new { table = tgtTable.Name, column = tgt.c.Name };
            }

            var tables = schema.Tables
                .Where(t => keyPermissions is null || keyPermissions.ContainsKey(t.Name))
                .Select(t => new
                {
                    name = t.Name,
                    canWrite = keyPermissions is not null ? keyPermissions[t.Name] : sessionCanWrite,
                    columns = t.Columns.Select(c => new
                    {
                        name = c.Name,
                        type = c.Type,
                        length = c.Length,
                        isPK = c.IsPK,
                        isFK = c.IsFK,
                        isNullable = c.IsNullable,
                        // Değeri veritabanı mı üretiyor (SERIAL/IDENTITY/AUTO_INCREMENT).
                        // Desk ekleme formu bu kolonu GİZLER. Önceden bu bilgi
                        // gönderilmiyordu ve Desk tipe bakarak tahmin ediyordu:
                        // her tamsayı birincil anahtarı otomatik sayıp formdan
                        // düşürüyor, dışarıdan atanan bir kimliği girmeyi
                        // imkânsız kılıyordu.
                        isIdentity = c.Identity == true,
                        // Veritabanı tarafında bir varsayılan var mı — NOT NULL
                        // olsa bile formda zorunlu işaretlenmemesi için.
                        hasDefault = !string.IsNullOrWhiteSpace(c.DefaultValue),
                        // Desk formu bunu kullanarak FK alanını açılır listeye çevirir.
                        references = fkTargets.TryGetValue($"{t.Name}.{c.Name}", out var fk) ? fk : null,
                    }),
                });

            return Ok(new { tables });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not allowed"))
        {
            return BadRequest(new { message = "Connection target is not allowed (private or reserved address)." });
        }
        catch (Exception)
        {
            return StatusCode(500, new { message = "Could not read the database schema." });
        }
    }

    [HttpGet("tables")]
    public async Task<IActionResult> Tables(CancellationToken cancellationToken)
    {
        var (key, keyFailure) = await ResolveKeyAsync(cancellationToken);
        if (keyFailure is not null) return keyFailure;
        if (key is null) return Unauthorized(new { message = "A valid API key is required." });

        var permissions = await _gate.TableGrantsAsync(key, readableOnly: true, cancellationToken);

        return Ok(permissions.Select(p => new
        {
            table = p.TableName,
            canRead = p.CanRead,
            // Tablo okunabilir ama anahtarın yazma yetkisi yoksa, yazma bu anahtar
            // için fiilen kapalıdır; iki koşulun birleşimi gösterilir ki istemci
            // "izinli görünüp 403 alan" bir uca gitmesin.
            canWrite = p.CanWrite,
        }));
    }

    /// <summary>
    /// Şemadan üretilmiş OpenAPI 3.1 belgesi (08 §2).
    ///
    /// Şema, projenin varsayılan branch'indeki EN SON sürümden okunur — belgenin
    /// canlı veritabanına bağlanmadan üretilebilmesi bilinçli: bağlantı dizesi
    /// gerektirseydi, istemci SDK'sı üretmek için üretim kimlik bilgisi paylaşmak
    /// gerekirdi.
    /// </summary>
    [HttpGet("openapi.json")]
    public async Task<IActionResult> OpenApi(CancellationToken cancellationToken)
    {
        var (key, keyFailure) = await ResolveKeyAsync(cancellationToken);
        if (keyFailure is not null) return keyFailure;
        if (key is null) return Unauthorized(new { message = "A valid API key is required." });

        var branch = (await _branches.ListAsync(key.ProjectId, cancellationToken)).FirstOrDefault(b => b.IsDefault);
        var latest = branch is null ? null : await _branches.GetLatestVersionAsync(branch.Id, cancellationToken);

        if (latest is null)
            return NotFound(new { message = "This project has no schema version yet." });

        DatabaseSchema schema;
        try
        {
            schema = JsonSerializer.Deserialize<DatabaseSchema>(latest.SchemaJson, SchemaJsonOptions)
                     ?? new DatabaseSchema();
        }
        catch (JsonException)
        {
            return StatusCode(500, new { message = "The stored schema could not be read." });
        }

        var permissions = await _gate.TableGrantsAsync(key, readableOnly: false, cancellationToken);

        var allowed = permissions.ToDictionary(
            p => p.TableName,
            p => (CanRead: p.CanRead, CanWrite: p.CanWrite),
            StringComparer.Ordinal);

        return Ok(GatewayOpenApiGenerator.Generate(schema, allowed));
    }

    private async Task<(GatewayApiKey? Key, IActionResult? Failure)> ResolveKeyAsync(CancellationToken ct)
    {
        var decision = await _gate.ResolveMetadataKeyAsync(Caller(), ct);
        return decision.Allowed ? (decision.Key, null) : (null, Deny(decision));
    }
}
