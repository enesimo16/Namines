using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Billing.Application;
using Namines.Billing.Domain;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Governance.Application;
using Namines.Governance.Domain;
using Namines.Tests.TestSupport;
using Stripe;
using Xunit;
using PlanTier = Namines.Core.Analysis.PlanTier;

namespace Namines.Tests.Services;

// Faz 14.2D-2: Billing ve Governance uygulama servislerinin başarı, hata, yetkisiz, geçersiz, tekrar ve
// idempotency yolları. SQLite + gerçek yetki kuralı (ModuleTestDb); eşzamanlılığı gerçek PostgreSQL testleri sınıyor.

/// <summary>AI politikası: varsayılanlar, kısmi kayıt, açılış düzeltmesi.</summary>
public sealed class AiPolicyServiceTests : IDisposable
{
    private readonly ModuleTestDb _t = new();
    private AiPolicyService Service => _t.Kit.Policies;

    public void Dispose() => _t.Dispose();

    [Fact]
    public async Task Find_returns_null_when_the_user_never_saved_a_policy()
    {
        Assert.Null(await Service.FindAsync(ModuleTestDb.Owner));
    }

    [Fact]
    public async Task Ensure_creates_the_defaults_once_and_is_idempotent()
    {
        var first = await Service.EnsureAsync(ModuleTestDb.Owner);
        var second = await Service.EnsureAsync(ModuleTestDb.Owner);

        Assert.Equal(AiPolicySnapshot.Default, first with { AdvancedJson = null });
        Assert.Equal(first, second);
        Assert.Equal(1, await _t.Kit.Governance.UserAIPolicies.CountAsync());
    }

    [Fact]
    public async Task Save_creates_a_missing_row_with_the_given_modes()
    {
        var modes = AiPolicySnapshot.Default with { SmartSeed = AIMode.Ultra, Voice = AIMode.High };
        await Service.SaveAsync(ModuleTestDb.Editor, modes, advancedJson: """{"maxTokens":"8000"}""");

        var stored = await Service.FindAsync(ModuleTestDb.Editor);
        Assert.Equal(AIMode.Ultra, stored!.SmartSeed);
        Assert.Equal(AIMode.High, stored.Voice);
        Assert.Equal("""{"maxTokens":"8000"}""", stored.AdvancedJson);
    }

    [Fact]
    public async Task Save_without_advanced_json_keeps_the_stored_advanced_settings()
    {
        // Model seçimlerini kaydeden eski bir istemci, gelişmiş ayarları sıfırlamamalı.
        await Service.SaveAsync(ModuleTestDb.Owner, AiPolicySnapshot.Default, """{"temperature":"0.9"}""");
        await Service.SaveAsync(ModuleTestDb.Owner, AiPolicySnapshot.Default with { DbaAnalysis = AIMode.Medium }, advancedJson: null);

        var stored = await Service.FindAsync(ModuleTestDb.Owner);
        Assert.Equal(AIMode.Medium, stored!.DbaAnalysis);
        Assert.Equal("""{"temperature":"0.9"}""", stored.AdvancedJson);
    }

    [Fact]
    public async Task Rewrite_changes_only_rows_the_transform_returns_a_new_value_for()
    {
        await Service.SaveAsync(ModuleTestDb.Owner, AiPolicySnapshot.Default, "old-1");
        await Service.SaveAsync(ModuleTestDb.Admin, AiPolicySnapshot.Default, "keep");
        await Service.SaveAsync(ModuleTestDb.Editor, AiPolicySnapshot.Default, advancedJson: null);

        var changed = await Service.RewriteAdvancedJsonAsync(json => json.StartsWith("old", StringComparison.Ordinal) ? "new-1" : null);

        Assert.Equal(1, changed);
        Assert.Equal("new-1", (await Service.FindAsync(ModuleTestDb.Owner))!.AdvancedJson);
        Assert.Equal("keep", (await Service.FindAsync(ModuleTestDb.Admin))!.AdvancedJson);
        Assert.Null((await Service.FindAsync(ModuleTestDb.Editor))!.AdvancedJson);

        // İkinci çalıştırma: dönüştürücü artık eşleşmiyor, hiçbir şey değişmez (idempotent).
        Assert.Equal(0, await Service.RewriteAdvancedJsonAsync(json => json.StartsWith("old", StringComparison.Ordinal) ? "new-1" : null));
    }
}

/// <summary>Değişiklik isteği yaşam döngüsü: açma, oylama, geçersiz geçişler, yetki.</summary>
public sealed class ChangeRequestServiceTests : IDisposable
{
    private readonly ModuleTestDb _t = new();

    public void Dispose() => _t.Dispose();

    private IChangeRequestService Service(IBranchTestRunner? runner = null) => _t.Kit.ChangeRequests(testRunner: runner);

    private static string SchemaWith(params string[] tables) => JsonSerializer.Serialize(new DatabaseSchema
    {
        Name = "S",
        Tables = tables.Select((name, i) => new SchemaTable
        {
            Id = "t" + name,
            Name = name,
            // Sabit kimlik: aynı tablo iki sürümde AYNI sayılsın, yoksa her sürüm "sil ve yeniden oluştur" görünür.
            StableUuid = "uuid-" + name,
            Columns = new List<SchemaColumn> { new() { Id = "c" + name, StableUuid = "uuid-c-" + name, Name = "id", Type = "uuid", IsPK = true } },
        }).ToList(),
    }, SchemaJsonOptions.Default);

    private async Task<string> OpenAsync(string actor, string schema, string project = ModuleTestDb.ProjectId)
    {
        var outcome = await Service().CreateQuickAsync(actor, new QuickChangeRequestInput(project, schema, "t", "m"));
        Assert.True(outcome.IsOk, outcome.Error);
        return outcome.Value!.Id;
    }

    /// <summary>İki sürüm: ikincisi bir tabloyu siliyor, yani Breaking; iki farklı kişinin onayı gerekir.</summary>
    private async Task<string> OpenBreakingAsync()
    {
        await OpenAsync(ModuleTestDb.Owner, SchemaWith("users", "orders"));
        return await OpenAsync(ModuleTestDb.Owner, SchemaWith("users"));
    }

    [Fact]
    public async Task Creating_with_a_blank_project_or_schema_is_invalid()
    {
        Assert.Equal(ModuleOutcomeKind.Invalid, (await Service().CreateQuickAsync(ModuleTestDb.Owner, new("", "{}", null, null))).Kind);
        Assert.Equal(ModuleOutcomeKind.Invalid, (await Service().CreateQuickAsync(ModuleTestDb.Owner, new(ModuleTestDb.ProjectId, "  ", null, null))).Kind);
    }

    [Fact]
    public async Task Creating_with_unparseable_schema_json_is_invalid()
    {
        var outcome = await Service().CreateQuickAsync(ModuleTestDb.Owner, new(ModuleTestDb.ProjectId, "{ not json", null, null));
        Assert.Equal(ModuleOutcomeKind.Invalid, outcome.Kind);
    }

