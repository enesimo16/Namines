using Namines.Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Namines.Core.Analysis;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Core.Interfaces;
using Namines.Infrastructure.Data;

using Namines.API.Services;

namespace Namines.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AuthDbContext _context;
    private readonly ILogger<AdminController> _logger;
    private readonly AdminTelemetryService _telemetry;
    private readonly IConfiguration _configuration;
    private readonly IFeatureFlagService? _featureFlags;
    private readonly IAiToolRouter? _aiRouter;

    public AdminController(
        UserManager<ApplicationUser> userManager,
        AuthDbContext context,
        ILogger<AdminController> logger,
        AdminTelemetryService telemetry,
        IConfiguration configuration,
        IFeatureFlagService? featureFlags = null,
        IAiToolRouter? aiRouter = null)
    {
        _userManager = userManager;
        _context = context;
        _logger = logger;
        _telemetry = telemetry;
        _configuration = configuration;
        _featureFlags = featureFlags;
        _aiRouter = aiRouter;
    }

    private async Task<ApplicationUser?> EnsureAdminAsync()
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var email = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userId) && string.IsNullOrEmpty(email)) return null;

            ApplicationUser? user = null;
            if (!string.IsNullOrEmpty(userId))
            {
                user = await _userManager.FindByIdAsync(userId);
            }
            if (user == null && !string.IsNullOrEmpty(email))
            {
                user = await _userManager.FindByEmailAsync(email);
            }

            if (user == null) return null;

            var devConfigEmail = _configuration["Dev:Email"];
            var isConfiguredDevEmail = !string.IsNullOrWhiteSpace(devConfigEmail) &&
                                       string.Equals(user.Email, devConfigEmail, StringComparison.OrdinalIgnoreCase);

            if (user.IsDev || isConfiguredDevEmail)
            {
                if (isConfiguredDevEmail && !user.IsDev)
                {
                    user.IsDev = true;
                    try { await _userManager.UpdateAsync(user); } catch { }
                }
                return user;
            }

            if (_userManager.SupportsUserRole && await _userManager.IsInRoleAsync(user, "Admin"))
            {
                return user;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EnsureAdminAsync encountered error while validating admin privileges.");
            return null;
        }
    }

    private const double BytesPerMb = 1024.0 * 1024.0;
    private const int MaxBonusPoints = 100_000;

    /// <summary>
    /// Şema JSON'undaki gerçek tablo sayısı. Ayrıştırılamayan/boş şema 0 sayılır:
    /// uydurma bir taban değer vermektense "bilinmiyor"u sıfır göstermek dürüst olandır.
    /// (SchemaTable JSON'da <c>name</c> olarak serileşir; eski <c>tableName</c> araması hep 0 dönüyordu.)
    /// </summary>
    internal static int CountTables(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson)) return 0;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(schemaJson);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return 0;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.Equals("tables", StringComparison.OrdinalIgnoreCase) &&
                    prop.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    return prop.Value.GetArrayLength();
                }
            }
            return 0;
        }
        catch (System.Text.Json.JsonException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Admin sorgusundan gelen tarih Kind=Unspecified olabilir; Npgsql timestamptz
    /// kolonuna karşı Unspecified/Local DateTime'ı reddeder.
    /// </summary>
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static string? Cap(string? value, int max) =>
        value is null ? null : (value.Length <= max ? value : value[..max]);

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
            var search = q.Trim().ToLowerInvariant();
            query = query.Where(u =>
                (u.Email != null && u.Email.ToLowerInvariant().Contains(search)) ||
                (u.UserName != null && u.UserName.ToLowerInvariant().Contains(search)) ||
                (u.CompanyName != null && u.CompanyName.ToLowerInvariant().Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(plan) && plan != "all")
        {
            var p = plan.Trim().ToLowerInvariant();
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
        if (targetPlan == "dev")
        {
            // IsDev sınırsız kotalı süper yetkidir ve yalnızca yapılandırmadaki Dev:Email hesabına
            // aittir; bir admin ucu bunu başka bir hesaba (ya da kendine) veremez.
            return BadRequest(new { error = "The dev plan cannot be assigned from the admin panel. It is reserved for the configured Dev:Email account." });
        }
        if (targetPlan is not ("free" or "pro" or "team"))
        {
            return BadRequest(new { error = "Invalid plan code. Valid values: free, pro, team." });
        }

        // IsDev'e DOKUNULMAZ: plan değişikliği hesabın dev/admin durumunu ne yükseltir ne düşürür.
        user.PlanCode = targetPlan;

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

        quota.DailyLimit = user.IsDev ? int.MaxValue : targetPlan switch
        {
            "team" => 2000,
            "pro" => 500,
            _ => 100
        };

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            _logger.LogWarning("Admin plan update failed for User {UserId}: {Errors}",
                user.Id, string.Join("; ", updateResult.Errors.Select(e => e.Code)));
            return BadRequest(new
            {
                error = "Plan could not be updated.",
                details = updateResult.Errors.Select(e => e.Description).ToArray()
            });
        }
        await _context.SaveChangesAsync();

        _logger.LogInformation("Admin {AdminEmail} updated User {UserEmail} plan to {Plan}", admin.Email, user.Email, targetPlan);

        // Etkin plan, kayıtlı plan kodundan farklı olabilir: PlanQuotas.Resolve aktif abonelik
        // yoksa plan kodunu okumaz. Admin "Pro yaptım" derken kullanıcının hâlâ Free
        // olduğunu görebilmeli.
        var effective = PlanQuotas.Resolve(user.SubscriptionStatus, user.PlanCode, user.IsDev);
        var effectivePlan = effective.ToString().ToLowerInvariant();
        var message = $"User plan updated to {targetPlan.ToUpperInvariant()} successfully.";
        if (!string.Equals(effectivePlan, targetPlan, StringComparison.Ordinal))
        {
            message += $" Note: the effective plan is {effectivePlan.ToUpperInvariant()} because plan codes only apply with an active subscription (or the dev flag).";
        }

        return Ok(new
        {
            id = user.Id,
            planCode = targetPlan,
            effectivePlan,
            isDev = user.IsDev,
            dailyLimit = quota.DailyLimit,
            message
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
                // int taşması limiti negatife çevirip kullanıcıyı kilitlerdi; int.MaxValue ise
                // "sınırsız" işaretidir, bonusla ona ulaşılmamalı.
                var bonus = Math.Min(dto.BonusPoints.Value, MaxBonusPoints);
                quota.DailyLimit = (int)Math.Min((long)quota.DailyLimit + bonus, int.MaxValue - 1);
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
            query = query.Where(f => f.Category.ToLowerInvariant() == category.Trim().ToLowerInvariant());
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

    /// <summary>
    /// FAZ 2: Token & AI Kullanım Analizi (Modeller, Gelişmiş Ayarlar, Günlük/Aylık Trendler).
    /// </summary>
    [HttpGet("tokens/stats")]
    public async Task<IActionResult> GetTokenStats(
        [FromQuery] int days = 14,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] string? interval = "daily")
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        if (days < 1) days = 14;
        if (days > 180) days = 180;

        var filterStartDate = AsUtc(startDate ?? DateTime.UtcNow.Date.AddDays(-days));
        var filterEndDate = AsUtc(endDate ?? DateTime.UtcNow.Date.AddDays(1));

        // Gerçek istek sayıları: ölçüm olayları (UsageEvent) günlük gruplanır. Token'dan
        // "tahmini istek" türetmek uydurmaydı; olay yoksa istek sayısı 0'dır.
        var requestsByDay = new Dictionary<string, int>();
        var totalAiCalls = 0;
        try
        {
            var perDay = await _context.UsageEvents
                .AsNoTracking()
                .Where(e => e.Resource == UsageResource.AiCall && e.OccurredAt >= filterStartDate && e.OccurredAt <= filterEndDate)
                .GroupBy(e => e.OccurredAt.Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToListAsync();
            foreach (var row in perDay)
            {
                requestsByDay[row.Day.ToString("yyyy-MM-dd")] = row.Count;
                totalAiCalls += row.Count;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token stats: AI çağrı olayları okunamadı, istek sayıları 0 gösterilecek.");
        }

        // Geçmiş günler: GlobalAiUsages
        var historicalRaw = await _context.GlobalAiUsages
            .AsNoTracking()
            .Where(g => g.Date >= filterStartDate && g.Date <= filterEndDate)
            .OrderBy(g => g.Date)
            .Select(g => new { g.Date, g.TokensUsed })
            .ToListAsync();

        var timeline = historicalRaw
            .Select(g =>
            {
                var date = g.Date.ToString("yyyy-MM-dd");
                return new
                {
                    date,
                    tokens = g.TokensUsed,
                    requests = requestsByDay.GetValueOrDefault(date, 0)
                };
            })
            .ToList();

        // Bugünkü anlık tüketim
        long todayTokens = 0;
        try
        {
            todayTokens = await _context.UserAIQuotas.SumAsync(q => (long)q.DailyUsageCount);
        }
        catch { }

        var todayDateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (!timeline.Any(h => h.date == todayDateStr))
        {
            timeline.Add(new
            {
                date = todayDateStr,
                tokens = todayTokens,
                requests = requestsByDay.GetValueOrDefault(todayDateStr, 0)
            });
        }

        // En çok kullanılan veritabanı motorları (yalnızca gerçek proje kayıtları; veri yoksa boş)
        var serverDistribution = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var projects = await _context.CloudProjects.AsNoTracking().Select(p => p.DbType).ToListAsync();
        foreach (var p in projects)
        {
            var db = string.IsNullOrWhiteSpace(p) ? "Unknown" : p.Trim();
            serverDistribution[db] = serverDistribution.GetValueOrDefault(db, 0) + 1;
        }

        // Model bazında kullanım henüz ölçülmüyor: uydurma dağılım yerine boş döner
        // (arayüz alanı zaten opsiyonel işliyor).
        var modelDistribution = new Dictionary<string, int>();

        // Gelişmiş AI tercihi kullanımı ölçülmüyor: toplam gerçek AI çağrısı döner, oran bilinmiyor (0).
        var totalPrompts = totalAiCalls;
        var advancedUsed = 0;

        // En çok tüketen kullanıcılar
        var topUsers = await _context.UserAIQuotas
            .AsNoTracking()
            .Include(q => q.User)
            .OrderByDescending(q => q.DailyUsageCount)
            .Take(5)
            .Select(q => new
            {
                id = q.UserId,
                email = q.User.Email,
                username = q.User.UserName,
                dailyUsage = q.DailyUsageCount,
                dailyLimit = q.DailyLimit,
                plan = q.User.PlanCode ?? "free"
            })
            .ToListAsync();

        return Ok(new
        {
            timeline,
            modelDistribution,
            serverDistribution,
            advancedPreferences = new
            {
                totalPrompts,
                advancedCount = advancedUsed,
                ratioPercent = 0.0
            },
            topUsers
        });
    }

    /// <summary>
    /// FAZ 2: Oturumlar & Kullanıcı Gezinme Analizi.
    /// </summary>
    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions([FromQuery] int limit = 50)
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        var sessions = _telemetry.GetRecentSessions(limit);
        var overview = _telemetry.GetAnalyticsOverview();

        return Ok(new
        {
            sessions,
            overview
        });
    }

    /// <summary>
    /// FAZ 2: İstemci telemetri ve tıklama akışı alma ucu (Ziyaretçiler dahil).
    /// </summary>
    [AllowAnonymous]
    [RequestSizeLimit(16_384)]
    // Kimliksiz uç: "sensitive" (dk'da 5) tıklama akışı için çok dar; mevcut en geniş
    // kullanıcı/IP bölümlü politika kullanılıyor.
    [EnableRateLimiting("gateway")]
    [HttpPost("telemetry/track")]
    public IActionResult TrackClientEvent([FromBody] TelemetryEventItem item)
    {
        if (item == null) return BadRequest();

        // Kimlik istemciden ASLA alınmaz: aksi hâlde anonim biri admin panelindeki oturum
        // ve log listesine başkası adına kayıt yazabilir. Oturum yoksa null kalır.
        var authenticated = User?.Identity?.IsAuthenticated == true;
        item.UserId = authenticated ? User!.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        item.UserEmail = authenticated ? User!.FindFirstValue(ClaimTypes.Email) : null;

        // Bellekte tutulan (5000 olay) kuyruğu şişirmesin diye alan uzunlukları sınırlı.
        item.Id = Guid.NewGuid().ToString("N");
        item.SessionId = Cap(item.SessionId, 256) ?? string.Empty;
        item.Path = Cap(item.Path, 256) ?? string.Empty;
        item.Action = Cap(item.Action, 256) ?? string.Empty;
        item.Module = Cap(item.Module, 256) ?? "general";
        item.Metadata = Cap(item.Metadata, 2048);
        item.DurationSeconds = Math.Clamp(item.DurationSeconds, 0, 86_400);

        item.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        item.UserAgent = Cap(Request.Headers.UserAgent.ToString(), 256);

        // "gateway" politikası bölüm anahtarını X-Namines-Key başlığından da türetir; kimliksiz bir
        // istemci o başlığı değiştirerek sınırı aşabilir. IP başına sayaç bunu kapatır.
        if (!_telemetry.TryAcceptFrom(item.IpAddress))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Too many telemetry events." });
        }

        _telemetry.Track(item);
        return Ok(new { success = true });
    }

    /// <summary>
    /// FAZ 2: Projeler & Tablo/Depolama Analizi (Kaç tablo, kaç MB, hangi motor).
    /// </summary>
    [HttpGet("projects/stats")]
    public async Task<IActionResult> GetProjectStats([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        if (page < 1) page = 1;
        if (pageSize < 5) pageSize = 5;

        var totalProjects = await _context.CloudProjects.CountAsync();

        var projectsRaw = await _context.CloudProjects
            .AsNoTracking()
            .Include(p => p.User)
            .OrderByDescending(p => p.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var projectIds = projectsRaw.Select(p => p.Id).ToList();
        var vaultBackupCounts = await _context.VaultBackups
            .AsNoTracking()
            .Where(b => projectIds.Contains(b.ProjectId))
            .GroupBy(b => b.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count(), TotalBytes = g.Sum(x => x.SizeBytes) })
            .ToDictionaryAsync(x => x.ProjectId, x => new { x.Count, x.TotalBytes });

        var groundDbStatuses = (await _context.GroundDatabases
            .AsNoTracking()
            .Where(g => projectIds.Contains(g.ProjectId))
            .Select(g => g.ProjectId)
            .ToListAsync()).ToHashSet();

        var items = projectsRaw.Select(p =>
        {
            var tableCount = CountTables(p.SchemaJson);

            vaultBackupCounts.TryGetValue(p.Id, out var vb);
            long backupBytes = vb?.TotalBytes ?? 0;
            // Ölçülen depolama: şema JSON'unun bayt boyutu + gerçek Vault yedekleri.
            // Eski formül (tablo*0.45 + 1.2 MB) uydurma bir "tahmin"di.
            long schemaBytes = string.IsNullOrEmpty(p.SchemaJson) ? 0 : System.Text.Encoding.UTF8.GetByteCount(p.SchemaJson);
            double estimatedMb = Math.Round((backupBytes + schemaBytes) / BytesPerMb, 2);

            return new
            {
                id = p.Id,
                name = p.Name,
                ownerId = p.UserId,
                ownerEmail = p.User?.Email ?? "Bilinmiyor",
                ownerUsername = p.User?.UserName ?? "Kullanıcı",
                dbType = string.IsNullOrWhiteSpace(p.DbType) ? "PostgreSQL" : p.DbType,
                tableCount,
                estimatedMb,
                vaultBackupsCount = vb?.Count ?? 0,
                hasGroundDatabase = groundDbStatuses.Contains(p.Id),
                createdAt = p.CreatedAt,
                updatedAt = p.UpdatedAt
            };
        }).ToList();

        // Toplamlar TÜM projeler üzerinden gerçek değerlerle hesaplanır (sayfa toplamı değil,
        // taban değer de yok). Şemalar tek tek akıtılır; hepsi belleğe alınmaz.
        long totalTablesCount = 0;
        await foreach (var schemaJson in _context.CloudProjects.AsNoTracking().Select(p => p.SchemaJson).AsAsyncEnumerable())
        {
            totalTablesCount += CountTables(schemaJson);
        }
        var totalBackupBytes = await _context.VaultBackups.AsNoTracking().SumAsync(b => (long?)b.SizeBytes) ?? 0L;
        var totalSchemaChars = await _context.CloudProjects.AsNoTracking()
            .SumAsync(p => (long?)(p.SchemaJson == null ? 0 : p.SchemaJson.Length)) ?? 0L;

        return Ok(new
        {
            totalProjects,
            totalTables = totalTablesCount,
            totalStorageMb = Math.Round((totalBackupBytes + totalSchemaChars) / BytesPerMb, 2),
            items,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)totalProjects / pageSize)
        });
    }

    /// <summary>
    /// FAZ 2: Sistem Sağlık & Otonom Denetim Botu (Diagnostics).
    /// </summary>
    [HttpGet("bot/diagnostics")]
    public async Task<IActionResult> GetBotDiagnostics()
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        var report = _telemetry.GetLastAuditReport();
        if (report == null)
        {
            report = await _telemetry.RunSystemAuditAsync(_context, _configuration, _logger);
        }

        return Ok(report);
    }

    /// <summary>
    /// FAZ 2: Denetim Botunu Canlı Çalıştırma.
    /// </summary>
    [HttpPost("bot/run-audit")]
    public async Task<IActionResult> RunBotAudit()
    {
        try
        {
            var admin = await EnsureAdminAsync();
            if (admin == null) return StatusCode(403, new { error = "Admin access required." });

            _logger.LogInformation("Admin {AdminEmail} triggered on-demand autonomous bot audit", admin.Email);
            var freshReport = await _telemetry.RunSystemAuditAsync(_context, _configuration, _logger);
            return Ok(freshReport);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while running on-demand bot audit");
            // Denetimin kendisi çöktüyse hiçbir servis için "pass" uydurulmaz.
            return Ok(new SystemBotAuditReport
            {
                AuditTimestamp = DateTime.UtcNow,
                HealthScore = 0,
                OverallStatus = "Degraded",
                SummaryText = "Otonom denetim botu çalıştırılamadı; servis durumu doğrulanamadı.",
                Probes = new List<SystemBotProbeResult>
                {
                    new SystemBotProbeResult { ServiceName = "Audit Runner", Status = "fail", LatencyMs = 0, Message = "Denetim çalıştırılamadı. Ayrıntı için sunucu loglarına bakın." }
                }
            });
        }
    }

    /// <summary>
    /// FAZ 3: Sistem Hata & İstisna Logları Listesi.
    /// </summary>
    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? level = null,
        [FromQuery] string? source = null,
        [FromQuery] bool? resolved = null,
        [FromQuery] int limit = 100)
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        var logs = _telemetry.GetLogs(level, source, resolved, limit);
        var allLogs = _telemetry.GetLogs(limit: 500);

        var totalErrors = allLogs.Count(l => l.Level.Equals("Error", StringComparison.OrdinalIgnoreCase) || l.Level.Equals("Critical", StringComparison.OrdinalIgnoreCase));
        var totalWarnings = allLogs.Count(l => l.Level.Equals("Warning", StringComparison.OrdinalIgnoreCase));
        var unresolvedCount = allLogs.Count(l => !l.Resolved);

        return Ok(new
        {
            logs,
            stats = new
            {
                totalErrors,
                totalWarnings,
                unresolvedCount,
                totalRecorded = allLogs.Count
            }
        });
    }

    /// <summary>
    /// FAZ 3: Hatayı İncelendi / Çözüldü Olarak İşaretle.
    /// </summary>
    [HttpPost("logs/{id}/resolve")]
    public async Task<IActionResult> ResolveLog(string id)
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        var success = _telemetry.ResolveLog(id);
        if (!success) return NotFound(new { error = "Log not found." });

        return Ok(new { message = "Hata kaydı çözüldü olarak işaretlendi." });
    }

    /// <summary>
    /// FAZ 3: Hata Loglarını Temizle / Sıfırla.
    /// </summary>
    [HttpDelete("logs")]
    public async Task<IActionResult> ClearLogs()
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        _telemetry.ClearLogs();
        return Ok(new { message = "Hata kayıtları temizlendi." });
    }

    /// <summary>
    /// FAZ 3: Test Amaçlı Canlı Hata Simülasyonu.
    /// </summary>
    [HttpPost("logs/simulate")]
    public async Task<IActionResult> SimulateError([FromBody] SimulateErrorRequest? req)
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        var simLog = new AdminSystemLogItem
        {
            Level = req?.Level ?? "Error",
            Source = req?.Source ?? "AI Gateway",
            Message = req?.Message ?? "Simulated diagnostic alert: response threshold exceeded 5000ms.",
            StackTrace = "at Namines.API.Controllers.AdminController.SimulateError() in AdminController.cs:line 780",
            Path = "/api/admin/logs/simulate",
            UserEmail = admin.Email,
            Resolved = false
        };

        _telemetry.RecordLog(simLog);
        return Ok(new { message = "Simüle hata kaydı başarıyla oluşturuldu.", log = simLog });
    }

    /// <summary>
    /// FAZ 3: Otonom Bot Denetim Geçmişi ve Aktif Olaylar (Incidents).
    /// </summary>
    [HttpGet("bot/history")]
    public async Task<IActionResult> GetBotHistory()
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        var history = _telemetry.GetAuditHistory(20);
        var incidents = _telemetry.GetActiveIncidents();

        return Ok(new
        {
            history,
            incidents
        });
    }

    /// <summary>
    /// FAZ 4: Proje Modül Bazlı Derin İnceleme (Vault, Flow, Ground, Desk, Compile).
    /// </summary>
    [HttpGet("projects/{id}/deep-stats")]
    public async Task<IActionResult> GetProjectDeepStats(string id)
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        var project = await _context.CloudProjects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (project == null) return NotFound(new { error = "Project not found." });

        // Gerçek veritabanı varlıkları sorgulanır (M-3):
        var tableCount = CountTables(project.SchemaJson);
        var baseMb = Math.Round((string.IsNullOrEmpty(project.SchemaJson) ? 0 : System.Text.Encoding.UTF8.GetByteCount(project.SchemaJson)) / BytesPerMb, 2);

        // Vault: Bu projeye ait gerçek yedekler
        var backups = await _context.VaultBackups.AsNoTracking()
            .Where(b => b.ProjectId == id)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();
        var vaultBackups = backups.Count;
        var vaultTotalMb = Math.Round(backups.Sum(b => b.SizeBytes) / (1024.0 * 1024.0), 2);
        var lastBackup = backups.FirstOrDefault();

        // Flow: Bu projeye ait gerçek otomasyon kuralları ve çalışma logları
        var flowRules = await _context.AutomationRules.AsNoTracking()
            .Where(r => r.ProjectId == id)
            .ToListAsync();
        var flowRulesCount = flowRules.Count;
        var ruleIds = flowRules.Select(r => r.Id).ToList();
        var flowExecs = ruleIds.Count > 0
            ? await _context.AutomationRunLogs.AsNoTracking().CountAsync(l => ruleIds.Contains(l.RuleId))
            : 0;
        var lastTriggeredAt = ruleIds.Count > 0
            ? await _context.AutomationRunLogs.AsNoTracking()
                .Where(l => ruleIds.Contains(l.RuleId))
                .OrderByDescending(l => l.TriggeredAt)
                .Select(l => (DateTime?)l.TriggeredAt)
                .FirstOrDefaultAsync()
            : null;

        // Ground: Yönetilen DB kaydı
        var groundDb = await _context.GroundDatabases.AsNoTracking()
            .FirstOrDefaultAsync(g => g.ProjectId == id);
        var hasGround = groundDb != null;

        // Desk: Bu projenin SQL geçmişi
        var deskQueries = await _context.SqlQueryHistory.AsNoTracking()
            .CountAsync(q => q.ProjectId == id);
        var lastDeskQuery = await _context.SqlQueryHistory.AsNoTracking()
            .Where(q => q.ProjectId == id)
            .OrderByDescending(q => q.CreatedAt)
            .FirstOrDefaultAsync();

        // Compile: Change request sayısı
        var ddlGenerated = await _context.ChangeRequests.AsNoTracking()
            .CountAsync(c => c.ProjectId == id);
        var hasActiveMigrations = await _context.ChangeRequests.AsNoTracking()
            .AnyAsync(c => c.ProjectId == id && (c.Status == ChangeRequestStatus.Approved || c.Status == ChangeRequestStatus.PendingReview));

        return Ok(new
        {
            projectId = project.Id,
            projectName = project.Name,
            dbType = project.DbType,
            tableCount,
            estimatedMb = baseMb,
            vault = new
            {
                snapshotsCount = vaultBackups,
                totalStorageMb = vaultTotalMb,
                lastBackupAt = lastBackup?.CreatedAt,
                encryption = "AES-256-GCM",
                retentionPolicy = "30 Gün"
            },
            flow = new
            {
                rulesCount = flowRulesCount,
                totalExecutions = flowExecs,
                lastTriggeredAt = lastTriggeredAt,
                activeTriggers = flowRules.Select(r => r.TriggerType).Distinct().ToArray()
            },
            ground = new
            {
                isHosted = hasGround,
                status = hasGround ? groundDb!.Status.ToString() : "Not Provisioned",
                hostEndpoint = hasGround ? $"{groundDb!.Provider} ({groundDb!.Region ?? "local"})" : null,
                activeConnections = 0, // canlı bağlantı sayısı ölçülmüyor
                allocatedMb = 0 // ayrılan alan ölçülmüyor
            },
            desk = new
            {
                totalQueriesExecuted = deskQueries,
                lastQueryAt = lastDeskQuery?.CreatedAt,
                exportCount = 0
            },
            compile = new
            {
                ddlGeneratedCount = ddlGenerated,
                lastExportFormat = project.DbType ?? "PostgreSQL",
                hasActiveMigrations = hasActiveMigrations
            }
        });
    }

    /// <summary>
    /// FAZ 4: Belirli Kullanıcının Oturum Geçmişi & Sayfa Gezinmeleri.
    /// </summary>
    [HttpGet("users/{id}/sessions")]
    public async Task<IActionResult> GetUserSessions(string id)
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        var targetUser = await _userManager.FindByIdAsync(id);
        if (targetUser == null) return NotFound(new { error = "User not found." });

        var allSessions = _telemetry.GetRecentSessions(100);
        var userSessions = allSessions.Where(s => s.UserId == id || (!string.IsNullOrEmpty(targetUser.Email) && s.UserEmail.Equals(targetUser.Email, StringComparison.OrdinalIgnoreCase))).ToList();

        return Ok(new
        {
            userId = targetUser.Id,
            userEmail = targetUser.Email,
            sessions = userSessions,
            totalSessions = userSessions.Count,
            totalDurationMinutes = userSessions.Sum(s => s.DurationMinutes)
        });
    }

    /// <summary>
    /// FAZ 4: Sistem Verilerini CSV Olarak Dışa Aktarma.
    /// </summary>
    [HttpGet("export/csv")]
    public async Task<IActionResult> ExportCsv([FromQuery] string type = "users")
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        // Dosya adına serbest metin girmesin: bilinmeyen tür "users" sayılır.
        var kind = (type ?? "users").Trim().ToLowerInvariant();
        if (kind is not ("projects" or "logs" or "feedbacks" or "sessions" or "tokens")) kind = "users";

        var sb = new System.Text.StringBuilder();

        if (kind == "projects")
        {
            sb.AppendLine("ID,Name,OwnerEmail,Engine,TableCount,StorageMb,CreatedAt");
            var projs = await _context.CloudProjects.AsNoTracking().Include(p => p.User).Take(500).ToListAsync();
            var projIds = projs.Select(p => p.Id).ToList();
            var backupBytes = await _context.VaultBackups.AsNoTracking()
                .Where(b => projIds.Contains(b.ProjectId))
                .GroupBy(b => b.ProjectId)
                .Select(g => new { ProjectId = g.Key, Bytes = g.Sum(x => x.SizeBytes) })
                .ToDictionaryAsync(x => x.ProjectId, x => x.Bytes);
            foreach (var p in projs)
            {
                var schemaBytes = string.IsNullOrEmpty(p.SchemaJson) ? 0 : System.Text.Encoding.UTF8.GetByteCount(p.SchemaJson);
                var storageMb = Math.Round((schemaBytes + backupBytes.GetValueOrDefault(p.Id, 0L)) / BytesPerMb, 2);
                sb.AppendLine(AdminCsv.Row(p.Id, p.Name ?? "Unnamed", p.User?.Email, p.DbType, CountTables(p.SchemaJson), storageMb, p.CreatedAt));
            }
        }
        else if (kind == "logs")
        {
            sb.AppendLine("ID,Timestamp,Level,Source,Message,Path,UserEmail,Resolved");
            var logs = _telemetry.GetLogs(limit: 500);
            foreach (var l in logs)
            {
                sb.AppendLine(AdminCsv.Row(l.Id, l.Timestamp, l.Level, l.Source, l.Message, l.Path, l.UserEmail, l.Resolved));
            }
        }
        else if (kind == "feedbacks")
        {
            sb.AppendLine("ID,Category,UserEmail,Message,CreatedAt");
            var feedbacks = await _context.Feedbacks.AsNoTracking().OrderByDescending(f => f.CreatedAt).Take(500).ToListAsync();
            foreach (var f in feedbacks)
            {
                sb.AppendLine(AdminCsv.Row(f.Id, f.Category, f.Email, f.Message, f.CreatedAt));
            }
        }
        else if (kind == "sessions")
        {
            sb.AppendLine("SessionId,UserId,UserEmail,StartedAt,LastActiveAt,DurationMinutes,StartMode,TotalClicks,PagesVisited");
            var sessions = _telemetry.GetRecentSessions(limit: 500);
            foreach (var s in sessions)
            {
                var pages = string.Join(" > ", s.PagesVisited ?? new List<string>());
                sb.AppendLine(AdminCsv.Row(s.SessionId, s.UserId, s.UserEmail, s.StartedAt, s.LastActiveAt, s.DurationMinutes, s.StartMode, s.TotalClicks, pages));
            }
        }
        else if (kind == "tokens")
        {
            // Gerçek ölçülen alanlar: günlük token ve AI çağrı sayısı. Prompt/completion
            // ayrımı kaydedilmiyor; eski %65/%35 sabit oranı uydurmaydı → boş bırakılır.
            sb.AppendLine("Date,TokensUsed,EstimatedRequests,PromptTokens,CompletionTokens");
            var now = DateTime.UtcNow.Date;
            var from = now.AddDays(-30);
            var usages = await _context.GlobalAiUsages.AsNoTracking()
                .Where(g => g.Date >= from)
                .ToDictionaryAsync(g => g.Date.Date, g => g.TokensUsed);
            var callsPerDay = (await _context.UsageEvents.AsNoTracking()
                .Where(e => e.Resource == UsageResource.AiCall && e.OccurredAt >= from)
                .GroupBy(e => e.OccurredAt.Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToListAsync())
                .ToDictionary(x => x.Day.Date, x => x.Count);

            for (int i = 29; i >= 0; i--)
            {
                var d = now.AddDays(-i);
                var tokens = usages.TryGetValue(d, out var t) ? t : 0;
                var requests = callsPerDay.TryGetValue(d, out var c) ? c : 0;
                sb.AppendLine(AdminCsv.Row(d.ToString("yyyy-MM-dd"), tokens, requests, null, null));
            }
        }
        else
        {
            sb.AppendLine("ID,UserName,Email,Plan,DailyUsage,DailyLimit,CreatedAt");
            var users = await _context.Users.AsNoTracking().Take(500).ToListAsync();
            var quotas = await _context.UserAIQuotas.AsNoTracking().ToDictionaryAsync(q => q.UserId, q => q);
            foreach (var u in users)
            {
                quotas.TryGetValue(u.Id, out var quota);
                var dailyUsage = quota?.DailyUsageCount ?? 0;
                // Kota satırı yoksa GetUsers ile aynı varsayılan (dev sınırsız, diğerleri 100).
                var dailyLimit = quota?.DailyLimit ?? (u.IsDev ? int.MaxValue : 100);
                var plan = u.IsDev ? "dev" : (string.IsNullOrWhiteSpace(u.PlanCode) ? "free" : u.PlanCode);
                sb.AppendLine(AdminCsv.Row(u.Id, u.UserName, u.Email, plan, dailyUsage, dailyLimit, u.CreatedAt));
            }
        }

        // BOM'suz UTF-8; hücreler AdminCsv ile formül enjeksiyonuna karşı kaçışlanmış durumda.
        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"namines-export-{kind}-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    // ── Özellik Bayrakları (Kill-Switch) Yönetimi ─────────────────────────────
    [HttpGet("features")]
    public async Task<IActionResult> GetFeatures()
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        if (_featureFlags == null) return Ok(Array.Empty<AdminFeatureFlag>());
        var flags = await _featureFlags.GetAllFlagsAsync();
        return Ok(flags);
    }

    [HttpPut("features/{key}")]
    public async Task<IActionResult> UpdateFeature(string key, [FromBody] UpdateFeatureFlagRequest request)
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        if (_featureFlags == null) return BadRequest("Feature flag service is not available.");
        if (string.IsNullOrWhiteSpace(key)) return BadRequest("Feature key is required.");

        // Yalnızca katalogdaki bayraklar güncellenir; yazım hatası sessizce yeni bayrak yaratmasın.
        if (await _featureFlags.GetFlagAsync(key) == null)
        {
            return NotFound(new { error = $"Unknown feature key '{key}'." });
        }

        try
        {
            // Mod verilmediyse (null) servis mevcut modu korur; "Hidden" bayrak DisabledNotice'a dönmez.
            await _featureFlags.UpdateFlagAsync(
                key,
                request.IsEnabled,
                request.Mode ?? string.Empty,
                request.NoticeMessage);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var updated = await _featureFlags.GetFlagAsync(key);
        return Ok(updated);
    }

    // ── AI Model Yönlendirme (Tool-Based Routing) ───────────────────────────
    [HttpGet("ai-routes")]
    public async Task<IActionResult> GetAiRoutes()
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        if (_aiRouter == null) return Ok(new { routes = Array.Empty<AiToolRoute>(), providers = Array.Empty<string>() });
        var routes = await _aiRouter.GetAllRoutesAsync();
        var providers = _aiRouter.GetAvailableProviders();
        return Ok(new { routes, providers });
    }

    [HttpPut("ai-routes/{toolKey}")]
    public async Task<IActionResult> UpdateAiRoute(string toolKey, [FromBody] UpdateAiRouteRequest request)
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        if (_aiRouter == null) return BadRequest("AI router service is not available.");
        if (string.IsNullOrWhiteSpace(toolKey)) return BadRequest("Tool key is required.");

        await _aiRouter.UpdateRouteAsync(
            toolKey,
            request.Provider,
            request.ModelName,
            request.Temperature,
            request.FallbackProvider);

        var updated = await _aiRouter.GetRouteAsync(toolKey);
        return Ok(updated);
    }
}

