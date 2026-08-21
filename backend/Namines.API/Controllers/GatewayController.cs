using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.API.Controllers;

public sealed record GatewayListRequest(
    string ConnectionString, string DbType, string TableName,
    int Page = 1, int PageSize = 25,
    string? OrderByColumn = null, bool IncludeTotalCount = true,
    GatewaySortDirection SortDirection = GatewaySortDirection.Asc,
    IReadOnlyList<GatewayFilter>? Filters = null);
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
[Authorize]
[EnableRateLimiting("sensitive")]
[ApiController]
[Route("api/gateway")]
public class GatewayController : ControllerBase
{
    private readonly IGatewayService _gateway;

    public GatewayController(IGatewayService gateway)
    {
        _gateway = gateway;
    }

    [HttpPost("list")]
    public async Task<IActionResult> List([FromBody] GatewayListRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString) || string.IsNullOrWhiteSpace(request.TableName))
            return BadRequest(new { message = "Connection string and table name are required." });

        try
        {
            var result = await _gateway.ListAsync(
                request.ConnectionString, request.DbType, request.TableName,
                request.Page, request.PageSize, request.OrderByColumn,
                request.IncludeTotalCount, request.SortDirection, request.Filters,
                cancellationToken);

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
}
