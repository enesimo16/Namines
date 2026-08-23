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

namespace Namines.API.Controllers;

public sealed record GatewayListRequest(
    string ConnectionString, string DbType, string TableName,
    int Page = 1, int PageSize = 25,
    string? OrderByColumn = null, bool IncludeTotalCount = true,
    GatewaySortDirection SortDirection = GatewaySortDirection.Asc,
    IReadOnlyList<GatewayFilter>? Filters = null,
    IReadOnlyList<GatewayFilterGroup>? OrGroups = null,
    IReadOnlyList<string>? Select = null,
    IReadOnlyList<GatewayExpand>? Expand = null);

public sealed record GatewayExportRequest(
    string ConnectionString, string DbType, string TableName,
    string Format = "csv",
    int MaxRows = 10_000,
    string? OrderByColumn = null,
    GatewaySortDirection SortDirection = GatewaySortDirection.Asc,
    IReadOnlyList<GatewayFilter>? Filters = null,
    IReadOnlyList<GatewayFilterGroup>? OrGroups = null,
    IReadOnlyList<string>? Select = null);
public sealed record GatewayDetailRequest(string ConnectionString, string DbType, string TableName, string PkColumn, string PkValue);

public sealed record GatewayCreateRequest(
    string ConnectionString, string DbType, string TableName,
    Dictionary<string, string?> Values);

public sealed record GatewayUpdateRequest(
    string ConnectionString, string DbType, string TableName,
    string PkColumn, string PkValue,
    Dictionary<string, string?> Values);

public sealed record GatewayDeleteRequest(
    string ConnectionString, string DbType, string TableName,
    string PkColumn, string PkValue);

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
    bool ReadOnly = true);

