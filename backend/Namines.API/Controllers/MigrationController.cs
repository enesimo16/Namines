using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MigrationController : ControllerBase
{
    private readonly IMigrationService _migrationService;
    private readonly ILogger<MigrationController> _logger;

    public MigrationController(IMigrationService migrationService, ILogger<MigrationController> logger)
    {
        _migrationService = migrationService;
        _logger = logger;
    }

    [HttpPost("parse")]
    public async Task<IActionResult> ParseDbContext([FromBody] ParseDbContextRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.DbContextCode))
        {
            _logger.LogWarning("Migration: ParseDbContext için geçersiz istek veya boş DbContext kodu");
            return BadRequest(new { error = "DbContext kodu boş olamaz" });
        }

        _logger.LogInformation("Migration: C# DbContext parse işlemi başlatıldı. Veritabanı Tipi: {DbType}", request.DbType);

        try
        {
            var schema = await _migrationService.ParseDbContextAsync(request.DbContextCode, request.DbType);
            return Ok(schema);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migration: DbContext parse işlemi sırasında beklenmeyen hata");
            return StatusCode(500, new { error = $"DbContext parse hatası: {ex.Message}" });
        }
    }

    [HttpPost("diff")]
    public async Task<IActionResult> CalculateDiff([FromBody] SchemaDiffRequest request)
    {
        if (request == null)
        {
            _logger.LogWarning("Migration: CalculateDiff için geçersiz istek (null request)");
            return BadRequest(new { error = "Şemalar boş olamaz" });
        }

        _logger.LogInformation("Migration: Lokal şema karşılaştırma işlemi başlatıldı.");

        try
        {
            var engine = request.DbType ?? DatabaseType.PostgreSQL;
            var diffResult = await _migrationService.CalculateDiffAsync(request.OldSchema, request.NewSchema, engine);
            return Ok(diffResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migration: Şema karşılaştırma sırasında hata oluştu");
            return StatusCode(500, new { error = $"Şema karşılaştırma hatası: {ex.Message}" });
        }
    }

    [HttpPost("generate")]
    public async Task<IActionResult> GenerateMigration([FromBody] MigrationGenerateRequest request)
    {
        if (request == null)
        {
            _logger.LogWarning("Migration: GenerateMigration için geçersiz istek (null request)");
            return BadRequest(new { error = "Migration isteği boş olamaz" });
        }

        _logger.LogInformation("Migration: C# EF Core Migration kod üretimi başlatıldı. Hedef Veritabanı: {DbType}", request.DbType);

        try
        {
            var migrationResult = await _migrationService.GenerateMigrationAsync(request.OldSchema, request.NewSchema, request.DbType);
            return Ok(migrationResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migration: Migration üretimi sırasında hata oluştu");
            return StatusCode(500, new { error = $"Migration kod üretim hatası: {ex.Message}" });
        }
    }
}
