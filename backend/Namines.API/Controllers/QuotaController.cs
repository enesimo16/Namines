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

        public QuotaController(AuthDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
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
            var status = await _context.Users.AsNoTracking()
                .Where(u => u.Id == userId).Select(u => u.SubscriptionStatus).FirstOrDefaultAsync();
            var tier = PlanQuotas.Resolve(status);
            var limits = PlanQuotas.For(tier);

            int dailyLimit = tier == PlanTier.Free &&
                             int.TryParse(_config["AiPool:PerUserDailyTokens"], out var pu)
                ? pu
                : limits.DailyAiTokens;

            var lastReset = quota?.LastResetDate ?? DateTime.UtcNow;
            bool resetDue = lastReset.AddHours(3).Date < DateTime.UtcNow.AddHours(3).Date;
            int used = (quota == null || resetDue) ? 0 : quota.DailyUsageCount;

            // Paylaşılan havuz da gösteriliyor: kullanıcının kendi hakkı dolmadığı
            // hâlde "AI şu an kısıtlı" cevabı alması, sebebi görünmezse arıza gibi
            // hissettirir.
            var today = DateTime.UtcNow.Date;
            long poolUsed = await _context.GlobalAiUsages.AsNoTracking()
                .Where(g => g.Date == today).Select(g => (long?)g.TokensUsed).FirstOrDefaultAsync() ?? 0;
            long poolLimit = long.TryParse(_config["AiPool:DailyTokenPool"], out var dp) ? dp : 100_000;

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
