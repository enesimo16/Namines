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
        ApplicationUser? user = null;
        if (owner == "me" && !string.IsNullOrEmpty(CurrentUserId))
        {
            user = await _userManager.FindByIdAsync(CurrentUserId);
        }
        else if (owner != "_" && owner != "me")
        {
            user = await _userManager.FindByNameAsync(owner) ?? await _userManager.FindByIdAsync(owner);
        }

        CloudProject? project = null;
        var normalizedSlugOrId = slugOrId.Trim().ToLowerInvariant();
        if (user != null)
        {
            project = await _context.CloudProjects
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.UserId == user.Id && (p.Slug == slugOrId || p.Slug == normalizedSlugOrId || p.Id == slugOrId), ct);
        }

        if (project == null)
        {
            project = await _context.CloudProjects
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.Id == slugOrId || p.Slug == slugOrId || p.Slug == normalizedSlugOrId, ct);
        }

        if (project == null)
        {
            return NotFound(new { error = "Repository not found." });
        }

        var currentUserId = CurrentUserId;

        // Yetki kontrolü: Public olmayan repoları yalnızca üyeler görebilir
        if (project.Visibility != ProjectVisibility.Public)
        {
            if (string.IsNullOrEmpty(currentUserId))
                return Unauthorized();

            var canView = await _context.CanViewAsync(project.Id, currentUserId, ct);
            if (!canView)
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
                Username = project.User?.UserName ?? "unknown",
                Email = project.User?.Email
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

    /// <summary>
    /// Repo ayarlarını güncelleme (Slug, Visibility, Category, Tags, Description, LinkedGithubRepo).
    /// Yalnızca Editor / Admin / Owner rollerine açıktır.
    /// </summary>
    [HttpPatch("repos/{projectId}/settings")]
    [Authorize]
    public async Task<IActionResult> UpdateSettings(string projectId, [FromBody] UpdateRepoSettingsRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var canEdit = await _context.CanEditAsync(projectId, userId, ct);
        if (!canEdit)
            return Forbid();

        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null)
            return NotFound(new { error = "Repository not found." });

        if (!string.IsNullOrWhiteSpace(request.Name))
            project.Name = request.Name.Trim();

        if (request.Slug != null)
        {
            var raw = request.Slug.Trim().ToLowerInvariant();
            var slug = Regex.Replace(raw, @"[^a-z0-9\-]", "");
            if (!string.IsNullOrEmpty(slug))
            {
                var conflict = await _context.CloudProjects.AnyAsync(p => p.Id != projectId && p.Slug == slug, ct);
                if (conflict)
                    return Conflict(new { error = "This slug is already taken." });

                project.Slug = slug;
            }
        }

        if (request.Description != null)
            project.Description = request.Description.Trim();

        if (request.Visibility.HasValue)
            project.Visibility = request.Visibility.Value;

        if (request.Category != null)
            project.Category = request.Category.Trim();

        if (request.Tags != null)
            project.Tags = request.Tags.Trim();

        if (request.LinkedGithubRepo != null)
            project.LinkedGithubRepo = request.LinkedGithubRepo.Trim();

        if (request.AutoApproveSafeChanges.HasValue)
            project.AutoApproveSafeChanges = request.AutoApproveSafeChanges.Value;

        if (!string.IsNullOrWhiteSpace(request.DbType))
            project.DbType = request.DbType.Trim();

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
    /// Yalnızca proje sahibi veya organizasyon sahibi silebilir.
    /// </summary>
    [HttpDelete("repos/{projectId}")]
    [Authorize]
    public async Task<IActionResult> DeleteRepo(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null)
            return NotFound(new { error = "Repository not found." });

        if (project.UserId != userId)
        {
            var isOrgOwner = project.OrganizationId != null && await _context.OrganizationMembers.AnyAsync(
                m => m.OrganizationId == project.OrganizationId && m.UserId == userId && m.Role == OrgRole.Owner,
                ct);
            if (!isOrgOwner)
                return Forbid();
        }

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
    public async Task<IActionResult> ToggleStar(string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound(new { error = "Repository not found." });

        if (project.Visibility != ProjectVisibility.Public && !await _context.CanViewAsync(projectId, userId, ct))
            return NotFound(new { error = "Repository not found." });

        var existing = await _context.RepoStars.FirstOrDefaultAsync(s => s.ProjectId == projectId && s.UserId == userId, ct);
        bool isStarred;
        if (existing != null)
        {
            _context.RepoStars.Remove(existing);
            project.StarCount = Math.Max(0, project.StarCount - 1);
            isStarred = false;
        }
        else
        {
            await _context.RepoStars.AddAsync(new RepoStar
            {
                ProjectId = projectId,
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            }, ct);
            project.StarCount++;
            isStarred = true;
        }

        await _context.SaveChangesAsync(ct);
        return Ok(new { isStarred, starCount = project.StarCount });
    }

    /// <summary>
    /// Repo kopyalama (Fork). Orijinal şema ve main branch klonlanır, fork_count++ artar.
    /// </summary>
    [HttpPost("repos/{projectId}/fork")]
    [Authorize]
    public async Task<IActionResult> Fork(string projectId, [FromBody] ForkRepoRequest? request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var project = await _context.CloudProjects
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

        var baseSlug = !string.IsNullOrWhiteSpace(project.Slug) ? project.Slug : project.Name.ToLowerInvariant();
        var cleanBaseSlug = Regex.Replace(baseSlug, @"[^a-z0-9\-]", "");
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
        project.ForkCount++;

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
    public async Task<IActionResult> CreateIssue(string projectId, [FromBody] CreateIssueRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { error = "Title is required." });

        var project = await _context.CloudProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound(new { error = "Repository not found." });

        if (project.Visibility != ProjectVisibility.Public && !await _context.CanViewAsync(projectId, userId, ct))
            return NotFound(new { error = "Repository not found." });

        var nextNumber = (await _context.RepoIssues
            .Where(i => i.ProjectId == projectId)
            .MaxAsync(i => (int?)i.IssueNumber, ct) ?? 0) + 1;

        var issue = new RepoIssue
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

        await _context.RepoIssues.AddAsync(issue, ct);
        await _context.SaveChangesAsync(ct);

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
    public async Task<IActionResult> UpdateIssueState(string issueId, [FromBody] UpdateIssueStateRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var issue = await _context.RepoIssues
            .Include(i => i.Project)
            .FirstOrDefaultAsync(i => i.Id == issueId, ct);

        if (issue == null) return NotFound(new { error = "Issue not found." });

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
    public async Task<IActionResult> CreateComment(string issueId, [FromBody] CreateCommentRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Body))
            return BadRequest(new { error = "Comment body cannot be empty." });

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
        ApplicationUser? user = null;
        if (owner != "_")
        {
            user = await _userManager.FindByNameAsync(owner) ?? await _userManager.FindByIdAsync(owner);
        }

        CloudProject? project = null;
        if (user != null)
        {
            project = await _context.CloudProjects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == user.Id && (p.Slug == slug || p.Id == slug), ct);
        }

        if (project == null)
        {
            project = await _context.CloudProjects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Slug == slug || p.Id == slug, ct);
        }

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
            rightText = $"{schema.Tables.Count} tables · non-3NF";
            rightColor = "#f59e0b";
        }
        else
        {
            rightText = $"{schema.Tables.Count} tables · 3NF verified";
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
