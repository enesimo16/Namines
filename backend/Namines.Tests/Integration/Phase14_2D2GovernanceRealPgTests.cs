using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.Core.Enums;
using Namines.Core.Models.Auth;
using Namines.Governance.Application;
using Namines.Governance.Domain;
using Namines.Identity.Infrastructure.Persistence;
using Namines.Tests.TestSupport;
using Npgsql;
using Xunit;

namespace Namines.Tests.Integration;

/// <summary>
/// Faz 14.2D-2 yönetişim, gerçek PostgreSQL'de: oylama yarışları (iki onaylayıcı aynı anda, onay ile red, çift oy)
/// satır kilidiyle sıraya girer; denetim izi veritabanı tetikleyicisiyle değiştirilemezdir (UPDATE/DELETE/TRUNCATE
/// reddedilir; yalnızca FK anonimleştirmesi ve saklama süresi temizliği geçer). Her çağrı kendi bağlamını kullanır.
/// </summary>
[Collection("Docker")]
public sealed class Phase14_2D2GovernanceRealPgTests : IClassFixture<Phase14_2BPostgresFixture>
{
    private const string ProjectId = "p1";
    private static readonly string[] Approvers = { "a1", "a2", "a3", "a4" };

    private readonly Phase14_2BPostgresFixture _pg;

    public Phase14_2D2GovernanceRealPgTests(Phase14_2BPostgresFixture pg) => _pg = pg;

    private static AuthDbContext Open(string cs) =>
        new(new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(cs).Options);