/// <param name="Execute">
/// Üretilen SQL çalıştırılsın mı? Varsayılan <b>false</b>: doğal dilden üretilen
/// bir sorguyu görmeden çalıştırmak, "geçen ayki siparişleri göster" diyen birinin
/// isteğinin başka bir şeye dönüşme ihtimalini kabul etmek olur. true verilse bile
/// yalnızca OKUMA sorguları çalışır.
/// </param>
public sealed record GatewayNlQueryRequest(
    string ConnectionString, string DbType, string Question,
    bool Execute = false);

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
    private readonly AuthDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly GroqAIService _groq;
    private readonly ILogger<GatewayController> _logger;

    public GatewayController(
        IGatewayService gateway, AuthDbContext context, IConfiguration configuration,
        GroqAIService groq, ILogger<GatewayController> logger)
    {
        _gateway = gateway;
        _context = context;
        _configuration = configuration;
        _groq = groq;
        _logger = logger;
    }

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
    /// İki kimlik yolu var ve YETKİLERİ FARKLI:
    ///
    /// - <b>Oturum (JWT):</b> Studio'nun kendi kullanımı. Kullanıcı zaten bağlantı
    ///   dizesini kendisi giriyor, yani erişebileceği her tabloya zaten erişebilir;
    ///   burada tablo izinleri uygulanmaz.
    /// - <b>API anahtarı:</b> müşterinin uygulaması. Burada 08 §1'in kuralı geçerli:
    ///   <b>hiçbir tablo varsayılan olarak açık değildir</b>, her tablo açıkça
    ///   izinlendirilmelidir ve yazma ayrı bir yetkidir.
    ///
    /// Anahtarın kendi yetkisini genişletememesi için anahtar yönetimi uçları
    /// (GatewayKeyController) yalnızca oturumla korunur.
    /// </summary>
    private async Task<(bool Allowed, IActionResult? Failure, GatewayApiKey? Key)> AuthorizeAsync(
        string tableName, bool forWrite, CancellationToken ct)
    {
        if (!Request.Headers.TryGetValue(ApiKeyHeader, out var header) ||
            string.IsNullOrWhiteSpace(header.ToString()))
        {
            // Anahtar yok → oturum yolundayız. [Authorize] zaten kimliği doğruladı.
            return User?.Identity?.IsAuthenticated == true
                ? (true, null, null)
                : (false, Unauthorized(), null);
        }

        var key = await _context.AuthenticateAsync(header.ToString(), ct);
        if (key is null)
            return (false, Unauthorized(new { message = "Invalid, expired or revoked API key." }), null);

        // Kaynak kısıtları tablo izninden ÖNCE: kısıtı geçemeyen bir çağrının hangi
        // tabloya erişmeye çalıştığı bilgisini geri vermeye gerek yok.
        if (!GatewayKeyRestrictions.IsOriginAllowed(key, Request.Headers.Origin.ToString()))
            return (false, StatusCode(403, new { message = "This key is not allowed from this origin." }), null);

        if (!GatewayKeyRestrictions.IsIpAllowed(
                key, HttpContext.Connection.RemoteIpAddress, ClientAddressIsTrustworthy, out var ipReason))
            return (false, StatusCode(403, new { message = ipReason }), null);

        if (!GatewayRateLimiter.TryAcquire(key))
            return (false, StatusCode(429, new
            {
                message = $"This key is limited to {key.RateLimitPerMinute} requests per minute.",
            }), null);

        if (!await _context.IsTableAllowedAsync(key, tableName, forWrite, ct))
            // 403 ve 404 arasında bilinçli tercih: anahtar geçerli, tablo yok demek
            // hangi tabloların var olduğunu sızdırırdı; erişim reddi doğru mesaj.
        {
            // Reddedilenler hatadan AYRI sayılıyor: birleştirilseydi, izin
            // yapılandırması eksik olan bir kurulum "sistem bozuk" gibi görünürdü.
            NaminesMetrics.GatewayRequest(forWrite ? "write" : "read", "denied");
            return (false, StatusCode(403, new
            {
                message = forWrite
                    ? $"This API key is not allowed to write to '{tableName}'."
                    : $"This API key is not allowed to read '{tableName}'.",
            }), null);
        }

        // Kullanılmayan anahtarları fark edip kapatabilmek için. Her istekte yazmak
        // gereksiz yük olurdu; dakikada birden sık güncellenmez.
        if (key.LastUsedAt is null || DateTime.UtcNow - key.LastUsedAt > TimeSpan.FromMinutes(1))
        {
            key.LastUsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }

        NaminesMetrics.GatewayRequest(forWrite ? "write" : "read", "ok");

        // API isteği ölçümü (22 §5). Anahtarı ÜRETEN kullanıcıya yazılıyor:
        // isteği yapan taraf anonim bir uygulama, faturayı ödeyen ise hesap sahibi.
        await _context.RecordAsync(
            key.CreatedByUserId, UsageResource.ApiRequest, 1, tableName, ct);

        return (true, null, key);
    }

    /// <summary>
    /// PII maskeleme (06 §4). Yalnızca API-ANAHTARI yolunda uygulanır.
    ///
    /// Oturum yolunda uygulanmıyor, çünkü orada kullanıcı bağlantı dizesini kendisi
    /// giriyor — istediği anda psql açıp aynı veriyi ham hâliyle görebilir. Orada
    /// maskelemek güvenlik sağlamaz, yalnızca kendi ekranını işe yaramaz kılar.
    /// Anahtar yolunda ise çağıran veritabanına doğrudan erişemez; maskeleme orada
    /// gerçek bir sınır.
    ///
    /// Maskeleme gizli anahtarı PROJE BAŞINA türetiliyor: iki farklı projede aynı
    /// e-posta farklı maskelenir, yani bir projenin çıktısı diğeriyle eşleştirilerek
    /// kimlik çözülemez.
    /// </summary>
    private async Task<IReadOnlyList<GatewayRow>> MaskAsync(
        GatewayApiKey? key, string tableName, IReadOnlyList<GatewayRow> rows, CancellationToken ct)
    {
        if (key is null || rows.Count == 0) return rows;

        var masked = await _context.MaskedColumnsAsync(key.ProjectId, tableName, ct);
        if (masked.Count == 0) return rows;

        var secret = MaskingSecret(key.ProjectId);
        var lookup = new HashSet<string>(masked, StringComparer.OrdinalIgnoreCase);

        return rows.Select(row =>
        {
            var values = new Dictionary<string, object?>(row.Values);
            foreach (var column in values.Keys.ToList())
            {
                if (!lookup.Contains(column)) continue;

                values[column] = values[column] switch
                {
                    null => null,
                    long l => PiiMasker.MaskNumber(l, secret),
                    int i => PiiMasker.MaskNumber(i, secret),
                    // Diğer her tip metne çevrilip maskeleniyor: maskelenmemiş bir
                    // tipi "tanımadım" diye geçirmek, tek bir kolon tipiyle tüm
                    // korumayı sessizce delerdi.
                    var other => PiiMasker.Mask(other.ToString(), secret),
                };
            }
            return new GatewayRow(values);
        }).ToList();
    }

    /// <summary>
    /// Proje başına maskeleme anahtarı. Sunucu gizli anahtarından türetiliyor —
    /// ayrı bir sır yönetimi gerektirmesin, ama projeler arasında da ortak olmasın.
    /// </summary>
    private string MaskingSecret(string projectId) =>
        (_configuration["Jwt:Key"] ?? "namines-masking-fallback") + ":mask:" + projectId;

    [HttpPost("list")]
    public async Task<IActionResult> List([FromBody] GatewayListRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString) || string.IsNullOrWhiteSpace(request.TableName))
            return BadRequest(new { message = "Connection string and table name are required." });

        var (allowed, failure, apiKey) = await AuthorizeAsync(request.TableName, forWrite: false, cancellationToken);
        if (!allowed) return failure!;

        try
        {
            var result = await _gateway.ListAsync(
                request.ConnectionString, request.DbType, request.TableName,
                request.Page, request.PageSize, request.OrderByColumn,
                request.IncludeTotalCount, request.SortDirection, request.Filters,
                request.OrGroups, request.Select, request.Expand, cancellationToken);

            var rows = await MaskAsync(apiKey, request.TableName, result.Rows, cancellationToken);
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
        if (string.IsNullOrWhiteSpace(request.ConnectionString) || string.IsNullOrWhiteSpace(request.TableName)
            || string.IsNullOrWhiteSpace(request.PkColumn) || request.PkValue is null)
            return BadRequest(new { message = "Connection string, table name, PK column and PK value are required." });

        var (allowed, failure, apiKey) = await AuthorizeAsync(request.TableName, forWrite: false, cancellationToken);
        if (!allowed) return failure!;

        try
        {
            var row = await _gateway.DetailAsync(
                request.ConnectionString, request.DbType, request.TableName,
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
        if (string.IsNullOrWhiteSpace(request.ConnectionString) || string.IsNullOrWhiteSpace(request.TableName))
            return BadRequest(new { message = "Connection string and table name are required." });

        if (request.Values is null || request.Values.Count == 0)
            return BadRequest(new { message = "At least one column value is required." });

        var (allowed, failure, apiKey) = await AuthorizeAsync(request.TableName, forWrite: true, cancellationToken);
        if (!allowed) return failure!;

        return await AuditedAsync(apiKey, GatewayWriteKind.Create, request.TableName, null,
            request.Values.Keys, cancellationToken, async () =>
            {
                var result = await _gateway.CreateAsync(
                    request.ConnectionString, request.DbType, request.TableName,
                    request.Values, cancellationToken);
                return (Ok(result), result.AffectedRows);
            });
    }

    [HttpPost("update")]
    public async Task<IActionResult> Update([FromBody] GatewayUpdateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString) || string.IsNullOrWhiteSpace(request.TableName)
            || string.IsNullOrWhiteSpace(request.PkColumn) || request.PkValue is null)
            return BadRequest(new { message = "Connection string, table name, PK column and PK value are required." });

        if (request.Values is null || request.Values.Count == 0)
            return BadRequest(new { message = "At least one column value is required." });

        var (allowed, failure, apiKey) = await AuthorizeAsync(request.TableName, forWrite: true, cancellationToken);
        if (!allowed) return failure!;

        return await AuditedAsync(apiKey, GatewayWriteKind.Update, request.TableName, request.PkValue,
            request.Values.Keys, cancellationToken, async () =>
            {
                var result = await _gateway.UpdateAsync(
                    request.ConnectionString, request.DbType, request.TableName,
                    request.PkColumn, request.PkValue, request.Values, cancellationToken);

                return (result.AffectedRows == 0
                    ? NotFound(new { message = "No row found for the given key." })
                    : Ok(result), result.AffectedRows);
            });
    }

    [HttpPost("delete")]
    public async Task<IActionResult> Delete([FromBody] GatewayDeleteRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString) || string.IsNullOrWhiteSpace(request.TableName)
            || string.IsNullOrWhiteSpace(request.PkColumn) || request.PkValue is null)
            return BadRequest(new { message = "Connection string, table name, PK column and PK value are required." });

        var (allowed, failure, apiKey) = await AuthorizeAsync(request.TableName, forWrite: true, cancellationToken);
        if (!allowed) return failure!;

        return await AuditedAsync(apiKey, GatewayWriteKind.Delete, request.TableName, request.PkValue,
            null, cancellationToken, async () =>
            {
                var result = await _gateway.DeleteAsync(
                    request.ConnectionString, request.DbType, request.TableName,
                    request.PkColumn, request.PkValue, cancellationToken);

                return (result.AffectedRows == 0
                    ? NotFound(new { message = "No row found for the given key." })
                    : Ok(result), result.AffectedRows);
            });
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
        Func<Task<(IActionResult Result, int AffectedRows)>> action)
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

        await _context.RecordAsync(
            key, User?.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            kind, tableName, rowKey, columns, affected, succeeded, cancellationToken);

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
        if (string.IsNullOrWhiteSpace(request.ConnectionString) || string.IsNullOrWhiteSpace(request.TableName))
            return BadRequest(new { message = "Connection string and table name are required." });

        var (allowed, failure, apiKey) = await AuthorizeAsync(request.TableName, forWrite: false, cancellationToken);
        if (!allowed) return failure!;

        var format = (request.Format ?? "csv").Trim().ToLowerInvariant();
        if (format is not ("csv" or "json"))
            return BadRequest(new { message = "Format must be 'csv' or 'json'." });

        try
        {
            IReadOnlyList<GatewayRow> rows = await _gateway.ExportAsync(
                request.ConnectionString, request.DbType, request.TableName,
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
    /// </summary>
    [HttpPost("query")]
    public async Task<IActionResult> Query([FromBody] GatewayQueryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString) || string.IsNullOrWhiteSpace(request.Sql))
            return BadRequest(new { message = "Connection string and SQL are required." });

        var (allowed, failure, key) = await AuthorizeForSqlAsync(cancellationToken);
        if (!allowed) return failure!;

        // Salt-okunur olmayan bir sorgu, yazma yetkisi de ister: ham SQL yetkisi
        // "her şeyi oku" demek, "her şeyi değiştir" demek değil.
        if (!request.ReadOnly && key is not null && !key.CanWrite)
            return StatusCode(403, new { message = "This API key may run read-only SQL only." });

        // Salt-okunur sorgular kaydedilmiyor: bir raporlama panelinin her
        // yenilenmesini denetim kaydına yazmak, gerçek değişiklikleri gürültünün
        // içinde kaybederdi. Yazma niyetiyle çalışan ham SQL ise kaydediliyor.
        if (request.ReadOnly)
        {
            return await ExecuteAsync(async () =>
            {
                var result = await _gateway.QueryAsync(
                    request.ConnectionString, request.DbType, request.Sql, true, cancellationToken);
                return Ok(result);
            });
        }

        return await AuditedAsync(key, GatewayWriteKind.Sql, null, null, null, cancellationToken, async () =>
        {
            var result = await _gateway.QueryAsync(
                request.ConnectionString, request.DbType, request.Sql, false, cancellationToken);

            return (Ok(result), result.AffectedRows);
        });
    }

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
        if (string.IsNullOrWhiteSpace(request.ConnectionString) || string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { message = "Connection string and question are required." });

        var (allowed, failure, key) = await AuthorizeForSqlAsync(cancellationToken);
        if (!allowed) return failure!;

        // Oturum yolunda proje bilinmiyor; şema olmadan model tablo adı uydurur.
        if (key is null)
            return BadRequest(new { message = "Natural language queries need an API key, because the schema is read from the key's project." });

        var project = await _context.CloudProjects
            .AsNoTracking()
            .Where(p => p.Id == key.ProjectId)
            .Select(p => p.SchemaJson)
            .FirstOrDefaultAsync(cancellationToken);

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

        // AI çağrısı ÖLÇÜLÜYOR. Bu uç, kota denetimi yapan AIQuotaMiddleware'in
        // yolunda değil (o JWT bekler, buraya API anahtarıyla geliniyor) — ölçüm
        // olmasaydı bir anahtar sahibi, hesap sahibinin AI bütçesini görünmez
        // biçimde harcayabilirdi. Fatura anahtarı ÜRETEN kullanıcıya yazılıyor:
        // isteği yapan taraf anonim bir uygulama, ödeyen hesap sahibi.
        var quota = await _context.UserAIQuotas
            .FirstOrDefaultAsync(q => q.UserId == key.CreatedByUserId, cancellationToken);

        if (quota is not null)
        {
            // Gün dönünce sayaç sıfırlanır. Bu kontrol olmadan dünkü kullanım
            // bugünü de kapatırdı.
            if (quota.LastResetDate.Date != DateTime.UtcNow.Date)
            {
                quota.DailyUsageCount = 0;
                quota.LastResetDate = DateTime.UtcNow;
            }

            if (quota.DailyUsageCount >= quota.DailyLimit)
            {
                return StatusCode(429, new
                {
                    message = "The daily AI limit for the account that owns this key has been reached.",
                });
            }

            // Sayaç BURADA artırılıyor. Yalnızca UsageEvent yazmak, okunan sayaçla
            // yazılan sayacın farklı olması demekti: kapı hiçbir zaman kapanmaz ve
            // bir anahtar sahibi hesap sahibinin AI bütçesini sınırsız harcardı.
            // Çağrıdan ÖNCE artırılıyor — sonra artırmak, eşzamanlı isteklerin
            // hepsinin aynı anda geçmesine izin verir. Çağrı başarısız olursa
            // aşağıda geri veriliyor.
            quota.DailyUsageCount++;
            await _context.SaveChangesAsync(cancellationToken);
        }

        string sql;
        try
        {
            sql = await _groq.GenerateSqlFromQuestionAsync(schema, engine, request.Question);
        }
        catch (Exception ex)
        {
            // Hak GERİ VERİLİYOR: sağlayıcıya ulaşılamadığında kullanıcı hiçbir şey
            // almadı. Peşin alınan hakkı iade etmemek, dış bir servisin arızasını
            // kullanıcının günlük bütçesinden kesmek olurdu — canlı denemede tam
            // olarak bu görüldü (Groq anahtarı yokken sayaç 1'e çıktı).
            if (quota is not null)
            {
                quota.DailyUsageCount--;
                await _context.SaveChangesAsync(cancellationToken);
            }

            // Upstream'in hata GÖVDESİ geçirilmiyor: sağlayıcı mesajları uç adresi,
            // model adı ve kota ayrıntısı taşıyabiliyor ve bunlar çağıranın işine
            // yaramaz, saldırganın işine yarar. Ayrıntı log'a gidiyor.
            _logger.LogWarning(ex, "Natural language query could not be translated.");
            return StatusCode(502, new { message = "The language model could not be reached." });
        }

        await _context.RecordAsync(key.CreatedByUserId, UsageResource.AiCall, 1, "query/nl", cancellationToken);

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

        return await ExecuteAsync(async () =>
        {
            var result = await _gateway.QueryAsync(
                request.ConnectionString, request.DbType, sql, readOnly: true, cancellationToken);

            return Ok(new { sql, kind = "read", executed = true, result.Rows, result.Truncated });
        });
    }

    /// <summary>
    /// <c>/query</c> için kimlik doğrulama — tablo kontrolü OLMADAN.
    ///
    /// <see cref="AuthorizeAsync"/> bir tablo adı ister ve ham SQL'in tek bir
    /// tablosu yoktur. Uydurma bir tablo adı vermek, izin kontrolünü anlamsız bir
    /// kayda bağlamak olurdu; bunun yerine anahtarın kendi yetkisi kontrol ediliyor.
    /// Kaynak/IP/rate limit kısıtları aynen uygulanır.
    /// </summary>
    private async Task<(bool Allowed, IActionResult? Failure, GatewayApiKey? Key)> AuthorizeForSqlAsync(
        CancellationToken ct)
    {
        if (!Request.Headers.TryGetValue(ApiKeyHeader, out var header) ||
            string.IsNullOrWhiteSpace(header.ToString()))
        {
            return User?.Identity?.IsAuthenticated == true
                ? (true, null, null)
                : (false, Unauthorized(), null);
        }

        var key = await _context.AuthenticateAsync(header.ToString(), ct);
        if (key is null)
            return (false, Unauthorized(new { message = "Invalid, expired or revoked API key." }), null);

        if (!GatewayKeyRestrictions.IsOriginAllowed(key, Request.Headers.Origin.ToString()))
            return (false, StatusCode(403, new { message = "This key is not allowed from this origin." }), null);

        if (!GatewayKeyRestrictions.IsIpAllowed(
                key, HttpContext.Connection.RemoteIpAddress, ClientAddressIsTrustworthy, out var ipReason))
            return (false, StatusCode(403, new { message = ipReason }), null);

        if (!GatewayRateLimiter.TryAcquire(key))
            return (false, StatusCode(429, new
            {
                message = $"This key is limited to {key.RateLimitPerMinute} requests per minute.",
            }), null);

        if (!key.CanExecuteSql)
        {
            NaminesMetrics.GatewayRequest("sql", "denied");
            return (false, StatusCode(403, new
            {
                message = "This API key is not allowed to run raw SQL. Raw SQL bypasses table " +
                          "permissions, so it is a separate grant from write access.",
            }), null);
        }

        NaminesMetrics.GatewayRequest("sql", "ok");
        await _context.RecordAsync(key.CreatedByUserId, UsageResource.ApiRequest, 1, "sql", ct);

        return (true, null, key);
    }

    [HttpGet("tables")]
    public async Task<IActionResult> Tables(CancellationToken cancellationToken)
    {
        var key = await ResolveKeyAsync(cancellationToken);
        if (key is null) return Unauthorized(new { message = "A valid API key is required." });

        var permissions = await _context.ReadableTablesAsync(key.ProjectId, cancellationToken);

        return Ok(permissions.Select(p => new
        {
            table = p.TableName,
            canRead = p.CanRead,
            // Tablo okunabilir ama anahtarın yazma yetkisi yoksa, yazma bu anahtar
            // için fiilen kapalıdır; iki koşulun birleşimi gösterilir ki istemci
            // "izinli görünüp 403 alan" bir uca gitmesin.
            canWrite = p.CanWrite && key.CanWrite,
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
        var key = await ResolveKeyAsync(cancellationToken);
        if (key is null) return Unauthorized(new { message = "A valid API key is required." });

        var branch = await _context.Branches
            .Where(b => b.ProjectId == key.ProjectId && b.IsDefault)
            .FirstOrDefaultAsync(cancellationToken);

        var latest = branch is null ? null : await _context.SchemaVersions
            .Where(v => v.BranchId == branch.Id)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(cancellationToken);

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

        var permissions = await _context.GatewayTablePermissions
            .Where(p => p.ProjectId == key.ProjectId)
            .ToListAsync(cancellationToken);

        var allowed = permissions.ToDictionary(
            p => p.TableName,
            p => (CanRead: p.CanRead, CanWrite: p.CanWrite && key.CanWrite),
            StringComparer.Ordinal);

        return Ok(GatewayOpenApiGenerator.Generate(schema, allowed));
    }

    private async Task<GatewayApiKey?> ResolveKeyAsync(CancellationToken ct)
    {
        if (!Request.Headers.TryGetValue(ApiKeyHeader, out var header)) return null;
        return await _context.AuthenticateAsync(header.ToString(), ct);
    }
}
