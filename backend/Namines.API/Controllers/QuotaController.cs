using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using System;
using Namines.Core.Analysis;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Namines.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/quota")]
    public class QuotaController : ControllerBase
    {
        private readonly AuthDbContext _context;
        private readonly IConfiguration _config;
        private readonly AiQuotaService _quota;

        public QuotaController(AuthDbContext context, IConfiguration config, AiQuotaService quota)
        {
            _context = context;
            _config = config;
            _quota = quota;
        }

        /// <summary>
        /// Havuzun doluluk baskısı — havuzu büyütme kararını verecek sayı.
        ///
        /// <b>Yalnızca Dev hesabı.</b> Bu bir işletme metriği: kaç gün havuz
        /// doldu, bir üst kademeye çıkmalı mıyız. Normal kullanıcıya
        /// göstermenin bir anlamı yok ve altyapı kapasitemizi sızdırırdı.
        ///
        /// <b>Öneri döner, uygulamaz</b> — havuzu büyütmek para harcamaktır ve
        /// bu kararı bir sayaç veremez (bkz. AiQuotaService.PoolPressureAsync).
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
                // Kararı verirken parayı da görsün: harmanlanmış Groq ücretli
                // oranı ≈ $0.2475 / 1M token (girdi %60, çıktı %40).
                estimatedMonthlyUsd = Math.Round(pressure.CurrentPool / 1_000_000.0 * 0.2475 * 30, 2),
                suggestedMonthlyUsd = Math.Round(pressure.SuggestedPool / 1_000_000.0 * 0.2475 * 30, 2),
            });
        }

        /// <summary>
        /// Kullanılabilir Namines AI modelleri (36 §3).
        ///
        /// <b>Sağlayıcı model kimlikleri DÖNMÜYOR.</b> Kullanıcının "llama" ya da
        /// "gpt" görmesi gerekmiyor; hangi modelin arkada olduğu bizim
        /// yapılandırmamız ve değişebilir. Kimliği göstermek, kullanıcının ona
        /// bağlanması ve bir gün sağlayıcı o modeli kaldırdığında ürünün
        /// bozulmuş gibi görünmesi demekti.
        ///
        /// Planın izin vermediği model listede ama <c>available:false</c>: gizlemek
        /// yerine göstermek, yükseltme sebebini de göstermek demek.
        /// </summary>
        // Misafir de gorebilmeli: landing sayfasindaki secici, kullanici giris
        // yapmadan once de doluyor. Kimlik yoksa plan Free varsayiliyor, yani
        // misafire yalnizca ucretsiz planin modelleri "available" gorunuyor.
        [AllowAnonymous]
        [HttpGet("models")]
        public async Task<IActionResult> Models()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var account = string.IsNullOrEmpty(userId)
                ? null
                : await _context.Users.AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.SubscriptionStatus, u.PlanCode, u.IsDev })
                    .FirstOrDefaultAsync();

            var tier = PlanQuotas.Resolve(account?.SubscriptionStatus, account?.PlanCode, account?.IsDev ?? false);
            var max = NaiCatalog.MaxFor(tier);

            return Ok(NaiCatalog.All.Select(m =>
            {
                var model = NaiCatalog.Resolve(m.Id);
                return new
                {
                    m.Id,
                    m.DisplayName,
                    m.Description,
                    // Maliyet çarpanı gösteriliyor: kullanıcı Pro'nun bütçesini
                    // daha hızlı tükettiğini bilmeli, faturayı görünce değil.
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

            // GET idempotent olmalı → yazma yapmadan oku. Provizyon register'da, reset AI çağrısı
            // sırasında middleware'de kalıcılaşır; burada yalnızca reset-farkındalıklı değerler hesaplanır.
            var quota = await _context.UserAIQuotas.AsNoTracking().FirstOrDefaultAsync(q => q.UserId == userId);

            // Sınır PLANDAN geliyor. Yapılandırmadaki tek sayı, ücretli kullanıcıya
            // ücretsizle aynı bütçeyi gösteriyordu — kullanıcı ödediği şeyin
            // karşılığını ekranda göremiyordu.
            var account = await _context.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.SubscriptionStatus, u.PlanCode, u.IsDev })
                .FirstOrDefaultAsync();
            var tier = PlanQuotas.Resolve(account?.SubscriptionStatus, account?.PlanCode, account?.IsDev ?? false);

            // Tavan, KOTAYI UYGULAYAN servisten okunuyor — burada yeniden
            // hesaplanmıyor. Önceden bu uç kendi hesabını yapıyordu ve adil
            // paylaşımdan (bkz. AiQuotaService.CalculateFreeUserCap) habersizdi:
            // ekranda 20.000 yazarken gerçek tavan 10.000 olabiliyordu, yani
            // kullanıcı hakkının yarısında kesiliyor ve sebebini göremiyordu.
            // Gösterilen sayı ile uygulanan sayı aynı kaynaktan gelmeli.
            int dailyLimit = await _quota.PerUserCapAsync(userId);

            // Plan limitlerinin AI dışı kalemleri (branch DB, gateway rpm...)
            // hâlâ katalogdan; onlar havuzdan etkilenmiyor.
            var limits = PlanQuotas.For(tier);

            var lastReset = quota?.LastResetDate ?? DateTime.UtcNow;
            bool resetDue = lastReset.AddHours(3).Date < DateTime.UtcNow.AddHours(3).Date;
            int used = (quota == null || resetDue) ? 0 : quota.DailyUsageCount;

            // Paylaşılan havuz da gösteriliyor: kullanıcının kendi hakkı dolmadığı
            // hâlde "AI şu an kısıtlı" cevabı alması, sebebi görünmezse arıza gibi
            // hissettirir.
            var today = DateTime.UtcNow.Date;
            long poolUsed = await _context.GlobalAiUsages.AsNoTracking()
                .Where(g => g.Date == today).Select(g => (long?)g.TokensUsed).FirstOrDefaultAsync() ?? 0;
            // Havuz da aynı kaynaktan — varsayılanı burada tekrarlamak, iki yerin
            // farklı sayı göstermesi demekti.
            long poolLimit = _quota.DailyPool;

            return Ok(new
            {
                Plan = tier.ToString(),
                DailyLimit = dailyLimit,
                Used = used,
                Remaining = Math.Max(0, dailyLimit - used),
                ResetAt = lastReset.AddHours(3).Date.AddDays(1).AddHours(-3).ToString("o"),

                // Planın diğer hakları da burada: kullanıcının neyi neden
                // yapamadığını anlaması için tek bir yere bakması yeterli olmalı.
                Limits = new
                {
                    limits.BranchDatabases,
                    limits.EphemeralRunsPerDay,
                    limits.ByodbConnections,
                    limits.GatewayRequestsPerMinute,
                },

                SharedPool = new
                {
                    Limit = poolLimit,
                    Used = poolUsed,
                    Remaining = Math.Max(0, poolLimit - poolUsed),
                },
            });
        }
    }
}
