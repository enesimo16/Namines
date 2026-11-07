using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Namines.BuildingBlocks.Contracts;

namespace Namines.API.Controllers;

public sealed record UpdateBillingSettingsRequest(bool OverageEnabled, decimal? MonthlyCapUsd);

/// <summary>Kullanım ekranı ve aşırı kullanım tercihi. Karar ve kayıt Billing'de; burada yalnızca HTTP.</summary>
[Authorize]
[ApiController]
[Route("api/usage")]
public class UsageController : ControllerBase
{
    private readonly IUsageMeter _usage;

    public UsageController(IUsageMeter usage) => _usage = usage;

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet]
    public async Task<IActionResult> Current(CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var overview = await _usage.GetOverviewAsync(userId, ct);

        return Ok(new
        {
            plan = overview.Plan.ToString(),
            billingPeriod = overview.BillingPeriod,
            overageEnabled = overview.OverageEnabled,
            monthlyCapUsd = overview.MonthlyCapUsd,
            totalOverageUsd = overview.TotalOverageUsd,
            resources = overview.Resources.Select(r => new
            {
                resource = r.Resource.ToString(),
                used = r.Used,
                included = r.Included,
                overageUnitPriceUsd = r.OverageUnitPriceUsd,
                overageUnitSize = r.OverageUnitSize,
                overageCostUsd = r.OverageCostUsd,
            }).ToList(),
        });
    }

    [HttpPut("billing")]
    public async Task<IActionResult> UpdateBilling([FromBody] UpdateBillingSettingsRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        try
        {
            var saved = await _usage.UpdateSettingsAsync(userId, request.OverageEnabled, request.MonthlyCapUsd, ct);
            return Ok(new { saved.OverageEnabled, saved.MonthlyCapUsd });
        }
        catch (ArgumentOutOfRangeException)
        {
            return BadRequest(new { error = "Monthly cap must be greater than zero, or omitted for no cap." });
        }
    }
}