    [Theory]
    [InlineData(ModuleTestDb.Viewer)]
    [InlineData(ModuleTestDb.Outsider)]
    public async Task Creating_without_edit_rights_looks_like_a_missing_project(string actor)
    {
        // Yetkisiz ile yok aynı sonuç: başkasının projesinin varlığı sızmasın.
        var outcome = await Service().CreateQuickAsync(actor, new(ModuleTestDb.ProjectId, SchemaWith("users"), null, null));
        Assert.Equal(ModuleOutcomeKind.NotFound, outcome.Kind);
        Assert.Empty(await _t.Kit.Governance.ChangeRequests.ToListAsync());
    }

    [Fact]
    public async Task A_new_request_is_pending_pins_the_team_size_and_writes_a_created_audit_row()
    {
        var id = await OpenAsync(ModuleTestDb.Editor, SchemaWith("users"));

        var cr = await _t.Kit.Governance.ChangeRequests.AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.Equal(ChangeRequestStatus.PendingReview, cr.Status);
        Assert.Equal(ModuleTestDb.Editor, cr.CreatedByUserId);
        // Oy verebilen üyeler: Owner, Admin, Editor (Viewer oy veremez).
        Assert.Equal(3, cr.VotingMembersAtCreation);

        var audit = await _t.Kit.Governance.ChangeRequestAuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal(ChangeRequestAuditAction.Created, audit.Action);
        Assert.Equal(ModuleTestDb.Editor, audit.ActorUserId);
    }

    [Fact]
    public async Task A_safe_change_on_an_opted_in_project_is_auto_approved_with_a_system_audit_row()
    {
        await _t.Kit.Studio.Projects.Where(p => p.Id == ModuleTestDb.ProjectId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.AutoApproveSafeChanges, true));

        // İlk talepte taban yok: Safe olsa bile otomatik onay YOK.
        var first = await OpenAsync(ModuleTestDb.Owner, SchemaWith("users"));
        Assert.Equal(ChangeRequestStatus.PendingReview, (await _t.Kit.Governance.ChangeRequests.AsNoTracking().SingleAsync(c => c.Id == first)).Status);

        var second = await OpenAsync(ModuleTestDb.Owner, SchemaWith("users", "orders"));
        var cr = await _t.Kit.Governance.ChangeRequests.AsNoTracking().SingleAsync(c => c.Id == second);
        Assert.Equal(ChangeRequestStatus.Approved, cr.Status);
        Assert.NotNull(cr.ResolvedAt);

