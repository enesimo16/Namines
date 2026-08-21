using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Namines.Core.Interfaces;

namespace Namines.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DbIntrospectController : ControllerBase
{
    private readonly IDbIntrospectionService _introspection;
    private readonly IDbPrivilegeInspector _privileges;
    private readonly ILogger<DbIntrospectController> _logger;

    public DbIntrospectController(
        IDbIntrospectionService introspection,
        IDbPrivilegeInspector privileges,
        ILogger<DbIntrospectController> logger)
    {
        _introspection = introspection;
        _privileges = privileges;
        _logger = logger;
    }

    public sealed record IntrospectRequest(string ConnectionString, string DbType);

    /// <summary>
    /// Canlı DB'ye bağlanır, şemayı okur ve Namines DatabaseSchema olarak döndürür.
    /// Connection string asla loglanmaz.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Introspect(
        [FromBody] IntrospectRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString))
            return BadRequest(new { message = "Connection string is required." });

        if (string.IsNullOrWhiteSpace(request.DbType))
            return BadRequest(new { message = "Database type is required." });

        try
        {
            var schema = await _introspection.IntrospectAsync(
                request.ConnectionString,
                request.DbType,
                cancellationToken);

            if (schema.Tables.Count == 0)
                return UnprocessableEntity(new { message = "No tables found. Check the connection string and permissions." });

            return Ok(schema);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not allowed"))
        {
            // SSRF red — bağlantı hedefi özel/reserved adres
            _logger.LogWarning("DbIntrospect SSRF block: {Reason}", ex.Message);
            return BadRequest(new { message = "Connection target is not allowed (private or reserved address)." });
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new { message = "Request cancelled." });
        }
        catch (Exception ex)
        {
            // Connection hatası — credentials sızdırma
            _logger.LogWarning("DbIntrospect failed for DbType={DbType}: {ErrorType}", request.DbType, ex.GetType().Name);
            return StatusCode(500, new { message = "Could not connect or query the database. Check credentials and network access." });
        }
    }

    /// <summary>
    /// Bağlanan kullanıcının yetkilerini raporlar (06 §5).
    ///
    /// Ayrı bir uç: introspection'la birleştirmek, şema okumayı yetki sorgusuna
    /// bağımlı kılardı — yetki kataloğuna erişemeyen ama tabloları okuyabilen bir
    /// kullanıcıda şema içe aktarma tamamen çalışmaz hâle gelirdi.
    /// </summary>
    [HttpPost("privileges")]
    public async Task<IActionResult> Privileges(
        [FromBody] IntrospectRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString))
            return BadRequest(new { message = "Connection string is required." });
        if (string.IsNullOrWhiteSpace(request.DbType))
            return BadRequest(new { message = "Database type is required." });

        try
        {
            var report = await _privileges.InspectAsync(
                request.ConnectionString, request.DbType, cancellationToken);
            return Ok(report);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not allowed"))
        {
            return BadRequest(new { message = "Connection target is not allowed (private or reserved address)." });
        }
        catch (Exception)
        {
            // Connection string asla loglanmaz; mesaj da onu yansıtmaz.
            return StatusCode(500, new { message = "Could not inspect privileges on this database." });
        }
    }
}
