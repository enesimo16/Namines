using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Core.Models.Hub;
using Namines.Core.Nsl;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;

namespace Namines.API.Controllers;

public sealed record UpdateRepoSettingsRequest(
    string? Name,
    string? Slug,
    string? Description,
    ProjectVisibility? Visibility,
    string? Category,
    string? Tags,
    string? LinkedGithubRepo,
    bool? AutoApproveSafeChanges,
    string? DbType);

public sealed record ForkRepoRequest(string? Name);

public sealed record CreateIssueRequest(
    string Title,
    string? Body,
    string? RelatedTableName,
    string? NslRuleCode,
    string? Labels);

public sealed record UpdateIssueStateRequest(bool IsClosed);

public sealed record CreateCommentRequest(string Body);

/// <summary>
/// Namines Hub (G57 — Faz 1): GitHub formunda veritabanı şeması ve mimari paylaşım platformu.
/// Keşif, repo ayarları, yıldızlama, fork, issue ve issue yorumları uçlarını sunar.
/// </summary>
[ApiController]
[Route("api/hub")]
public class HubController : ControllerBase
{
    private readonly AuthDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public HubController(AuthDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>
    /// <c>{owner}/{slugOrId}</c> çiftini TEK bir projeye çözer.
    ///
    /// <b>Bulunan açık:</b> sahip verildiği hâlde eşleşme yoksa eskiden slug TÜM
    /// sahipler arasında aranıyordu (<c>/hub/alice/fintech</c> → bob'un "fintech"
    /// reposu). Slug sahipler arasında benzersiz olmadığı için bu, başkasının reposunu
    /// "alice/fintech" adıyla göstermek (ve rozet üretmek) demekti. Artık sahip
    /// çözülemezse ya da onun altında eşleşme yoksa sonuç yok. "_" sahip belirtilmemiş
    /// demek: yalnızca benzersiz proje Id'si kabul edilir, slug değil.
    /// </summary>
    private async Task<CloudProject?> FindProjectByOwnerAsync(string owner, string slugOrId, CancellationToken ct)
    {
        var raw = (slugOrId ?? string.Empty).Trim();
        if (raw.Length == 0) return null;
        var normalized = raw.ToLowerInvariant();

        var query = _context.CloudProjects.AsNoTracking().Include(p => p.User);

        if (owner == "_")
            return await query.FirstOrDefaultAsync(p => p.Id == raw, ct);

        string? ownerId;
        if (owner == "me")
        {
            ownerId = CurrentUserId;
        }
        else
        {
            var user = await _userManager.FindByNameAsync(owner) ?? await _userManager.FindByIdAsync(owner);
            ownerId = user?.Id;
        }

        if (string.IsNullOrEmpty(ownerId)) return null;

        return await query.FirstOrDefaultAsync(
            p => p.UserId == ownerId && (p.Id == raw || p.Slug == raw || p.Slug == normalized), ct);
    }

    /// <summary>
    /// Keşif akışı (Explore feed). Yalnızca Public ve Quality Gate'i geçen mimarileri listeler.
    /// Quality Gate: En az 4 tablo, en az 2 ilişki, boş olmayan açıklama ve 0 NSL hata kuralı.
    /// </summary>
    [HttpGet("explore")]
    [AllowAnonymous]
    public async Task<IActionResult> Explore(
        [FromQuery] string? q,
        [FromQuery] string? category,
        [FromQuery] string? dbType,
        [FromQuery] string? sort = "popular", // "popular" | "recent"
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _context.CloudProjects
            .AsNoTracking()
            .Include(p => p.User)
            .Where(p => p.Visibility == ProjectVisibility.Public &&
                        !string.IsNullOrEmpty(p.Description));

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(p =>
                p.Name.ToLower().Contains(term) ||
                (p.Description != null && p.Description.ToLower().Contains(term)) ||
                (p.Tags != null && p.Tags.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var cat = category.Trim().ToLower();
            query = query.Where(p => p.Category.ToLower() == cat);
        }

        if (!string.IsNullOrWhiteSpace(dbType))
        {
            var engine = dbType.Trim().ToLower();
            query = query.Where(p => p.DbType.ToLower() == engine);
        }

        if (sort?.Equals("recent", StringComparison.OrdinalIgnoreCase) == true)
        {
            query = query.OrderByDescending(p => p.CreatedAt);
        }
        else
        {
            query = query.OrderByDescending(p => p.StarCount).ThenByDescending(p => p.CreatedAt);
        }

        // Quality Gate filtresi: En az 4 tablo, 2 ilişki, 0 NSL error
        var candidates = await query.Take(200).ToListAsync(ct);

        var qualified = new List<(CloudProject Project, DatabaseSchema Schema)>();
        foreach (var p in candidates)
        {
            try
            {
                var schema = JsonSerializer.Deserialize<DatabaseSchema>(p.SchemaJson, SchemaJsonOptions.Default);
                if (schema != null &&
                    schema.Tables.Count >= 4 &&
                    schema.Relations.Count >= 2)
                {
                    var findings = NslValidator.Validate(schema);
                    var hasError = findings.Any(f => f.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
                    if (!hasError)
                    {
                        qualified.Add((p, schema));
                    }
                }
            }
            catch (JsonException)
            {
                // Bozuk şemalar keşif akışına dahil edilmez
            }
        }

        var total = qualified.Count;
        var pagedItems = qualified
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Project.Id,
                x.Project.Name,
                x.Project.Slug,
                x.Project.Description,
                x.Project.DbType,
                x.Project.Category,
                x.Project.Tags,
                x.Project.StarCount,
                x.Project.ForkCount,
                x.Project.ForkedFromProjectId,
                x.Project.CreatedAt,
                x.Project.UpdatedAt,
                Owner = new
                {
                    Id = x.Project.User?.Id ?? x.Project.UserId,
                    Username = x.Project.User?.UserName ?? "unknown"
                },
                Stats = new
                {
                    TableCount = x.Schema.Tables.Count,
                    RelationCount = x.Schema.Relations.Count
                }
            })
            .ToList();

        return Ok(new
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = pagedItems
        });
    }

