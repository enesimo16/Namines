using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Namines.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;
using Microsoft.Extensions.Configuration;
using System.Linq;
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

/// <summary>
/// G14 — Minimal Gateway. Şemadan otomatik salt-okunur REST (liste + detay).
/// DatabaseExecutorController/DbIntrospectController ile aynı güvenlik modeli:
/// login zorunlu, rate-limit'li, connection string hiçbir yerde saklanmaz.
/// </summary>
// [Authorize] DEĞİL: iki kimlik yolu var (oturum ve API anahtarı) ve ikincisi
// JWT taşımaz. Kimlik doğrulama her uçta AuthorizeAsync ile YAPILIR — anonim
// erişim yoktur, yalnızca kontrol middleware'den controller'a taşınmıştır.
[AllowAnonymous]
[EnableRateLimiting("sensitive")]
[ApiController]
[Route("api/gateway")]
public class GatewayController : ControllerBase
{
    private readonly IGatewayService _gateway;
    private readonly AuthDbContext _context;
    private readonly IConfiguration _configuration;

    public GatewayController(IGatewayService gateway, AuthDbContext context, IConfiguration configuration)
    {
        _gateway = gateway;
        _context = context;
        _configuration = configuration;
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
    private async Task<(bool Allowed, IActionResult? Failure)> AuthorizeAsync(
        string tableName, bool forWrite, CancellationToken ct)
    {
        if (!Request.Headers.TryGetValue(ApiKeyHeader, out var header) ||
            string.IsNullOrWhiteSpace(header.ToString()))
        {
            // Anahtar yok → oturum yolundayız. [Authorize] zaten kimliği doğruladı.
            return User?.Identity?.IsAuthenticated == true
                ? (true, null)
                : (false, Unauthorized());
        }

        var key = await _context.AuthenticateAsync(header.ToString(), ct);
        if (key is null)
            return (false, Unauthorized(new { message = "Invalid, expired or revoked API key." }));

        // Kaynak kısıtları tablo izninden ÖNCE: kısıtı geçemeyen bir çağrının hangi
        // tabloya erişmeye çalıştığı bilgisini geri vermeye gerek yok.
        if (!GatewayKeyRestrictions.IsOriginAllowed(key, Request.Headers.Origin.ToString()))
            return (false, StatusCode(403, new { message = "This key is not allowed from this origin." }));

        if (!GatewayKeyRestrictions.IsIpAllowed(
                key, HttpContext.Connection.RemoteIpAddress, ClientAddressIsTrustworthy, out var ipReason))
            return (false, StatusCode(403, new { message = ipReason }));

        if (!GatewayRateLimiter.TryAcquire(key))
            return (false, StatusCode(429, new
            {
                message = $"This key is limited to {key.RateLimitPerMinute} requests per minute.",
            }));

        if (!await _context.IsTableAllowedAsync(key, tableName, forWrite, ct))
            // 403 ve 404 arasında bilinçli tercih: anahtar geçerli, tablo yok demek
            // hangi tabloların var olduğunu sızdırırdı; erişim reddi doğru mesaj.
            return (false, StatusCode(403, new
            {
                message = forWrite
                    ? $"This API key is not allowed to write to '{tableName}'."
                    : $"This API key is not allowed to read '{tableName}'.",
            }));

        // Kullanılmayan anahtarları fark edip kapatabilmek için. Her istekte yazmak
        // gereksiz yük olurdu; dakikada birden sık güncellenmez.
        if (key.LastUsedAt is null || DateTime.UtcNow - key.LastUsedAt > TimeSpan.FromMinutes(1))
        {
            key.LastUsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }

        return (true, null);
    }

    [HttpPost("list")]
    public async Task<IActionResult> List([FromBody] GatewayListRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString) || string.IsNullOrWhiteSpace(request.TableName))
            return BadRequest(new { message = "Connection string and table name are required." });

        var (allowed, failure) = await AuthorizeAsync(request.TableName, forWrite: false, cancellationToken);
        if (!allowed) return failure!;

        try
        {
            var result = await _gateway.ListAsync(
                request.ConnectionString, request.DbType, request.TableName,
                request.Page, request.PageSize, request.OrderByColumn,
                request.IncludeTotalCount, request.SortDirection, request.Filters,
                request.OrGroups, request.Select, cancellationToken);

            return Ok(result);
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

        var (allowed, failure) = await AuthorizeAsync(request.TableName, forWrite: false, cancellationToken);
        if (!allowed) return failure!;

        try
        {
            var row = await _gateway.DetailAsync(
                request.ConnectionString, request.DbType, request.TableName,
                request.PkColumn, request.PkValue, cancellationToken);

            return row is null ? NotFound(new { message = "No row found for the given key." }) : Ok(row);
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

        var (allowed, failure) = await AuthorizeAsync(request.TableName, forWrite: true, cancellationToken);
        if (!allowed) return failure!;

        return await ExecuteAsync(async () =>
        {
            var result = await _gateway.CreateAsync(
                request.ConnectionString, request.DbType, request.TableName,
                request.Values, cancellationToken);
            return Ok(result);
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

        var (allowed, failure) = await AuthorizeAsync(request.TableName, forWrite: true, cancellationToken);
        if (!allowed) return failure!;

        return await ExecuteAsync(async () =>
        {
            var result = await _gateway.UpdateAsync(
                request.ConnectionString, request.DbType, request.TableName,
                request.PkColumn, request.PkValue, request.Values, cancellationToken);

            return result.AffectedRows == 0
                ? NotFound(new { message = "No row found for the given key." })
                : Ok(result);
        });
    }

    [HttpPost("delete")]
    public async Task<IActionResult> Delete([FromBody] GatewayDeleteRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString) || string.IsNullOrWhiteSpace(request.TableName)
            || string.IsNullOrWhiteSpace(request.PkColumn) || request.PkValue is null)
            return BadRequest(new { message = "Connection string, table name, PK column and PK value are required." });

        var (allowed, failure) = await AuthorizeAsync(request.TableName, forWrite: true, cancellationToken);
        if (!allowed) return failure!;

        return await ExecuteAsync(async () =>
        {
            var result = await _gateway.DeleteAsync(
                request.ConnectionString, request.DbType, request.TableName,
                request.PkColumn, request.PkValue, cancellationToken);

            return result.AffectedRows == 0
                ? NotFound(new { message = "No row found for the given key." })
                : Ok(result);
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
