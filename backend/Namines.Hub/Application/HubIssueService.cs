using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.BuildingBlocks.Security;
using Namines.Hub.Domain;

namespace Namines.Hub.Application;

public sealed record CreateIssueCommand(string? Title, string? Body, string? RelatedTableName, string? NslRuleCode, string? Labels);

public sealed record IssuePerson(string Id, string Username);

public sealed record IssueListItem(
    string Id, int IssueNumber, string Title, string Body, bool IsClosed, string? RelatedTableName, string? NslRuleCode,
    string? Labels, DateTime CreatedAt, DateTime? ClosedAt, IssuePerson CreatedBy, int CommentsCount);

public sealed record IssuePage(int Total, int OpenCount, int ClosedCount, int Page, int PageSize, IReadOnlyList<IssueListItem> Items);

public sealed record CreatedIssue(
    string Id, int IssueNumber, string Title, string Body, bool IsClosed, string? RelatedTableName, string? NslRuleCode, string? Labels, DateTime CreatedAt);

public sealed record IssueStateView(string Id, int IssueNumber, bool IsClosed, DateTime? ClosedAt);

public sealed record CommentView(string Id, string Body, DateTime CreatedAt, IssuePerson Author);

/// <summary>Issue ve yorumlar. Herkese açık repoda oturumu olan herkes yazabilir; özel repoda üyelik şart.</summary>
public interface IHubIssueService
{
    Task<ModuleOutcome<IssuePage>> ListAsync(string? actorUserId, string projectId, string? status, string? relatedTable, string? label, int page, int pageSize, CancellationToken ct = default);

    Task<ModuleOutcome<CreatedIssue>> CreateAsync(string actorUserId, string projectId, CreateIssueCommand command, CancellationToken ct = default);

    Task<ModuleOutcome<IssueStateView>> SetStateAsync(string actorUserId, string issueId, bool isClosed, CancellationToken ct = default);

    Task<ModuleOutcome<IReadOnlyList<CommentView>>> ListCommentsAsync(string? actorUserId, string issueId, CancellationToken ct = default);

    Task<ModuleOutcome<CommentView>> AddCommentAsync(string actorUserId, string issueId, string? body, CancellationToken ct = default);
}

/// <summary>Modül içi sayımlar; repo görünümü N+1 üretmesin.</summary>
internal interface IHubIssueQueries
{
    Task<(int Open, int Closed)> CountIssuesAsync(string projectId, CancellationToken ct = default);
}

internal sealed class HubIssueService : IHubIssueService, IHubIssueQueries
{
    private const string RepositoryNotFound = "Repository not found.";
    private const string IssueNotFound = "Issue not found.";
    private const int MaxIssueBodyLength = 20_000;
    private const int MaxCommentLength = 10_000;
    private const int NumberAttempts = 5;

    private readonly IHubData _db;
    private readonly IHubApplicationService _hub;
    private readonly IStudioProjects _projects;
    private readonly ITenantProjectAuthority _authority;
    private readonly IIdentityDirectory _identity;

    public HubIssueService(IHubData db, IHubApplicationService hub, IStudioProjects projects, ITenantProjectAuthority authority, IIdentityDirectory identity)
    {
        _db = db;
        _hub = hub;
        _projects = projects;
        _authority = authority;
        _identity = identity;
    }

    private async Task<bool> CanSeeAsync(string projectId, string? userId, CancellationToken ct) =>
        await _hub.GetVisibilityAsync(projectId, ct) == ProjectVisibility.Public ||
        (!string.IsNullOrEmpty(userId) && await _authority.CanViewAsync(projectId, userId, ct));

    public async Task<(int Open, int Closed)> CountIssuesAsync(string projectId, CancellationToken ct = default)
    {
        var open = await _db.RepoIssues.AsNoTracking().CountAsync(i => i.ProjectId == projectId && !i.IsClosed, ct);
        var closed = await _db.RepoIssues.AsNoTracking().CountAsync(i => i.ProjectId == projectId && i.IsClosed, ct);
        return (open, closed);
    }

