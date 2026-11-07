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
using Namines.Ops.Application;
using Namines.Ops.Domain;
using Namines.Infrastructure.Data;

using Namines.API.Services;
using Namines.Desk.Abstractions;
using Namines.Flow.Abstractions;
using Namines.Ground.Abstractions;
using Namines.Vault.Abstractions;

namespace Namines.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IIdentityAdmin _users;
    private readonly IBillingAccounts _billing;
    private readonly IGovernanceAdminQueries _governance;
    private readonly ILogger<AdminController> _logger;
    private readonly AdminTelemetryService _telemetry;
    private readonly IConfiguration _configuration;
    private readonly IOpsAdminService _ops;
    private readonly IStudioAdminQueries _studio;
    private readonly IStudioProjects _projects;
    private readonly IIdentityDirectory _identity;
    private readonly IAiToolRouter? _aiRouter;
    private readonly IVaultModule? _vault;
    private readonly IGroundModule? _ground;
    private readonly IFlowModule? _flow;
    private readonly IDeskModule? _desk;

    public AdminController(
        UserManager<ApplicationUser> userManager,
        IIdentityAdmin users,
        IBillingAccounts billing,
        IGovernanceAdminQueries governance,
        ILogger<AdminController> logger,
        AdminTelemetryService telemetry,
        IConfiguration configuration,
        IOpsAdminService ops,
        IStudioAdminQueries studio,
        IStudioProjects projects,
        IIdentityDirectory identity,
        IAiToolRouter? aiRouter = null,
        IVaultModule? vault = null,
        IGroundModule? ground = null,
        IFlowModule? flow = null,
        IDeskModule? desk = null)
    {
        _userManager = userManager;
        _users = users;
        _billing = billing;
        _governance = governance;
        _logger = logger;
        _telemetry = telemetry;
        _configuration = configuration;
        _ops = ops;
        _studio = studio;
        _projects = projects;
        _identity = identity;
        _aiRouter = aiRouter;
        _vault = vault;
        _ground = ground;
        _flow = flow;
        _desk = desk;
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

        var totalUsers = await _users.CountUsersAsync();
        var totalProjects = await _studio.CountProjectsAsync();
        var totalFeedbacks = await _ops.CountFeedbacksAsync(admin.Id);
        var activeDatabases = _ground != null ? await _ground.Databases.CountTotalDatabasesAsync() : 0;

        var devIds = (await _identity.ListDevUserIdsAsync()).ToHashSet();
        var devUsers = devIds.Count;
        var (proUsers, teamUsers) = await _billing.CountPaidPlansAsync(devIds);
        // free: dev olmayan ve ücretli plan kodu taşımayanlar (plan kodu boş ya da "free")
        var paidNonDev = (await _billing.ListUserIdsWithPaidPlanAsync()).Count(id => !devIds.Contains(id));
        var freeUsers = totalUsers - devUsers - paidNonDev;

        long totalQuotaUsedToday = 0;
        try
        {
            totalQuotaUsedToday = await _billing.SumQuotaUsedTodayAsync();
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
    /// Tamamlanmamış abonelik iptalleri (hesabı silinmiş ama Stripe'ta iptal edilmemiş). <c>stuck</c>: uyarı
    /// eşiğini aşmış, müşteri ücretlendirilmeye devam ediyor olabilir; elle bakılmalı.
    /// </summary>
    [HttpGet("subscription-cancellations")]
    public async Task<IActionResult> GetPendingSubscriptionCancellations(CancellationToken ct)
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        var backlog = await _billing.GetCancellationBacklogAsync(200, ct);

        // total ve stuck listenin degil kuyrugun GERCEK sayisi: 200'den fazla acik satir varsa da dogru gorunur.
        return Ok(new
        {
            total = backlog.Total,
            stuck = backlog.Stuck,
            items = backlog.Items.Select(c => new { c.Id, c.SubscriptionId, c.CreatedAt, c.Attempts, c.LastAttemptAt, c.NextAttemptAt, stuck = c.Stuck }).ToList(),
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

        // Plan Billing'de, kimlik Identity'de: plan suzgeci once Billing'den kimlik kumesine cevrilir.
        IReadOnlyCollection<string>? restrict = null, exclude = null;
        bool? devOnly = null;
        var excludeDev = false;
        if (!string.IsNullOrWhiteSpace(plan) && plan != "all")
        {
            var p = plan.Trim().ToLowerInvariant();
            if (p == "dev") devOnly = true;
            else if (p == "free") { excludeDev = true; exclude = await _billing.ListUserIdsWithPaidPlanAsync(); }
            else { excludeDev = true; restrict = await _billing.ListUserIdsByPlanAsync(p); }
        }

        var found = await _users.QueryUsersAsync(new AdminUserQuery(q, restrict, exclude, devOnly, excludeDev, (page - 1) * pageSize, pageSize));
        var totalCount = found.Total;
        var users = found.Users;
        var userIds = users.Select(u => u.Id).ToList();

        var quotas = await _billing.GetQuotaRowsAsync(userIds);
        var subscriptions = await _billing.GetSubscriptionsAsync(userIds);

        var projectCounts = await _studio.CountByOwnerAsync(userIds);

        var items = users.Select(u =>
        {
            quotas.TryGetValue(u.Id, out var quota);
            projectCounts.TryGetValue(u.Id, out var projCount);
            subscriptions.TryGetValue(u.Id, out var sub);

            var resolvedPlan = u.IsDev ? "dev" : (string.IsNullOrWhiteSpace(sub?.PlanCode) ? "free" : sub!.PlanCode!.ToLowerInvariant());

            return new
            {
                id = u.Id,
                userName = u.UserName,
                email = u.Email,
                companyName = u.CompanyName,
                createdAt = u.CreatedAt,
                isDev = u.IsDev,
                planCode = resolvedPlan,
                subscriptionStatus = sub?.Status,
                currentPeriodEnd = sub?.CurrentPeriodEnd,
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

        var quota = await _billing.GetQuotaRowAsync(id);
        var sub = await _billing.GetSubscriptionAsync(id);

        var projects = (await _studio.ListByOwnerAsync(id))
            .OrderByDescending(p => p.Id, StringComparer.Ordinal)
            .Select(p => new
            {
                id = p.Id,
                name = p.Name,
                dbType = p.DbType
            })
            .ToList();

        var feedbacks = await _ops.ListFeedbacksForUserAsync(admin.Id, id, user.Email, 10);

        return Ok(new
        {
            id = user.Id,
            userName = user.UserName,
            email = user.Email,
            companyName = user.CompanyName,
            createdAt = user.CreatedAt,
            isDev = user.IsDev,
            planCode = user.IsDev ? "dev" : (sub?.PlanCode ?? "free"),
            subscriptionStatus = sub?.Status,
            currentPeriodEnd = sub?.CurrentPeriodEnd,
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
        var quota = await _billing.AdminSetPlanAsync(id, targetPlan, user.IsDev);

        _logger.LogInformation("Admin {AdminEmail} updated User {UserEmail} plan to {Plan}", admin.Email, user.Email, targetPlan);

        // Etkin plan, kayıtlı plan kodundan farklı olabilir: PlanQuotas.Resolve aktif abonelik
        // yoksa plan kodunu okumaz. Admin "Pro yaptım" derken kullanıcının hâlâ Free
        // olduğunu görebilmeli.
        var subscription = await _billing.GetSubscriptionAsync(id);
        var effective = PlanQuotas.Resolve(subscription?.Status, subscription?.PlanCode, user.IsDev);
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

        var quota = await _billing.AdminAdjustQuotaAsync(id, user.IsDev, dto.ResetToZero, dto.BonusPoints, MaxBonusPoints);
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

        var result = await _ops.ListFeedbacksAsync(admin.Id, category, page, pageSize);

        return Ok(new
        {
            items = result.Items,
            totalCount = result.TotalCount,
            page = result.Page,
            pageSize = result.PageSize,
            totalPages = result.TotalPages
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

        if (!await _ops.DeleteFeedbackAsync(admin.Id, id))
            return NotFound(new { error = "Feedback not found." });

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
            var perDay = await _billing.ListUsageCallsPerDayAsync(filterStartDate, filterEndDate);
            foreach (var row in perDay)
            {
                requestsByDay[row.Date.ToString("yyyy-MM-dd")] = (int)row.Tokens;
                totalAiCalls += (int)row.Tokens;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token stats: AI çağrı olayları okunamadı, istek sayıları 0 gösterilecek.");
        }

        // Geçmiş günler: GlobalAiUsages
        var historicalRaw = await _billing.ListGlobalUsageAsync(filterStartDate, filterEndDate);

        var timeline = historicalRaw
            .Select(g =>
            {
                var date = g.Date.ToString("yyyy-MM-dd");
                return new
                {
                    date,
                    tokens = g.Tokens,
                    requests = requestsByDay.GetValueOrDefault(date, 0)
                };
            })
            .ToList();

        // Bugünkü anlık tüketim
        long todayTokens = 0;
        try
        {
            todayTokens = await _billing.SumQuotaUsedTodayAsync();
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
        var projects = await _studio.ListDbTypesAsync();
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
        var topQuotas = await _billing.ListTopQuotaUsersAsync(5);
        var topIds = topQuotas.Select(t => t.UserId).ToList();
        var topNames = await _identity.GetUserNamesAsync(topIds);
        var topEmails = await _identity.GetUserEmailsAsync(topIds);
        var topSubs = await _billing.GetSubscriptionsAsync(topIds);
        var topUsers = topQuotas.Select(t => new
        {
            id = t.UserId,
            email = topEmails.GetValueOrDefault(t.UserId),
            username = topNames.GetValueOrDefault(t.UserId),
            dailyUsage = t.DailyUsageCount,
            dailyLimit = t.DailyLimit,
            plan = topSubs.TryGetValue(t.UserId, out var ts) && ts.PlanCode is not null ? ts.PlanCode : "free",
        }).ToList();

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

        var totalProjects = await _studio.CountProjectsAsync();

        var projectsRaw = await _studio.PageAsync((page - 1) * pageSize, pageSize);
        var ownerIds = projectsRaw.Select(p => p.UserId).Distinct().ToList();
        var ownerEmails = await _identity.GetUserEmailsAsync(ownerIds);
        var ownerNames = await _identity.GetUserNamesAsync(ownerIds);

        var projectIds = projectsRaw.Select(p => p.Id).ToList();
        var vaultBackupCounts = _vault != null
            ? await _vault.Backups.GetProjectBackupStatsAsync(projectIds)
            : (IReadOnlyDictionary<string, (int Count, long TotalBytes)>)new Dictionary<string, (int Count, long TotalBytes)>();

        var groundDbStatuses = _ground != null
            ? await _ground.Databases.GetProjectIdsWithDatabaseAsync(projectIds)
            : (IReadOnlySet<string>)new HashSet<string>();

        var items = projectsRaw.Select(p =>
        {
            var tableCount = CountTables(p.SchemaJson);

            var hasVb = vaultBackupCounts.TryGetValue(p.Id, out var vb);
            long backupBytes = hasVb ? vb.TotalBytes : 0;
            // Ölçülen depolama: şema JSON'unun bayt boyutu + gerçek Vault yedekleri.
            // Eski formül (tablo*0.45 + 1.2 MB) uydurma bir "tahmin"di.
            long schemaBytes = string.IsNullOrEmpty(p.SchemaJson) ? 0 : System.Text.Encoding.UTF8.GetByteCount(p.SchemaJson);
            double estimatedMb = Math.Round((backupBytes + schemaBytes) / BytesPerMb, 2);

            return new
            {
                id = p.Id,
                name = p.Name,
                ownerId = p.UserId,
                ownerEmail = ownerEmails.GetValueOrDefault(p.UserId) ?? "Bilinmiyor",
                ownerUsername = ownerNames.GetValueOrDefault(p.UserId) ?? "Kullanıcı",
                dbType = string.IsNullOrWhiteSpace(p.DbType) ? "PostgreSQL" : p.DbType,
                tableCount,
                estimatedMb,
                vaultBackupsCount = hasVb ? vb.Count : 0,
                hasGroundDatabase = groundDbStatuses.Contains(p.Id),
                createdAt = p.CreatedAt,
                updatedAt = p.UpdatedAt
            };
        }).ToList();

        // Toplamlar TÜM projeler üzerinden gerçek değerlerle hesaplanır (sayfa toplamı değil,
        // taban değer de yok). Şemalar tek tek akıtılır; hepsi belleğe alınmaz.
        long totalTablesCount = 0;
        await foreach (var schemaJson in _studio.StreamSchemasAsync())
        {
            totalTablesCount += CountTables(schemaJson);
        }
        var totalBackupBytes = _vault != null ? await _vault.Backups.GetTotalBackupBytesAsync() : 0L;
        var totalSchemaChars = await _studio.SumSchemaCharsAsync();

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
            report = await _telemetry.RunSystemAuditAsync(_identity, _configuration, _logger, HttpContext.RequestServices);
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
            var freshReport = await _telemetry.RunSystemAuditAsync(_identity, _configuration, _logger, HttpContext.RequestServices);
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

        var project = await _projects.GetAsync(id);
        if (project == null) return NotFound(new { error = "Project not found." });

        // Gerçek veritabanı varlıkları sorgulanır (M-3):
        var tableCount = CountTables(project.SchemaJson);
        var baseMb = Math.Round((string.IsNullOrEmpty(project.SchemaJson) ? 0 : System.Text.Encoding.UTF8.GetByteCount(project.SchemaJson)) / BytesPerMb, 2);

        // Vault: Bu projeye ait gerçek yedekler (IVaultModule sözleşmesi üzerinden)
        var backups = _vault != null
            ? await _vault.Backups.ListBackupsAsync(id)
            : Array.Empty<VaultBackup>();
        var vaultBackups = backups.Count;
        var vaultTotalMb = Math.Round(backups.Sum(b => b.SizeBytes) / (1024.0 * 1024.0), 2);
        var lastBackup = backups.FirstOrDefault();

        // Flow: Bu projeye ait gerçek otomasyon kuralları ve çalışma logları (IFlowModule sözleşmesi üzerinden)
        var (flowRulesCount, flowExecs, lastTriggeredAt, activeTriggers) = _flow != null
            ? await _flow.Application.GetProjectFlowSummaryAsync(id)
            : (0, 0, (DateTime?)null, Array.Empty<string>());

        // Ground: Yönetilen DB kaydı (IGroundModule sözleşmesi üzerinden)
        var groundDb = _ground != null
            ? await _ground.Databases.GetByProjectIdAsync(id)
            : null;
        var hasGround = groundDb != null;

        // Desk: Bu projenin SQL geçmişi (IDeskModule sözleşmesi üzerinden)
        var (deskQueries, lastDeskQueryAt) = _desk != null
            ? await _desk.Application.GetProjectQuerySummaryAsync(id)
            : (0, (DateTime?)null);

        // Compile: Change request sayısı
        var crFlags = await _governance.GetProjectFlagsAsync(id);
        var ddlGenerated = crFlags.Total;
        var hasActiveMigrations = crFlags.HasActiveMigrations;

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
                activeTriggers = activeTriggers
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
                lastQueryAt = lastDeskQueryAt,
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
            var projs = await _studio.ExportAsync(500);
            var projOwnerEmails = await _identity.GetUserEmailsAsync(projs.Select(p => p.UserId).Distinct().ToList());
            var projIds = projs.Select(p => p.Id).ToList();
            var backupStats = _vault != null
                ? await _vault.Backups.GetProjectBackupStatsAsync(projIds)
                : (IReadOnlyDictionary<string, (int Count, long TotalBytes)>)new Dictionary<string, (int Count, long TotalBytes)>();
            foreach (var p in projs)
            {
                var schemaBytes = string.IsNullOrEmpty(p.SchemaJson) ? 0 : System.Text.Encoding.UTF8.GetByteCount(p.SchemaJson);
                var storageMb = Math.Round((schemaBytes + (backupStats.TryGetValue(p.Id, out var bs) ? bs.TotalBytes : 0L)) / BytesPerMb, 2);
                sb.AppendLine(AdminCsv.Row(p.Id, p.Name ?? "Unnamed", projOwnerEmails.GetValueOrDefault(p.UserId), p.DbType, CountTables(p.SchemaJson), storageMb, p.CreatedAt));
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
            var feedbacks = await _ops.ExportFeedbacksAsync(admin.Id, 500);
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
            var usages = (await _billing.ListGlobalUsageAsync(from, DateTime.MaxValue)).ToDictionary(g => g.Date.Date, g => g.Tokens);
            var callsPerDay = (await _billing.ListUsageCallsPerDayAsync(from, DateTime.MaxValue)).ToDictionary(x => x.Date.Date, x => (int)x.Tokens);

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
            var users = await _users.ListRecentUsersAsync(500);
            var csvIds = users.Select(u => u.Id).ToList();
            var quotas = await _billing.GetQuotaRowsAsync(csvIds);
            var csvSubs = await _billing.GetSubscriptionsAsync(csvIds);
            foreach (var u in users)
            {
                quotas.TryGetValue(u.Id, out var quota);
                csvSubs.TryGetValue(u.Id, out var csvSub);
                var dailyUsage = quota?.DailyUsageCount ?? 0;
                // Kota satırı yoksa GetUsers ile aynı varsayılan (dev sınırsız, diğerleri 100).
                var dailyLimit = quota?.DailyLimit ?? (u.IsDev ? int.MaxValue : 100);
                var plan = u.IsDev ? "dev" : (string.IsNullOrWhiteSpace(csvSub?.PlanCode) ? "free" : csvSub!.PlanCode!);
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

        return Ok(await _ops.ListFlagsAsync(admin.Id));
    }

    [HttpPut("features/{key}")]
    public async Task<IActionResult> UpdateFeature(string key, [FromBody] UpdateFeatureFlagRequest request)
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        if (string.IsNullOrWhiteSpace(key)) return BadRequest("Feature key is required.");

        try
        {
            // Mod verilmediyse (null) servis mevcut modu korur; "Hidden" bayrak DisabledNotice'a dönmez.
            // Yalnızca katalogdaki bayraklar güncellenir; yazım hatası sessizce yeni bayrak yaratmasın.
            var updated = await _ops.UpdateFlagAsync(admin.Id, key, request.IsEnabled, request.Mode ?? string.Empty, request.NoticeMessage);
            return updated is null ? NotFound(new { error = $"Unknown feature key '{key}'." }) : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── AI Model Yönlendirme (Tool-Based Routing) ───────────────────────────
    [HttpGet("ai-routes")]
    public async Task<IActionResult> GetAiRoutes()
    {
        var admin = await EnsureAdminAsync();
        if (admin == null) return StatusCode(403, new { error = "Admin access required." });

        if (_aiRouter == null) return Ok(new { routes = Array.Empty<AiToolRoute>(), providers = Array.Empty<string>() });
        var routes = await _ops.ListRoutesAsync(admin.Id);
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

        var updated = await _ops.UpdateRouteAsync(admin.Id, toolKey, request.Provider, request.ModelName, request.Temperature, request.FallbackProvider);
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