        var audit = await _t.Kit.Governance.ChangeRequestAuditLogs.AsNoTracking()
            .Where(a => a.ChangeRequestId == second).OrderBy(a => a.CreatedAt).ToListAsync();
        Assert.Equal(new[] { ChangeRequestAuditAction.Created, ChangeRequestAuditAction.AutoApproved }, audit.Select(a => a.Action));
        Assert.Null(audit[1].ActorUserId);
    }

    [Fact]
    public async Task A_breaking_change_is_never_auto_approved()
    {
        await _t.Kit.Studio.Projects.Where(p => p.Id == ModuleTestDb.ProjectId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.AutoApproveSafeChanges, true));

        var id = await OpenBreakingAsync();

        var cr = await _t.Kit.Governance.ChangeRequests.AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.Equal(RiskLevel.Breaking, cr.RiskLevel);
        Assert.Equal(ChangeRequestStatus.PendingReview, cr.Status);
    }

    [Fact]
    public async Task Two_distinct_approvers_are_needed_for_a_breaking_change_and_each_vote_is_audited()
    {
        var id = await OpenBreakingAsync();

        var first = await Service().DecideAsync(ModuleTestDb.Admin, id, ApprovalDecision.Approved, "ok");
        Assert.True(first.IsOk);
        Assert.Equal(ChangeRequestStatus.PendingReview, first.Value!.Status);

        var second = await Service().DecideAsync(ModuleTestDb.Editor, id, ApprovalDecision.Approved, null);
        Assert.Equal(ChangeRequestStatus.Approved, second.Value!.Status);

        var cr = await _t.Kit.Governance.ChangeRequests.AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.NotNull(cr.ResolvedAt);
        var votes = await _t.Kit.Governance.ChangeRequestAuditLogs.AsNoTracking()
            .Where(a => a.ChangeRequestId == id && a.Action == ChangeRequestAuditAction.Approved).ToListAsync();
        Assert.Equal(2, votes.Count);
    }

    [Fact]
    public async Task The_author_cannot_approve_their_own_breaking_change()
    {
        var id = await OpenBreakingAsync();

        var outcome = await Service().DecideAsync(ModuleTestDb.Owner, id, ApprovalDecision.Approved, null);

        Assert.Equal(ModuleOutcomeKind.Forbidden, outcome.Kind);
        Assert.Empty(await _t.Kit.Governance.ChangeRequestApprovals.ToListAsync());
    }

    [Fact]
    public async Task Voting_twice_is_a_conflict_and_records_nothing_more()
    {
        var id = await OpenBreakingAsync();
        Assert.True((await Service().DecideAsync(ModuleTestDb.Admin, id, ApprovalDecision.Approved, null)).IsOk);

        var again = await Service().DecideAsync(ModuleTestDb.Admin, id, ApprovalDecision.Rejected, null);

        Assert.Equal(ModuleOutcomeKind.Conflict, again.Kind);
        Assert.Equal(1, await _t.Kit.Governance.ChangeRequestApprovals.CountAsync());
        Assert.Equal(ChangeRequestStatus.PendingReview, (await _t.Kit.Governance.ChangeRequests.AsNoTracking().SingleAsync(c => c.Id == id)).Status);
    }

    [Fact]
    public async Task A_rejection_resolves_the_request_and_later_votes_are_illegal_transitions()
    {
        var id = await OpenBreakingAsync();

        var rejected = await Service().DecideAsync(ModuleTestDb.Admin, id, ApprovalDecision.Rejected, "no");
        Assert.Equal(ChangeRequestStatus.Rejected, rejected.Value!.Status);

        // Çözülmüş istek yeniden çözülemez: ne onay ne ret.
        Assert.Equal(ModuleOutcomeKind.Conflict, (await Service().DecideAsync(ModuleTestDb.Editor, id, ApprovalDecision.Approved, null)).Kind);
        Assert.Equal(ModuleOutcomeKind.Conflict, (await Service().DecideAsync(ModuleTestDb.Editor, id, ApprovalDecision.Rejected, null)).Kind);
        Assert.Equal(ChangeRequestStatus.Rejected, (await _t.Kit.Governance.ChangeRequests.AsNoTracking().SingleAsync(c => c.Id == id)).Status);
        Assert.Equal(1, await _t.Kit.Governance.ChangeRequestApprovals.CountAsync());
    }

    [Fact]
    public async Task An_approved_request_cannot_be_rejected_afterwards()
    {
        await OpenAsync(ModuleTestDb.Owner, SchemaWith("users"));
        var id = await OpenAsync(ModuleTestDb.Owner, SchemaWith("users", "orders")); // Safe: tek onay yeter
        Assert.Equal(ChangeRequestStatus.Approved, (await Service().DecideAsync(ModuleTestDb.Admin, id, ApprovalDecision.Approved, null)).Value!.Status);

        var late = await Service().DecideAsync(ModuleTestDb.Editor, id, ApprovalDecision.Rejected, null);

        Assert.Equal(ModuleOutcomeKind.Conflict, late.Kind);
        Assert.Equal(ChangeRequestStatus.Approved, (await _t.Kit.Governance.ChangeRequests.AsNoTracking().SingleAsync(c => c.Id == id)).Status);
    }

    [Theory]
    [InlineData(ModuleTestDb.Viewer)]
    [InlineData(ModuleTestDb.Outsider)]
    public async Task Voting_without_edit_rights_looks_like_a_missing_request(string actor)
    {
        var id = await OpenBreakingAsync();

        var outcome = await Service().DecideAsync(actor, id, ApprovalDecision.Approved, null);

        Assert.Equal(ModuleOutcomeKind.NotFound, outcome.Kind);
        Assert.Empty(await _t.Kit.Governance.ChangeRequestApprovals.ToListAsync());
    }

    [Fact]
    public async Task Voting_on_an_unknown_request_is_not_found()
    {
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Service().DecideAsync(ModuleTestDb.Admin, "nope", ApprovalDecision.Approved, null)).Kind);
    }

    [Fact]
    public async Task A_removed_voter_is_shown_as_deleted_user_in_the_detail()
    {
        var id = await OpenBreakingAsync();
        Assert.True((await Service().DecideAsync(ModuleTestDb.Admin, id, ApprovalDecision.Approved, null)).IsOk);
        // Hesap silme oyun kullanıcı kimliğini boşaltır (satır kalır).
        await _t.Kit.Governance.ChangeRequestApprovals.ExecuteUpdateAsync(s => s.SetProperty(a => a.UserId, (string?)null));
        // AI üretimi gerekmesin: saklanmış sonuçlar var.
        await _t.Kit.Governance.ChangeRequests.ExecuteUpdateAsync(s => s
            .SetProperty(c => c.MigrationResultJson, "{}").SetProperty(c => c.AiExplanation, "stored"));
        // ExecuteUpdate izlenen varlığı güncellemez: bayat örnek servisin okumasını bozmasın.
        _t.Kit.Governance.ChangeTracker.Clear();

        var detail = await Service().GetDetailAsync(ModuleTestDb.Viewer, id);

        Assert.True(detail.IsOk, detail.Error);
        Assert.Equal("Deleted user", Assert.Single(detail.Value!.Approvals).Username);
        Assert.Equal(1, detail.Value.ApprovedCount);
        Assert.Equal(2, detail.Value.RequiredApprovals);
    }

    [Fact]
    public async Task Detail_list_and_audit_are_visible_to_viewers_but_not_to_outsiders()
    {
        var id = await OpenBreakingAsync();
        await _t.Kit.Governance.ChangeRequests.ExecuteUpdateAsync(s => s
            .SetProperty(c => c.MigrationResultJson, "{}").SetProperty(c => c.AiExplanation, "stored"));
        // ExecuteUpdate izlenen varlığı güncellemez: bayat örnek servisin okumasını bozmasın.
        _t.Kit.Governance.ChangeTracker.Clear();

        Assert.True((await Service().GetDetailAsync(ModuleTestDb.Viewer, id)).IsOk);
        Assert.True((await Service().GetAuditAsync(ModuleTestDb.Viewer, id)).IsOk);
        Assert.Equal(2, (await Service().ListForProjectAsync(ModuleTestDb.Viewer, ModuleTestDb.ProjectId)).Value!.Count);

        Assert.Equal(ModuleOutcomeKind.NotFound, (await Service().GetDetailAsync(ModuleTestDb.Outsider, id)).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Service().GetAuditAsync(ModuleTestDb.Outsider, id)).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Service().ListForProjectAsync(ModuleTestDb.Outsider, ModuleTestDb.ProjectId)).Kind);
        Assert.Equal(ModuleOutcomeKind.NotFound, (await Service().GetDetailAsync(ModuleTestDb.Viewer, "nope")).Kind);
    }

    private sealed class FakeRunner : IBranchTestRunner
    {
        public int Calls { get; private set; }

        public Task<TestRunResult> RunAsync(DatabaseSchema schema, DatabaseType engine, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new TestRunResult(true, true, "ok", null, 12));
        }
    }

    [Fact]
    public async Task Running_tests_needs_edit_rights_and_stores_the_last_result_on_the_request()
    {
        var id = await OpenAsync(ModuleTestDb.Owner, SchemaWith("users"));
        var runner = new FakeRunner();

        Assert.Equal(ModuleOutcomeKind.NotFound, (await Service(runner).RunTestsAsync(ModuleTestDb.Viewer, id)).Kind);
        Assert.Equal(0, runner.Calls);

        var outcome = await Service(runner).RunTestsAsync(ModuleTestDb.Editor, id);
        Assert.True(outcome.IsOk);
        var cr = await _t.Kit.Governance.ChangeRequests.AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.True(cr.TestRunSuccess);
        Assert.Equal(12, cr.TestRunDurationMs);
        Assert.NotNull(cr.TestRunAt);
    }

    [Fact]
    public async Task Scanning_affected_code_needs_view_rights()
    {
        var id = await OpenBreakingAsync();

        var ok = await Service().ScanAffectedCodeAsync(ModuleTestDb.Viewer, id,
            new[] { new AffectedCodeFile("a.ts", "select * from orders") });
        Assert.True(ok.IsOk);
        Assert.Equal(1, ok.Value!.FilesScanned);

        Assert.Equal(ModuleOutcomeKind.NotFound,
            (await Service().ScanAffectedCodeAsync(ModuleTestDb.Outsider, id, Array.Empty<AffectedCodeFile>())).Kind);
    }
}

/// <summary>Plan, abonelik, kota ve temizlik okumaları/yazımları (BillingAccountService).</summary>
public sealed class BillingAccountServiceTests : IDisposable
{
    private readonly ModuleTestDb _t = new();

    public void Dispose() => _t.Dispose();

    private sealed class FakeCanceller(bool result, bool throws = false) : ISubscriptionCanceller
    {
        public List<string> Calls { get; } = new();

