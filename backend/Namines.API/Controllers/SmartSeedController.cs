using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
using System.Security.Claims;

namespace Namines.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SmartSeedController : ControllerBase
{
    private readonly ISmartSeedService _smartSeedService;
    private readonly AuthDbContext _context;
    private readonly SemanticCacheService _cacheService;
    private readonly ILogger<SmartSeedController> _logger;

    public SmartSeedController(
        ISmartSeedService smartSeedService, 
        AuthDbContext context,
        SemanticCacheService cacheService,
        ILogger<SmartSeedController> logger)
    {
        _smartSeedService = smartSeedService;
        _context = context;
        _cacheService = cacheService;
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

        bool forceDeterministic = !request.EnhanceWithAI;
        if (HttpContext.Items.ContainsKey("FallbackToLocal") && HttpContext.Items["FallbackToLocal"] is true)
        {
            forceDeterministic = true;
        }
        else
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
            {
                var policy = await _context.UserAIPolicies.FirstOrDefaultAsync(p => p.UserId == userId);
                if (policy != null && policy.SmartSeed == AIMode.DefaultNamines)
                {
                    forceDeterministic = true;
                }
            }
            else
            {
                // If Guest has NOT provided a BYOK Key, force deterministic mockup data to save API cost
                if (string.IsNullOrEmpty(Request.Headers["X-BYOK-Key"]) && string.IsNullOrEmpty(Request.Headers["X-User-Api-Key"]))
                {
                    forceDeterministic = true;
                }
            }
        }

        if (forceDeterministic)
        {
            _logger.LogInformation("SmartSeed: Policy or Request forces Deterministic local generator.");
        }
        else
        {
            // Cache lookup for AI-powered path
            try
            {
                var cached = await _cacheService.TryGetAsync("smartseed", request.Schema, new { dbType = request.DbType.ToString(), request.DomainHint, request.RowCount });
                if (cached != null)
                {
                    _logger.LogInformation("SmartSeed: Cache HIT.");
                    var cachedResult = JsonSerializer.Deserialize<SmartSeedResult>(cached, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (cachedResult != null)
                    {
                        return Ok(cachedResult);
                    }
                }
            }
            catch (Exception cacheEx)
            {
                _logger.LogWarning(cacheEx, "SmartSeed: Cache lookup failed");
            }
        }

        try
        {
            var result = await _smartSeedService.GenerateSmartSeedAsync(
                request.Schema,
                request.DbType,
                request.DomainHint,
                request.RowCount,
                forceDeterministic
            );

            if (!forceDeterministic && result != null)
            {
                try
                {
                    await _cacheService.SetAsync("smartseed", request.Schema, new { dbType = request.DbType.ToString(), request.DomainHint, request.RowCount }, JsonSerializer.Serialize(result), TimeSpan.FromHours(1));
                }
                catch (Exception cacheEx)
                {
                    _logger.LogWarning(cacheEx, "SmartSeed: Failed to write to cache");
                }
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SmartSeed: Error occurred during test data generation");
            return StatusCode(500, new { error = $"Data generation error: {ex.Message}" });
        }
    }
}
