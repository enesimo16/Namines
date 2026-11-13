using Namines.API.Filters;
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
[FeatureGate("smart_seed")]
public class SmartSeedController : ControllerBase
{
    private readonly ISmartSeedService _smartSeedService;
    private readonly Namines.BuildingBlocks.Contracts.IAiPolicies _policies;
    private readonly SemanticCacheService _cacheService;
    private readonly ILogger<SmartSeedController> _logger;
    private readonly IAiUsageTracker _usage;

    public SmartSeedController(
        ISmartSeedService smartSeedService, 
        Namines.BuildingBlocks.Contracts.IAiPolicies policies,
        SemanticCacheService cacheService,
        ILogger<SmartSeedController> logger,
        IAiUsageTracker usage)
    {
        _usage = usage;
        _smartSeedService = smartSeedService;
        _policies = policies;
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

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        bool forceDeterministic = !request.EnhanceWithAI;
        if (HttpContext.Items.ContainsKey("FallbackToLocal") && HttpContext.Items["FallbackToLocal"] is true)
        {
            forceDeterministic = true;
        }
        else
        {
            if (!string.IsNullOrEmpty(userId))
            {
                var policy = await _policies.FindAsync(userId);
                if (policy != null && policy.SmartSeed == AIMode.DefaultNamines)
                {
                    forceDeterministic = true;
                }
            }
            else
            {
                // Misafir (kimliksiz) istek — API maliyetini korumak için deterministik üretime düş.
                forceDeterministic = true;
            }
        }

        if (forceDeterministic)
        {
            _logger.LogInformation("SmartSeed: Policy or Request forces Deterministic local generator.");
            _usage.RecordServedWithoutModel(); // yerel üretici: modele gidilmedi
        }
        else
        {
            // Cache lookup for AI-powered path
            try
            {
                var cached = await _cacheService.TryGetAsync("smartseed", request.Schema, new { dbType = request.DbType.ToString(), request.DomainHint, request.RowCount }, userId);
                if (cached != null)
                {
                    _logger.LogInformation("SmartSeed: Cache HIT.");
                    _usage.RecordServedWithoutModel(); // önbellekten: modele gidilmedi
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
                    await _cacheService.SetAsync("smartseed", request.Schema, new { dbType = request.DbType.ToString(), request.DomainHint, request.RowCount }, JsonSerializer.Serialize(result), TimeSpan.FromHours(1), userId);
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
            return StatusCode(500, new { error = "Data generation failed due to an internal error." });
        }
    }
}