        public Task<bool> CancelAsync(string subscriptionId, CancellationToken ct)
        {
            Calls.Add(subscriptionId);
            if (throws) throw new HttpRequestException("stripe down");
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingObserver : ISubscriptionCancellationObserver
    {
        public List<(string OutboxId, string SubscriptionId)> Completed { get; } = new();

        public Task OnCompletedAsync(string outboxId, string subscriptionId, CancellationToken ct)
        {
            Completed.Add((outboxId, subscriptionId));
            return Task.CompletedTask;
        }
    }

    private BillingAccountService Service(ISubscriptionCanceller? canceller = null, params ISubscriptionCancellationObserver[] observers) =>
        new(_t.Kit.Billing, _t.Kit.Identity, _t.Kit.AiQuota(),
            NullLogger<BillingAccountService>.Instance, canceller, observers);

    [Fact]
    public async Task Plan_snapshot_is_null_for_an_unknown_user_and_joins_identity_with_the_subscription()
    {
        Assert.Null(await Service().GetPlanSnapshotAsync("ghost"));

        await _t.Db.SeedSubscriptionAsync(ModuleTestDb.Admin, "active", "team", "sub_1", "cus_1");
        var snapshot = await Service().GetPlanSnapshotAsync(ModuleTestDb.Admin);

        Assert.Equal("active", snapshot!.SubscriptionStatus);
        Assert.Equal("team", snapshot.PlanCode);
        Assert.False(snapshot.IsDev);

        // Subscription'ı olmayan bilinen kullanıcı: plan alanları boş, anlaşılır (Free).
        var none = await Service().GetPlanSnapshotAsync(ModuleTestDb.Editor);
        Assert.Null(none!.SubscriptionStatus);
    }

    [Fact]
    public async Task Subscription_views_report_a_stripe_customer_without_exposing_its_id()
    {
        await _t.Db.SeedSubscriptionAsync(ModuleTestDb.Admin, "active", "pro", "sub_1", "cus_1");
        await _t.Db.SeedSubscriptionAsync(ModuleTestDb.Editor, "past_due", "pro");

        var views = await Service().GetSubscriptionsAsync(new[] { ModuleTestDb.Admin, ModuleTestDb.Editor, ModuleTestDb.Viewer });

        Assert.True(views[ModuleTestDb.Admin].HasStripeCustomer);
        Assert.False(views[ModuleTestDb.Editor].HasStripeCustomer);
        Assert.Null(views[ModuleTestDb.Viewer].Status);
        Assert.Empty(await Service().GetSubscriptionsAsync(Array.Empty<string>()));
    }

    [Fact]
    public async Task Paid_plan_counts_and_listings_honour_the_exclusions()
    {
        await _t.Db.SeedSubscriptionAsync(ModuleTestDb.Admin, "active", "pro");
        await _t.Db.SeedSubscriptionAsync(ModuleTestDb.Editor, "active", "pro");
        await _t.Db.SeedSubscriptionAsync(ModuleTestDb.Viewer, "active", "team");
        await _t.Db.SeedSubscriptionAsync(ModuleTestDb.Outsider, "canceled", "free");

        Assert.Equal((2, 1), await Service().CountPaidPlansAsync(Array.Empty<string>()));
        Assert.Equal((1, 1), await Service().CountPaidPlansAsync(new[] { ModuleTestDb.Admin }));
        Assert.Equal(2, (await Service().ListUserIdsByPlanAsync("pro")).Count);
        // "free" koduna sahip satır ücretli listede YOK.
        Assert.DoesNotContain(ModuleTestDb.Outsider, await Service().ListUserIdsWithPaidPlanAsync());
        Assert.Equal(3, (await Service().ListUserIdsWithPaidPlanAsync()).Count);
    }

    [Fact]
    public async Task Provisioning_the_default_quota_never_overwrites_an_existing_row()
    {
        var created = await Service().ProvisionDefaultQuotaAsync(ModuleTestDb.Editor);
        Assert.Equal(100, created.DailyLimit);

        await _t.Kit.Billing.UserAIQuotas.ExecuteUpdateAsync(s => s.SetProperty(q => q.DailyLimit, 777));
        var again = await Service().ProvisionDefaultQuotaAsync(ModuleTestDb.Editor);

        Assert.Equal(777, again.DailyLimit);
    }

    [Fact]
    public async Task Login_sync_lifts_low_limits_makes_devs_unlimited_and_resets_a_stale_day()
    {
        await _t.Kit.Billing.UserAIQuotas.AddAsync(new UserAIQuota
        {
            UserId = ModuleTestDb.Editor, DailyLimit = 40, DailyUsageCount = 33, LastResetDate = DateTime.UtcNow.AddDays(-3),
        });
        await _t.Kit.Billing.SaveChangesAsync();

        var row = await Service().SyncQuotaOnLoginAsync(ModuleTestDb.Editor, isDev: false);
        Assert.Equal(100, row.DailyLimit);
        Assert.Equal(0, row.DailyUsageCount);

        var dev = await Service().SyncQuotaOnLoginAsync(ModuleTestDb.Owner, isDev: true);
        Assert.Equal(int.MaxValue, dev.DailyLimit);
    }

    [Theory]
    [InlineData("team", false, 2000)]
    [InlineData("pro", false, 500)]
    [InlineData("free", false, 100)]
    [InlineData("pro", true, int.MaxValue)]
    public async Task Admin_plan_change_writes_the_plan_code_without_touching_the_status_and_adjusts_the_limit(string plan, bool isDev, int expected)
    {
        var quota = await Service().AdminSetPlanAsync(ModuleTestDb.Editor, plan, isDev);

        Assert.Equal(expected, quota.DailyLimit);
        var subscription = await _t.Kit.Billing.Subscriptions.AsNoTracking().SingleAsync(s => s.UserId == ModuleTestDb.Editor);
        Assert.Equal(plan, subscription.PlanCode);
        Assert.Null(subscription.Status);
    }

    [Fact]
    public async Task Admin_quota_adjustment_caps_the_bonus_and_never_touches_unlimited_accounts()
    {
        await Service().ProvisionDefaultQuotaAsync(ModuleTestDb.Editor);
        await _t.Kit.Billing.UserAIQuotas.ExecuteUpdateAsync(s => s.SetProperty(q => q.DailyUsageCount, 60));

        var row = await Service().AdminAdjustQuotaAsync(ModuleTestDb.Editor, isDev: false, resetToZero: true, bonusPoints: 5000, maxBonus: 1000);
        Assert.Equal(0, row.DailyUsageCount);
        Assert.Equal(1100, row.DailyLimit);

        var dev = await Service().AdminAdjustQuotaAsync(ModuleTestDb.Owner, isDev: true, resetToZero: false, bonusPoints: 1000, maxBonus: 1000);
        Assert.Equal(int.MaxValue, dev.DailyLimit);
    }

    [Fact]
    public async Task Queueing_a_cancellation_needs_a_subscription_id_and_writes_one_outbox_row()
    {
        Assert.Null(await Service().QueueSubscriptionCancellationAsync(ModuleTestDb.Editor));

        await _t.Db.SeedSubscriptionAsync(ModuleTestDb.Editor, status: null, planCode: null, stripeSubscriptionId: "  ");
        Assert.Null(await Service().QueueSubscriptionCancellationAsync(ModuleTestDb.Editor));

        await _t.Db.SeedSubscriptionAsync(ModuleTestDb.Editor, "active", "pro", "sub_9");
        var id = await Service().QueueSubscriptionCancellationAsync(ModuleTestDb.Editor);

        var row = await _t.Kit.Billing.PendingSubscriptionCancellations.AsNoTracking().SingleAsync();
        Assert.Equal(id, row.Id);
        Assert.Equal("sub_9", row.SubscriptionId);
        Assert.Null(row.CompletedAt);
    }

    [Fact]
    public async Task A_successful_immediate_cancellation_completes_the_row_and_tells_the_observers_once()
    {
        await _t.Db.SeedSubscriptionAsync(ModuleTestDb.Editor, "active", "pro", "sub_9");
        var observer = new RecordingObserver();
        var service = Service(new FakeCanceller(true), observer);
        var id = (await service.QueueSubscriptionCancellationAsync(ModuleTestDb.Editor))!;

        Assert.True(await service.TryCancelNowAsync(id));
        // Tamamlanan satır bir daha denenmez ve gözlemciye ikinci kez haber verilmez.
        Assert.False(await service.TryCancelNowAsync(id));

        Assert.Equal(new[] { (id, "sub_9") }, observer.Completed);
        Assert.NotNull((await _t.Kit.Billing.PendingSubscriptionCancellations.AsNoTracking().SingleAsync()).CompletedAt);
    }

    [Fact]
    public async Task A_failed_immediate_cancellation_leaves_the_row_open_with_a_backoff()
    {
        await _t.Db.SeedSubscriptionAsync(ModuleTestDb.Editor, "active", "pro", "sub_9");
        var observer = new RecordingObserver();
        var service = Service(new FakeCanceller(false), observer);
        var id = (await service.QueueSubscriptionCancellationAsync(ModuleTestDb.Editor))!;

        Assert.False(await service.TryCancelNowAsync(id));

        var row = await _t.Kit.Billing.PendingSubscriptionCancellations.AsNoTracking().SingleAsync();
        Assert.Equal(1, row.Attempts);
        Assert.Null(row.CompletedAt);
        Assert.True(row.NextAttemptAt > row.LastAttemptAt);
        Assert.Empty(observer.Completed);
    }

    [Fact]
    public async Task The_cancellation_backlog_reports_the_true_total_not_the_page_size()
    {
        // BL-3: yönetim paneli toplamı listeyle sınırlı göstermemeli.
        var now = DateTime.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            _t.Kit.Billing.PendingSubscriptionCancellations.Add(new PendingSubscriptionCancellation
            {
                Id = "o" + i, SubscriptionId = "s" + i, Attempts = i == 0 ? SubscriptionCancellationProcessor.AlertAfterAttempts : 1,
            });
        }
        _t.Kit.Billing.PendingSubscriptionCancellations.Add(new PendingSubscriptionCancellation { Id = "done", SubscriptionId = "x", CompletedAt = now });
        await _t.Kit.Billing.SaveChangesAsync();

        var backlog = await Service().GetCancellationBacklogAsync(take: 2);

        Assert.Equal(5, backlog.Total);
        Assert.Equal(1, backlog.Stuck);
        Assert.Equal(2, backlog.Items.Count);
        Assert.True(backlog.Items[0].Stuck);
    }

