using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Namines.Infrastructure.Services;
using Stripe;

namespace Namines.API.Services;

/// <summary>
/// Hesap silinirken aboneliği Stripe'ta hemen iptal eder. Anahtar yoksa ya da çağrı başarısızsa
/// <c>false</c> döner; hesap silme bloklanmaz, denetim kaydına "elle iptal gerekli" yazılır.
/// </summary>
public sealed class StripeSubscriptionCanceller : ISubscriptionCanceller
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<StripeSubscriptionCanceller> _logger;

    public StripeSubscriptionCanceller(IConfiguration configuration, ILogger<StripeSubscriptionCanceller> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> CancelAsync(string subscriptionId, CancellationToken ct)
    {
        var key = _configuration["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(key)) return false;

        try
        {
            var service = new SubscriptionService(new StripeClient(key));
            await service.CancelAsync(subscriptionId, cancellationToken: ct);
            return true;
        }
        catch (StripeException ex) when (ex.StripeError?.Code == "resource_missing")
        {
            // Abonelik zaten yok/iptal: istenen son durum sağlanmış.
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe aboneligi iptal edilemedi: {Sub}", subscriptionId);
            return false;
        }
    }
}