    public async Task<ModuleOutcome<IssuePage>> ListAsync(
        string? actorUserId, string projectId, string? status, string? relatedTable, string? label, int page, int pageSize, CancellationToken ct = default)
    {
        if (!await _projects.ExistsAsync(projectId, ct) || !await CanSeeAsync(projectId, actorUserId, ct))
            return ModuleOutcome<IssuePage>.NotFound(RepositoryNotFound);

        var query = _db.RepoIssues.AsNoTracking().Where(i => i.ProjectId == projectId);

        if (status?.Equals("closed", StringComparison.OrdinalIgnoreCase) == true)
            query = query.Where(i => i.IsClosed);
        else if (status?.Equals("all", StringComparison.OrdinalIgnoreCase) != true)
            query = query.Where(i => !i.IsClosed);

        if (!string.IsNullOrWhiteSpace(relatedTable))
            query = query.Where(i => i.RelatedTableName == relatedTable);

        if (!string.IsNullOrWhiteSpace(label))
        {
            query = query.WithLabel(projectId, label);
        }

        var total = await query.CountAsync(ct);
        var (open, closed) = await CountIssuesAsync(projectId, ct);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var rows = await query
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
                i.CreatedByUserId,
                CommentsCount = _db.RepoIssueComments.Count(c => c.IssueId == i.Id),
            })
            .ToListAsync(ct);

        var names = await _identity.GetUserNamesAsync(rows.Where(r => r.CreatedByUserId != null).Select(r => r.CreatedByUserId!).Distinct().ToList(), ct);

        // Yanıtın tarihsel biçimi: Labels virgüllü metin (frontend onu ayırıyor).
        var items = rows.Select(r => new IssueListItem(
                r.Id, r.IssueNumber, r.Title, r.Body, r.IsClosed, r.RelatedTableName, r.NslRuleCode,
                IssueLabelRules.ToCsv(r.LabelNames), r.CreatedAt, r.ClosedAt,
                Person(r.CreatedByUserId, names), r.CommentsCount))
            .ToList();

        return ModuleOutcome<IssuePage>.Ok(new IssuePage(total, open, closed, page, pageSize, items));
    }

    /// <summary>Hesabı silinen yazar: FK SET NULL ile kimlik boşalmış, ad "Deleted user".</summary>
    private static IssuePerson Person(string? userId, IReadOnlyDictionary<string, string?> names)
    {
        if (string.IsNullOrEmpty(userId) || !names.TryGetValue(userId, out var name))
            return new IssuePerson(string.Empty, "Deleted user");
        return new IssuePerson(userId, name ?? "unknown");
    }

    public async Task<ModuleOutcome<CreatedIssue>> CreateAsync(string actorUserId, string projectId, CreateIssueCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Title))
            return ModuleOutcome<CreatedIssue>.Invalid("Title is required.");
        // Kolon sınırlarını aşan değer veritabanında 500 olarak patlıyordu.
        if (command.Title.Trim().Length > 256)
            return ModuleOutcome<CreatedIssue>.Invalid("Title must be at most 256 characters.");
        if (command.Labels != null && command.Labels.Trim().Length > 256)
            return ModuleOutcome<CreatedIssue>.Invalid("Labels must be at most 256 characters.");
        if (command.RelatedTableName != null && command.RelatedTableName.Trim().Length > 128)
            return ModuleOutcome<CreatedIssue>.Invalid("RelatedTableName must be at most 128 characters.");
        if (command.NslRuleCode != null && command.NslRuleCode.Trim().Length > 32)
            return ModuleOutcome<CreatedIssue>.Invalid("NslRuleCode must be at most 32 characters.");
        if (command.Body != null && command.Body.Trim().Length > MaxIssueBodyLength)
            return ModuleOutcome<CreatedIssue>.Invalid($"Body must be at most {MaxIssueBodyLength} characters.");

        if (!await _projects.ExistsAsync(projectId, ct) || !await CanSeeAsync(projectId, actorUserId, ct))
            return ModuleOutcome<CreatedIssue>.NotFound(RepositoryNotFound);

        if (!_db.Database.IsNpgsql()) return await AllocateAndInsertAsync(actorUserId, projectId, command, ct);

        // Numara = MAX+1 okuma-değiştirme-yazma. Yalnızca yeniden denemek eşzamanlı istekte kullanıcıya 409 döndürüyordu
        // (gerçek PG'de 6 istekte görüldü); proje başına kısa kilit numara seçimini ve yeni etiket satırını sıraya koyar.
        // Satır kilidi değil: projenin repo satırı olmak zorunda değil.
        return await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            var lockKey = "hub.issue:" + projectId;
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
            var outcome = await AllocateAndInsertAsync(actorUserId, projectId, command, ct);
            if (outcome.IsOk) await tx.CommitAsync(ct);
            return outcome;
        });
    }

    private async Task<ModuleOutcome<CreatedIssue>> AllocateAndInsertAsync(string actorUserId, string projectId, CreateIssueCommand command, CancellationToken ct)
    {
        // Kilit almayan yol (SQLite) ya da sıraya girmeyen bir yazıcı için benzersiz indeks son sınır: kaybeden yeniden hesaplar.
        for (var attempt = 1; attempt <= NumberAttempts; attempt++)
        {
            _db.ChangeTracker.Clear();
            var next = (await _db.RepoIssues.Where(i => i.ProjectId == projectId).MaxAsync(i => (int?)i.IssueNumber, ct) ?? 0) + 1;

            var issue = new RepoIssue
            {
                Id = Guid.NewGuid().ToString(),
                ProjectId = projectId,
                IssueNumber = next,
                Title = command.Title!.Trim(),
                Body = command.Body?.Trim() ?? string.Empty,
                RelatedTableName = command.RelatedTableName?.Trim(),
                NslRuleCode = command.NslRuleCode?.Trim(),
                CreatedByUserId = actorUserId,
                CreatedAt = DateTime.UtcNow,
                IsClosed = false,
            };

            // Etiket satırı da bu işlemde: eşzamanlı aynı yeni etiket UNIQUE'e takılır ve numara
            // çakışmasıyla aynı yoldan yeniden denenir (ikinci denemede satır artık var).
            await RepoIssueLabelStore.AttachAsync(_db, issue, command.Labels, ct);
            _db.RepoIssues.Add(issue);
            try
            {
                await _db.SaveChangesAsync(ct);
                var labels = IssueLabelRules.ToCsv(issue.LabelLinks.OrderBy(l => l.Position).Select(l => l.DisplayName));
                _db.ChangeTracker.Clear();
                return ModuleOutcome<CreatedIssue>.Ok(new CreatedIssue(
                    issue.Id, issue.IssueNumber, issue.Title, issue.Body, issue.IsClosed, issue.RelatedTableName, issue.NslRuleCode, labels, issue.CreatedAt));
            }
            catch (DbUpdateException) when (attempt < NumberAttempts && _db.Database.CurrentTransaction is null)
            {
                _db.ChangeTracker.Clear();
            }
            catch (DbUpdateException)
            {
                _db.ChangeTracker.Clear();
                break;
            }
        }

        return ModuleOutcome<CreatedIssue>.Conflict("Could not allocate an issue number. Please retry.");
    }

    public async Task<ModuleOutcome<IssueStateView>> SetStateAsync(string actorUserId, string issueId, bool isClosed, CancellationToken ct = default)
    {
        _db.ChangeTracker.Clear();
        var issue = await _db.RepoIssues.FirstOrDefaultAsync(i => i.Id == issueId, ct);
        if (issue is null) return ModuleOutcome<IssueStateView>.NotFound(IssueNotFound);

        // Yazar olmak tek başına yetki değil: repodan çıkarılmış yazar da issue'yu açıp kapatabiliyordu.
        if (!await CanSeeAsync(issue.ProjectId, actorUserId, ct))
            return ModuleOutcome<IssueStateView>.NotFound(IssueNotFound);

        if (issue.CreatedByUserId != actorUserId && !await _authority.CanEditAsync(issue.ProjectId, actorUserId, ct))
            return ModuleOutcome<IssueStateView>.Forbidden();

        issue.IsClosed = isClosed;
        issue.ClosedAt = isClosed ? DateTime.UtcNow : null;
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return ModuleOutcome<IssueStateView>.Ok(new IssueStateView(issue.Id, issue.IssueNumber, issue.IsClosed, issue.ClosedAt));
    }

    public async Task<ModuleOutcome<IReadOnlyList<CommentView>>> ListCommentsAsync(string? actorUserId, string issueId, CancellationToken ct = default)
    {
        var projectId = await _db.RepoIssues.AsNoTracking().Where(i => i.Id == issueId).Select(i => i.ProjectId).FirstOrDefaultAsync(ct);
        if (projectId is null || !await CanSeeAsync(projectId, actorUserId, ct))
            return ModuleOutcome<IReadOnlyList<CommentView>>.NotFound(IssueNotFound);

        var rows = await _db.RepoIssueComments.AsNoTracking()
            .Where(c => c.IssueId == issueId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new { c.Id, c.Body, c.CreatedAt, c.AuthorUserId })
            .ToListAsync(ct);

        var names = await _identity.GetUserNamesAsync(rows.Where(r => r.AuthorUserId != null).Select(r => r.AuthorUserId!).Distinct().ToList(), ct);
        IReadOnlyList<CommentView> views = rows.Select(r => new CommentView(r.Id, r.Body, r.CreatedAt, Person(r.AuthorUserId, names))).ToList();
        return ModuleOutcome<IReadOnlyList<CommentView>>.Ok(views);
    }

    public async Task<ModuleOutcome<CommentView>> AddCommentAsync(string actorUserId, string issueId, string? body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body))
            return ModuleOutcome<CommentView>.Invalid("Comment body cannot be empty.");
        if (body.Trim().Length > MaxCommentLength)
            return ModuleOutcome<CommentView>.Invalid($"Comment must be at most {MaxCommentLength} characters.");

        var projectId = await _db.RepoIssues.AsNoTracking().Where(i => i.Id == issueId).Select(i => i.ProjectId).FirstOrDefaultAsync(ct);
        if (projectId is null) return ModuleOutcome<CommentView>.NotFound(IssueNotFound);
        if (!await CanSeeAsync(projectId, actorUserId, ct))
            return ModuleOutcome<CommentView>.NotFound(RepositoryNotFound);

        var comment = new RepoIssueComment
        {
            Id = Guid.NewGuid().ToString(),
            IssueId = issueId,
            Body = body.Trim(),
            AuthorUserId = actorUserId,
            CreatedAt = DateTime.UtcNow,
        };
        _db.RepoIssueComments.Add(comment);
        await _db.SaveChangesAsync(ct);
        _db.Entry(comment).State = EntityState.Detached;

        var names = await _identity.GetUserNamesAsync(new[] { actorUserId }, ct);
        return ModuleOutcome<CommentView>.Ok(new CommentView(comment.Id, comment.Body, comment.CreatedAt, new IssuePerson(actorUserId, names.GetValueOrDefault(actorUserId) ?? "unknown")));
    }
}