    /// <summary>
    /// Repo detayları, şema AST, branch'ler, issue sayıları, PR sayıları ve caller yetkisi.
    /// </summary>
    [HttpGet("repos/{owner}/{slugOrId}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetRepo(string owner, string slugOrId, CancellationToken ct)
    {
        var project = await FindProjectByOwnerAsync(owner, slugOrId, ct);

        if (project == null)
        {
            return NotFound(new { error = "Repository not found." });
        }

        var currentUserId = CurrentUserId;

        // Yetki kontrolü: Public olmayan repoları yalnızca üyeler görebilir.
        // Giriş yapmamış istemciye 401, giriş yapmış ama üye olmayana 404 dönmek
        // "bu private repo var" bilgisini sızdıran bir oracle'dı; ikisi de 404.
        if (project.Visibility != ProjectVisibility.Public)
        {
            if (string.IsNullOrEmpty(currentUserId) ||
                !await _context.CanViewAsync(project.Id, currentUserId, ct))
                return NotFound(new { error = "Repository not found." });
        }

        OrgRole? callerRole = null;
        bool canEdit = false;
        bool isStarred = false;

        if (!string.IsNullOrEmpty(currentUserId))
        {
            callerRole = await _context.GetRoleAsync(project.Id, currentUserId, ct);
            canEdit = callerRole is OrgRole.Editor or OrgRole.Admin or OrgRole.Owner;
            isStarred = await _context.RepoStars.AnyAsync(s => s.ProjectId == project.Id && s.UserId == currentUserId, ct);
        }

        var branches = await _context.Branches
            .AsNoTracking()
            .Where(b => b.ProjectId == project.Id && b.ClosedAt == null)
            .OrderByDescending(b => b.IsDefault)
            .ThenBy(b => b.Name)
            .Select(b => new { b.Id, b.Name, b.IsDefault, b.CreatedAt })
            .ToListAsync(ct);

        var openIssuesCount = await _context.RepoIssues.CountAsync(i => i.ProjectId == project.Id && !i.IsClosed, ct);
        var closedIssuesCount = await _context.RepoIssues.CountAsync(i => i.ProjectId == project.Id && i.IsClosed, ct);

        var openPrCount = await _context.ChangeRequests.CountAsync(c => c.ProjectId == project.Id && c.Status == ChangeRequestStatus.PendingReview, ct);
        var closedPrCount = await _context.ChangeRequests.CountAsync(c => c.ProjectId == project.Id && c.Status != ChangeRequestStatus.PendingReview, ct);

        object? forkedFrom = null;
        if (!string.IsNullOrEmpty(project.ForkedFromProjectId))
        {
            var parent = await _context.CloudProjects
                .AsNoTracking()
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.Id == project.ForkedFromProjectId, ct);

            if (parent != null)
            {
                forkedFrom = new
                {
                    parent.Id,
                    parent.Name,
                    parent.Slug,
                    OwnerUsername = parent.User?.UserName ?? "unknown"
                };
            }
        }

        DatabaseSchema schema;
        try
        {
            schema = JsonSerializer.Deserialize<DatabaseSchema>(project.SchemaJson, SchemaJsonOptions.Default)
                     ?? new DatabaseSchema();
        }
        catch (JsonException)
        {
            schema = new DatabaseSchema { Name = project.Name };
        }

        return Ok(new
        {
            project.Id,
            project.Name,
            project.Slug,
            project.Description,
            project.Visibility,
            project.Category,
            project.Tags,
            project.DbType,
            project.StarCount,
            project.ForkCount,
            project.ForkedFromProjectId,
            ForkedFrom = forkedFrom,
            project.LinkedGithubRepo,
            project.AutoApproveSafeChanges,
            project.CreatedAt,
            project.UpdatedAt,
            Owner = new
            {
                Id = project.User?.Id ?? project.UserId,
                Username = project.User?.UserName ?? "unknown"
            },
            Caller = new
            {
                IsStarred = isStarred,
                Role = callerRole?.ToString(),
                CanEdit = canEdit
            },
            Stats = new
            {
                TableCount = schema.Tables.Count,
                RelationCount = schema.Relations.Count,
                OpenIssuesCount = openIssuesCount,
                ClosedIssuesCount = closedIssuesCount,
                OpenPrCount = openPrCount,
                ClosedPrCount = closedPrCount
            },
            Branches = branches,
            Schema = schema,
            NodePositionsJson = project.NodePositionsJson
        });
    }

    // Slug biçimi: küçük harf/rakam, tek tire ile ayrılmış parçalar. Hub URL'sinin parçası
    // olduğu için bir sayfa yoluyla çakışan adlar ayrılmış.
    private static readonly Regex SlugPattern = new(
        @"^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);

    private static readonly HashSet<string> ReservedSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "issues", "settings", "explore", "new", "api", "admin", "star", "fork", "badge", "me", "_",
    };

    private static readonly Regex GithubRepoPattern = new(
        @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

    private const int MaxNameLength = 128;
    private const int MaxDescriptionLength = 1000;
    private const int MaxCategoryLength = 64;
    private const int MaxTagsLength = 256;
    private const int MaxGithubRepoLength = 140;
    private const int MaxIssueBodyLength = 20_000;
    private const int MaxCommentLength = 10_000;

    /// <summary>
    /// Repo ayarlarını güncelleme (Slug, Visibility, Category, Tags, Description, LinkedGithubRepo).
    /// Ad/açıklama/etiket gibi içerik alanları Editor / Admin / Owner'a açıktır; görünürlük,
    /// slug, Safe otomatik onay ve bağlı GitHub deposu ise erişim/otomasyon yüzeyini
    /// değiştirdiği için yalnızca Admin / Owner (üye yönetimi yetkisi) değiştirebilir.
    /// </summary>
    [HttpPatch("repos/{projectId}/settings")]
    [Authorize]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> UpdateSettings(string projectId, [FromBody] UpdateRepoSettingsRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var canEdit = await _context.CanEditAsync(projectId, userId, ct);
        if (!canEdit)
            return Forbid();

        // Doğrulamalar kayıt YAPILMADAN önce: yarım uygulanmış bir PATCH bırakmamak için.
        if (request.Name != null && request.Name.Trim().Length > MaxNameLength)
            return BadRequest(new { error = $"Name must be at most {MaxNameLength} characters." });
        if (request.Description != null && request.Description.Trim().Length > MaxDescriptionLength)
            return BadRequest(new { error = $"Description must be at most {MaxDescriptionLength} characters." });
        if (request.Category != null && request.Category.Trim().Length > MaxCategoryLength)
            return BadRequest(new { error = $"Category must be at most {MaxCategoryLength} characters." });
        if (request.Tags != null && request.Tags.Trim().Length > MaxTagsLength)
            return BadRequest(new { error = $"Tags must be at most {MaxTagsLength} characters." });

        string? newSlug = null;
        if (request.Slug != null)
        {
            // Eskiden geçersiz karakterler sessizce atılıyor, boş kalırsa istek yok sayılıyordu;
            // kullanıcı ne kaydedildiğini bilemiyordu. Artık biçim açıkça doğrulanıyor.
            var slug = request.Slug.Trim().ToLowerInvariant();
            if (slug.Length < 3 || slug.Length > 64 || !SlugPattern.IsMatch(slug))
                return BadRequest(new { error = "Slug must be 3-64 characters: lowercase letters, digits and single hyphens." });
            if (ReservedSlugs.Contains(slug))
                return BadRequest(new { error = "This slug is reserved." });
            newSlug = slug;
        }

        string? newDbType = null;
        if (!string.IsNullOrWhiteSpace(request.DbType))
        {
            // Serbest metin DbType, DDL/eject yollarında bilinmeyen motora düşüyordu.
            var dbTypeText = request.DbType.Trim();
            if (dbTypeText.Equals("postgres", StringComparison.OrdinalIgnoreCase)) dbTypeText = nameof(DatabaseType.PostgreSQL);
            if (dbTypeText.Equals("sqlserver", StringComparison.OrdinalIgnoreCase)) dbTypeText = nameof(DatabaseType.MSSQL);
            if (!Enum.TryParse<DatabaseType>(dbTypeText, ignoreCase: true, out var parsedDbType) ||
                !Enum.IsDefined(parsedDbType))
                return BadRequest(new { error = $"Unknown DbType. Use one of: {string.Join(", ", Enum.GetNames<DatabaseType>())}." });
            newDbType = parsedDbType.ToString();
        }

        string? newGithubRepo = null;
        if (request.LinkedGithubRepo != null)
        {
            newGithubRepo = request.LinkedGithubRepo.Trim();
            // Boş değer bağlantıyı kaldırır; doluysa owner/repo biçimi.
            if (newGithubRepo.Length > 0 &&
                (newGithubRepo.Length > MaxGithubRepoLength || !GithubRepoPattern.IsMatch(newGithubRepo)))
                return BadRequest(new { error = "LinkedGithubRepo must look like 'owner/repo'." });
        }

        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null)
            return NotFound(new { error = "Repository not found." });

        // Editor, repoyu herkese açabilmeli mi? Hayır: ChangeRequestController
        // SetAutoApproveSafe zaten üye yönetimi yetkisi istiyor; aynı ayar buradan
        // yetkisiz değişebiliyordu. Aynı kural görünürlük/slug/GitHub bağlantısına da
        // uygulanıyor. Yalnızca DEĞİŞEN alanlar sayılıyor: ayarlar formu değişmemiş
        // değerleri de geri gönderdiğinde bir Editor'ün ad/açıklama kaydı 403 almamalı.
        var touchesPrivileged =
            (request.Visibility.HasValue && request.Visibility.Value != project.Visibility) ||
            (request.AutoApproveSafeChanges.HasValue && request.AutoApproveSafeChanges.Value != project.AutoApproveSafeChanges) ||
            (newSlug != null && !string.Equals(newSlug, project.Slug, StringComparison.Ordinal)) ||
            (newGithubRepo != null && !string.Equals(newGithubRepo, project.LinkedGithubRepo ?? string.Empty, StringComparison.Ordinal));

        if (touchesPrivileged && !await _context.CanManageMembersAsync(projectId, userId, ct))
            return Forbid();

        if (newSlug != null && !string.Equals(newSlug, project.Slug, StringComparison.Ordinal))
        {
            var conflict = await _context.CloudProjects.AnyAsync(p => p.Id != projectId && p.Slug == newSlug, ct);
            if (conflict)
                return Conflict(new { error = "This slug is already taken." });

            project.Slug = newSlug;
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
            project.Name = request.Name.Trim();

        if (request.Description != null)
            project.Description = request.Description.Trim();

        if (request.Visibility.HasValue)
            project.Visibility = request.Visibility.Value;

        if (request.Category != null)
            project.Category = request.Category.Trim();

        if (request.Tags != null)
            project.Tags = request.Tags.Trim();

        if (newGithubRepo != null)
            project.LinkedGithubRepo = newGithubRepo;

        if (request.AutoApproveSafeChanges.HasValue)
            project.AutoApproveSafeChanges = request.AutoApproveSafeChanges.Value;

        if (newDbType != null)
            project.DbType = newDbType;

        project.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return Ok(new
        {
            project.Id,
            project.Name,
            project.Slug,
            project.Description,
            project.Visibility,
            project.Category,
            project.Tags,
            project.DbType,
            project.AutoApproveSafeChanges,
            project.LinkedGithubRepo,
            project.UpdatedAt
        });
    }

    /// <summary>
    /// Projeyi/Depoyu Hub'dan ve veritabanından kalıcı olarak siler.
    /// Yalnızca projenin GÜNCEL organizasyon rolü Owner olan kullanıcı silebilir.
    /// </summary>
    [HttpDelete("repos/{projectId}")]
    [Authorize]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> DeleteRepo(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null)
            return NotFound(new { error = "Repository not found." });

        // Eskiden `project.UserId == userId` tek başına yetkiydi: projeyi oluşturup sonra
        // organizasyondan çıkarılan (ya da rolü düşürülen) kullanıcı repoyu silebiliyordu.
        // Rol artık OrgAccess üzerinden, diğer uçlarla aynı kaynaktan okunuyor.
        var role = await _context.GetRoleAsync(projectId, userId, ct);
        if (role != OrgRole.Owner)
            return Forbid();

        // Varsa çapraz veritabanı kısıtlarını temizle
        var crossRels = await _context.CrossDatabaseRelations
            .Where(r => r.SourceProjectId == projectId || r.TargetProjectId == projectId)
            .ToListAsync(ct);
        if (crossRels.Count > 0)
        {
            _context.CrossDatabaseRelations.RemoveRange(crossRels);
        }

        _context.CloudProjects.Remove(project);
        await _context.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Repo yıldızlama (toggle). Yıldız ekler veya kaldırır, StarCount'u günceller.
    /// </summary>
    [HttpPost("repos/{projectId}/star")]
    [Authorize]
    [EnableRateLimiting("hub-write")]
    public async Task<IActionResult> ToggleStar(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var visibility = await _context.CloudProjects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => (ProjectVisibility?)p.Visibility)
            .FirstOrDefaultAsync(ct);
        if (visibility == null) return NotFound(new { error = "Repository not found." });

        if (visibility != ProjectVisibility.Public && !await _context.CanViewAsync(projectId, userId, ct))
            return NotFound(new { error = "Repository not found." });

        var existing = await _context.RepoStars.FirstOrDefaultAsync(s => s.ProjectId == projectId && s.UserId == userId, ct);
        bool isStarred;
        if (existing != null)
        {
            // Silmeyi ve sayacı SQL'de yapıyoruz: yüklenmiş varlık üzerinde `StarCount - 1`
            // okuma-değiştirme-yazma idi ve eşzamanlı iki yıldız birbirinin sayısını ezerdi.
            var removed = await _context.RepoStars
                .Where(s => s.ProjectId == projectId && s.UserId == userId)
                .ExecuteDeleteAsync(ct);
            if (removed > 0)
                await AdjustStarCountAsync(projectId, -1, ct);
            isStarred = false;
        }
        else
        {
            try
            {
                await _context.RepoStars.AddAsync(new RepoStar
                {
                    ProjectId = projectId,
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow
                }, ct);
                await _context.SaveChangesAsync(ct);
                await AdjustStarCountAsync(projectId, +1, ct);
            }
            catch (DbUpdateException)
            {
                // Aynı kullanıcının eşzamanlı ikinci isteği (çift tıklama): (ProjectId, UserId)
                // birincil anahtarı ikinci eklemeyi reddetti — zaten yıldızlı. 500 yerine
                // olması gereken durumu döndür, sayacı ARTIRMA.
                _context.ChangeTracker.Clear();
                if (!await _context.RepoStars.AnyAsync(s => s.ProjectId == projectId && s.UserId == userId, ct))
                    throw;
            }
            isStarred = true;
        }

        var starCount = await _context.CloudProjects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => p.StarCount)
            .FirstOrDefaultAsync(ct);
        return Ok(new { isStarred, starCount });
    }

