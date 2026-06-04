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
            _logger.LogWarning("SmartSeed: Invalid request - Schema is null");
            return BadRequest(new { error = "Schema cannot be empty" });
        }

        // SECURITY: Hard limit — never allow more than 500 rows regardless of client input
        const int HARD_LIMIT = 500;
        request.RowCount = Math.Clamp(request.RowCount, 1, HARD_LIMIT);

        _logger.LogInformation("SmartSeed: Test data generation request received. Schema: {Name}, DbType: {DbType}, RowCount: {RowCount}",
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
            _logger.LogError(ex, "SmartSeed: Error occurred during test data generation");
            return StatusCode(500, new { error = $"Data generation error: {ex.Message}" });
        }
    }
}