    [Fact]
    public async Task Erasing_personal_data_detaches_usage_events_and_removes_the_billing_rows()
    {
        var u = ModuleTestDb.Editor;
        await _t.Db.SeedSubscriptionAsync(u, "active", "pro", "sub_1", "cus_1");
        _t.Kit.Billing.UsageEvents.Add(new UsageEvent { UserId = u, Resource = UsageResource.AiCall, BillingPeriod = UsageMeter.CurrentPeriod() });
        _t.Kit.Billing.UserBillingSettings.Add(new UserBillingSettings { UserId = u, OverageEnabled = true });
        _t.Kit.Billing.UserAIQuotas.Add(new UserAIQuota { UserId = u });
        await _t.Kit.Billing.SaveChangesAsync();

        var erased = await Service().ErasePersonalDataAsync(u);

        Assert.True(erased >= 5);
        // Fatura kaydı kalır, kişi bağı kopar.
        Assert.Null((await _t.Kit.Billing.UsageEvents.AsNoTracking().SingleAsync()).UserId);
        Assert.Empty(await _t.Kit.Billing.Subscriptions.ToListAsync());
        Assert.Empty(await _t.Kit.Billing.Customers.ToListAsync());
        Assert.Empty(await _t.Kit.Billing.UserBillingSettings.ToListAsync());
        Assert.Empty(await _t.Kit.Billing.UserAIQuotas.ToListAsync());
    }

    [Fact]
    public async Task Retention_purges_only_old_usage_events_completed_cancellations_and_old_webhook_records()
    {
        var old = DateTime.UtcNow.AddDays(-400);
        var cutoff = DateTime.UtcNow.AddDays(-30);
        _t.Kit.Billing.UsageEvents.AddRange(
            new UsageEvent { UserId = "u", Resource = UsageResource.AiCall, OccurredAt = old, BillingPeriod = UsageMeter.PeriodOf(old) },
            new UsageEvent { UserId = "u", Resource = UsageResource.AiCall, OccurredAt = DateTime.UtcNow, BillingPeriod = UsageMeter.CurrentPeriod() });
        _t.Kit.Billing.PendingSubscriptionCancellations.AddRange(
            new PendingSubscriptionCancellation { Id = "open-old", SubscriptionId = "a", CreatedAt = old },
            new PendingSubscriptionCancellation { Id = "done-old", SubscriptionId = "b", CreatedAt = old, CompletedAt = old },
            new PendingSubscriptionCancellation { Id = "done-new", SubscriptionId = "c", CreatedAt = old, CompletedAt = DateTime.UtcNow });
        _t.Kit.Billing.StripeWebhookEvents.AddRange(
            new StripeWebhookEvent { Id = "evt_old", EventType = "x", ProcessedAt = old },
            new StripeWebhookEvent { Id = "evt_new", EventType = "x", ProcessedAt = DateTime.UtcNow });
        await _t.Kit.Billing.SaveChangesAsync();

        Assert.Equal(1, await Service().PurgeUsageEventsAsync(cutoff, TimeSpan.Zero));
        Assert.Equal(1, await Service().PurgeCompletedCancellationsAsync(cutoff, TimeSpan.Zero));
        Assert.Equal(1, await Service().PurgeStripeWebhookEventsAsync(cutoff, TimeSpan.Zero));

        Assert.Equal(new[] { "done-new", "open-old" },
            await _t.Kit.Billing.PendingSubscriptionCancellations.AsNoTracking().Select(c => c.Id).OrderBy(x => x).ToListAsync());
        Assert.Equal(1, await _t.Kit.Billing.UsageEvents.CountAsync());
        Assert.Equal("evt_new", (await _t.Kit.Billing.StripeWebhookEvents.SingleAsync()).Id);
    }
}

/// <summary>Plan listesi, ödeme doğrulama hataları ve durum görünümü (BillingSubscriptionService).</summary>
public sealed class BillingSubscriptionServiceTests : IDisposable
{
    private readonly ModuleTestDb _t = new();

    public void Dispose() => _t.Dispose();

    private BillingSubscriptionService Service(IDictionary<string, string?>? settings = null) =>
        new(_t.Kit.Billing, _t.Kit.Identity,
            new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build());

