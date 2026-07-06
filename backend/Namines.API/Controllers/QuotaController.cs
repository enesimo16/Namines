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

            var quota = await _context.UserAIQuotas.FirstOrDefaultAsync(q => q.UserId == userId);
            if (quota == null)
            {
                quota = new UserAIQuota
                {
                    UserId = userId,
                    DailyLimit = 100,
                    DailyUsageCount = 0,
                    LastResetDate = DateTime.UtcNow
                };
                await _context.UserAIQuotas.AddAsync(quota);
                await _context.SaveChangesAsync();
            }
            else
            {
                if (quota.DailyLimit < 100)
                {
                    quota.DailyLimit = 100;
                }

                if (quota.LastResetDate.AddHours(3).Date < DateTime.UtcNow.AddHours(3).Date)
                {
                    quota.DailyUsageCount = 0;
                    quota.LastResetDate = DateTime.UtcNow;
                }
                _context.UserAIQuotas.Update(quota);
                await _context.SaveChangesAsync();
            }

            return Ok(new
            {
                DailyLimit = quota.DailyLimit,
                Used = quota.DailyUsageCount,
                Remaining = Math.Max(0, quota.DailyLimit - quota.DailyUsageCount),
                ResetAt = quota.LastResetDate.AddHours(3).Date.AddDays(1).AddHours(-3).ToString("o")
            });
        }
    }
}
