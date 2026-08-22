using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;

namespace Namines.API.Controllers;

public sealed record UpdateBillingSettingsRequest(bool OverageEnabled, decimal? MonthlyCapUsd);

/// <summary>
/// Kullanım ve aşırı kullanım ayarları (new-phase/22-BUSINESS-MODEL.md §5).
///
/// Kullanıcı kendi kullanımını GÖREBİLMELİ. Bir limitin varlığını ancak ona
/// çarpınca öğrenmek, hizmetin neden durduğunu anlamamak demek — ve aşırı
/// kullanım açıkken bu, beklenmeyen bir fatura anlamına gelir.
/// </summary>
[Authorize]
[ApiController]
[Route("api/usage")]
public class UsageController : ControllerBase
{
    private readonly AuthDbContext _context;

    public UsageController(AuthDbContext context) => _context = context;

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>Bu fatura dönemindeki kullanım, plana dahil miktar ve tahakkuk eden tutar.</summary>
    [HttpGet]
    public async Task<IActionResult> Current(CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        var tier = PlanQuotas.Resolve(user?.SubscriptionStatus);

        var summary = await _context.PeriodSummaryAsync(userId, ct);
        var settings = await _context.UserBillingSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);

        // Kullanılmamış kaynaklar da listeleniyor: yalnızca kullanılanları
        // göstermek, kullanıcının neyi ne kadar kullanabileceğini gizlerdi.
        var resources = Enum.GetValues<UsageResource>().Select(resource =>
        {
            var used = summary.TryGetValue(resource, out var value) ? value : 0m;
            var pricing = OveragePricing.For(tier, resource);

            return new
            {
                resource = resource.ToString(),
                used,
                included = pricing.Included < 0 ? (decimal?)null : pricing.Included,
                overageUnitPriceUsd = pricing.UnitPriceUsd,
                overageUnitSize = pricing.UnitSize,
                overageCostUsd = OveragePricing.Cost(tier, resource, used),
            };
        }).ToList();

        return Ok(new
        {
            plan = tier.ToString(),
            billingPeriod = UsageMeter.CurrentPeriod(),
            overageEnabled = settings?.OverageEnabled ?? false,
            monthlyCapUsd = settings?.MonthlyCapUsd,
            totalOverageUsd = resources.Sum(r => r.overageCostUsd),
            resources,
        });
    }

    /// <summary>
    /// Aşırı kullanım tercihini günceller.
    ///
    /// Açmak bilinçli bir eylem olmalı — varsayılan kapalı olduğu için kullanıcı
    /// bu ucu çağırmadan asla beklemediği bir fatura almaz.
    /// </summary>
    [HttpPut("billing")]
    public async Task<IActionResult> UpdateBilling([FromBody] UpdateBillingSettingsRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        // Negatif tavan "sınırsız" ile karışır; sıfır tavan aşırı kullanımı zaten
        // imkânsız kılar ve "açtım ama çalışmıyor" şikâyetine yol açar.
        if (request.MonthlyCapUsd is <= 0)
            return BadRequest(new { error = "Monthly cap must be greater than zero, or omitted for no cap." });

        var settings = await _context.UserBillingSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (settings is null)
        {
            settings = new UserBillingSettings { UserId = userId };
            _context.UserBillingSettings.Add(settings);
        }

        settings.OverageEnabled = request.OverageEnabled;
        settings.MonthlyCapUsd = request.MonthlyCapUsd;
        settings.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return Ok(new { settings.OverageEnabled, settings.MonthlyCapUsd });
    }
}
