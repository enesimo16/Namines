using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SmartSeedController : ControllerBase
{
    private readonly ISmartSeedService _smartSeedService;
    private readonly ILogger<SmartSeedController> _logger;

    public SmartSeedController(ISmartSeedService smartSeedService, ILogger<SmartSeedController> logger)
    {
        _smartSeedService = smartSeedService;
        _logger = logger;
    }

    [HttpPost("generate")]
    public async Task<IActionResult> GenerateSmartSeed([FromBody] SmartSeedRequest request)
    {
        if (request?.Schema == null)
        {
            _logger.LogWarning("SmartSeed: Geçersiz istek - Schema null");
            return BadRequest(new { error = "Schema boş olamaz" });
        }

        _logger.LogInformation("SmartSeed: Test verisi üretim talebi alındı. Şema: {Name}, DbType: {DbType}, Satır Sayısı: {RowCount}", 
            request.Schema.Name, request.DbType, request.RowCount);

        try
        {
            var result = await _smartSeedService.GenerateSmartSeedAsync(
                request.Schema, 
                request.DbType, 
                request.DomainHint, 
                request.RowCount
            );
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SmartSeed: Test verisi üretimi sırasında hata oluştu");
            return StatusCode(500, new { error = $"Veri üretim hatası: {ex.Message}" });
        }
    }
}