    [Fact]
    public void Plans_list_pro_and_team_and_flag_prices_without_a_configured_stripe_id_as_unavailable()
    {
        var plans = Service(new Dictionary<string, string?> { ["Stripe:ProPriceId"] = "price_pro_m" }).GetPlans();

        Assert.Equal(new[] { "pro", "team" }, plans.Select(p => p.Plan));
        var pro = plans[0];
        Assert.Equal(17, pro.YearlyDiscountPercent);
        Assert.True(pro.Prices.Single(p => p.Interval == "monthly").Available);
        Assert.False(pro.Prices.Single(p => p.Interval == "yearly").Available);
        Assert.All(plans[1].Prices, price => Assert.False(price.Available));
    }

    [Fact]
    public async Task Checkout_for_an_unknown_user_is_unauthorized()
    {
        Assert.Equal(401, (await Service().CreateCheckoutAsync("ghost", "pro", "monthly")).StatusCode);
        Assert.Equal(401, (await Service().CreatePortalAsync("ghost")).StatusCode);
    }

    [Theory]
    [InlineData("enterprise", "monthly", "Unknown plan")]
    [InlineData("", "monthly", "Unknown plan")]
    [InlineData("pro", "weekly", "Unknown billing interval")]
    public async Task Checkout_rejects_unknown_plan_or_interval_without_defaulting(string plan, string interval, string message)
    {
        var result = await Service().CreateCheckoutAsync(ModuleTestDb.Editor, plan, interval);

        Assert.Equal(400, result.StatusCode);
        Assert.Contains(message, result.Error);
        Assert.Null(result.Url);
    }

    [Fact]
    public async Task Checkout_reports_a_missing_price_configuration_instead_of_opening_a_broken_page()
    {
        var result = await Service().CreateCheckoutAsync(ModuleTestDb.Editor, "team", "yearly");

        Assert.Equal(500, result.StatusCode);
        Assert.Contains("not configured", result.Error);
    }

    [Fact]
    public async Task An_active_subscriber_is_sent_to_the_portal_instead_of_a_second_checkout()
    {
        await _t.Db.SeedSubscriptionAsync(ModuleTestDb.Editor, "active", "pro", "sub_1", "cus_1");

        var result = await Service().CreateCheckoutAsync(ModuleTestDb.Editor, "pro", "monthly");

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("portal", result.Redirect);
        Assert.Null(result.Url);
    }

    [Fact]
    public async Task The_portal_needs_a_stripe_customer()
    {
        var result = await Service().CreatePortalAsync(ModuleTestDb.Editor);

        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Status_is_null_for_unknown_users_none_for_free_and_follows_the_subscription()
    {
        Assert.Null(await Service().GetStatusAsync("ghost"));

        var free = await Service().GetStatusAsync(ModuleTestDb.Editor);
        Assert.Equal(("none", "Free", false), (free!.Status, free.Plan, free.IsPro));

        await _t.Db.SeedSubscriptionAsync(ModuleTestDb.Admin, "active", "team", "sub_1", "cus_1", DateTime.UtcNow.AddDays(10));
        var team = await Service().GetStatusAsync(ModuleTestDb.Admin);
        Assert.Equal(("active", "Team", true, true), (team!.Status, team.Plan, team.IsPro, team.HasStripeCustomer));

        // Ödemesi aksayan abonelik ücretli hak vermez ama durum görünür kalır.
        await _t.Db.SeedSubscriptionAsync(ModuleTestDb.Viewer, "past_due", "pro", "sub_2", "cus_2");
        var pastDue = await Service().GetStatusAsync(ModuleTestDb.Viewer);
        Assert.Equal(("past_due", "Free", false), (pastDue!.Status, pastDue.Plan, pastDue.IsPro));
    }

    [Fact]
    public async Task A_developer_account_is_reported_as_unlimited()
    {
        await _t.Db.Users.Where(u => u.Id == ModuleTestDb.Owner).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsDev, true));

        var status = await Service().GetStatusAsync(ModuleTestDb.Owner);

        Assert.True(status!.IsDev);
        Assert.Equal("active", status.Status);
        Assert.True(status.IsPro);
    }
}

/// <summary>Outbox işçisinin çekirdeği: tamamlanma, başarısızlık, geri çekilme, gözlemci hataları.</summary>
public sealed class SubscriptionCancellationProcessorTests : IDisposable
{
    private readonly ModuleTestDb _t = new();

    public void Dispose() => _t.Dispose();

    private sealed class Canceller(Func<string, bool> decide) : ISubscriptionCanceller
    {
        public List<string> Calls { get; } = new();

        public Task<bool> CancelAsync(string subscriptionId, CancellationToken ct)
        {
            Calls.Add(subscriptionId);
            return Task.FromResult(decide(subscriptionId));
        }
    }

    private sealed class Observer(bool fails = false) : ISubscriptionCancellationObserver
    {
        public List<(string Id, string Sub)> Seen { get; } = new();

        public Task OnCompletedAsync(string outboxId, string subscriptionId, CancellationToken ct)
        {
            Seen.Add((outboxId, subscriptionId));
            return fails ? throw new InvalidOperationException("audit store down") : Task.CompletedTask;
        }
    }

    private sealed class Logs : ILogger
    {
        public List<LogLevel> Levels { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Levels.Add(logLevel);
    }

    private async Task<PendingSubscriptionCancellation> SeedAsync(string id = "r1", string sub = "sub_1", int attempts = 0, DateTime? next = null)
    {
        var row = new PendingSubscriptionCancellation { Id = id, SubscriptionId = sub, Attempts = attempts, NextAttemptAt = next ?? DateTime.UtcNow.AddMinutes(-1) };
        _t.Kit.Billing.PendingSubscriptionCancellations.Add(row);
        await _t.Kit.Billing.SaveChangesAsync();
        _t.Kit.Billing.ChangeTracker.Clear();
        return row;
    }

    private SubscriptionCancellationProcessor Processor(ISubscriptionCanceller? canceller, ILogger? logger = null, params ISubscriptionCancellationObserver[] observers) =>
        new(_t.Kit.Billing, canceller, logger ?? NullLogger.Instance, observers);

    [Fact]
    public async Task Completion_calls_the_canceller_marks_the_row_done_and_notifies_the_observer_with_the_outbox_id()
    {
        await SeedAsync();
        var canceller = new Canceller(_ => true);
        var observer = new Observer();

        Assert.True(await Processor(canceller, null, observer).AttemptAsync("r1", DateTime.UtcNow));

        Assert.Equal(new[] { "sub_1" }, canceller.Calls);
        Assert.Equal(new[] { ("r1", "sub_1") }, observer.Seen);
        var row = await _t.Kit.Billing.PendingSubscriptionCancellations.AsNoTracking().SingleAsync();
        Assert.NotNull(row.CompletedAt);
        Assert.Equal(1, row.Attempts);
    }

