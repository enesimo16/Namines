using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Xunit;
using static Namines.Tests.TestSupport.ModuleTestDb;

namespace Namines.Tests.Services;

/// <summary>Hub Application: CRUD, görünürlük, yıldız, fork, issue/yorum, etiket, keşif.</summary>
public sealed class HubApplicationTests : IDisposable
{
    private readonly ModuleTestDb _t = new();
    public void Dispose() => _t.Dispose();

    private ModuleKit Kit => _t.Kit;

    private sealed class NoDeletion : IProjectDeletion
    {
        public string? Deleted;
        public ProjectDeletionResult Result = ProjectDeletionResult.Success();
        public Task<ProjectDeletionResult> DeleteAsync(string projectId, string? actorUserId, CancellationToken ct, bool recordName = true)
        {
            Deleted = projectId;
            return Task.FromResult(Result);
        }
    }

    private readonly NoDeletion _deletion = new();
    private IHubRepositoryService Repos => Kit.HubRepositories(_deletion);
    private IHubIssueService Issues => Kit.HubIssues;

    private async Task SeedRepoAsync(ProjectVisibility visibility, string project = ProjectId, string slug = "repo-one", string? description = "d") =>
        await Kit.SeedRepositoryAsync(project, Owner, slug, visibility, description);

    // ── Görünürlük ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData(Outsider)]
    public async Task Private_repo_is_invisible_to_non_members(string? actor)
    {
        await SeedRepoAsync(ProjectVisibility.Private);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Repos.GetRepoAsync(actor, "_", ProjectId)).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Issues.ListAsync(actor, ProjectId, null, null, null, 1, 20)).Kind);
        Assert.Null(await Repos.GetBadgeAsync(actor, "_", ProjectId));
    }

    [Fact]
    public async Task Public_repo_is_readable_anonymously_but_caller_has_no_edit_right()
    {
        await SeedRepoAsync(ProjectVisibility.Public);
        var repo = (await Repos.GetRepoAsync(null, "_", ProjectId)).Value!;
        Assert.False(repo.Caller.CanEdit);
        Assert.Null(repo.Caller.Role);
        Assert.Equal(Owner, repo.Owner.Username);
    }

    [Fact]
    public async Task Member_sees_private_repo_with_role()
    {
        await SeedRepoAsync(ProjectVisibility.Private);
        var repo = (await Repos.GetRepoAsync(Viewer, "_", ProjectId)).Value!;
        Assert.Equal("Viewer", repo.Caller.Role);
        Assert.False(repo.Caller.CanEdit);
    }

    [Fact]
    public async Task Owner_slug_lookup_does_not_cross_owners()
    {
        await SeedRepoAsync(ProjectVisibility.Public);
        Assert.True((await Repos.GetRepoAsync(null, Owner, "repo-one")).IsOk);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Repos.GetRepoAsync(null, Editor, "repo-one")).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Repos.GetRepoAsync(null, "nobody", "repo-one")).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Repos.GetRepoAsync(null, Owner, " ")).Kind);
    }

    [Fact]
    public async Task Broken_schema_json_renders_empty_schema_instead_of_failing()
    {
        await SeedRepoAsync(ProjectVisibility.Public);
        var row = await Kit.Studio.Projects.SingleAsync(p => p.Id == ProjectId);
        row.SchemaJson = "{broken";
        await Kit.Studio.SaveChangesAsync();
        Kit.Studio.ChangeTracker.Clear();
        var repo = (await Repos.GetRepoAsync(null, "_", ProjectId)).Value!;
        Assert.Equal(row.Name, repo.Schema.Name);
        Assert.NotNull(await Repos.GetBadgeAsync(null, "_", ProjectId));
    }

    // ── Ayarlar ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Viewer)]
    [InlineData(Outsider)]
    public async Task Settings_reject_non_editors(string actor)
    {
        await SeedRepoAsync(ProjectVisibility.Private);
        Assert.Equal(ModuleOutcomeKind.Forbidden, (await Repos.UpdateSettingsAsync(actor, ProjectId, Command(description: "x"))).Kind);
    }

    private static RepoSettingsCommand Command(
        string? name = null, string? slug = null, string? description = null, ProjectVisibility? visibility = null,
        string? tags = null, string? github = null, bool? autoApprove = null, string? dbType = null) =>
        new(name, slug, description, visibility, null, tags, github, autoApprove, dbType);

    [Fact]
    public async Task Editor_changes_description_but_visibility_needs_admin()
    {
        await SeedRepoAsync(ProjectVisibility.Private);
        Assert.True((await Repos.UpdateSettingsAsync(Editor, ProjectId, Command(description: "new"))).IsOk);
        Assert.Equal(ModuleOutcomeKind.Forbidden, (await Repos.UpdateSettingsAsync(Editor, ProjectId, Command(visibility: ProjectVisibility.Public))).Kind);
        Assert.Equal(ModuleOutcomeKind.Forbidden, (await Repos.UpdateSettingsAsync(Editor, ProjectId, Command(slug: "another-slug"))).Kind);
        Assert.Equal(ModuleOutcomeKind.Forbidden, (await Repos.UpdateSettingsAsync(Editor, ProjectId, Command(autoApprove: true))).Kind);
        // Değişmeyen değer yetki gerektirmez: form tüm alanları geri gönderiyor.
        Assert.True((await Repos.UpdateSettingsAsync(Editor, ProjectId, Command(visibility: ProjectVisibility.Private, slug: "repo-one"))).IsOk);
    }

    [Theory]
    [InlineData("A B")]
    [InlineData("ab")]
    [InlineData("settings")]
    [InlineData("-x-")]
    public async Task Invalid_slug_is_rejected_before_any_write(string slug)
    {
        await SeedRepoAsync(ProjectVisibility.Private);
        Assert.Equal(ModuleOutcomeKind.Invalid, (await Repos.UpdateSettingsAsync(Owner, ProjectId, Command(slug: slug, description: "nope"))).Kind);
        Assert.Equal("d", (await Kit.HubService.GetRepositoryAsync(ProjectId))!.Description);
    }

    [Fact]
    public async Task Duplicate_slug_conflicts_and_invalid_inputs_fail()
    {
        await SeedRepoAsync(ProjectVisibility.Private);
        await SeedRepoAsync(ProjectVisibility.Private, OtherProjectId, "taken-slug");
        Assert.Equal(ModuleOutcomeKind.Conflict, (await Repos.UpdateSettingsAsync(Owner, ProjectId, Command(slug: "taken-slug"))).Kind);
        Assert.Equal(ModuleOutcomeKind.Invalid, (await Repos.UpdateSettingsAsync(Owner, ProjectId, Command(dbType: "Oraclish"))).Kind);
        Assert.Equal(ModuleOutcomeKind.Invalid, (await Repos.UpdateSettingsAsync(Owner, ProjectId, Command(github: "not a repo"))).Kind);
        Assert.Equal(ModuleOutcomeKind.Invalid, (await Repos.UpdateSettingsAsync(Owner, ProjectId, Command(name: new string('n', 129)))).Kind);
    }

    [Fact]
    public async Task Rename_updates_project_and_read_model_together()
    {
        await SeedRepoAsync(ProjectVisibility.Private);
        var view = (await Repos.UpdateSettingsAsync(Owner, ProjectId, Command(name: "Fresh", dbType: "postgres", tags: "B2B, saas ,b2b"))).Value!;
        Assert.Equal(("Fresh", "PostgreSQL", "B2B,saas"), (view.Name, view.DbType, view.Tags));

        Assert.Equal("Fresh", (await Kit.Projects.GetHeaderAsync(ProjectId))!.Name);
        var read = (await Kit.HubService.GetRepositoryAsync(ProjectId))!;
        Assert.Equal(("Fresh", "PostgreSQL"), (read.ProjectName, read.ProjectDbType));
    }

    // ── Silme ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Admin)]
    [InlineData(Editor)]
    [InlineData(Outsider)]
    public async Task Delete_requires_owner(string actor)
    {
        await SeedRepoAsync(ProjectVisibility.Public);
        Assert.Equal(ModuleOutcomeKind.Forbidden, (await Repos.DeleteRepoAsync(actor, ProjectId)).Kind);
        Assert.Null(_deletion.Deleted);
    }

    [Fact]
    public async Task Delete_goes_through_contract_and_carries_failure_code()
    {
        await SeedRepoAsync(ProjectVisibility.Public);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Repos.DeleteRepoAsync(Owner, "missing")).Kind);

        _deletion.Result = new ProjectDeletionResult(false, 409, "BACKUP_IN_PROGRESS", "busy");
        var failed = await Repos.DeleteRepoAsync(Owner, ProjectId);
        Assert.Equal((ModuleOutcomeKind.Failed, 409, "BACKUP_IN_PROGRESS"), (failed.Kind, failed.StatusCode, failed.Code));

        _deletion.Result = ProjectDeletionResult.Success();
        Assert.True((await Repos.DeleteRepoAsync(Owner, ProjectId)).IsOk);
        Assert.Equal(ProjectId, _deletion.Deleted);
    }

    // ── Yıldız ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Star_toggles_and_count_tracks_rows()
    {
        await SeedRepoAsync(ProjectVisibility.Public);
        Assert.Equal((true, 1), (await Repos.ToggleStarAsync(Outsider, ProjectId)).Value);
        Assert.Equal((true, 2), (await Repos.ToggleStarAsync(Viewer, ProjectId)).Value);
        Assert.Equal((false, 1), (await Repos.ToggleStarAsync(Outsider, ProjectId)).Value);
        Assert.Equal(1, await Kit.Hub.RepoStars.CountAsync());
    }

    [Fact]
    public async Task Star_private_repo_hidden_and_missing_repo_not_found()
    {
        await SeedRepoAsync(ProjectVisibility.Private);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Repos.ToggleStarAsync(Outsider, ProjectId)).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Repos.ToggleStarAsync(Owner, "missing")).Kind);
    }

    [Fact]
    public async Task Removing_user_stars_decrements_counts_once()
    {
        await SeedRepoAsync(ProjectVisibility.Public);
        await Repos.ToggleStarAsync(Outsider, ProjectId);
        await Kit.HubService.RemoveStarsByUserAsync(Outsider);
        await Kit.HubService.RemoveStarsByUserAsync(Outsider);
        Assert.Equal(0, (await Kit.HubService.GetRepositoryAsync(ProjectId))!.StarCount);
    }

    // ── Fork ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Fork_belongs_to_forker_is_private_and_increments_source_once()
    {
        await SeedRepoAsync(ProjectVisibility.Public);
        var fork = (await Repos.ForkAsync(Outsider, ProjectId, " Mine ")).Value!;

        Assert.Equal(("Mine", ProjectVisibility.Private, ProjectId), (fork.Name, fork.Visibility, fork.ForkedFromProjectId));
        var project = (await Kit.Projects.GetHeaderAsync(fork.Id))!;
        Assert.Equal(Outsider, project.UserId);
        Assert.NotEqual(OrgId, project.OrganizationId);
        Assert.Equal(1, (await Kit.HubService.GetRepositoryAsync(ProjectId))!.ForkCount);
        Assert.Single(await Kit.Branches.ListAsync(fork.Id));

        // Kaynağın üyesi fork'u görmez: ekibe sızmaz.
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Repos.GetRepoAsync(Admin, "_", fork.Id)).Kind);
    }

    [Fact]
    public async Task Fork_of_private_repo_is_hidden_and_long_name_rejected()
    {
        await SeedRepoAsync(ProjectVisibility.Private);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Repos.ForkAsync(Outsider, ProjectId, null)).Kind);
        Assert.Equal(ModuleOutcomeKind.Invalid, (await Repos.ForkAsync(Owner, ProjectId, new string('x', 129))).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Repos.ForkAsync(Owner, "missing", null)).Kind);
    }

    [Fact]
    public async Task Fork_without_source_repository_row_creates_it()
    {
        var fork = await Repos.ForkAsync(Owner, ProjectId, null);
        Assert.True(fork.IsOk);
        Assert.Equal(1, (await Kit.HubService.GetRepositoryAsync(ProjectId))!.ForkCount);
    }

    // ── Issue ve yorum ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Issue_requires_title(string? title)
    {
        await SeedRepoAsync(ProjectVisibility.Public);
        Assert.Equal(ModuleOutcomeKind.Invalid, (await Issues.CreateAsync(Owner, ProjectId, new CreateIssueCommand(title, null, null, null, null))).Kind);
    }

    [Fact]
    public async Task Issue_length_limits_are_enforced()
    {
        await SeedRepoAsync(ProjectVisibility.Public);
        Assert.Equal(ModuleOutcomeKind.Invalid, (await Issues.CreateAsync(Owner, ProjectId, new CreateIssueCommand(new string('t', 257), null, null, null, null))).Kind);
        Assert.Equal(ModuleOutcomeKind.Invalid, (await Issues.CreateAsync(Owner, ProjectId, new CreateIssueCommand("t", new string('b', 20_001), null, null, null))).Kind);
        Assert.Equal(ModuleOutcomeKind.Invalid, (await Issues.CreateAsync(Owner, ProjectId, new CreateIssueCommand("t", null, new string('r', 129), null, null))).Kind);
        Assert.Equal(ModuleOutcomeKind.Invalid, (await Issues.CreateAsync(Owner, ProjectId, new CreateIssueCommand("t", null, null, new string('c', 33), null))).Kind);
        Assert.Equal(ModuleOutcomeKind.Invalid, (await Issues.CreateAsync(Owner, ProjectId, new CreateIssueCommand("t", null, null, null, new string('l', 257)))).Kind);
    }

    [Fact]
    public async Task Issue_private_repo_blocks_outsider_public_allows_anyone_signed_in()
    {
        await SeedRepoAsync(ProjectVisibility.Private);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Issues.CreateAsync(Outsider, ProjectId, new CreateIssueCommand("t", null, null, null, null))).Kind);
        await SeedRepoAsync(ProjectVisibility.Public, OtherProjectId, "public-one");
        Assert.True((await Issues.CreateAsync(Outsider, OtherProjectId, new CreateIssueCommand("t", null, null, null, null))).IsOk);
    }

    [Fact]
    public async Task Labels_dedupe_keep_first_spelling_and_filter_exactly()
    {
        await SeedRepoAsync(ProjectVisibility.Public);
        var issue = (await Issues.CreateAsync(Owner, ProjectId, new CreateIssueCommand("t", null, null, null, " Bug, bug ,BUG,perf"))).Value!;
        Assert.Equal("Bug,perf", issue.Labels);

        Assert.Equal(1, (await Issues.ListAsync(null, ProjectId, "open", null, "BUG", 1, 20)).Value!.Total);
        Assert.Equal(0, (await Issues.ListAsync(null, ProjectId, "open", null, "bu", 1, 20)).Value!.Total);
        Assert.Equal(2, await Kit.Hub.IssueLabels.CountAsync());
        Assert.All(await Kit.Hub.RepoIssueLabels.AsNoTracking().ToListAsync(), l => Assert.Equal(ProjectId, l.ProjectId));
    }

    [Fact]
    public async Task Issue_numbers_are_sequential_per_project_and_status_filter_counts()
    {
        await SeedRepoAsync(ProjectVisibility.Public);
        await SeedRepoAsync(ProjectVisibility.Public, OtherProjectId, "other-repo");
        var a = (await Issues.CreateAsync(Owner, ProjectId, new CreateIssueCommand("a", null, null, null, null))).Value!;
        var b = (await Issues.CreateAsync(Owner, ProjectId, new CreateIssueCommand("b", null, null, null, null))).Value!;
        var c = (await Issues.CreateAsync(Owner, OtherProjectId, new CreateIssueCommand("c", null, null, null, null))).Value!;
        Assert.Equal((1, 2, 1), (a.IssueNumber, b.IssueNumber, c.IssueNumber));

        await Issues.SetStateAsync(Owner, a.Id, true);
        var open = (await Issues.ListAsync(null, ProjectId, "open", null, null, 1, 20)).Value!;
        Assert.Equal((1, 1, 1), (open.Total, open.OpenCount, open.ClosedCount));
        Assert.Equal(2, (await Issues.ListAsync(null, ProjectId, "all", null, null, 0, 1000)).Value!.Total);
    }

    [Fact]
    public async Task Close_requires_author_or_editor_and_visibility()
    {
        await SeedRepoAsync(ProjectVisibility.Public);
        var issue = (await Issues.CreateAsync(Outsider, ProjectId, new CreateIssueCommand("t", null, null, null, null))).Value!;
        Assert.Equal(ModuleOutcomeKind.Forbidden, (await Issues.SetStateAsync(Viewer, issue.Id, true)).Kind);
        Assert.True((await Issues.SetStateAsync(Outsider, issue.Id, true)).Value!.IsClosed);
        Assert.Null((await Issues.SetStateAsync(Editor, issue.Id, false)).Value!.ClosedAt);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Issues.SetStateAsync(Editor, "missing", false)).Kind);

        // Repo özele dönünce eski yazar da issue'ya dokunamaz.
        await Repos.UpdateSettingsAsync(Owner, ProjectId, Command(visibility: ProjectVisibility.Private));
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Issues.SetStateAsync(Outsider, issue.Id, true)).Kind);
    }

    [Fact]
    public async Task Comments_validate_and_show_deleted_author()
    {
        await SeedRepoAsync(ProjectVisibility.Public);
        var issue = (await Issues.CreateAsync(Owner, ProjectId, new CreateIssueCommand("t", null, null, null, null))).Value!;
        Assert.Equal(ModuleOutcomeKind.Invalid, (await Issues.AddCommentAsync(Owner, issue.Id, " ")).Kind);
        Assert.Equal(ModuleOutcomeKind.Invalid, (await Issues.AddCommentAsync(Owner, issue.Id, new string('c', 10_001))).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Issues.AddCommentAsync(Owner, "missing", "x")).Kind);

        var comment = (await Issues.AddCommentAsync(Outsider, issue.Id, " hello ")).Value!;
        Assert.Equal(("hello", Outsider), (comment.Body, comment.Author.Username));

        var row = await Kit.Hub.RepoIssueComments.SingleAsync();
        row.AuthorUserId = null;
        await Kit.Hub.SaveChangesAsync();
        Kit.Hub.ChangeTracker.Clear();
        Assert.Equal("Deleted user", (await Issues.ListCommentsAsync(null, issue.Id)).Value!.Single().Author.Username);
    }

    // ── Keşif ────────────────────────────────────────────────────────────────

    private static string QualitySchema() => JsonSerializer.Serialize(new
    {
        tables = new[] { "a", "b", "c", "d" }.Select(t => new
        {
            id = t, name = t,
            columns = new[] { new { id = t + "id", name = "id", type = "int", isPK = true }, new { id = t + "ref", name = "ref", type = "int", isPK = false } },
        }),
        relations = new[]
        {
            new { id = "r1", type = "one-to-many", sourceTableId = "a", sourceColumnId = "aref", targetTableId = "b", targetColumnId = "bid" },
            new { id = "r2", type = "one-to-many", sourceTableId = "c", sourceColumnId = "cref", targetTableId = "d", targetColumnId = "did" },
        },
    });

    private async Task MakeQualifiedAsync(string projectId)
    {
        var row = await Kit.Studio.Projects.SingleAsync(p => p.Id == projectId);
        row.SchemaJson = QualitySchema();
        await Kit.Studio.SaveChangesAsync();
        Kit.Studio.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Explore_shows_only_public_described_and_qualified()
    {
        await MakeQualifiedAsync(ProjectId);
        await SeedRepoAsync(ProjectVisibility.Public);
        await SeedRepoAsync(ProjectVisibility.Public, OtherProjectId, "thin-repo");

        var page = await Repos.ExploreAsync(new ExploreCriteria(null, null, null, null, false), 1, 12);
        var item = Assert.Single(page.Items);
        Assert.Equal((ProjectId, Owner, 4), (item.Id, item.Owner.Username, item.Stats.TableCount));

        await Repos.UpdateSettingsAsync(Owner, ProjectId, Command(visibility: ProjectVisibility.Private));
        Assert.Empty((await Repos.ExploreAsync(new ExploreCriteria(null, null, null, null, false), 1, 12)).Items);
    }

    [Fact]
    public async Task Explore_filters_by_tag_query_engine_and_clamps_paging()
    {
        await MakeQualifiedAsync(ProjectId);
        await SeedRepoAsync(ProjectVisibility.Public);
        await Repos.UpdateSettingsAsync(Owner, ProjectId, Command(tags: "B2B,saas", name: "Billing Hub"));

        Assert.Single((await Repos.ExploreAsync(new ExploreCriteria(null, "b2b", null, null, true), 1, 12)).Items);
        Assert.Empty((await Repos.ExploreAsync(new ExploreCriteria(null, "other", null, null, true), 1, 12)).Items);
        Assert.Single((await Repos.ExploreAsync(new ExploreCriteria(null, null, "billing", null, false), 1, 12)).Items);
        Assert.Empty((await Repos.ExploreAsync(new ExploreCriteria(null, null, null, "MySQL", false), 1, 12)).Items);

        var clamped = await Repos.ExploreAsync(new ExploreCriteria(null, null, null, null, false), -5, 5000);
        Assert.Equal((1, 50), (clamped.Page, clamped.PageSize));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Repos.ExploreAsync(null!, 1, 1));
    }

    // ── Silme sözleşmesi ─────────────────────────────────────────────────────

    [Fact]
    public async Task Purge_removes_all_hub_rows_of_project_and_detaches_forks()
    {
        await SeedRepoAsync(ProjectVisibility.Public);
        await Repos.ToggleStarAsync(Outsider, ProjectId);
        var issue = (await Issues.CreateAsync(Owner, ProjectId, new CreateIssueCommand("t", null, null, null, "bug"))).Value!;
        await Issues.AddCommentAsync(Owner, issue.Id, "c");
        var fork = (await Repos.ForkAsync(Outsider, ProjectId, null)).Value!;

        await Kit.HubService.PurgeProjectAsync(ProjectId);

        Assert.Null(await Kit.HubService.GetRepositoryAsync(ProjectId));
        Assert.Equal(0, await Kit.Hub.RepoIssues.CountAsync(i => i.ProjectId == ProjectId));
        Assert.Equal(0, await Kit.Hub.RepoIssueComments.CountAsync());
        Assert.Equal(0, await Kit.Hub.IssueLabels.CountAsync(l => l.ProjectId == ProjectId));
        Assert.Equal(0, await Kit.Hub.RepoStars.CountAsync(s => s.ProjectId == ProjectId));
        Assert.Null((await Kit.HubService.GetRepositoryAsync(fork.Id))!.ForkedFromProjectId);
        await Kit.HubService.PurgeProjectAsync(ProjectId);
    }
}
