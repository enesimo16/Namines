using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Studio.Application;
using Xunit;
using static Namines.Tests.TestSupport.ModuleTestDb;

namespace Namines.Tests.Services;

/// <summary>Studio Application: başarı, başarısızlık, yetkisiz, geçersiz girdi, tekrar, eşzamanlılık, idempotensi.</summary>
public sealed class StudioApplicationTests : IDisposable
{
    private readonly ModuleTestDb _t = new();
    public void Dispose() => _t.Dispose();

    private ModuleKit Kit => _t.Kit;

    private const string TwoTables = """{"tables":[{"id":"a","name":"a"},{"id":"b","name":"b"}],"relations":[]}""";

    // ── Proje sözleşmesi ─────────────────────────────────────────────────────

    [Fact]
    public async Task Save_creates_then_updates_and_clears_tracker()
    {
        await Kit.Projects.SaveAsync(new[] { new ProjectWrite("n1", "New", "PostgreSQL", "{}", "{}", Owner, OrgId) });
        var created = (await Kit.Projects.GetHeaderAsync("n1"))!;
        await Kit.Projects.SaveAsync(new[] { new ProjectWrite("n1", "Renamed", "MySQL", "{}", "{}", Owner, OrgId, created.RowVersion) });

        var row = await Kit.Projects.GetAsync("n1");
        Assert.Equal(("Renamed", "MySQL"), (row!.Name, row.DbType));
        Assert.Empty(Kit.Studio.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Save_empty_is_a_no_op_and_null_is_rejected()
    {
        await Kit.Projects.SaveAsync(Array.Empty<ProjectWrite>());
        await Assert.ThrowsAsync<ArgumentNullException>(() => Kit.Projects.SaveAsync(null!));
    }

    [Fact]
    public async Task Get_unknown_returns_null_and_batch_skips_unknown()
    {
        Assert.Null(await Kit.Projects.GetAsync("missing"));
        Assert.False(await Kit.Projects.ExistsAsync("missing"));
        var many = await Kit.Projects.GetManyAsync(new[] { ProjectId, "missing" });
        Assert.Equal(new[] { ProjectId }, many.Keys);
        Assert.Empty(await Kit.Projects.GetManyAsync(Array.Empty<string>()));
    }

    [Fact]
    public async Task Delete_is_idempotent()
    {
        Assert.True(await Kit.Projects.DeleteAsync(OtherProjectId));
        Assert.False(await Kit.Projects.DeleteAsync(OtherProjectId));
    }

    [Fact]
    public async Task Reassign_and_organization_change_only_touch_the_named_project()
    {
        await Kit.Projects.ReassignCreatorAsync(ProjectId, Editor);
        await Kit.Projects.SetOrganizationAsync(ProjectId, "org-2");
        Assert.Equal((Editor, "org-2"), ((await Kit.Projects.GetHeaderAsync(ProjectId))!.UserId, (await Kit.Projects.GetHeaderAsync(ProjectId))!.OrganizationId));
        Assert.Equal(Owner, (await Kit.Projects.GetHeaderAsync(OtherProjectId))!.UserId);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Kit.Projects.SetOrganizationAsync(ProjectId, " "));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Kit.Projects.ReassignCreatorAsync(ProjectId, ""));
    }

    // ── Yetki ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Owner, true, true, true)]
    [InlineData(Admin, true, true, true)]
    [InlineData(Editor, true, true, false)]
    [InlineData(Viewer, true, false, false)]
    [InlineData(Outsider, false, false, false)]
    public async Task Role_matrix(string user, bool view, bool edit, bool manage)
    {
        var authority = Kit.DefaultAuthority;
        Assert.Equal(view, await authority.CanViewAsync(ProjectId, user));
        Assert.Equal(edit, await authority.CanEditAsync(ProjectId, user));
        Assert.Equal(manage, await authority.CanManageMembersAsync(ProjectId, user));
    }

