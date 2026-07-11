using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using System;
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

            int perUserCap = int.TryParse(_config["AiPool:PerUserDailyTokens"], out var pu) ? pu : 20000;
            int dailyLimit = quota == null ? perUserCap : Math.Max(perUserCap, quota.DailyLimit);
            var lastReset = quota?.LastResetDate ?? DateTime.UtcNow;
            bool resetDue = lastReset.AddHours(3).Date < DateTime.UtcNow.AddHours(3).Date;
            int used = (quota == null || resetDue) ? 0 : quota.DailyUsageCount;

            return Ok(new
            {
                DailyLimit = dailyLimit,
                Used = used,
                Remaining = Math.Max(0, dailyLimit - used),
                ResetAt = lastReset.AddHours(3).Date.AddDays(1).AddHours(-3).ToString("o")
            });
        }
    }
}