    // Sayaç değişimi tek SQL ifadesi (UPDATE ... SET StarCount = StarCount + n): okuma-değiştirme-yazma
    // yarışı yok. Sıfırın altına inmez.
    private Task<int> AdjustStarCountAsync(string projectId, int delta, CancellationToken ct) =>
        _context.CloudProjects
            .Where(p => p.Id == projectId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                p => p.StarCount,
                p => p.StarCount + delta < 0 ? 0 : p.StarCount + delta), ct);

    /// <summary>
    /// Repo kopyalama (Fork). Orijinal şema ve main branch klonlanır, fork_count++ artar.
    /// </summary>
    [HttpPost("repos/{projectId}/fork")]
    [Authorize]
    [EnableRateLimiting("sensitive")]
    public async Task<IActionResult> Fork(string projectId, [FromBody] ForkRepoRequest? request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (request?.Name != null && request.Name.Trim().Length > MaxNameLength)
            return BadRequest(new { error = $"Name must be at most {MaxNameLength} characters." });

        var project = await _context.CloudProjects
            .AsNoTracking()
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

        if (project == null) return NotFound(new { error = "Repository not found." });

        if (project.Visibility != ProjectVisibility.Public && !await _context.CanViewAsync(projectId, userId, ct))
            return NotFound(new { error = "Repository not found." });

        var callerUser = await _userManager.FindByIdAsync(userId);
        var callerName = callerUser?.UserName ?? "User";
        var personalOrg = await _context.GetOrCreatePersonalOrgAsync(userId, callerName, ct);

        var forkName = !string.IsNullOrWhiteSpace(request?.Name)
            ? request.Name.Trim()
            : $"{project.Name}-fork";
        if (forkName.Length > MaxNameLength) forkName = forkName[..MaxNameLength];

        var baseSlug = !string.IsNullOrWhiteSpace(project.Slug) ? project.Slug : project.Name.ToLowerInvariant();
        var cleanBaseSlug = Regex.Replace(baseSlug, @"[^a-z0-9\-]", "").Trim('-');
        if (cleanBaseSlug.Length == 0) cleanBaseSlug = "repo";
        var forkSlug = $"{cleanBaseSlug}-{Guid.NewGuid().ToString()[..6]}";

        var forkedProject = new CloudProject
        {
            Id = Guid.NewGuid().ToString(),
            Name = forkName,
            Slug = forkSlug,
            DbType = project.DbType,
            SchemaJson = project.SchemaJson,
            NodePositionsJson = project.NodePositionsJson,
            UserId = userId,
            OrganizationId = personalOrg.Id,
            Visibility = ProjectVisibility.Private,
            Category = project.Category,
            Tags = project.Tags,
            ForkedFromProjectId = project.Id,
            Description = $"Forked from {project.User?.UserName ?? "unknown"}/{project.Name}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.CloudProjects.AddAsync(forkedProject, ct);

        var defaultBranch = new Branch
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = forkedProject.Id,
            Name = "main",
            IsDefault = true,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        await _context.Branches.AddAsync(defaultBranch, ct);

        await _context.SaveChangesAsync(ct);

        // Sayaç SQL'de artırılıyor (eşzamanlı fork'lar birbirini ezmesin).
        await _context.CloudProjects
            .Where(p => p.Id == projectId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ForkCount, p => p.ForkCount + 1), ct);

        return CreatedAtAction(nameof(GetRepo), new { owner = callerName, slugOrId = forkedProject.Slug }, new
        {
            forkedProject.Id,
            forkedProject.Name,
            forkedProject.Slug,
            forkedProject.ForkedFromProjectId,
            forkedProject.Visibility,
            forkedProject.CreatedAt
        });
    }

    /// <summary>
    /// Projenin issue listesi (open/closed filtreli).
    /// </summary>
    [HttpGet("repos/{projectId}/issues")]
    [AllowAnonymous]
    public async Task<IActionResult> ListIssues(
        string projectId,
        [FromQuery] string? status = "open", // "open", "closed", "all"
        [FromQuery] string? relatedTable = null,
        [FromQuery] string? label = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound(new { error = "Repository not found." });

        var userId = CurrentUserId;
        if (project.Visibility != ProjectVisibility.Public &&
            (string.IsNullOrEmpty(userId) || !await _context.CanViewAsync(projectId, userId, ct)))
        {
            return NotFound(new { error = "Repository not found." });
        }

        var query = _context.RepoIssues
            .AsNoTracking()
            .Where(i => i.ProjectId == projectId);

        if (status?.Equals("closed", StringComparison.OrdinalIgnoreCase) == true)
            query = query.Where(i => i.IsClosed);
        else if (status?.Equals("all", StringComparison.OrdinalIgnoreCase) == true)
        { /* no filter */ }
        else
            query = query.Where(i => !i.IsClosed);

        if (!string.IsNullOrWhiteSpace(relatedTable))
            query = query.Where(i => i.RelatedTableName == relatedTable);

        if (!string.IsNullOrWhiteSpace(label))
            query = query.Where(i => i.Labels != null && i.Labels.Contains(label));

        var totalCount = await query.CountAsync(ct);
        var openCount = await _context.RepoIssues.CountAsync(i => i.ProjectId == projectId && !i.IsClosed, ct);
        var closedCount = await _context.RepoIssues.CountAsync(i => i.ProjectId == projectId && i.IsClosed, ct);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var issues = await query
            .OrderByDescending(i => i.IssueNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new
            {
                i.Id,
                i.IssueNumber,
                i.Title,
                i.Body,
                i.IsClosed,
                i.RelatedTableName,
                i.NslRuleCode,
                i.Labels,
                i.CreatedAt,
                i.ClosedAt,
                CreatedBy = new
                {
                    Id = i.CreatedByUser.Id,
                    Username = i.CreatedByUser.UserName ?? "unknown"
                },
                CommentsCount = _context.RepoIssueComments.Count(c => c.IssueId == i.Id)
            })
            .ToListAsync(ct);

        return Ok(new
        {
            Total = totalCount,
            OpenCount = openCount,
            ClosedCount = closedCount,
            Page = page,
            PageSize = pageSize,
            Items = issues
        });
    }

    /// <summary>
    /// Yeni issue aç. IssueNumber proje bazında otomatik artar (#1, #2...).
    /// Public repolarda oturum açmış her kullanıcı issue açabilir.
    /// </summary>
    [HttpPost("repos/{projectId}/issues")]
    [Authorize]
    [EnableRateLimiting("hub-write")]
    public async Task<IActionResult> CreateIssue(string projectId, [FromBody] CreateIssueRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { error = "Title is required." });

        // Kolon sınırlarını aşan değer veritabanında 500 olarak patlıyordu (Title 256,
        // Labels 256, RelatedTableName 128, NslRuleCode 32 — bkz. AuthDbContext).
        if (request.Title.Trim().Length > 256)
            return BadRequest(new { error = "Title must be at most 256 characters." });
        if (request.Labels != null && request.Labels.Trim().Length > 256)
            return BadRequest(new { error = "Labels must be at most 256 characters." });
        if (request.RelatedTableName != null && request.RelatedTableName.Trim().Length > 128)
            return BadRequest(new { error = "RelatedTableName must be at most 128 characters." });
        if (request.NslRuleCode != null && request.NslRuleCode.Trim().Length > 32)
            return BadRequest(new { error = "NslRuleCode must be at most 32 characters." });
        if (request.Body != null && request.Body.Trim().Length > MaxIssueBodyLength)
            return BadRequest(new { error = $"Body must be at most {MaxIssueBodyLength} characters." });

        var project = await _context.CloudProjects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound(new { error = "Repository not found." });

        if (project.Visibility != ProjectVisibility.Public && !await _context.CanViewAsync(projectId, userId, ct))
            return NotFound(new { error = "Repository not found." });

        // Numara = MAX+1 okuma-değiştirme-yazma: eşzamanlı iki istek aynı numarayı üretir ve
        // (ProjectId, IssueNumber) benzersiz indeksi ikincisini reddeder. Bu çakışma artık
        // 500 değil: kaybeden numarayı yeniden hesaplayıp birkaç kez dener.
        RepoIssue? issue = null;
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var nextNumber = (await _context.RepoIssues
                .Where(i => i.ProjectId == projectId)
                .MaxAsync(i => (int?)i.IssueNumber, ct) ?? 0) + 1;

            var candidate = new RepoIssue
            {
                Id = Guid.NewGuid().ToString(),
                ProjectId = projectId,
                IssueNumber = nextNumber,
                Title = request.Title.Trim(),
                Body = request.Body?.Trim() ?? string.Empty,
                RelatedTableName = request.RelatedTableName?.Trim(),
                NslRuleCode = request.NslRuleCode?.Trim(),
                Labels = request.Labels?.Trim(),
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow,
                IsClosed = false
            };

            await _context.RepoIssues.AddAsync(candidate, ct);
            try
            {
                await _context.SaveChangesAsync(ct);
                issue = candidate;
                break;
            }
            catch (DbUpdateException) when (attempt < maxAttempts)
            {
                _context.Entry(candidate).State = EntityState.Detached;
            }
            catch (DbUpdateException)
            {
                _context.Entry(candidate).State = EntityState.Detached;
                return Conflict(new { error = "Could not allocate an issue number. Please retry." });
            }
        }

        if (issue == null)
            return Conflict(new { error = "Could not allocate an issue number. Please retry." });

        return CreatedAtAction(nameof(ListIssues), new { projectId }, new
        {
            issue.Id,
            issue.IssueNumber,
            issue.Title,
            issue.Body,
            issue.IsClosed,
            issue.RelatedTableName,
            issue.NslRuleCode,
            issue.Labels,
            issue.CreatedAt
        });
    }

    /// <summary>
    /// Issue durumunu aç/kapat (toggle state).
    /// Yalnızca Issue oluşturan veya proje Editor/Admin/Owner'ı kapatabilir.
    /// </summary>
    [HttpPatch("issues/{issueId}/state")]
    [Authorize]
    [EnableRateLimiting("hub-write")]
    public async Task<IActionResult> UpdateIssueState(string issueId, [FromBody] UpdateIssueStateRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var issue = await _context.RepoIssues
            .Include(i => i.Project)
            .FirstOrDefaultAsync(i => i.Id == issueId, ct);

        if (issue == null) return NotFound(new { error = "Issue not found." });

        // Yazar olmak tek başına yetki değil: repodan çıkarılmış (artık göremeyen) yazar da
        // issue'yu yeniden açıp kapatabiliyordu. Önce görme yetkisi, sonra yazar/editör.
        // Public repoda herkes görebilir; özel repoda üyelik şart. Göremeyene 404.
        if (issue.Project.Visibility != ProjectVisibility.Public &&
            !await _context.CanViewAsync(issue.ProjectId, userId, ct))
            return NotFound(new { error = "Issue not found." });

        var isAuthor = issue.CreatedByUserId == userId;
        var canEdit = await _context.CanEditAsync(issue.ProjectId, userId, ct);

        if (!isAuthor && !canEdit)
            return Forbid();

        issue.IsClosed = request.IsClosed;
        issue.ClosedAt = request.IsClosed ? DateTime.UtcNow : null;

        await _context.SaveChangesAsync(ct);

        return Ok(new
        {
            issue.Id,
            issue.IssueNumber,
            issue.IsClosed,
            issue.ClosedAt
        });
    }

    /// <summary>
    /// Issue yorumlarını kronolojik sırayla getirir.
    /// </summary>
    [HttpGet("issues/{issueId}/comments")]
    [AllowAnonymous]
    public async Task<IActionResult> ListComments(string issueId, CancellationToken ct)
    {
        var issue = await _context.RepoIssues
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == issueId, ct);

        if (issue == null) return NotFound(new { error = "Issue not found." });

        var project = await _context.CloudProjects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == issue.ProjectId, ct);

        if (project == null) return NotFound(new { error = "Repository not found." });

        var userId = CurrentUserId;
        if (project.Visibility != ProjectVisibility.Public &&
            (string.IsNullOrEmpty(userId) || !await _context.CanViewAsync(project.Id, userId, ct)))
        {
            return NotFound(new { error = "Issue not found." });
        }

        var comments = await _context.RepoIssueComments
            .AsNoTracking()
            .Where(c => c.IssueId == issueId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new
            {
                c.Id,
                c.Body,
                c.CreatedAt,
                Author = new
                {
                    Id = c.AuthorUser.Id,
                    Username = c.AuthorUser.UserName ?? "unknown"
                }
            })
            .ToListAsync(ct);

        return Ok(comments);
    }

    /// <summary>
    /// Issue'ya yorum ekle.
    /// </summary>
    [HttpPost("issues/{issueId}/comments")]
    [Authorize]
    [EnableRateLimiting("hub-write")]
    public async Task<IActionResult> CreateComment(string issueId, [FromBody] CreateCommentRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Body))
            return BadRequest(new { error = "Comment body cannot be empty." });

        if (request.Body.Trim().Length > MaxCommentLength)
            return BadRequest(new { error = $"Comment must be at most {MaxCommentLength} characters." });

        var issue = await _context.RepoIssues.FirstOrDefaultAsync(i => i.Id == issueId, ct);
        if (issue == null) return NotFound(new { error = "Issue not found." });

        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == issue.ProjectId, ct);
        if (project == null) return NotFound(new { error = "Repository not found." });

        if (project.Visibility != ProjectVisibility.Public && !await _context.CanViewAsync(project.Id, userId, ct))
            return NotFound(new { error = "Repository not found." });

        var comment = new RepoIssueComment
        {
            Id = Guid.NewGuid().ToString(),
            IssueId = issueId,
            Body = request.Body.Trim(),
            AuthorUserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        await _context.RepoIssueComments.AddAsync(comment, ct);
        await _context.SaveChangesAsync(ct);

        var user = await _userManager.FindByIdAsync(userId);

        return Ok(new
        {
            comment.Id,
            comment.Body,
            comment.CreatedAt,
            Author = new
            {
                Id = userId,
                Username = user?.UserName ?? "unknown"
            }
        });
    }

    /// <summary>
    /// Dinamik SVG rozet (README badge). Tablo sayısı ve 3NF / NSL doğrulama durumunu gösterir.
    /// </summary>
    [HttpGet("badge/{owner}/{slug}.svg")]
    [AllowAnonymous]
    public async Task<IActionResult> Badge(string owner, string slug, CancellationToken ct)
    {
        // GetRepo ile aynı çözümleme: sahip verildiyse YALNIZCA onun repoları (slug'ı
        // sahipler arasında aramak, başkasının reposuna "owner/slug" rozeti üretmekti).
        var project = await FindProjectByOwnerAsync(owner, slug, ct);

        if (project == null || project.Visibility != ProjectVisibility.Public)
        {
            var fallbackSvg = GenerateShieldBadge("namines hub", "private / not found", "#64748b");
            Response.Headers.CacheControl = "public, max-age=60";
            return Content(fallbackSvg, "image/svg+xml; charset=utf-8");
        }

        DatabaseSchema schema;
        try
        {
            schema = JsonSerializer.Deserialize<DatabaseSchema>(project.SchemaJson, SchemaJsonOptions.Default)
                     ?? new DatabaseSchema();
        }
        catch (JsonException)
        {
            schema = new DatabaseSchema();
        }

        var findings = NslValidator.Validate(schema);
        var has3nfIssue = findings.Any(f => f.Code == "NSL020" || f.Code == "NSL019");
        var hasError = findings.Any(f => f.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));

        string rightText;
        string rightColor;

        if (hasError)
        {
            rightText = $"{schema.Tables.Count} tables · schema error";
            rightColor = "#ef4444";
        }
        else if (has3nfIssue)
        {
            rightText = $"{schema.Tables.Count} tables · normalization warnings";
            rightColor = "#f59e0b";
        }
        else
        {
            // "3NF verified" bir iddiaydı: NslValidator'ın NSL019/020 sezgisi 3NF'yi KANITLAMAZ,
            // yalnızca bilinen şüpheli kalıpları arar. Rozet yalnızca gerçekte bilinen şeyi söylüyor.
            rightText = $"{schema.Tables.Count} tables · no normalization warnings";
            rightColor = "#10b981";
        }

        var svg = GenerateShieldBadge("namines hub", rightText, rightColor);
        Response.Headers.CacheControl = "public, max-age=300";
        return Content(svg, "image/svg+xml; charset=utf-8");
    }

    private static string GenerateShieldBadge(string leftText, string rightText, string rightColor)
    {
        var safeLeft = WebUtility.HtmlEncode(leftText);
        var safeRight = WebUtility.HtmlEncode(rightText);

        var leftWidth = (int)Math.Round(safeLeft.Length * 6.5) + 14;
        var rightWidth = (int)Math.Round(safeRight.Length * 6.5) + 14;
        var totalWidth = leftWidth + rightWidth;

        var leftMid = leftWidth / 2;
        var rightMid = leftWidth + (rightWidth / 2);

        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="{totalWidth}" height="20" role="img" aria-label="{safeLeft}: {safeRight}">
              <title>{safeLeft}: {safeRight}</title>
              <linearGradient id="s" x2="0" y2="100%">
                <stop offset="0" stop-color="#bbb" stop-opacity=".1"/>
                <stop offset="1" stop-opacity=".1"/>
              </linearGradient>
              <clipPath id="r">
                <rect width="{totalWidth}" height="20" rx="3" fill="#fff"/>
              </clipPath>
              <g clip-path="url(#r)">
                <rect width="{leftWidth}" height="20" fill="#1e293b"/>
                <rect x="{leftWidth}" width="{rightWidth}" height="20" fill="{rightColor}"/>
                <rect width="{totalWidth}" height="20" fill="url(#s)"/>
              </g>
              <g fill="#fff" text-anchor="middle" font-family="DejaVu Sans,Verdana,Geneva,sans-serif" font-size="11">
                <text x="{leftMid}" y="15" fill="#010101" fill-opacity=".3">{safeLeft}</text>
                <text x="{leftMid}" y="14">{safeLeft}</text>
                <text x="{rightMid}" y="15" fill="#010101" fill-opacity=".3">{safeRight}</text>
                <text x="{rightMid}" y="14">{safeRight}</text>
              </g>
            </svg>
            """;
    }
}
