using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Namines.BuildingBlocks.Contracts;
using System;
using System.Linq;
using Namines.Core.Analysis;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Namines.API.Controllers
{
    /// <summary>Günlük AI bütçesi ekranı. Kuralın tek sahibi Billing (<see cref="IAiQuota"/>); burada yalnızca HTTP.</summary>
    [Authorize]
    [ApiController]
    [Route("api/quota")]
    public class QuotaController : ControllerBase
    {
        private readonly IAiQuota _quota;

        public QuotaController(IAiQuota quota)
        {
            _quota = quota;
        }

        /// <summary>
        /// Havuz baskısı: büyütme kararını INSAN verir, sayı yalnızca geliştirici hesabına açıktır. Kararı verirken para da
        /// gösterilir (harmanlanmış ücretli oran ≈ $0.2475 / 1M token, girdi %60 çıktı %40).
        /// </summary>
        [Authorize]
        [HttpGet("pool-pressure")]
        public async Task<IActionResult> PoolPressure([FromQuery] int lookbackDays = 7)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (await _quota.TierAsync(userId) != PlanTier.Dev)
                return Forbid();

            var pressure = await _quota.PoolPressureAsync(Math.Clamp(lookbackDays, 1, 90));

            return Ok(new
            {
                pressure.CurrentPool,
                pressure.MaxPool,
                pressure.DaysObserved,
                pressure.DaysFull,
                pressure.ShouldGrow,
                pressure.SuggestedPool,
                estimatedMonthlyUsd = Math.Round(pressure.CurrentPool / 1_000_000.0 * 0.2475 * 30, 2),
                suggestedMonthlyUsd = Math.Round(pressure.SuggestedPool / 1_000_000.0 * 0.2475 * 30, 2),
            });
        }

        // Misafir de gorebilmeli: landing sayfasindaki secici, kullanici giris yapmadan once de doluyor. Kimlik yoksa
        // plan Free varsayiliyor, yani misafire yalnizca ucretsiz planin modelleri "available" gorunuyor.
        [AllowAnonymous]
        [HttpGet("models")]
        public async Task<IActionResult> Models()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var tier = string.IsNullOrEmpty(userId) ? PlanTier.Free : await _quota.TierAsync(userId);
            var max = NaiCatalog.MaxFor(tier);

            return Ok(NaiCatalog.All.Select(m =>
            {
                var model = NaiCatalog.Resolve(m.Id);
                return new
                {
                    m.Id,
                    m.DisplayName,
                    m.Description,
                    // Maliyet çarpanı gösteriliyor: kullanıcı Pro'nun bütçesini daha hızlı tükettiğini bilmeli, faturayı görünce değil.
                    costMultiplier = m.TokenMultiplier,
                    available = model <= max,
                    isDefault = model == NaiModel.Standard,
                };
            }));
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            // GET idempotent: yazma yok. Sınır PLANDAN, kullanım ve havuz KOTAYI uygulayan servisten okunur; burada yeniden
            // hesaplanmaz. Ekranda 20.000 yazarken gerçek tavan 10.000 olabiliyordu (adil paylaşım bilinmiyordu).
            var status = await _quota.GetStatusAsync(userId);

            // Plan limitlerinin AI dışı kalemleri (branch DB, gateway rpm...) hâlâ katalogdan; onlar havuzdan etkilenmiyor.
            var limits = PlanQuotas.For(status.Plan);

            return Ok(new
            {
                Plan = status.Plan.ToString(),
                DailyLimit = status.DailyLimit,
                Used = status.Used,
                Remaining = Math.Max(0, status.DailyLimit - status.Used),
                ResetAt = status.LastResetDate.AddHours(3).Date.AddDays(1).AddHours(-3).ToString("o"),

                // Planın diğer hakları da burada: kullanıcının neyi neden yapamadığını anlaması için tek yere bakması yeterli.
                Limits = new
                {
                    limits.BranchDatabases,
                    limits.EphemeralRunsPerDay,
                    limits.ByodbConnections,
                    limits.GatewayRequestsPerMinute,
                },

                // Paylaşılan havuz da gösteriliyor: kendi hakkı dolmadığı hâlde "AI şu an kısıtlı" cevabı sebebi görünmezse arıza gibi hissettirir.
                SharedPool = new
                {
                    Limit = status.PoolLimit,
                    Used = status.PoolUsed,
                    Remaining = Math.Max(0, status.PoolLimit - status.PoolUsed),
                },
            });
        }
    }
}
