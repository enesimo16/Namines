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
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Persistence;
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
    private readonly ProjectDeletionService _projectDeletion;
    private readonly IHubApplicationService _hub;
    private readonly IStudioApplicationService _studio;
    private readonly ICrossModuleTransaction _transaction;

    public HubController(
        AuthDbContext context,
        UserManager<ApplicationUser> userManager,
        ProjectDeletionService projectDeletion,
        IHubApplicationService hub,
        IStudioApplicationService studio,
        ICrossModuleTransaction transaction)
    {
        _context = context;
        _userManager = userManager;
        _projectDeletion = projectDeletion;
        _hub = hub;
        _studio = studio;
        _transaction = transaction;
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

        var projectId = await _hub.ResolveProjectIdAsync(ownerId, raw, ct);
        return projectId is null
            ? null
            : await query.FirstOrDefaultAsync(p => p.Id == projectId, ct);
    }

    /// <summary>
    /// Keşif akışı (Explore feed). Yalnızca Public ve Quality Gate'i geçen mimarileri listeler.
    /// Quality Gate: En az 4 tablo, en az 2 ilişki, boş olmayan açıklama ve 0 NSL hata kuralı.
    /// </summary>
    [HttpGet("explore")]
    [AllowAnonymous]
    [EnableRateLimiting("explore-public")]
    public async Task<IActionResult> Explore(
        [FromQuery] string? q,
        [FromQuery] string? category,
        [FromQuery] string? dbType,
        [FromQuery] string? sort = "popular", // "popular" | "recent"
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        [FromQuery] string? tag = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var candidates = await CollectExploreCandidatesAsync(q, category, dbType, tag, sort, ct);

        // Quality Gate filtresi: En az 4 tablo, 2 ilişki, 0 NSL error
        var candidateIds = candidates.Select(c => c.Repository.ProjectId).ToList();
        var schemaJsonById = await _studio.GetSchemaJsonAsync(candidateIds, ct);

        var qualified = new List<(ExploreCandidate Candidate, DatabaseSchema Schema)>();
        foreach (var candidate in candidates)
        {
            if (!schemaJsonById.TryGetValue(candidate.Repository.ProjectId, out var schemaJson)) continue;
            try
            {
                var schema = JsonSerializer.Deserialize<DatabaseSchema>(schemaJson, SchemaJsonOptions.Default);
                if (schema != null &&
                    schema.Tables.Count >= 4 &&
                    schema.Relations.Count >= 2)
                {
                    var findings = NslValidator.Validate(schema);
                    var hasError = findings.Any(f => f.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
                    if (!hasError)
                    {
                        qualified.Add((candidate, schema));
                    }
                }
            }
            catch (JsonException)
            {
                // Bozuk şemalar keşif akışına dahil edilmez
            }
        }

        var total = qualified.Count;
        var visibleItems = qualified.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        // Yalnızca görünen sayfanın sahipleri: tek sorgu, N+1 yok.
        var ownerIds = visibleItems.Select(x => x.Candidate.Project.UserId).Distinct().ToList();
        var ownerNames = await _context.Users.AsNoTracking()
            .Where(u => ownerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName })
            .ToDictionaryAsync(u => u.Id, u => u.UserName, ct);

        var pagedItems = visibleItems
            .Select(x => new
            {
                x.Candidate.Project.Id,
                x.Candidate.Project.Name,
                x.Candidate.Repository.Slug,
                x.Candidate.Repository.Description,
                x.Candidate.Project.DbType,
                x.Candidate.Repository.Category,
                Tags = x.Candidate.TagsCsv,
                x.Candidate.Repository.StarCount,
                x.Candidate.Repository.ForkCount,
                x.Candidate.Repository.ForkedFromProjectId,
                x.Candidate.Project.CreatedAt,
                x.Candidate.Project.UpdatedAt,
                Owner = new
                {
                    Id = x.Candidate.Project.UserId,
                    Username = ownerNames.GetValueOrDefault(x.Candidate.Project.UserId) ?? "unknown"
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

    private sealed record ExploreCandidate(ProjectRepository Repository, StudioProjectSummary Project, string? TagsCsv);

    // Quality Gate şemayı ayrıştırıyor; bu pahalı olduğu için istek başına aday tavanı var.
    private const int ExploreCandidateLimit = 200;

    /// <summary>
    /// Filtrelerin hepsi Hub'daki tek sorguda; Studio'dan yalnızca bulunan adayların özeti okunur.
    /// Sorgu sayısı public repo sayısından bağımsız.
    /// </summary>
    private async Task<List<ExploreCandidate>> CollectExploreCandidatesAsync(
        string? q, string? category, string? dbType, string? tag, string? sort, CancellationToken ct)
    {
        var criteria = new ExploreCriteria(
            category, tag, q, dbType, sort?.Equals("recent", StringComparison.OrdinalIgnoreCase) == true);

        var repositories = await _hub.GetExploreCandidatesAsync(criteria, ExploreCandidateLimit, ct);
        var ids = repositories.Select(r => r.ProjectId).ToList();
        var projects = await _studio.GetProjectSummariesAsync(ids, ct);
        var tags = await _hub.GetTagsCsvAsync(ids, ct);

        return repositories
            .Where(r => projects.ContainsKey(r.ProjectId))
            .Select(r => new ExploreCandidate(r, projects[r.ProjectId], tags.GetValueOrDefault(r.ProjectId)))
            .ToList();
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
        var repository = await _hub.GetRepositoryAsync(project.Id, ct);
        var visibility = repository?.Visibility ?? ProjectVisibility.Private;

        // Yetki kontrolü: Public olmayan repoları yalnızca üyeler görebilir.
        // Giriş yapmamış istemciye 401, giriş yapmış ama üye olmayana 404 dönmek
        // "bu private repo var" bilgisini sızdıran bir oracle'dı; ikisi de 404.
        if (visibility != ProjectVisibility.Public)
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
            isStarred = await _hub.IsStarredByUserAsync(project.Id, currentUserId, ct);
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
        if (!string.IsNullOrEmpty(repository?.ForkedFromProjectId))
        {
            var parent = await _context.CloudProjects
                .AsNoTracking()
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.Id == repository.ForkedFromProjectId, ct);

            if (parent != null)
            {
                var parentRepository = await _hub.GetRepositoryAsync(parent.Id, ct);
                forkedFrom = new
                {
                    parent.Id,
                    parent.Name,
                    parentRepository?.Slug,
                    OwnerUsername = parent.User?.UserName ?? "unknown"
                };
            }
        }

        var tagsCsv = await _hub.GetTagsCsvAsync(project.Id, ct);

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
            repository?.Slug,
            repository?.Description,
            Visibility = visibility,
            Category = repository?.Category ?? "General",
            Tags = tagsCsv,
            project.DbType,
            StarCount = repository?.StarCount ?? 0,
            ForkCount = repository?.ForkCount ?? 0,
            repository?.ForkedFromProjectId,
            ForkedFrom = forkedFrom,
            repository?.LinkedGithubRepo,
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
        var repository = await _hub.GetRepositoryAsync(projectId, ct);
        var currentVisibility = repository?.Visibility ?? ProjectVisibility.Private;

        var touchesPrivileged =
            (request.Visibility.HasValue && request.Visibility.Value != currentVisibility) ||
            (request.AutoApproveSafeChanges.HasValue && request.AutoApproveSafeChanges.Value != project.AutoApproveSafeChanges) ||
            (newSlug != null && !string.Equals(newSlug, repository?.Slug, StringComparison.Ordinal)) ||
            (newGithubRepo != null && !string.Equals(newGithubRepo, repository?.LinkedGithubRepo ?? string.Empty, StringComparison.Ordinal));

        if (touchesPrivileged && !await _context.CanManageMembersAsync(projectId, userId, ct))
            return Forbid();

        var slugChanged = newSlug != null && !string.Equals(newSlug, repository?.Slug, StringComparison.Ordinal);
        if (slugChanged && await _hub.IsSlugTakenAsync(newSlug!, projectId, ct))
            return Conflict(new { error = "This slug is already taken." });

        if (!string.IsNullOrWhiteSpace(request.Name))
            project.Name = request.Name.Trim();

        if (request.AutoApproveSafeChanges.HasValue)
            project.AutoApproveSafeChanges = request.AutoApproveSafeChanges.Value;

        if (newDbType != null)
            project.DbType = newDbType;

        project.UpdatedAt = DateTime.UtcNow;

        var change = new RepositorySettingsChange(
            Slug: slugChanged ? newSlug : null,
            Description: request.Description?.Trim(),
            Visibility: request.Visibility,
            Category: request.Category?.Trim(),
            Tags: request.Tags?.Trim(),
            LinkedGithubRepo: newGithubRepo);

        // Proje ve repo tek işlemde: yarım uygulanmış PATCH bırakılmaz.
        ProjectRepository updated = null!;
        try
        {
            await _transaction.ExecuteAsync(async c =>
            {
                await _context.SaveChangesAsync(acceptAllChangesOnSuccess: false, c);
                updated = await _hub.ApplySettingsAsync(projectId, project.UserId, project.Name, project.DbType, change, c);
            }, ct);
            _context.ChangeTracker.AcceptAllChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict.Result();
        }
        catch (DbUpdateException ex) when (DbUniqueViolation.IsUniqueViolation(ex))
        {
            // Ön kontrol ile kayıt arasında aynı sahibin başka bir isteği slug'ı aldıysa
            // (OwnerUserId, Slug) unique index yakalar; 500 yerine ön kontroldekiyle aynı 409.
            return Conflict(new { error = "This slug is already taken." });
        }

        return Ok(new
        {
            project.Id,
            project.Name,
            updated.Slug,
            updated.Description,
            updated.Visibility,
            updated.Category,
            Tags = await _hub.GetTagsCsvAsync(projectId, ct),
            project.DbType,
            project.AutoApproveSafeChanges,
            updated.LinkedGithubRepo,
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

        // Vault yedekleri + Ground sağlayıcı kaynağı + denetim kaydı + cascade, tek serviste
        // (yedek/Ground FK'leri Restrict; elle temizlenmeden proje silinemez).
        var result = await _projectDeletion.DeleteAsync(project, userId, ct);
        if (!result.Ok)
            return StatusCode(result.StatusCode, new { error = result.Error, code = result.Code });

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

        var visibility = await _hub.GetVisibilityAsync(projectId, ct);
        if (visibility == null) return NotFound(new { error = "Repository not found." });

        if (visibility != ProjectVisibility.Public && !await _context.CanViewAsync(projectId, userId, ct))
            return NotFound(new { error = "Repository not found." });

        var (starCount, isStarred) = await _hub.ToggleStarAsync(projectId, userId, ct);
        return Ok(new { isStarred, starCount });
    }

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

        var sourceRepository = await _hub.GetRepositoryAsync(projectId, ct);
        if ((sourceRepository?.Visibility ?? ProjectVisibility.Private) != ProjectVisibility.Public &&
            !await _context.CanViewAsync(projectId, userId, ct))
            return NotFound(new { error = "Repository not found." });

        var callerUser = await _userManager.FindByIdAsync(userId);
        var callerName = callerUser?.UserName ?? "User";
        var personalOrg = await _context.GetOrCreatePersonalOrgAsync(userId, callerName, ct);

        var forkName = !string.IsNullOrWhiteSpace(request?.Name)
            ? request.Name.Trim()
            : $"{project.Name}-fork";
        if (forkName.Length > MaxNameLength) forkName = forkName[..MaxNameLength];

        var baseSlug = !string.IsNullOrWhiteSpace(sourceRepository?.Slug) ? sourceRepository.Slug : project.Name.ToLowerInvariant();
        var cleanBaseSlug = Regex.Replace(baseSlug, @"[^a-z0-9\-]", "").Trim('-');
        if (cleanBaseSlug.Length == 0) cleanBaseSlug = "repo";
        var forkSlug = $"{cleanBaseSlug}-{Guid.NewGuid().ToString()[..6]}";

        var forkedProject = new CloudProject
        {
            Id = Guid.NewGuid().ToString(),
            Name = forkName,
            DbType = project.DbType,
            SchemaJson = project.SchemaJson,
            NodePositionsJson = project.NodePositionsJson,
            UserId = userId,
            OrganizationId = personalOrg.Id,
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

        var forkedRepository = new ProjectRepository
        {
            ProjectId = forkedProject.Id,
            OwnerUserId = userId,
            ProjectName = forkedProject.Name,
            ProjectDbType = forkedProject.DbType,
            Slug = forkSlug,
            Description = $"Forked from {project.User?.UserName ?? "unknown"}/{project.Name}",
            Visibility = ProjectVisibility.Private,
            Category = sourceRepository?.Category ?? "General",
            ForkedFromProjectId = project.Id,
            CreatedAt = forkedProject.CreatedAt,
            UpdatedAt = forkedProject.UpdatedAt
        };
        var sourceTags = await _hub.GetTagsCsvAsync(project.Id, ct);

        // Proje, repo satırı ve kaynağın sayacı tek işlemde: fork ya tam olur ya hiç olmaz.
        await _transaction.ExecuteAsync(async c =>
        {
            await _context.SaveChangesAsync(acceptAllChangesOnSuccess: false, c);
            // Self-FK kaynak repo satırını ister; satırsız eski proje burada tamamlanır.
            if (sourceRepository is null)
                await _hub.EnsureRepositoryAsync(project.Id, project.UserId, project.Name, project.DbType, null, c);
            await _hub.CreateRepositoryAsync(forkedRepository, sourceTags, c);
            await _hub.IncrementForkCountAsync(project.Id, c);
        }, ct);
        _context.ChangeTracker.AcceptAllChanges();

        return CreatedAtAction(nameof(GetRepo), new { owner = callerName, slugOrId = forkSlug }, new
        {
            forkedProject.Id,
            forkedProject.Name,
            Slug = forkSlug,
            forkedRepository.ForkedFromProjectId,
            forkedRepository.Visibility,
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
        if (!await _context.CloudProjects.AsNoTracking().AnyAsync(p => p.Id == projectId, ct))
            return NotFound(new { error = "Repository not found." });

        var userId = CurrentUserId;
        if (await _hub.GetVisibilityAsync(projectId, ct) != ProjectVisibility.Public &&
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
            query = query.WithLabel(projectId, label);

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
                LabelNames = i.LabelLinks.OrderBy(l => l.Position).Select(l => l.DisplayName).ToList(),
                i.CreatedAt,
                i.ClosedAt,
                CreatedBy = new
                {
                    // Hesabı silinen yazar: nav null (FK SET NULL) -> "Deleted user".
                    Id = i.CreatedByUser != null ? i.CreatedByUser.Id : string.Empty,
                    Username = i.CreatedByUser != null ? (i.CreatedByUser.UserName ?? "unknown") : "Deleted user"
                },
                CommentsCount = _context.RepoIssueComments.Count(c => c.IssueId == i.Id)
            })
            .ToListAsync(ct);

        // Yanıtın tarihsel biçimi: Labels virgüllü metin (frontend onu ayırıyor).
        var items = issues.Select(i => new
        {
            i.Id,
            i.IssueNumber,
            i.Title,
            i.Body,
            i.IsClosed,
            i.RelatedTableName,
            i.NslRuleCode,
            Labels = IssueLabelRules.ToCsv(i.LabelNames),
            i.CreatedAt,
            i.ClosedAt,
            i.CreatedBy,
            i.CommentsCount,
        }).ToList();

        return Ok(new
        {
            Total = totalCount,
            OpenCount = openCount,
            ClosedCount = closedCount,
            Page = page,
            PageSize = pageSize,
            Items = items
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

        if (!await _context.CloudProjects.AsNoTracking().AnyAsync(p => p.Id == projectId, ct))
            return NotFound(new { error = "Repository not found." });

        if (await _hub.GetVisibilityAsync(projectId, ct) != ProjectVisibility.Public && !await _context.CanViewAsync(projectId, userId, ct))
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
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow,
                IsClosed = false
            };

            // Etiket satırı da bu işlemde açılıyor: eşzamanlı aynı yeni etiket UNIQUE'e takılır ve
            // numara çakışmasıyla aynı yoldan yeniden denenir (ikinci denemede satır artık var).
            await _context.AttachAsync(candidate, request.Labels, ct);
            await _context.RepoIssues.AddAsync(candidate, ct);
            try
            {
                await _context.SaveChangesAsync(ct);
                issue = candidate;
                break;
            }
            catch (DbUpdateException) when (attempt < maxAttempts)
            {
                _context.DetachPending();
            }
            catch (DbUpdateException)
            {
                _context.DetachPending();
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
            Labels = IssueLabelRules.ToCsv(issue.LabelLinks.OrderBy(l => l.Position).Select(l => l.DisplayName)),
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
            .FirstOrDefaultAsync(i => i.Id == issueId, ct);

        if (issue == null) return NotFound(new { error = "Issue not found." });

        // Yazar olmak tek başına yetki değil: repodan çıkarılmış (artık göremeyen) yazar da
        // issue'yu yeniden açıp kapatabiliyordu. Önce görme yetkisi, sonra yazar/editör.
        // Public repoda herkes görebilir; özel repoda üyelik şart. Göremeyene 404.
        if (await _hub.GetVisibilityAsync(issue.ProjectId, ct) != ProjectVisibility.Public &&
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

        var userId = CurrentUserId;
        if (await _hub.GetVisibilityAsync(issue.ProjectId, ct) != ProjectVisibility.Public &&
            (string.IsNullOrEmpty(userId) || !await _context.CanViewAsync(issue.ProjectId, userId, ct)))
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
                    Id = c.AuthorUser != null ? c.AuthorUser.Id : string.Empty,
                    Username = c.AuthorUser != null ? (c.AuthorUser.UserName ?? "unknown") : "Deleted user"
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

        if (await _hub.GetVisibilityAsync(issue.ProjectId, ct) != ProjectVisibility.Public && !await _context.CanViewAsync(issue.ProjectId, userId, ct))
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

        if (project == null || await _hub.GetVisibilityAsync(project.Id, ct) != ProjectVisibility.Public)
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
