using Microsoft.AspNetCore.Mvc;
using Namines.BuildingBlocks.Contracts;

namespace Namines.API.Controllers;

/// <summary>
/// Stripe webhook ucu. Ham gövde imzanın girdisi olduğu için model bağlamadan ÖNCE okunur; imza doğrulaması,
/// tekrar iletim, sıra ve abonelik etkisi Billing'de (<see cref="IStripeWebhookService"/>).
/// </summary>
[ApiController]
[Route("api/webhooks")]
public class StripeWebhookController : ControllerBase
{
    private readonly IStripeWebhookService _webhooks;

    public StripeWebhookController(IStripeWebhookService webhooks) => _webhooks = webhooks;

    // POST /api/webhooks/stripe
    [HttpPost("stripe")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    [Consumes("application/json")]
    public async Task<IActionResult> HandleStripeWebhook(CancellationToken ct)
    {
        string json;
        using (var reader = new System.IO.StreamReader(Request.Body, leaveOpen: false))
        {
            json = await reader.ReadToEndAsync(ct);
        }

        var result = await _webhooks.HandleAsync(json, Request.Headers["Stripe-Signature"], ct);
        return StatusCode(result.StatusCode, result.Body);
    }
}