    [Fact]
    public async Task Legacy_project_without_organization_belongs_to_creator_only()
    {
        Kit.Studio.Projects.Add(ModuleTestDb.Project("legacy", null));
        await Kit.Studio.SaveChangesAsync();
        Assert.Equal(OrgRole.Owner, await Kit.DefaultAuthority.GetRoleAsync("legacy", Owner));
        Assert.Null(await Kit.DefaultAuthority.GetRoleAsync("legacy", Admin));
        Assert.Equal(1, await Kit.DefaultAuthority.CountVotingMembersAsync("legacy"));
    }

    [Fact]
    public async Task Voting_members_exclude_viewer()
    {
        Assert.Equal(3, await Kit.DefaultAuthority.CountVotingMembersAsync(ProjectId));
    }

    [Fact]
    public async Task Workspace_lists_org_projects_with_owner_flag_and_hides_from_outsider()
    {
        var owner = await Kit.Workspace.ListForUserAsync(Owner);
        Assert.Equal(2, owner.Count);
        Assert.All(owner, p => Assert.True(p.IsOwner));
        Assert.Equal(Owner, owner[0].OwnerName);

        var viewer = await Kit.Workspace.ListForUserAsync(Viewer);
        Assert.Equal(2, viewer.Count);
        Assert.All(viewer, p => Assert.False(p.IsOwner));

        Assert.Empty(await Kit.Workspace.ListForUserAsync(Outsider));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Kit.Workspace.ListForUserAsync(""));
    }

    // ── Yaşam döngüsü (denetim M-2) ──────────────────────────────────────────

    private async Task AddOrgAsync(string orgId, string member, OrgRole role)
    {
        Kit.Db.Organizations.Add(new Organization { Id = orgId, Name = orgId });
        Kit.Db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = orgId, UserId = member, Role = role });
        await Kit.Db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(Viewer)]
    [InlineData(Editor)]
    [InlineData(Outsider)]
    public async Task Sharing_a_project_requires_member_management(string actor)
    {
        await AddOrgAsync("org-2", actor, OrgRole.Owner);
        var outcome = await Kit.Lifecycle.ShareToOrganizationAsync(actor, ProjectId, "org-2");
        Assert.Equal(ModuleOutcomeKind.NotFound, outcome.Kind);
        Assert.Equal(OrgId, (await Kit.Projects.GetHeaderAsync(ProjectId))!.OrganizationId);
    }

    [Fact]
    public async Task Sharing_into_an_organization_the_actor_is_not_in_is_refused()
    {
        // Proje yöneticisi bile projeyi yabancı bir org'a (ve onun sahiplerine) veremez: m-5'in IDOR'u.
        Kit.Db.Organizations.Add(new Organization { Id = "foreign", Name = "Foreign" });
        await Kit.Db.SaveChangesAsync();
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Kit.Lifecycle.ShareToOrganizationAsync(Owner, ProjectId, "foreign")).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Kit.Lifecycle.ShareToOrganizationAsync(Owner, ProjectId, "")).Kind);
        Assert.Equal(OrgId, (await Kit.Projects.GetHeaderAsync(ProjectId))!.OrganizationId);
    }

    [Fact]
    public async Task Admin_who_is_a_member_of_the_target_can_share_and_repeat_is_idempotent()
    {
        await AddOrgAsync("org-2", Admin, OrgRole.Viewer);
        Assert.Equal("org-2", (await Kit.Lifecycle.ShareToOrganizationAsync(Admin, ProjectId, "org-2")).Value);
        Assert.Equal("org-2", (await Kit.Projects.GetHeaderAsync(ProjectId))!.OrganizationId);
        // Proje artık org-2'de; Admin orada Viewer, yeniden taşıyamaz.
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Kit.Lifecycle.ShareToOrganizationAsync(Admin, ProjectId, "org-2")).Kind);
    }

    [Theory]
    [InlineData(Admin)]
    [InlineData(Editor)]
    [InlineData(Outsider)]
    public async Task Unshare_is_owner_only(string actor)
    {
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Kit.Lifecycle.UnshareAsync(actor, ProjectId)).Kind);
        Assert.Equal(OrgId, (await Kit.Projects.GetHeaderAsync(ProjectId))!.OrganizationId);
    }

    [Fact]
    public async Task Unshare_returns_the_project_to_the_founders_personal_organization()
    {
        var outcome = await Kit.Lifecycle.UnshareAsync(Owner, ProjectId);
        Assert.True(outcome.IsOk);
        var personal = await Kit.Db.Organizations.SingleAsync(o => o.Id == outcome.Value);
        Assert.True(personal.IsPersonal);
        Assert.Equal(outcome.Value, (await Kit.Projects.GetHeaderAsync(ProjectId))!.OrganizationId);
    }

    [Fact]
    public async Task Resolving_organization_needs_view_access_and_does_not_migrate_for_outsiders()
    {
        Assert.Null(await Kit.Lifecycle.ResolveOrganizationAsync(Outsider, ProjectId));
        Assert.Null(await Kit.Lifecycle.ResolveOrganizationAsync("", ProjectId));
        Assert.Equal(OrgId, await Kit.Lifecycle.ResolveOrganizationAsync(Viewer, ProjectId));
    }

    private static ProjectSyncItem Item(string id, string name = "N", string schema = "{}", uint? version = null) =>
        new(id, name, "PostgreSQL", schema, "{}", version);

    [Fact]
    public async Task Sync_updates_editable_creates_new_and_writes_the_read_model()
    {
        var outcome = await Kit.Lifecycle.SyncAsync(Editor, "editor", new[] { Item(ProjectId, "Edited", TwoTables), Item("fresh-1", "Fresh") });
        Assert.Equal(ProjectSyncStatus.Saved, outcome.Status);
        Assert.Equal(2, outcome.SavedCount);
        Assert.Equal(TwoTables, (await Kit.Projects.GetAsync(ProjectId))!.SchemaJson);
        var change = Assert.Single(outcome.Changes);
        Assert.Equal(ProjectId, change.ProjectId);

        var fresh = (await Kit.Projects.GetAsync("fresh-1"))!;
        Assert.Equal(Editor, fresh.UserId);
        Assert.NotEqual(OrgId, fresh.OrganizationId);
        var repo = await Kit.HubService.GetRepositoryAsync("fresh-1");
        Assert.Equal(("Fresh", "fresh"), (repo!.ProjectName, repo.Slug));
        Assert.Equal("Edited", (await Kit.HubService.GetRepositoryAsync(ProjectId))!.ProjectName);
    }

    [Fact]
    public async Task Sync_skips_read_only_projects_for_viewers()
    {
        var outcome = await Kit.Lifecycle.SyncAsync(Viewer, "viewer", new[] { Item(ProjectId, "Hijack", TwoTables) });
        Assert.Equal(0, outcome.SavedCount);
        Assert.NotEqual("Hijack", (await Kit.Projects.GetAsync(ProjectId))!.Name);
    }

    [Fact]
    public async Task Sync_by_an_outsider_never_overwrites_and_gets_a_new_identity()
    {
        var outcome = await Kit.Lifecycle.SyncAsync(Outsider, "outsider", new[] { Item(ProjectId, "Hijack", TwoTables), Item("../etc", "Bad id") });
        Assert.Equal(2, outcome.SavedCount);
        Assert.NotEqual("Hijack", (await Kit.Projects.GetAsync(ProjectId))!.Name);
        Assert.Null(await Kit.Projects.GetAsync("../etc"));
        Assert.Equal(2, await Kit.Studio.Projects.CountAsync(p => p.UserId == Outsider));
    }

    [Fact]
    public async Task Sync_with_a_stale_version_is_a_conflict_and_writes_nothing()
    {
        var header = (await Kit.Projects.GetHeaderAsync(ProjectId))!;
        var outcome = await Kit.Lifecycle.SyncAsync(Owner, "owner", new[] { Item("other-new", "X"), Item(ProjectId, "Stale", TwoTables, header.RowVersion + 1) });
        Assert.Equal(ProjectSyncStatus.VersionConflict, outcome.Status);
        Assert.Equal((ProjectId, header.RowVersion + 1, header.RowVersion), (outcome.Conflict!.ProjectId, outcome.Conflict.YourVersion, outcome.Conflict.CurrentVersion));
        Assert.Null(await Kit.Projects.GetAsync("other-new"));
    }

    [Fact]
    public async Task Sync_reserves_slugs_inside_one_batch_and_rejects_bad_input()
    {
        var outcome = await Kit.Lifecycle.SyncAsync(Owner, "owner", new[] { Item("s1", "Same"), Item("s2", "Same"), Item("s3", "!!!") });
        Assert.Equal(3, outcome.SavedCount);
        Assert.Equal("same", (await Kit.HubService.GetRepositoryAsync("s1"))!.Slug);
        Assert.Equal("same-2", (await Kit.HubService.GetRepositoryAsync("s2"))!.Slug);
        Assert.StartsWith("project-", (await Kit.HubService.GetRepositoryAsync("s3"))!.Slug);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Kit.Lifecycle.SyncAsync("", "x", Array.Empty<ProjectSyncItem>()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Kit.Lifecycle.SyncAsync(Owner, "owner", null!));
    }

    [Fact]
    public async Task Auto_approve_requires_member_management()
    {
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Kit.Lifecycle.SetAutoApproveAsync(Editor, ProjectId, true)).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Kit.Lifecycle.SetAutoApproveAsync(Admin, "missing", true)).Kind);
        Assert.False((await Kit.Projects.GetHeaderAsync(ProjectId))!.AutoApproveSafeChanges);
        Assert.True((await Kit.Lifecycle.SetAutoApproveAsync(Admin, ProjectId, true)).IsOk);
        Assert.True((await Kit.Lifecycle.SetAutoApproveAsync(Admin, ProjectId, true)).IsOk);
        Assert.True((await Kit.Projects.GetHeaderAsync(ProjectId))!.AutoApproveSafeChanges);
    }

    [Fact]
    public async Task Rename_touches_only_name_engine_and_timestamp()
    {
        await Kit.Projects.SaveAsync(new[] { new ProjectWrite(ProjectId, "P", "PostgreSQL", TwoTables, "{\"a\":1}", Owner, OrgId, (await Kit.Projects.GetHeaderAsync(ProjectId))!.RowVersion) });
        Assert.True(await Kit.Projects.RenameAsync(ProjectId, "Renamed", "MySQL"));
        var row = (await Kit.Projects.GetAsync(ProjectId))!;
        Assert.Equal(("Renamed", "MySQL", TwoTables, "{\"a\":1}", OrgId), (row.Name, row.DbType, row.SchemaJson, row.NodePositionsJson, row.OrganizationId));
        Assert.False(await Kit.Projects.RenameAsync("missing", "x", "PostgreSQL"));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Kit.Projects.RenameAsync(ProjectId, " ", "MySQL"));
    }

    [Fact]
    public async Task Updating_an_existing_project_without_its_version_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Kit.Projects.SaveAsync(new[] { new ProjectWrite(ProjectId, "X", "PostgreSQL", "{}", "{}", Owner, OrgId) }));
    }

    // ── Branch ve sürüm ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(Viewer)]
    [InlineData(Outsider)]
    public async Task Branch_operations_hide_project_from_non_editors(string actor)
    {
        var service = Kit.BranchService;
        Assert.Equal(ModuleOutcomeKind.NotFound, (await service.CreateAsync(actor, new CreateBranchCommand(ProjectId, "x", null, null, false))).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await service.ListAsync(actor, ProjectId)).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await service.EnsureDefaultAsync(actor, ProjectId)).Kind);
        Assert.Empty(await Kit.Branches.ListAsync(ProjectId));
    }

    [Fact]
    public async Task Create_branch_validates_input_duplicate_and_foreign_parent()
    {
        var service = Kit.BranchService;
        Assert.Equal(ModuleOutcomeKind.Invalid, (await service.CreateAsync(Editor, new CreateBranchCommand(ProjectId, " ", null, null, false))).Kind);

        var created = await service.CreateAsync(Editor, new CreateBranchCommand(ProjectId, "dev", null, null, false));
        Assert.True(created.IsOk);
        Assert.Equal(ModuleOutcomeKind.Conflict, (await service.CreateAsync(Editor, new CreateBranchCommand(ProjectId, "dev", null, null, false))).Kind);

        var other = await service.CreateAsync(Editor, new CreateBranchCommand(OtherProjectId, "other", null, null, false));
        var foreign = await service.CreateAsync(Editor, new CreateBranchCommand(ProjectId, "child", other.Value!.Id, null, false));
        Assert.Equal(ModuleOutcomeKind.Invalid, foreign.Kind);
    }

    [Fact]
    public async Task New_default_branch_demotes_previous_default()
    {
        var service = Kit.BranchService;
        var first = await service.CreateAsync(Editor, new CreateBranchCommand(ProjectId, "a", null, null, true));
        var second = await service.CreateAsync(Editor, new CreateBranchCommand(ProjectId, "b", null, null, true));
        var branches = await Kit.Branches.ListAsync(ProjectId);
        Assert.Equal(second.Value!.Id, Assert.Single(branches, b => b.IsDefault).Id);
        Assert.False(branches.Single(b => b.Id == first.Value!.Id).IsDefault);
    }

    [Fact]
    public async Task Ensure_default_is_idempotent_and_reports_creation()
    {
        var first = await Kit.BranchService.EnsureDefaultAsync(Editor, ProjectId);
        var second = await Kit.BranchService.EnsureDefaultAsync(Editor, ProjectId);
        Assert.True(first.Value.Created);
        Assert.False(second.Value.Created);
        Assert.Equal(first.Value.Branch.Id, second.Value.Branch.Id);
    }

    [Fact]
    public async Task Commit_numbers_versions_checksums_and_counts_tables()
    {
        var branch = (await Kit.BranchService.EnsureDefaultAsync(Editor, ProjectId)).Value.Branch;
        var v1 = await Kit.BranchService.CommitAsync(Editor, branch.Id, TwoTables, "m1");
        var v2 = await Kit.BranchService.CommitAsync(Editor, branch.Id, TwoTables, null);

        Assert.Equal((1, (short)2, Editor), (v1.Value!.Version, v1.Value.TableCount, v1.Value.AuthorUserId));
        Assert.Equal(2, v2.Value!.Version);
        Assert.Equal(v1.Value.Checksum, v2.Value.Checksum);
        Assert.Equal(64, v1.Value.Checksum.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Commit_rejects_empty_schema(string? schema)
    {
        var branch = (await Kit.BranchService.EnsureDefaultAsync(Editor, ProjectId)).Value.Branch;
        Assert.Equal(ModuleOutcomeKind.Invalid, (await Kit.BranchService.CommitAsync(Editor, branch.Id, schema, null)).Kind);
    }

    [Fact]
    public async Task Commit_rejects_broken_json_and_closed_branch_and_foreign_actor()
    {
        var branch = (await Kit.BranchService.EnsureDefaultAsync(Editor, ProjectId)).Value.Branch;
        Assert.Equal(ModuleOutcomeKind.Invalid, (await Kit.BranchService.CommitAsync(Editor, branch.Id, "{not json", null)).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Kit.BranchService.CommitAsync(Viewer, branch.Id, TwoTables, null)).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Kit.BranchService.CommitAsync(Editor, "missing", TwoTables, null)).Kind);

        var row = await Kit.Studio.Branches.SingleAsync(b => b.Id == branch.Id);
        row.ClosedAt = DateTime.UtcNow;
        await Kit.Studio.SaveChangesAsync();
        Kit.Studio.ChangeTracker.Clear();
        Assert.Equal(ModuleOutcomeKind.Conflict, (await Kit.BranchService.CommitAsync(Editor, branch.Id, TwoTables, null)).Kind);
    }

    [Fact]
    public async Task Version_read_hides_other_tenants_rows()
    {
        var branch = (await Kit.BranchService.EnsureDefaultAsync(Editor, ProjectId)).Value.Branch;
        var v1 = (await Kit.BranchService.CommitAsync(Editor, branch.Id, TwoTables, null)).Value!;
        Assert.True((await Kit.BranchService.GetVersionAsync(Editor, v1.Id)).IsOk);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Kit.BranchService.GetVersionAsync(Outsider, v1.Id)).Kind);
    }

    [Fact]
    public async Task Merge_preview_requires_ancestor_and_versions()
    {
        var service = Kit.BranchService;
        var main = (await service.EnsureDefaultAsync(Editor, ProjectId)).Value.Branch;
        Assert.Equal(ModuleOutcomeKind.Invalid, (await service.PreviewMergeAsync(Editor, main.Id)).Kind);

        var feature = (await service.CreateAsync(Editor, new CreateBranchCommand(ProjectId, "f", main.Id, 1, false))).Value!;
        Assert.Equal(ModuleOutcomeKind.Invalid, (await service.PreviewMergeAsync(Editor, feature.Id)).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await service.PreviewMergeAsync(Outsider, feature.Id)).Kind);
    }

    [Fact]
    public async Task Merge_preview_reports_clean_merge_and_conflict()
    {
        const string Base = """{"tables":[{"id":"a","stableUuid":"ua","name":"a","columns":[{"id":"c","stableUuid":"uc","name":"c","type":"int"}]}],"relations":[]}""";
        const string Ours = """{"tables":[{"id":"a","stableUuid":"ua","name":"a","columns":[{"id":"c","stableUuid":"uc","name":"c","type":"bigint"}]}],"relations":[]}""";
        const string Theirs = """{"tables":[{"id":"a","stableUuid":"ua","name":"a","columns":[{"id":"c","stableUuid":"uc","name":"c","type":"text"}]}],"relations":[]}""";
        var service = Kit.BranchService;
        var main = (await service.EnsureDefaultAsync(Editor, ProjectId)).Value.Branch;
        await service.CommitAsync(Editor, main.Id, Base, "base");
        var feature = (await service.CreateAsync(Editor, new CreateBranchCommand(ProjectId, "f", main.Id, 1, false))).Value!;
        await service.CommitAsync(Editor, feature.Id, Base, "same");

        var clean = await service.PreviewMergeAsync(Editor, feature.Id);
        Assert.True(clean.IsOk);
        Assert.Empty(clean.Value!.Result.Conflicts);

        await service.CommitAsync(Editor, main.Id, Ours, "ours");
        await service.CommitAsync(Editor, feature.Id, Theirs, "theirs");
        var conflicted = await service.PreviewMergeAsync(Editor, feature.Id);
        Assert.True(conflicted.IsOk);
        Assert.NotEmpty(conflicted.Value!.Result.Conflicts);
    }

    [Fact]
    public async Task Resolve_room_finds_project_with_and_without_prefix()
    {
        var branch = (await Kit.BranchService.EnsureDefaultAsync(Editor, ProjectId)).Value.Branch;
        Assert.Equal(ProjectId, await Kit.Branches.ResolveProjectIdAsync(branch.Id));
        Assert.Equal(ProjectId, await Kit.Branches.ResolveProjectIdAsync("room-" + branch.Id));
        Assert.Null(await Kit.Branches.ResolveProjectIdAsync("room-nope"));
    }

    [Fact]
    public async Task Add_version_to_missing_branch_fails_loudly()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Kit.Branches.AddVersionAsync("missing", "{}", null, 0, Owner));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Kit.Branches.AddVersionAsync("missing", " ", null, 0, Owner));
    }

    // ── Paylaşım ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Editor)]
    [InlineData(Viewer)]
    [InlineData(Outsider)]
    [InlineData("")]
    public async Task Share_lifecycle_requires_member_management(string actor)
    {
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Kit.Shares.CreateAsync(actor, ProjectId)).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Kit.Shares.RevokeAsync(actor, ProjectId)).Kind);
        Assert.Equal(0, await Kit.Studio.ProjectShares.CountAsync());
    }

    [Fact]
    public async Task Share_stores_hash_only_and_revoke_is_idempotent()
    {
        var created = (await Kit.Shares.CreateAsync(Admin, ProjectId)).Value!;
        Assert.Equal(0, created.ClosedOldestLinks);
        var row = await Kit.Studio.ProjectShares.AsNoTracking().SingleAsync();
        Assert.NotEqual(created.Token, row.ShareTokenHash);
        Assert.Equal(ShareTokenHasher.Hash(created.Token), row.ShareTokenHash);

        Assert.NotNull(await Kit.StudioService.GetSharedProjectByTokenHashAsync(row.ShareTokenHash));
        Assert.Equal(1, (await Kit.Shares.RevokeAsync(Admin, ProjectId)).Value);
        Assert.Equal(0, (await Kit.Shares.RevokeAsync(Admin, ProjectId)).Value);
        Assert.Null(await Kit.StudioService.GetSharedProjectByTokenHashAsync(row.ShareTokenHash));
    }

    [Fact]
    public async Task Share_cap_closes_oldest_and_says_so()
    {
        for (var i = 0; i < StudioApplicationService.MaxActiveSharesPerProject; i++)
            Assert.Equal(0, (await Kit.Shares.CreateAsync(Admin, ProjectId)).Value!.ClosedOldestLinks);

        var overflow = (await Kit.Shares.CreateAsync(Admin, ProjectId)).Value!;
        Assert.Equal(1, overflow.ClosedOldestLinks);
        Assert.Equal(StudioApplicationService.MaxActiveSharesPerProject, await Kit.Studio.ProjectShares.CountAsync(s => s.RevokedAt == null));
    }

    [Fact]
    public async Task Share_for_missing_project_is_not_found()
    {
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Kit.Shares.CreateAsync(Admin, "missing")).Kind);
    }

    // ── İlişkiler ───────────────────────────────────────────────────────────

    private static CreateRelationCommand Relation(string source = ProjectId, string target = OtherProjectId) =>
        new(source, "t1", "c1", target, "t2", "c2", "note");

    [Fact]
    public async Task Relation_rejects_self_link_foreign_project_and_plan_limit()
    {
        var service = Kit.RelationService;
        Assert.Equal(ModuleOutcomeKind.Invalid, (await service.CreateAsync(Editor, Relation(ProjectId, ProjectId), -1)).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await service.CreateAsync(Outsider, Relation(), -1)).Kind);

        Assert.True((await service.CreateAsync(Editor, Relation(), 1)).IsOk);
        Assert.Equal(ModuleOutcomeKind.TooMany, (await service.CreateAsync(Editor, Relation(), 1)).Kind);
        // Sınır kişi başına: başka üyenin kurduğu sayılmaz.
        Assert.True((await service.CreateAsync(Admin, Relation(), 1)).IsOk);
    }

    [Fact]
    public async Task Relation_list_and_impact_hide_from_outsider_and_resolve_deleted_side()
    {
        var service = Kit.RelationService;
        var id = (await service.CreateAsync(Editor, Relation(), -1)).Value!;
        Assert.Equal(ModuleOutcomeKind.NotFound, (await service.ListAsync(Outsider, ProjectId)).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await service.ImpactAsync(Outsider, ProjectId, "t1", null)).Kind);

        var listed = Assert.Single((await service.ListAsync(Viewer, ProjectId)).Value!);
        Assert.Equal((id, "outgoing", OtherProjectId), (listed.Id, listed.Direction, listed.OtherProjectId));
        Assert.Single((await service.ImpactAsync(Viewer, ProjectId, "t1", null)).Value!);
        Assert.Empty((await service.ImpactAsync(Viewer, ProjectId, "nope", null)).Value!);
    }

    [Fact]
    public async Task Relation_delete_requires_edit_on_either_side_and_is_not_found_twice()
    {
        var service = Kit.RelationService;
        var id = (await service.CreateAsync(Editor, Relation(), -1)).Value!;
        Assert.Equal(ModuleOutcomeKind.Forbidden, (await service.DeleteAsync(Viewer, id)).Kind);
        Assert.True((await service.DeleteAsync(Editor, id)).IsOk);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await service.DeleteAsync(Editor, id)).Kind);
    }

    [Fact]
    public async Task Relations_are_removed_from_both_directions_for_project_deletion()
    {
        var service = Kit.RelationService;
        await service.CreateAsync(Editor, Relation(), -1);
        await service.CreateAsync(Editor, Relation(OtherProjectId, ProjectId), -1);
        Assert.Equal(2, await Kit.Relations.CountForProjectAsync(ProjectId));
        Assert.Equal(2, await Kit.Relations.RemoveForProjectAsync(ProjectId));
        Assert.Equal(0, await Kit.Relations.RemoveForProjectAsync(ProjectId));
    }

    // ── Bağlantı sırrı ──────────────────────────────────────────────────────

    [Fact]
    public async Task Secret_provider_requires_view_right_and_never_returns_for_outsider()
    {
        var authority = Kit.Authority(new PlainProtector());
        Assert.True(await authority.SetConnectionAsync(ProjectId, "Host=db;Password=pw", "PostgreSQL"));
        Assert.Equal("Host=db;Password=pw", (await authority.ResolveForUserAsync(ProjectId, Viewer))!.ConnectionString);
        Assert.Null(await authority.ResolveForUserAsync(ProjectId, Outsider));
        Assert.Null(await authority.ResolveForUserAsync(ProjectId, ""));
        Assert.False(await authority.SetConnectionAsync(ProjectId, " ", "PostgreSQL"));
        Assert.False(await authority.SetConnectionAsync("missing", "x", "PostgreSQL"));
    }

    [Fact]
    public async Task Clearing_connection_keeps_desk_sql_consent_and_row_goes_when_meaningless()
    {
        var authority = Kit.Authority(new PlainProtector());
        await authority.SetConnectionAsync(ProjectId, "Host=db", "PostgreSQL");
        await authority.SetDeskSqlEnabledAsync(ProjectId, true);
        await authority.ClearConnectionAsync(ProjectId);

        Assert.Null(await authority.ResolveAsync(ProjectId));
        Assert.True((await Kit.StudioService.GetConnectionMetadataAsync(ProjectId)).AllowDeskSql);

        await authority.SetDeskSqlEnabledAsync(ProjectId, false);
        Assert.Equal(0, await Kit.Studio.ProjectConnections.CountAsync());
    }

    [Fact]
    public async Task Connection_metadata_never_carries_ciphertext()
    {
        var authority = Kit.Authority(new PlainProtector());
        await authority.SetConnectionAsync(ProjectId, "Host=db;Password=pw", "PostgreSQL");
        var meta = await Kit.StudioService.GetConnectionMetadataAsync(ProjectId);
        Assert.Equal((true, "PostgreSQL"), (meta.HasConnection, meta.ConnectionDbType));
        Assert.DoesNotContain("Password", meta.ToString());
    }

    private sealed class PlainProtector : Namines.Core.Security.IConnectionSecretProtector
    {
        public string Protect(string plaintext) => "enc:" + plaintext;
        public string Unprotect(string ciphertext) => ciphertext["enc:".Length..];
    }
}
