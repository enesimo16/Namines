using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        public QuotaController(AuthDbContext context)
        {
            _context = context;
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

            int dailyLimit = quota == null ? 100 : Math.Max(100, quota.DailyLimit);
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
