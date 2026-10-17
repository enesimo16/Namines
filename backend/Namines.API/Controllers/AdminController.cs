using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;

namespace Namines.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AuthDbContext _context;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        UserManager<ApplicationUser> userManager,
        AuthDbContext context,
        ILogger<AdminController> logger)
    {
        _userManager = userManager;
        _context = context;
        _logger = logger;
    }

    private async Task<ApplicationUser?> EnsureAdminAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return null;

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return null;

        var isDesignatedAdmin = string.Equals(user.Email, "namines@namines.com", StringComparison.OrdinalIgnoreCase);
        var isAdminRole = await _userManager.IsInRoleAsync(user, "Admin");
        if (!user.IsDev && !isAdminRole && !isDesignatedAdmin)
        {
            return null;
        }

        if (isDesignatedAdmin && !user.IsDev)
        {
            user.IsDev = true;
            await _userManager.UpdateAsync(user);
        }

        return user;
    }

    /// <summary>
    /// Genel Bakış KPI metrikleri (kullanıcılar, projeler, abonelikler, kotalar).
    /// </summary>
    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview()
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        var totalUsers = await _userManager.Users.CountAsync();
        var totalProjects = await _context.CloudProjects.CountAsync();
        var totalFeedbacks = await _context.Feedbacks.CountAsync();
        var activeDatabases = await _context.GroundDatabases.CountAsync();

        var freeUsers = await _userManager.Users.CountAsync(u => !u.IsDev && (u.PlanCode == null || u.PlanCode == "" || u.PlanCode == "free"));
        var proUsers = await _userManager.Users.CountAsync(u => !u.IsDev && u.PlanCode == "pro");
        var teamUsers = await _userManager.Users.CountAsync(u => !u.IsDev && u.PlanCode == "team");
        var devUsers = await _userManager.Users.CountAsync(u => u.IsDev);

        long totalQuotaUsedToday = 0;
        try
        {
            totalQuotaUsedToday = await _context.UserAIQuotas.SumAsync(q => (long)q.DailyUsageCount);
        }
        catch
        {
            // Quota tablosu henüz boşsa 0
        }

        return Ok(new
        {
            totalUsers,
            totalProjects,
            totalFeedbacks,
            activeDatabases,
            totalQuotaUsedToday,
            plans = new
            {
                free = freeUsers,
                pro = proUsers,
                team = teamUsers,
                dev = devUsers
            }
        });
    }

    /// <summary>
    /// Sayfalanmış ve aranabilir kullanıcı listesi.
    /// </summary>
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? q = null,
        [FromQuery] string? plan = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        if (page < 1) page = 1;
        if (pageSize < 5) pageSize = 5;
        if (pageSize > 100) pageSize = 100;

        var query = _userManager.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var search = q.Trim().ToLower();
            query = query.Where(u =>
                (u.Email != null && u.Email.ToLower().Contains(search)) ||
                (u.UserName != null && u.UserName.ToLower().Contains(search)) ||
                (u.CompanyName != null && u.CompanyName.ToLower().Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(plan) && plan != "all")
        {
            var p = plan.Trim().ToLower();
            if (p == "dev")
            {
                query = query.Where(u => u.IsDev);
            }
            else if (p == "free")
            {
                query = query.Where(u => !u.IsDev && (u.PlanCode == null || u.PlanCode == "" || u.PlanCode == "free"));
            }
            else
            {
                query = query.Where(u => !u.IsDev && u.PlanCode == p);
            }
        }

        var totalCount = await query.CountAsync();

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var userIds = users.Select(u => u.Id).ToList();

        // İlgili kullanıcıların kota ve proje sayılarını çek
        var quotas = await _context.UserAIQuotas
            .Where(q => userIds.Contains(q.UserId))
            .ToDictionaryAsync(q => q.UserId);

        var projectCounts = await _context.CloudProjects
            .Where(p => userIds.Contains(p.UserId))
            .GroupBy(p => p.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.UserId, g => g.Count);

        var items = users.Select(u =>
        {
            quotas.TryGetValue(u.Id, out var quota);
            projectCounts.TryGetValue(u.Id, out var projCount);

            var resolvedPlan = u.IsDev ? "dev" : (string.IsNullOrWhiteSpace(u.PlanCode) ? "free" : u.PlanCode.ToLowerInvariant());

            return new
            {
                id = u.Id,
                userName = u.UserName,
                email = u.Email,
                companyName = u.CompanyName,
                createdAt = u.CreatedAt,
                isDev = u.IsDev,
                planCode = resolvedPlan,
                subscriptionStatus = u.SubscriptionStatus,
                currentPeriodEnd = u.CurrentPeriodEnd,
                projectsCount = projCount,
                dailyLimit = quota?.DailyLimit ?? (u.IsDev ? int.MaxValue : 100),
                dailyUsageCount = quota?.DailyUsageCount ?? 0,
                lastResetDate = quota?.LastResetDate ?? u.CreatedAt
            };
        });

        return Ok(new
        {
            items,
            totalCount,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        });
    }

    /// <summary>
    /// Tekil kullanıcı detayları ve projeleri.
    /// </summary>
    [HttpGet("users/{id}")]
    public async Task<IActionResult> GetUserDetail(string id)
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound(new { error = "User not found." });

        var quota = await _context.UserAIQuotas.FirstOrDefaultAsync(q => q.UserId == id);
        var policy = await _context.UserAIPolicies.FirstOrDefaultAsync(p => p.UserId == id);

        var projects = await _context.CloudProjects
            .AsNoTracking()
            .Where(p => p.UserId == id)
            .OrderByDescending(p => p.Id)
            .Select(p => new
            {
                id = p.Id,
                name = p.Name,
                dbType = p.DbType
            })
            .ToListAsync();

        var feedbacks = await _context.Feedbacks
            .AsNoTracking()
            .Where(f => f.UserId == id || f.Email == user.Email)
            .OrderByDescending(f => f.CreatedAt)
            .Take(10)
            .ToListAsync();

        return Ok(new
        {
            id = user.Id,
            userName = user.UserName,
            email = user.Email,
            companyName = user.CompanyName,
            createdAt = user.CreatedAt,
            isDev = user.IsDev,
            planCode = user.IsDev ? "dev" : (user.PlanCode ?? "free"),
            subscriptionStatus = user.SubscriptionStatus,
            currentPeriodEnd = user.CurrentPeriodEnd,
            dailyLimit = quota?.DailyLimit ?? (user.IsDev ? int.MaxValue : 100),
            dailyUsageCount = quota?.DailyUsageCount ?? 0,
            lastResetDate = quota?.LastResetDate,
            projects,
            feedbacks
        });
    }

    public class UpdatePlanDto
    {
        public string PlanCode { get; set; } = "free";
    }

    /// <summary>
    /// Kullanıcı planını doğrudan değiştirme (Free, Pro, Team, Dev).
    /// </summary>
    [HttpPatch("users/{id}/plan")]
    public async Task<IActionResult> UpdateUserPlan(string id, [FromBody] UpdatePlanDto dto)
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound(new { error = "User not found." });

        var targetPlan = (dto.PlanCode ?? "free").Trim().ToLowerInvariant();
        if (targetPlan is not ("free" or "pro" or "team" or "dev"))
        {
            return BadRequest(new { error = "Invalid plan code. Valid values: free, pro, team, dev." });
        }

        user.PlanCode = targetPlan;
        if (targetPlan == "dev")
        {
            user.IsDev = true;
        }
        else if (user.IsDev && targetPlan != "dev")
        {
            user.IsDev = false;
        }

        var quota = await _context.UserAIQuotas.FirstOrDefaultAsync(q => q.UserId == id);
        if (quota == null)
        {
            quota = new UserAIQuota
            {
                UserId = id,
                DailyUsageCount = 0,
                LastResetDate = DateTime.UtcNow
            };
            await _context.UserAIQuotas.AddAsync(quota);
        }

        quota.DailyLimit = targetPlan switch
        {
            "dev" => int.MaxValue,
            "team" => 2000,
            "pro" => 500,
            _ => 100
        };

        await _userManager.UpdateAsync(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Admin {AdminEmail} updated User {UserEmail} plan to {Plan}", admin.Email, user.Email, targetPlan);

        return Ok(new
        {
            id = user.Id,
            planCode = targetPlan,
            isDev = user.IsDev,
            dailyLimit = quota.DailyLimit,
            message = $"User plan updated to {targetPlan.ToUpper()} successfully."
        });
    }

    public class ResetQuotaDto
    {
        public bool ResetToZero { get; set; } = true;
        public int? BonusPoints { get; set; }
    }

    /// <summary>
    /// Kullanıcının günlük kotasını sıfırlama veya ek bonus puan tanımlama.
    /// </summary>
    [HttpPost("users/{id}/reset-quota")]
    public async Task<IActionResult> ResetQuota(string id, [FromBody] ResetQuotaDto dto)
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound(new { error = "User not found." });

        var quota = await _context.UserAIQuotas.FirstOrDefaultAsync(q => q.UserId == id);
        if (quota == null)
        {
            quota = new UserAIQuota
            {
                UserId = id,
                DailyLimit = user.IsDev ? int.MaxValue : 100,
                DailyUsageCount = 0,
                LastResetDate = DateTime.UtcNow
            };
            await _context.UserAIQuotas.AddAsync(quota);
        }

        if (dto.ResetToZero)
        {
            quota.DailyUsageCount = 0;
            quota.LastResetDate = DateTime.UtcNow;
        }

        if (dto.BonusPoints.HasValue && dto.BonusPoints.Value > 0)
        {
            if (quota.DailyLimit != int.MaxValue)
            {
                quota.DailyLimit += dto.BonusPoints.Value;
            }
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Admin {AdminEmail} reset quota for User {UserEmail}", admin.Email, user.Email);

        return Ok(new
        {
            id = user.Id,
            dailyUsageCount = quota.DailyUsageCount,
            dailyLimit = quota.DailyLimit,
            remaining = quota.DailyLimit == int.MaxValue ? int.MaxValue : Math.Max(0, quota.DailyLimit - quota.DailyUsageCount),
            message = "Quota adjusted successfully."
        });
    }

    /// <summary>
    /// Kullanıcı geri bildirimlerini listeleme.
    /// </summary>
    [HttpGet("feedbacks")]
    public async Task<IActionResult> GetFeedbacks(
        [FromQuery] string? category = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        if (page < 1) page = 1;
        if (pageSize < 5) pageSize = 5;

        var query = _context.Feedbacks.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(category) && category != "all")
        {
            query = query.Where(f => f.Category.ToLower() == category.Trim().ToLower());
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            items,
            totalCount,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        });
    }

    /// <summary>
    /// Geri bildirimi silme.
    /// </summary>
    [HttpDelete("feedbacks/{id:int}")]
    public async Task<IActionResult> DeleteFeedback(int id)
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        var feedback = await _context.Feedbacks.FindAsync(id);
        if (feedback == null) return NotFound(new { error = "Feedback not found." });

        _context.Feedbacks.Remove(feedback);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Feedback deleted." });
    }
}