    [Fact]
    public async Task A_failing_observer_does_not_reopen_a_cancelled_subscription()
    {
        // İptal gerçekleşti; iz yazılamadı diye satırı geri açmak aboneliği ikinci kez iptale zorlardı.
        await SeedAsync();

        Assert.True(await Processor(new Canceller(_ => true), null, new Observer(fails: true)).AttemptAsync("r1", DateTime.UtcNow));

        Assert.NotNull((await _t.Kit.Billing.PendingSubscriptionCancellations.AsNoTracking().SingleAsync()).CompletedAt);
    }

    [Fact]
    public async Task A_refusal_keeps_the_row_open_counts_the_attempt_and_does_not_notify()
    {
        await SeedAsync();
        var observer = new Observer();
        var at = DateTime.UtcNow;

        Assert.False(await Processor(new Canceller(_ => false), null, observer).AttemptAsync("r1", at));

        var row = await _t.Kit.Billing.PendingSubscriptionCancellations.AsNoTracking().SingleAsync();
        Assert.Null(row.CompletedAt);
        Assert.Equal(1, row.Attempts);
        Assert.Equal(at + SubscriptionCancellationProcessor.BackoffFor(1), row.NextAttemptAt);
        Assert.Empty(observer.Seen);
    }

    [Fact]
    public async Task A_throwing_canceller_counts_as_a_failed_attempt()
    {
        await SeedAsync();
        var thrower = new ThrowingCanceller();

        Assert.False(await Processor(thrower).AttemptAsync("r1", DateTime.UtcNow));

        Assert.Equal(1, (await _t.Kit.Billing.PendingSubscriptionCancellations.AsNoTracking().SingleAsync()).Attempts);
    }

    private sealed class ThrowingCanceller : ISubscriptionCanceller
    {
        public Task<bool> CancelAsync(string subscriptionId, CancellationToken ct) => throw new HttpRequestException("boom");
    }

    [Fact]
    public async Task Without_a_canceller_nothing_completes_but_the_attempt_is_still_scheduled()
    {
        await SeedAsync();

        Assert.False(await Processor(canceller: null).AttemptAsync("r1", DateTime.UtcNow));

        Assert.Equal(1, (await _t.Kit.Billing.PendingSubscriptionCancellations.AsNoTracking().SingleAsync()).Attempts);
    }

    [Fact]
    public async Task Unknown_and_already_completed_rows_are_not_attempted()
    {
        await SeedAsync();
        var canceller = new Canceller(_ => true);

        Assert.False(await Processor(canceller).AttemptAsync("missing", DateTime.UtcNow));
        Assert.True(await Processor(canceller).AttemptAsync("r1", DateTime.UtcNow));
        Assert.False(await Processor(canceller).AttemptAsync("r1", DateTime.UtcNow));

        Assert.Single(canceller.Calls);
    }

    [Fact]
    public async Task The_worker_pass_only_takes_rows_that_are_due()
    {
        await SeedAsync("due", "sub_due", next: DateTime.UtcNow.AddMinutes(-5));
        await SeedAsync("later", "sub_later", next: DateTime.UtcNow.AddHours(2));
        var canceller = new Canceller(_ => true);

        var processed = await Processor(canceller).ProcessDueAsync(DateTime.UtcNow, batchSize: 10, default);

        Assert.Equal(1, processed);
        Assert.Equal(new[] { "sub_due" }, canceller.Calls);
        Assert.Null((await _t.Kit.Billing.PendingSubscriptionCancellations.AsNoTracking().SingleAsync(c => c.Id == "later")).CompletedAt);
    }

    [Fact]
    public async Task Crossing_the_alert_threshold_logs_an_error()
    {
        await SeedAsync(attempts: SubscriptionCancellationProcessor.AlertAfterAttempts - 1);
        var logs = new Logs();

        Assert.False(await Processor(new Canceller(_ => false), logs).AttemptAsync("r1", DateTime.UtcNow));

        Assert.Contains(LogLevel.Error, logs.Levels);
    }

    [Fact]
    public void Backoff_doubles_from_two_minutes_and_is_capped_at_six_hours()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), SubscriptionCancellationProcessor.BackoffFor(1));
        Assert.Equal(TimeSpan.FromMinutes(4), SubscriptionCancellationProcessor.BackoffFor(2));
        Assert.Equal(TimeSpan.FromMinutes(8), SubscriptionCancellationProcessor.BackoffFor(3));
        Assert.Equal(TimeSpan.FromHours(6), SubscriptionCancellationProcessor.BackoffFor(50));
        Assert.Equal(TimeSpan.FromMinutes(2), SubscriptionCancellationProcessor.BackoffFor(0));
    }
}

/// <summary>
/// Stripe iptali: <c>resource_missing</c> "zaten yok" demektir, başarı sayılır. Stripe SDK'sının HTTP sınırı sahte
/// <see cref="IHttpClient"/> ile değiştirilir; ağ yok, yanıt gövdeleri Stripe'ın belgelenmiş hata biçimi.
/// </summary>
public sealed class StripeSubscriptionCancellerTests
{
    private sealed class StubHttp(HttpStatusCode status, string body) : IHttpClient
    {
        public int Calls { get; private set; }

        public Task<StripeResponse> MakeRequestAsync(StripeRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            using var message = new HttpResponseMessage(status);
            return Task.FromResult(new StripeResponse(status, message.Headers, body));
        }

        public Task<StripeStreamedResponse> MakeStreamingRequestAsync(StripeRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static StripeSubscriptionCanceller Canceller(IHttpClient? http, string? key = "sk_test_x") =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Stripe:SecretKey"] = key }).Build(),
            NullLogger<StripeSubscriptionCanceller>.Instance, http);

    [Fact]
    public async Task A_missing_subscription_counts_as_cancelled()
    {
        var http = new StubHttp(HttpStatusCode.NotFound,
            """{"error":{"type":"invalid_request_error","code":"resource_missing","message":"No such subscription"}}""");

        Assert.True(await Canceller(http).CancelAsync("sub_gone", default));
        Assert.Equal(1, http.Calls);
    }

    [Fact]
    public async Task Other_stripe_errors_are_a_failure_so_the_outbox_retries()
    {
        var denied = new StubHttp(HttpStatusCode.Unauthorized,
            """{"error":{"type":"invalid_request_error","code":"api_key_expired","message":"expired"}}""");
        Assert.False(await Canceller(denied).CancelAsync("sub_1", default));

        var server = new StubHttp(HttpStatusCode.InternalServerError, """{"error":{"type":"api_error","message":"oops"}}""");
        Assert.False(await Canceller(server).CancelAsync("sub_1", default));
    }

    [Fact]
    public async Task A_successful_cancel_call_is_a_success()
    {
        var http = new StubHttp(HttpStatusCode.OK, """{"id":"sub_1","object":"subscription","status":"canceled"}""");

        Assert.True(await Canceller(http).CancelAsync("sub_1", default));
    }

