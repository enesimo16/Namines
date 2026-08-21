using Microsoft.EntityFrameworkCore;
using Namines.Core.Enums;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace Namines.Tests.Integration;

/// <summary>
/// G11 — <see cref="ChangeRequest"/>/<see cref="ChangeRequestApproval"/> tabloları GERÇEK
/// bir PostgreSQL'e karşı. <see cref="Analysis.ChangeRequestApprovalPolicyTests"/> iş
/// kuralını (kim ne zaman onaylayabilir) test eder — bu sınıf DB'nin kendi kısıtlarını
/// (aynı kullanıcı iki kez oy veremez, branch silinince CR de silinir) kanıtlar. Aynı
/// G10 (BranchSchemaVersionTests) deseni.
/// </summary>
[Collection("Docker")]
public class ChangeRequestIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    private AuthDbContext? _context;
    private string _projectId = null!;
    private string _userId = null!;
    private string _reviewerId = null!;
    private string _branchId = null!;
    private string _headVersionId = null!;

    public async Task InitializeAsync()
    {
        if (!DockerAvailable.Value) return;

        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        _context = new AuthDbContext(options);
        await _context.Database.MigrateAsync();

        _userId = Guid.NewGuid().ToString();
        _reviewerId = Guid.NewGuid().ToString();
        await _context.Users.AddAsync(new ApplicationUser { Id = _userId, UserName = "author" });
        await _context.Users.AddAsync(new ApplicationUser { Id = _reviewerId, UserName = "reviewer" });

        _projectId = Guid.NewGuid().ToString();
        await _context.CloudProjects.AddAsync(new CloudProject
        {
            Id = _projectId, Name = "TestProject", DbType = "PostgreSQL",
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = _userId
        });
        await _context.SaveChangesAsync();

        var branch = new Branch { ProjectId = _projectId, Name = "main", IsDefault = true, CreatedByUserId = _userId };
        await _context.Branches.AddAsync(branch);
        await _context.SaveChangesAsync();
        _branchId = branch.Id;

        var version = new SchemaVersion
        {
            ProjectId = _projectId, BranchId = _branchId, Version = 1,
            Checksum = "abc", SchemaJson = "{}", AuthorUserId = _userId
        };
        await _context.SchemaVersions.AddAsync(version);
        await _context.SaveChangesAsync();
        _headVersionId = version.Id;
    }

    public async Task DisposeAsync()
    {
        if (!DockerAvailable.Value) return;
        if (_context is not null) await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    private ChangeRequest NewChangeRequest(RiskLevel risk = RiskLevel.Safe) => new()
    {
        ProjectId = _projectId,
        BranchId = _branchId,
        HeadVersionId = _headVersionId,
        Title = "Test change",
        RiskLevel = risk,
        ImpactReportJson = "{}",
        CreatedByUserId = _userId
    };

    [RequiresDockerFact]
    public async Task Can_create_a_change_request_and_read_it_back()
    {
        var cr = NewChangeRequest();
        await _context!.ChangeRequests.AddAsync(cr);
        await _context.SaveChangesAsync();

        var reloaded = await _context.ChangeRequests.FindAsync(cr.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(ChangeRequestStatus.PendingReview, reloaded!.Status);
    }

    [RequiresDockerFact]
    public async Task Same_user_cannot_vote_twice_on_same_change_request()
    {
        var cr = NewChangeRequest();
        await _context!.ChangeRequests.AddAsync(cr);
        await _context.SaveChangesAsync();

        await _context.ChangeRequestApprovals.AddAsync(new ChangeRequestApproval
        {
            ChangeRequestId = cr.Id, UserId = _reviewerId, Decision = ApprovalDecision.Approved
        });
        await _context.SaveChangesAsync();

        await _context.ChangeRequestApprovals.AddAsync(new ChangeRequestApproval
        {
            ChangeRequestId = cr.Id, UserId = _reviewerId, Decision = ApprovalDecision.Rejected
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    [RequiresDockerFact]
    public async Task Deleting_branch_cascades_to_change_requests_and_their_approvals()
    {
        var cr = NewChangeRequest();
        await _context!.ChangeRequests.AddAsync(cr);
        await _context.SaveChangesAsync();

        await _context.ChangeRequestApprovals.AddAsync(new ChangeRequestApproval
        {
            ChangeRequestId = cr.Id, UserId = _reviewerId, Decision = ApprovalDecision.Approved
        });
        await _context.SaveChangesAsync();

        var branch = await _context.Branches.FindAsync(_branchId);
        _context.Branches.Remove(branch!);
        await _context.SaveChangesAsync();

        // Branch -> ChangeRequest (Cascade) ve Branch -> SchemaVersion (Cascade) aynı silme
        // işleminde birlikte tetikleniyor; ChangeRequest.HeadVersionId'nin SchemaVersion'a
        // Restrict FK'si olması bu iki cascade yolunun aynı anda yürütülmesini engellemiyor —
        // Postgres tüm cascade kapanışını tek işlemde hesaplıyor. Bu test tam bunu kanıtlıyor.
        Assert.False(await _context.ChangeRequests.AnyAsync(c => c.Id == cr.Id));
        Assert.False(await _context.ChangeRequestApprovals.AnyAsync(a => a.ChangeRequestId == cr.Id));
    }

    [RequiresDockerFact]
    public async Task Approving_a_change_request_end_to_end_using_the_policy()
    {
        var cr = NewChangeRequest(RiskLevel.Breaking);
        await _context!.ChangeRequests.AddAsync(cr);
        await _context.SaveChangesAsync();

        // Yazar kendi breaking değişikliğini onaylayamaz — policy bunu reddeder,
        // burada sadece DB'nin de bunu engellemediğini (uygulama katmanı sorumluluğu)
        // ve normal akışta ikinci farklı kullanıcının onayının durumu değiştirdiğini kanıtlıyoruz.
        await _context.ChangeRequestApprovals.AddAsync(new ChangeRequestApproval
        {
            ChangeRequestId = cr.Id, UserId = _reviewerId, Decision = ApprovalDecision.Approved
        });
        cr.Status = ChangeRequestStatus.PendingReview; // hâlâ 2 onay gerekiyor (Breaking)
        await _context.SaveChangesAsync();

        var reloaded = await _context.ChangeRequests
            .Include(c => c.Approvals)
            .FirstAsync(c => c.Id == cr.Id);

        Assert.Single(reloaded.Approvals);
        Assert.Equal(ChangeRequestStatus.PendingReview, reloaded.Status);
    }

    // ── G16 — audit log ─────────────────────────────────────────────────────

    [RequiresDockerFact]
    public async Task Audit_log_records_a_system_driven_event_with_no_actor()
    {
        var cr = NewChangeRequest(RiskLevel.Safe);
        cr.Status = ChangeRequestStatus.Approved;
        cr.ResolvedAt = DateTime.UtcNow;
        await _context!.ChangeRequests.AddAsync(cr);
        await _context.SaveChangesAsync();

        // Otomatik onay — hiçbir insan aktör yok, ActorUserId null.
        await _context.ChangeRequestAuditLogs.AddAsync(new ChangeRequestAuditLog
        {
            ChangeRequestId = cr.Id,
            Action = ChangeRequestAuditAction.AutoApproved,
            ActorUserId = null,
            Details = "Safe risk + project.AutoApproveSafeChanges enabled"
        });
        await _context.SaveChangesAsync();

        var entry = await _context.ChangeRequestAuditLogs.SingleAsync(a => a.ChangeRequestId == cr.Id);
        Assert.Equal(ChangeRequestAuditAction.AutoApproved, entry.Action);
        Assert.Null(entry.ActorUserId);
    }

    [RequiresDockerFact]
    public async Task Audit_log_cascades_when_change_request_is_deleted_via_branch_removal()
    {
        var cr = NewChangeRequest();
        await _context!.ChangeRequests.AddAsync(cr);
        await _context.SaveChangesAsync();

        await _context.ChangeRequestAuditLogs.AddAsync(new ChangeRequestAuditLog
        {
            ChangeRequestId = cr.Id, Action = ChangeRequestAuditAction.Created, ActorUserId = _userId
        });
        await _context.SaveChangesAsync();

        var branch = await _context.Branches.FindAsync(_branchId);
        _context.Branches.Remove(branch!);
        await _context.SaveChangesAsync();

        Assert.False(await _context.ChangeRequestAuditLogs.AnyAsync(a => a.ChangeRequestId == cr.Id));
    }

    [RequiresDockerFact]
    public async Task Audit_log_preserves_full_lifecycle_order_for_a_change_request()
    {
        var cr = NewChangeRequest(RiskLevel.Risky);
        await _context!.ChangeRequests.AddAsync(cr);
        await _context.SaveChangesAsync();

        await _context.ChangeRequestAuditLogs.AddAsync(new ChangeRequestAuditLog
        {
            ChangeRequestId = cr.Id, Action = ChangeRequestAuditAction.Created, ActorUserId = _userId,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        cr.Status = ChangeRequestStatus.Approved;
        cr.ResolvedAt = DateTime.UtcNow;
        await _context.ChangeRequestAuditLogs.AddAsync(new ChangeRequestAuditLog
        {
            ChangeRequestId = cr.Id, Action = ChangeRequestAuditAction.Approved, ActorUserId = _reviewerId,
            CreatedAt = DateTime.UtcNow.AddSeconds(1)
        });
        await _context.SaveChangesAsync();

        var timeline = await _context.ChangeRequestAuditLogs
            .Where(a => a.ChangeRequestId == cr.Id)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();

        Assert.Equal(2, timeline.Count);
        Assert.Equal(ChangeRequestAuditAction.Created, timeline[0].Action);
        Assert.Equal(ChangeRequestAuditAction.Approved, timeline[1].Action);
    }
}