    /// <summary>Yazar (Admin) + dört Editor + bir Viewer: ekip büyüklüğü 5, yani Breaking için iki onay gerekir.</summary>
    private async Task<string> NewDatabaseAsync(string name)
    {
        PostgresTestPools.Reset();
        var cs = await _pg.CreateDatabaseAsync(name);
        await using var db = Open(cs);
        await db.MigrateAllModulesAsync();

        foreach (var id in new[] { "author", "a1", "a2", "a3", "a4", "viewer" })
            db.Users.Add(new ApplicationUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant() });
        db.Organizations.Add(new Organization { Id = "org-a", Name = "A", CreatedByUserId = "author" });
        await db.SaveChangesAsync();
        db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = "org-a", UserId = "author", Role = OrgRole.Admin });
        foreach (var id in Approvers) db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = "org-a", UserId = id, Role = OrgRole.Editor });
        db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = "org-a", UserId = "viewer", Role = OrgRole.Viewer });
        await db.SaveChangesAsync();

        db.Studio().Projects.Add(new CloudProject
        {
            Id = ProjectId, Name = "p", DbType = "PostgreSQL", OrganizationId = "org-a",
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = "author",
        });
        await db.SaveChangesAsync();
        var branch = new Branch { ProjectId = ProjectId, Name = "main", IsDefault = true, CreatedByUserId = "author" };
        db.Studio().Branches.Add(branch);
        await db.SaveChangesAsync();
        db.Studio().SchemaVersions.Add(new SchemaVersion
        {
            Id = "v1", ProjectId = ProjectId, BranchId = branch.Id, Version = 1, Checksum = "c", SchemaJson = "{}", AuthorUserId = "author",
        });
        await db.SaveChangesAsync();
        return cs;
    }

    private static async Task<string> NewRequestAsync(string cs, RiskLevel risk)
    {
        await using var db = Open(cs);
        var branchId = await db.Studio().Branches.Select(b => b.Id).SingleAsync();
        var cr = new ChangeRequest
        {
            ProjectId = ProjectId, BranchId = branchId, HeadVersionId = "v1", Title = "t", RiskLevel = risk,
            ImpactReportJson = "{}", CreatedByUserId = "author", VotingMembersAtCreation = 5,
        };
        db.Governance().ChangeRequests.Add(cr);
        await db.SaveChangesAsync();
        return cr.Id;
    }

    private static async Task<ModuleOutcome<DecisionResult>[]> VoteTogetherAsync(string cs, string crId, params (string User, ApprovalDecision Decision)[] votes)
    {
        var start = new TaskCompletionSource();
        var tasks = votes.Select(async vote =>
        {
            await start.Task;
            await using var db = Open(cs);
            return await TestModules.For(db).ChangeRequests().DecideAsync(vote.User, crId, vote.Decision, null);
        }).ToArray();
        start.SetResult();
        return await Task.WhenAll(tasks);
    }

    // ── Oylama yarışları ─────────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task Two_approvers_at_once_both_count_and_the_request_becomes_approved()
    {
        var cs = await NewDatabaseAsync("g_two");

        // Kilitsiz sürümde iki istek de "0 onay var" görüp ikisi de bekleyen bırakırdı. Yarışı tekrarla ki gerçekten sınansın.
        for (var round = 0; round < 8; round++)
        {
            var id = await NewRequestAsync(cs, RiskLevel.Breaking);

            var outcomes = await VoteTogetherAsync(cs, id, ("a1", ApprovalDecision.Approved), ("a2", ApprovalDecision.Approved));

            Assert.All(outcomes, o => Assert.True(o.IsOk, o.Error));
            await using var verify = Open(cs);
            Assert.Equal(ChangeRequestStatus.Approved, (await verify.Governance().ChangeRequests.AsNoTracking().SingleAsync(c => c.Id == id)).Status);
            Assert.Equal(2, await verify.Governance().ChangeRequestApprovals.CountAsync(a => a.ChangeRequestId == id));
            Assert.Equal(2, await verify.Governance().ChangeRequestAuditLogs.CountAsync(a => a.ChangeRequestId == id && a.Action == ChangeRequestAuditAction.Approved));
        }
    }

    [RequiresDockerFact]
    public async Task A_third_approver_after_the_threshold_is_a_conflict_and_records_nothing()
    {
        var cs = await NewDatabaseAsync("g_three");
        var id = await NewRequestAsync(cs, RiskLevel.Breaking);

        var outcomes = await VoteTogetherAsync(cs, id,
            ("a1", ApprovalDecision.Approved), ("a2", ApprovalDecision.Approved), ("a3", ApprovalDecision.Approved), ("a4", ApprovalDecision.Approved));

        Assert.Equal(2, outcomes.Count(o => o.IsOk));
        Assert.All(outcomes.Where(o => !o.IsOk), o => Assert.Equal(ModuleOutcomeKind.Conflict, o.Kind));
        await using var verify = Open(cs);
        Assert.Equal(2, await verify.Governance().ChangeRequestApprovals.CountAsync(a => a.ChangeRequestId == id));
        Assert.Equal(2, await verify.Governance().ChangeRequestAuditLogs.CountAsync(a => a.ChangeRequestId == id));
        Assert.Equal(ChangeRequestStatus.Approved, (await verify.Governance().ChangeRequests.AsNoTracking().SingleAsync(c => c.Id == id)).Status);
    }

    [RequiresDockerFact]
    public async Task Approve_versus_reject_race_has_exactly_one_winner_and_the_final_status_matches_it()
    {
        var cs = await NewDatabaseAsync("g_race");

        for (var round = 0; round < 10; round++)
        {
            var id = await NewRequestAsync(cs, RiskLevel.Risky); // tek onay yeter: onay kazanırsa istek hemen Approved olur

            var outcomes = await VoteTogetherAsync(cs, id, ("a1", ApprovalDecision.Approved), ("a2", ApprovalDecision.Rejected));

            var winner = Assert.Single(outcomes, o => o.IsOk);
            var loser = Assert.Single(outcomes, o => !o.IsOk);
            Assert.Equal(ModuleOutcomeKind.Conflict, loser.Kind);

            await using var verify = Open(cs);
            var status = (await verify.Governance().ChangeRequests.AsNoTracking().SingleAsync(c => c.Id == id)).Status;
            // Çözülmüş istek yeniden çözülmedi: son durum kazananın kararı, tek oy, tek denetim satırı.
            Assert.Equal(winner.Value!.Status, status);
            Assert.Equal(1, await verify.Governance().ChangeRequestApprovals.CountAsync(a => a.ChangeRequestId == id));
            Assert.Equal(1, await verify.Governance().ChangeRequestAuditLogs.CountAsync(a => a.ChangeRequestId == id));
        }
    }

    [RequiresDockerFact]
    public async Task The_same_voter_submitting_twice_at_once_is_counted_once()
    {
        var cs = await NewDatabaseAsync("g_double");
        var id = await NewRequestAsync(cs, RiskLevel.Breaking); // iki onay gerekir: ilk oydan sonra hâlâ bekliyor

        var outcomes = await VoteTogetherAsync(cs, id, ("a1", ApprovalDecision.Approved), ("a1", ApprovalDecision.Approved), ("a1", ApprovalDecision.Rejected));

        Assert.Single(outcomes, o => o.IsOk);
        Assert.All(outcomes.Where(o => !o.IsOk), o => Assert.Equal(ModuleOutcomeKind.Conflict, o.Kind));
        await using var verify = Open(cs);
        var approval = await verify.Governance().ChangeRequestApprovals.AsNoTracking().SingleAsync(a => a.ChangeRequestId == id);
        Assert.Equal("a1", approval.UserId);
    }

    [RequiresDockerFact]
    public async Task The_author_cannot_self_approve_a_breaking_change_even_when_racing_other_approvers()
    {
        var cs = await NewDatabaseAsync("g_self");
        var id = await NewRequestAsync(cs, RiskLevel.Breaking);

        var outcomes = await VoteTogetherAsync(cs, id, ("author", ApprovalDecision.Approved), ("a1", ApprovalDecision.Approved));

        Assert.Equal(ModuleOutcomeKind.Forbidden, outcomes[0].Kind);
        Assert.True(outcomes[1].IsOk);
        await using var verify = Open(cs);
        Assert.Equal("a1", (await verify.Governance().ChangeRequestApprovals.AsNoTracking().SingleAsync()).UserId);
        Assert.Equal(ChangeRequestStatus.PendingReview, (await verify.Governance().ChangeRequests.AsNoTracking().SingleAsync()).Status);
    }

    [RequiresDockerFact]
    public async Task Concurrent_first_calls_for_the_ai_policy_meet_in_one_row()
    {
        var cs = await NewDatabaseAsync("g_policy");
        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            await start.Task;
            await using var db = Open(cs);
            return await TestModules.For(db).Policies.EnsureAsync("a1");
        }).ToArray();
        start.SetResult();

        var snapshots = await Task.WhenAll(tasks);

        Assert.All(snapshots, s => Assert.Equal(snapshots[0], s));
        await using var verify = Open(cs);
        Assert.Equal(1, await verify.Governance().UserAIPolicies.CountAsync());
    }

    // ── Denetim izi: değiştirilemezlik ───────────────────────────────────────

    private static async Task ExecAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<string> AppendAsync(string cs, string? actor = "a1", DateTime? at = null, ChangeRequestAuditAction action = ChangeRequestAuditAction.Created)
    {
        await using var db = Open(cs);
        var row = new ChangeRequestAuditLog
        {
            ChangeRequestId = "cr-x", ProjectId = ProjectId, ChangeRequestTitle = "title", Action = action,
            ActorUserId = actor, Details = "details", CreatedAt = at ?? DateTime.UtcNow,
        };
        db.Governance().ChangeRequestAuditLogs.Add(row);
        await db.Governance().SaveChangesAsync();
        return row.Id;
    }

    private static async Task AssertAppendOnlyAsync(Func<Task> statement, string fragment)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(statement);
        Assert.Equal("42501", ex.SqlState);
        Assert.Contains(fragment, ex.MessageText);
    }

    [RequiresDockerFact]
    public async Task Updating_an_audit_row_is_rejected_for_every_column()
    {
        var cs = await NewDatabaseAsync("a_update");
        var id = await AppendAsync(cs);

        foreach (var set in new[] { "\"Details\" = 'tampered'", "\"Action\" = 2", "\"ChangeRequestTitle\" = NULL", "\"CreatedAt\" = now()", "\"ProjectId\" = 'other'", "\"ChangeRequestId\" = 'other'" })
            await AssertAppendOnlyAsync(() => ExecAsync(cs, $"UPDATE governance.\"ChangeRequestAuditLogs\" SET {set} WHERE \"Id\" = '{id}'"), "UPDATE is not allowed");

        // EF yolu da aynı: ExecuteUpdate tetikleyiciye takılır.
        await using var db = Open(cs);
        await Assert.ThrowsAsync<PostgresException>(() => db.Governance().ChangeRequestAuditLogs
            .Where(a => a.Id == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Details, "x")));

        await using var tracked = Open(cs);
        var entity = await tracked.Governance().ChangeRequestAuditLogs.SingleAsync(a => a.Id == id);
        entity.Details = "changed through the change tracker";
        await Assert.ThrowsAsync<DbUpdateException>(() => tracked.Governance().SaveChangesAsync());

        await using var verify = Open(cs);
        Assert.Equal("details", (await verify.Governance().ChangeRequestAuditLogs.AsNoTracking().SingleAsync(a => a.Id == id)).Details);
    }

    [RequiresDockerFact]
    public async Task Deleting_or_truncating_audit_rows_is_rejected()
    {
        var cs = await NewDatabaseAsync("a_delete");
        var id = await AppendAsync(cs);

        await AssertAppendOnlyAsync(() => ExecAsync(cs, $"DELETE FROM governance.\"ChangeRequestAuditLogs\" WHERE \"Id\" = '{id}'"), "DELETE is only allowed for retention");
        await AssertAppendOnlyAsync(() => ExecAsync(cs, "DELETE FROM governance.\"ChangeRequestAuditLogs\""), "DELETE is only allowed for retention");
        await AssertAppendOnlyAsync(() => ExecAsync(cs, "TRUNCATE governance.\"ChangeRequestAuditLogs\""), "TRUNCATE is not allowed");
        await using var db = Open(cs);
        await Assert.ThrowsAsync<PostgresException>(() => db.Governance().ChangeRequestAuditLogs.ExecuteDeleteAsync());

        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM governance.\"ChangeRequestAuditLogs\""));
    }

    [RequiresDockerFact]
    public async Task Only_nulling_the_actor_is_allowed_as_an_update()
    {
        var cs = await NewDatabaseAsync("a_actor");
        var id = await AppendAsync(cs, actor: "a1");

        // Tek izinli değişiklik: ActorUserId dolu → NULL, başka hiçbir alan değişmeden.
        await ExecAsync(cs, $"UPDATE governance.\"ChangeRequestAuditLogs\" SET \"ActorUserId\" = NULL WHERE \"Id\" = '{id}'");
        Assert.Equal(1, await CountAsync(cs, $"SELECT count(*) FROM governance.\"ChangeRequestAuditLogs\" WHERE \"Id\" = '{id}' AND \"ActorUserId\" IS NULL AND \"Details\" = 'details'"));

        // Anonimleştirmeyi başka bir değişiklikle birleştirmek reddedilir; NULL → değer de reddedilir.
        var other = await AppendAsync(cs, actor: "a2");
        await AssertAppendOnlyAsync(() => ExecAsync(cs, $"UPDATE governance.\"ChangeRequestAuditLogs\" SET \"ActorUserId\" = NULL, \"Details\" = 'x' WHERE \"Id\" = '{other}'"), "UPDATE is not allowed");
        await AssertAppendOnlyAsync(() => ExecAsync(cs, $"UPDATE governance.\"ChangeRequestAuditLogs\" SET \"ActorUserId\" = 'a3' WHERE \"Id\" = '{id}'"), "UPDATE is not allowed");
    }

    [RequiresDockerFact]
    public async Task Deleting_a_user_nulls_the_actor_through_the_foreign_key_but_keeps_the_audit_row()
    {
        var cs = await NewDatabaseAsync("a_fk");
        await ExecAsync(cs, "INSERT INTO \"AspNetUsers\" (\"Id\",\"Type\",\"CreatedAt\",\"IsDev\",\"EmailConfirmed\",\"PhoneNumberConfirmed\",\"TwoFactorEnabled\",\"LockoutEnabled\",\"AccessFailedCount\",\"UserName\",\"NormalizedUserName\") VALUES ('gone',1,now(),false,true,false,false,true,0,'gone','GONE')");
        var id = await AppendAsync(cs, actor: "gone");

        await ExecAsync(cs, "DELETE FROM \"AspNetUsers\" WHERE \"Id\" = 'gone'");

        Assert.Equal(1, await CountAsync(cs, $"SELECT count(*) FROM governance.\"ChangeRequestAuditLogs\" WHERE \"Id\" = '{id}' AND \"ActorUserId\" IS NULL"));
    }

    [RequiresDockerFact]
    public async Task Erasing_a_users_personal_data_anonymises_votes_requests_and_audit_actors_without_losing_rows()
    {
        var cs = await NewDatabaseAsync("a_erase");
        var crId = await NewRequestAsync(cs, RiskLevel.Breaking);
        Assert.True((await VoteTogetherAsync(cs, crId, ("a1", ApprovalDecision.Approved)))[0].IsOk);
        await using (var seed = Open(cs))
        {
            seed.Governance().UserAIPolicies.Add(new UserAIPolicy { UserId = "a1" });
            await seed.Governance().SaveChangesAsync();
        }
        var before = await CountAsync(cs, "SELECT count(*) FROM governance.\"ChangeRequestAuditLogs\"");

        await using (var db = Open(cs))
            await TestModules.For(db).GovernanceAudit.ErasePersonalDataAsync("a1");
        await using (var db = Open(cs))
            await TestModules.For(db).GovernanceAudit.ErasePersonalDataAsync("author");

        Assert.Equal(before, await CountAsync(cs, "SELECT count(*) FROM governance.\"ChangeRequestAuditLogs\""));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM governance.\"ChangeRequestAuditLogs\" WHERE \"ActorUserId\" IN ('a1','author')"));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM governance.\"ChangeRequestApprovals\" WHERE \"UserId\" IS NULL"));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM governance.\"ChangeRequests\" WHERE \"CreatedByUserId\" IS NULL"));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM governance.\"UserAIPolicies\""));
    }

    [RequiresDockerFact]
    public async Task Retention_purge_removes_only_expired_rows_and_the_permission_does_not_leak_to_later_statements()
    {
        var cs = await NewDatabaseAsync("a_retention");
        var oldA = await AppendAsync(cs, at: DateTime.UtcNow.AddDays(-800));
        await AppendAsync(cs, at: DateTime.UtcNow.AddDays(-700));
        var fresh = await AppendAsync(cs, at: DateTime.UtcNow.AddDays(-1));

        int purged;
        await using (var db = Open(cs))
            purged = await TestModules.For(db).GovernanceAudit.PurgeAuditAsync(DateTime.UtcNow.AddDays(-365), TimeSpan.Zero);

        Assert.Equal(2, purged);
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM governance.\"ChangeRequestAuditLogs\""));
        Assert.Equal(1, await CountAsync(cs, $"SELECT count(*) FROM governance.\"ChangeRequestAuditLogs\" WHERE \"Id\" = '{fresh}'"));

        // Silme izni yalnızca temizlik işleminde açıktı; havuzdaki aynı bağlantıda sonraki DELETE yine reddedilir.
        for (var i = 0; i < 5; i++)
            await AssertAppendOnlyAsync(() => ExecAsync(cs, $"DELETE FROM governance.\"ChangeRequestAuditLogs\" WHERE \"Id\" = '{fresh}'"), "DELETE is only allowed for retention");
        Assert.NotEqual(oldA, fresh);
    }

    [RequiresDockerFact]
    public async Task The_retention_permission_is_transaction_local()
    {
        var cs = await NewDatabaseAsync("a_txlocal");
        var id = await AppendAsync(cs, at: DateTime.UtcNow.AddDays(-800));

        await using (var conn = new NpgsqlConnection(cs))
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await using (var enable = new NpgsqlCommand("SELECT set_config('namines.audit_retention', 'on', true)", conn, tx))
                await enable.ExecuteNonQueryAsync();
            await using (var del = new NpgsqlCommand($"DELETE FROM governance.\"ChangeRequestAuditLogs\" WHERE \"Id\" = '{id}'", conn, tx))
                Assert.Equal(1, await del.ExecuteNonQueryAsync());
            await tx.RollbackAsync();

            // Geri alındı: satır yerinde ve ayar kapandı.
            await using var again = new NpgsqlCommand($"DELETE FROM governance.\"ChangeRequestAuditLogs\" WHERE \"Id\" = '{id}'", conn);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => again.ExecuteNonQueryAsync());
            Assert.Equal("42501", ex.SqlState);
        }
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM governance.\"ChangeRequestAuditLogs\""));
    }

    [RequiresDockerFact]
    public async Task The_action_constraint_accepts_subscription_cancellation_completed_and_nothing_beyond()
    {
        var cs = await NewDatabaseAsync("a_check");
        await AppendAsync(cs, action: ChangeRequestAuditAction.SubscriptionCancellationCompleted);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => AppendAsync(cs, action: (ChangeRequestAuditAction)9));
        Assert.Equal("23514", ((PostgresException)ex.InnerException!).SqlState);
    }

    // ── Hesap silme: modül dışı FK davranışı ─────────────────────────────────

    [RequiresDockerFact]
    public async Task Deleting_a_user_removes_the_desk_sql_history_through_the_foreign_key()
    {
        var cs = await NewDatabaseAsync("a_desk");
        await ExecAsync(cs, "INSERT INTO \"AspNetUsers\" (\"Id\",\"Type\",\"CreatedAt\",\"IsDev\",\"EmailConfirmed\",\"PhoneNumberConfirmed\",\"TwoFactorEnabled\",\"LockoutEnabled\",\"AccessFailedCount\",\"UserName\",\"NormalizedUserName\") VALUES ('hist',1,now(),false,true,false,false,true,0,'hist','HIST')");
        await using (var db = Open(cs))
        {
            TestModules.For(db).Desk.SqlQueryHistory.Add(new SqlQueryHistoryEntry { UserId = "hist", ProjectId = ProjectId, Sql = "select 1" });
            await TestModules.For(db).Desk.SaveChangesAsync();
        }
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM desk.sql_query_history"));

        await ExecAsync(cs, "DELETE FROM \"AspNetUsers\" WHERE \"Id\" = 'hist'");

        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM desk.sql_query_history"));
    }
}
