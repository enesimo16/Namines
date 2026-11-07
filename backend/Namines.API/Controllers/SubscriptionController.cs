using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Namines.BuildingBlocks.Contracts;
using System.Security.Claims;

namespace Namines.API.Controllers;

/// <summary>Plan listesi, ödeme sayfası ve portal. Stripe oturumları Billing'de; kart verisi sunucuya hiç gelmez.</summary>
[ApiController]
[Route("api/subscription")]
[Authorize]
public class SubscriptionController : ControllerBase
{
    private readonly ISubscriptionService _subscriptions;

    public SubscriptionController(ISubscriptionService subscriptions) => _subscriptions = subscriptions;

    // GET /api/subscription/plans — anonim: fiyat listesi için hesap açmak gerekmesin.
    [AllowAnonymous]
    [HttpGet("plans")]
    public IActionResult GetPlans() =>
        Ok(_subscriptions.GetPlans().Select(plan => new
        {
            plan = plan.Plan,
            yearlyDiscountPercent = plan.YearlyDiscountPercent,
            prices = plan.Prices.Select(p => new
            {
                interval = p.Interval,
                amountUsd = p.AmountUsd,
                monthlyEquivalentUsd = p.MonthlyEquivalentUsd,
                available = p.Available,
            }),
        }));

    // POST /api/subscription/checkout?plan=pro|team&interval=monthly|yearly → { url }
    [HttpPost("checkout")]
    public async Task<IActionResult> CreateCheckoutSession(
        [FromQuery] string plan = "pro",
        [FromQuery] string interval = "monthly",
        CancellationToken ct = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var result = await _subscriptions.CreateCheckoutAsync(userId!, plan, interval, ct);
        return result.StatusCode switch
        {
            401 => Unauthorized(),
            200 when result.Redirect is not null => Ok(new { redirect = result.Redirect }),
            200 => Ok(new { url = result.Url }),
            400 => BadRequest(new { error = result.Error }),
            _ => StatusCode(result.StatusCode, new { error = result.Error }),
        };
    }

    // POST /api/subscription/portal → { url }
    [HttpPost("portal")]
    public async Task<IActionResult> CreatePortalSession(CancellationToken ct = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var result = await _subscriptions.CreatePortalAsync(userId!, ct);
        return result.StatusCode switch
        {
            401 => Unauthorized(),
            200 => Ok(new { url = result.Url }),
            400 => BadRequest(new { error = result.Error }),
            _ => StatusCode(result.StatusCode, new { error = result.Error }),
        };
    }

    // GET /api/subscription/status
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var status = await _subscriptions.GetStatusAsync(userId!, ct);
        if (status is null) return Unauthorized();

        return Ok(new
        {
            status = status.Status,
            plan = status.Plan,
            isPro = status.IsPro,
            isDev = status.IsDev,
            currentPeriodEnd = status.CurrentPeriodEnd,
            hasStripeCustomer = status.HasStripeCustomer,
        });
    }
}