public class UpdateFeatureFlagRequest
{
    public bool IsEnabled { get; set; }
    public string? Mode { get; set; } // "DisabledNotice" | "Hidden"
    public string? NoticeMessage { get; set; }
}

public class UpdateAiRouteRequest
{
    public string Provider { get; set; } = "groq";
    public string ModelName { get; set; } = "qwen-2.5-coder-32b";
    public double Temperature { get; set; } = 0.2;
    public string? FallbackProvider { get; set; }
}

public class SimulateErrorRequest
{
    public string? Level { get; set; }
    public string? Source { get; set; }
    public string? Message { get; set; }
}


/// <summary>
/// CSV hücre kaçışlama. Admin dışa aktarımı kullanıcı kaynaklı metin (proje adı, geri bildirim,
/// log mesajı) içerir; Excel/Sheets bunu açınca '=' '+' '-' '@' ile başlayan hücreyi formül
/// olarak çalıştırır (CSV injection). Bu yüzden metin hücrelerin başına tek tırnak konur,
/// çift tırnaklar ikilenir ve HER hücre tırnaklanır.
/// </summary>
public static class AdminCsv
{
    public static string Field(object? value)
    {
        string text = value switch
        {
            null => string.Empty,
            string s => s,
            DateTime dt => dt.ToString("s", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("s", System.Globalization.CultureInfo.InvariantCulture),
            bool b => b ? "True" : "False",
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        // Sayısal hücreler kendi başına formül tehlikesi taşımaz (negatif sayı '-' ile başlar);
        // yalnızca string kaynaklı metin tehlikeli önekler için kontrol edilir.
        if (value is string && text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            text = "'" + text;
        }

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    public static string Row(params object?[] cells) => string.Join(",", cells.Select(Field));
}