/// <summary>Etiketlerin tek yazma yolu; ikinci bir etiket sistemi yok.</summary>
internal static class RepoIssueLabelStore
{
    /// <summary>Etikete göre tam eşleşme; proje koşulu sorguda, eski LIKE alt dizge eşleşmesi yok.</summary>
    public static IQueryable<RepoIssue> WithLabel(this IQueryable<RepoIssue> query, string projectId, string label)
    {
        var key = ProjectTagRules.Key(label);
        return query.Where(i => i.LabelLinks.Any(l => l.ProjectId == projectId && l.Label.NormalizedName == key));
    }

    public static async Task AttachAsync(IHubData db, RepoIssue issue, string? labelsCsv, CancellationToken ct = default)
    {
        var parsed = IssueLabelRules.Parse(labelsCsv);
        if (parsed.Count == 0) return;

        var keys = parsed.Select(p => p.Key).ToList();
        var existing = await db.IssueLabels
            .Where(l => l.ProjectId == issue.ProjectId && keys.Contains(l.NormalizedName))
            .ToDictionaryAsync(l => l.NormalizedName, StringComparer.Ordinal, ct);

        foreach (var label in parsed)
        {
            if (!existing.TryGetValue(label.Key, out var row))
            {
                row = new IssueLabel { ProjectId = issue.ProjectId, Name = label.Display, NormalizedName = label.Key };
                db.IssueLabels.Add(row);
            }

            issue.LabelLinks.Add(new RepoIssueLabel
            {
                RepoIssueId = issue.Id,
                ProjectId = issue.ProjectId,
                Label = row,
                DisplayName = label.Display,
                Position = label.Position,
            });
        }
    }
}
