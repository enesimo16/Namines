using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AIDbaController : ControllerBase
{
    private readonly IAIDbaService _dbaService;
    private readonly ILogger<AIDbaController> _logger;

    public AIDbaController(IAIDbaService dbaService, ILogger<AIDbaController> logger)
    {
        _dbaService = dbaService;
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
            var result = await _dbaService.AnalyzeSchemaAsync(request.Schema, request.DbType);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AIDba: Analiz sırasında bir hata oluştu");
            return StatusCode(500, new { error = $"Analiz hatası: {ex.Message}" });
        }
    }
}