    [Fact]
    public async Task Without_a_secret_key_no_call_is_made_and_the_result_is_a_failure()
    {
        var http = new StubHttp(HttpStatusCode.OK, "{}");

        Assert.False(await Canceller(http, key: null).CancelAsync("sub_1", default));
        Assert.False(await Canceller(http, key: "  ").CancelAsync("sub_1", default));
        Assert.Equal(0, http.Calls);
    }
}

/// <summary>Kullanım ölçümünün ayar ve özet uçları (kayıt/karar yolları UsageMeterTests'te).</summary>
public sealed class UsageMeterServiceTests : IDisposable
{
    private readonly ModuleTestDb _t = new();

    public void Dispose() => _t.Dispose();

    private IUsageMeter Meter => _t.Kit.Usage;

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_zero_or_negative_cap_is_rejected_and_nothing_is_stored(double cap)
    {
        // Negatif tavan "sınırsız" ile, sıfır tavan "açtım ama çalışmıyor" ile karışır.
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Meter.UpdateSettingsAsync(ModuleTestDb.Editor, true, (decimal)cap));

        Assert.Empty(await _t.Kit.Billing.UserBillingSettings.ToListAsync());
    }

    [Fact]
    public async Task Settings_are_created_then_updated_in_place_and_a_missing_cap_means_no_cap()
    {
        Assert.Equal((true, 12.5m), await Meter.UpdateSettingsAsync(ModuleTestDb.Editor, true, 12.5m));
        Assert.Equal((true, (decimal?)null), await Meter.UpdateSettingsAsync(ModuleTestDb.Editor, true, null));
        Assert.Equal((false, (decimal?)null), await Meter.UpdateSettingsAsync(ModuleTestDb.Editor, false, null));

        Assert.Equal(1, await _t.Kit.Billing.UserBillingSettings.CountAsync());
    }

    [Fact]
    public void The_billing_period_is_the_first_of_the_month_in_utc_regardless_of_the_moment()
    {
        var late = new DateTime(2026, 10, 31, 23, 59, 59, DateTimeKind.Utc);
        var early = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), UsageMeter.PeriodOf(late));
        Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), UsageMeter.PeriodOf(early));
    }
}

/// <summary>Kotanın rezervasyon, iade ve okuma kuralları SQLite'ta (PostgreSQL eşzamanlılığı Phase14_2D2BillingRealPgTests'te).</summary>
public sealed class AiQuotaServiceBehaviourTests : IDisposable
{
    private readonly ModuleTestDb _t = new();

    public void Dispose() => _t.Dispose();

    private AiQuotaService Quota(int perUser = 10_000, long pool = 100_000_000) =>
        _t.Kit.AiQuota(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiPool:PerUserDailyTokens"] = perUser.ToString(),
            ["AiPool:DailyTokenPool"] = pool.ToString(),
        }).Build());

    [Fact]
    public async Task Reserving_up_to_the_cap_is_allowed_and_one_token_more_is_refused_without_a_partial_write()
    {
        var quota = Quota(perUser: 3_000);

        Assert.Equal(AiQuotaDecision.Allowed, await quota.TryReserveAsync(ModuleTestDb.Editor, 3_000));
        Assert.True(quota.HasReservedInScope);
        Assert.Equal(AiQuotaDecision.UserExhausted, await quota.TryReserveAsync(ModuleTestDb.Editor, 1));

        Assert.Equal(3_000, (await _t.Kit.Billing.UserAIQuotas.AsNoTracking().SingleAsync()).DailyUsageCount);
        Assert.Equal(3_000, await _t.Kit.Billing.GlobalAiUsages.SumAsync(g => g.TokensUsed));
    }

    [Fact]
    public async Task A_refused_reservation_does_not_mark_the_scope_as_reserved()
    {
        var quota = Quota(perUser: 100);

        Assert.Equal(AiQuotaDecision.UserExhausted, await quota.TryReserveAsync(ModuleTestDb.Editor, 500));

        Assert.False(quota.HasReservedInScope);
    }

    [Fact]
    public async Task The_free_pool_refuses_before_the_user_cap_is_consulted()
    {
        var quota = Quota(perUser: 20_000, pool: 1_000);

        Assert.Equal(AiQuotaDecision.PoolExhausted, await quota.TryReserveAsync(ModuleTestDb.Editor, 2_000));
        Assert.Empty(await _t.Kit.Billing.UserAIQuotas.Where(q => q.DailyUsageCount > 0).ToListAsync());
    }

    [Fact]
    public async Task A_developer_account_is_always_allowed_but_its_spend_is_still_measured()
    {
        await _t.Db.Users.Where(u => u.Id == ModuleTestDb.Owner).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsDev, true));
        var quota = Quota(perUser: 100, pool: 100);

        Assert.Equal(AiQuotaDecision.Allowed, await quota.TryReserveAsync(ModuleTestDb.Owner, 5_000_000));

        Assert.Equal(5_000_000, await _t.Kit.Billing.GlobalAiUsages.SumAsync(g => g.TokensUsed));
    }

    [Fact]
    public async Task Reconcile_charges_the_difference_when_the_provider_used_more_and_refunds_when_less()
    {
        var quota = Quota();
        await quota.TryReserveAsync(ModuleTestDb.Editor, 1_000);

        await quota.ReconcileAsync(ModuleTestDb.Editor, reservedTokens: 1_000, actualTokens: 1_400);
        Assert.Equal(1_400, (await _t.Kit.Billing.UserAIQuotas.AsNoTracking().SingleAsync()).DailyUsageCount);

        await quota.ReconcileAsync(ModuleTestDb.Editor, reservedTokens: 1_400, actualTokens: 900);
        Assert.Equal(900, (await _t.Kit.Billing.UserAIQuotas.AsNoTracking().SingleAsync()).DailyUsageCount);

        // Sağlayıcı kullanım bildirmediyse tahmin geçerli kalır: iade edip bedavaya saymak açığa yol açardı.
        await quota.ReconcileAsync(ModuleTestDb.Editor, reservedTokens: 900, actualTokens: 0);
        Assert.Equal(900, (await _t.Kit.Billing.UserAIQuotas.AsNoTracking().SingleAsync()).DailyUsageCount);
    }

    [Fact]
    public async Task Reading_the_status_never_writes()
    {
        var status = await Quota().GetStatusAsync(ModuleTestDb.Editor);

        Assert.Equal(0, status.Used);
        Assert.Equal(PlanTier.Free, status.Plan);
        Assert.Empty(await _t.Kit.Billing.UserAIQuotas.ToListAsync());
        Assert.Empty(await _t.Kit.Billing.GlobalAiUsages.ToListAsync());
    }

    [Fact]
    public async Task The_tier_follows_the_subscription_and_ignores_a_stale_plan_code_when_payment_lapsed()
    {
        await _t.Db.SeedSubscriptionAsync(ModuleTestDb.Admin, "active", "team");
        await _t.Db.SeedSubscriptionAsync(ModuleTestDb.Editor, "past_due", "team");

        Assert.Equal(PlanTier.Team, await Quota().TierAsync(ModuleTestDb.Admin));
        Assert.Equal(PlanTier.Free, await Quota().TierAsync(ModuleTestDb.Editor));
        Assert.Equal(PlanTier.Free, await Quota().TierAsync("ghost"));
    }
}
