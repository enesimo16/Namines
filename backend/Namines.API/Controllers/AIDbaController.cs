using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Infrastructure.Services;

namespace Namines.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AIDbaController : ControllerBase
{
    private readonly IAIDbaService _dbaService;
    private readonly SemanticCacheService _cacheService;
    private readonly ILogger<AIDbaController> _logger;

    public AIDbaController(
        IAIDbaService dbaService, 
        SemanticCacheService cacheService,
        ILogger<AIDbaController> logger)
    {
        _dbaService = dbaService;
        _cacheService = cacheService;
        _logger = logger;
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> AnalyzeSchema([FromBody] AIDbaRequest request)
    {
        if (request?.Schema == null)
        {
            _logger.LogWarning("AIDba: Geçersiz istek - Schema null");
            return BadRequest(new { error = "Schema boş olamaz" });
        }

        _logger.LogInformation("AIDba: Şema analiz talebi alındı. Şema: {Name}, DbType: {DbType}", request.Schema.Name, request.DbType);

        try
        {
            // Cache lookup
            try
            {
                var cached = await _cacheService.TryGetAsync("dba", request.Schema, new { dbType = request.DbType.ToString() });
                if (cached != null)
                {
                    _logger.LogInformation("AIDba: Cache HIT.");
                    using (var doc = JsonDocument.Parse(cached))
                    {
                        return Ok(doc.RootElement.Clone());
                    }
                }
            }
            catch (Exception cacheEx)
            {
                _logger.LogWarning(cacheEx, "AIDba: Cache lookup failed");
            }

            var result = await _dbaService.AnalyzeSchemaAsync(request.Schema, request.DbType);

            // Save cache
            if (result != null)
            {
                try
                {
                    await _cacheService.SetAsync("dba", request.Schema, new { dbType = request.DbType.ToString() }, JsonSerializer.Serialize(result), TimeSpan.FromHours(2));
                }
                catch (Exception cacheEx)
                {
                    _logger.LogWarning(cacheEx, "AIDba: Failed to save cache");
                }
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AIDba: Analiz sırasında bir hata oluştu");
            return StatusCode(500, new { error = $"Analiz hatası: {ex.Message}" });
        }
    }
}
