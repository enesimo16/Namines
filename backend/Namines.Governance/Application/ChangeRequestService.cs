using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.BuildingBlocks.Security;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Governance.Domain;

namespace Namines.Governance.Application;

public sealed record QuickChangeRequestInput(string ProjectId, string SchemaJson, string? Title, string? Message);

public sealed record CreatedChangeRequest(string Id);

public sealed record AuditEntryView(string Id, ChangeRequestAuditAction Action, string? ActorUserId, string? ActorUsername, string? Details, DateTime CreatedAt);

public sealed record VersionView(string Id, int Version, short TableCount, DateTime CreatedAt);

public sealed record TestRunView(bool? TestRunSupported, bool? TestRunSuccess, string? TestRunMessage, string? TestRunFailedStatement, long? TestRunDurationMs, DateTime? TestRunAt);

public sealed record ApprovalView(string Id, string? UserId, string? Username, ApprovalDecision Decision, string? Comment, DateTime CreatedAt);

public sealed record ChangeRequestDetail(
    string Id, string ProjectId, string BranchId, string BranchName, string? Title,
    ChangeRequestStatus Status, RiskLevel RiskLevel, string? CreatedByUserId, DateTime CreatedAt, DateTime? ResolvedAt,
    VersionView HeadVersion, VersionView? BaseVersion, ImpactReport? Impact, string? AiExplanation, MigrationResult? Migration,
    TestRunView? TestRun, int RequiredApprovals, int ApprovedCount, int RejectedCount, IReadOnlyList<ApprovalView> Approvals);

public sealed record ChangeRequestListItem(
    string Id, string? Title, ChangeRequestStatus Status, RiskLevel RiskLevel, string BranchName, short TableCount,
    DateTime CreatedAt, DateTime? ResolvedAt, int ApprovedCount, int RequiredApprovals);

public sealed record DecisionResult(string Id, ChangeRequestStatus Status);

public sealed record AffectedCodeFile(string FileName, string Content);

public sealed record AffectedCodeScan(IReadOnlyList<string> CandidateIdentifiers, object Matches, int FilesScanned);

/// <summary>
/// Veritabanı değişiklik incelemesi (Database PR): açma, oylama, test çalıştırma. Yetki kararı burada, controller
/// yalnızca sonucu HTTP'ye çevirir.
/// </summary>
public interface IChangeRequestService
{
    Task<ModuleOutcome<CreatedChangeRequest>> CreateQuickAsync(string userId, QuickChangeRequestInput input, CancellationToken ct = default);

    Task<ModuleOutcome<IReadOnlyList<AuditEntryView>>> GetAuditAsync(string userId, string id, CancellationToken ct = default);

    Task<ModuleOutcome<ChangeRequestDetail>> GetDetailAsync(string userId, string id, CancellationToken ct = default);

    Task<ModuleOutcome<IReadOnlyList<ChangeRequestListItem>>> ListForProjectAsync(string userId, string projectId, CancellationToken ct = default);

    Task<ModuleOutcome<DecisionResult>> DecideAsync(string userId, string id, ApprovalDecision decision, string? comment, CancellationToken ct = default);

    Task<ModuleOutcome<TestRunResult>> RunTestsAsync(string userId, string id, CancellationToken ct = default);

    Task<ModuleOutcome<AffectedCodeScan>> ScanAffectedCodeAsync(string userId, string id, IReadOnlyList<AffectedCodeFile> files, CancellationToken ct = default);
}

internal sealed class ChangeRequestService : IChangeRequestService
{
    private const int ImpactExplanationTokenEstimate = 2_000;
    private const string NotFoundCr = "Change request not found.";
    private const string NotFoundProject = "Project not found, or it does not belong to this user.";

    // Frontend enum'ları string yazıyor, dönüştürücü olmadan DatabaseSchema çözülemiyor.
    private static readonly JsonSerializerOptions SchemaJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IGovernanceData _db;
    private readonly ITenantProjectAuthority _authority;
    private readonly IStudioProjects _projects;
    private readonly IStudioBranches _branches;
    private readonly IIdentityDirectory _identity;
    private readonly IMigrationService _migrationService;
    private readonly IBranchTestRunner _testRunner;
    private readonly IAIService _aiService;
    private readonly IAiQuota _quota;

    public ChangeRequestService(
        IGovernanceData db, ITenantProjectAuthority authority, IStudioProjects projects, IStudioBranches branches,
        IIdentityDirectory identity, IMigrationService migrationService, IBranchTestRunner testRunner,
        IAIService aiService, IAiQuota quota)
    {
        _db = db;
        _authority = authority;
        _projects = projects;
        _branches = branches;
        _identity = identity;
        _migrationService = migrationService;
        _testRunner = testRunner;
        _aiService = aiService;
        _quota = quota;
    }

    private static DatabaseType EngineOf(string dbType) =>
        Enum.TryParse<DatabaseType>(dbType, ignoreCase: true, out var parsed) ? parsed : DatabaseType.PostgreSQL;

    private static DatabaseSchema ParseSchema(string json) =>
        JsonSerializer.Deserialize<DatabaseSchema>(json, SchemaJsonOptions) ?? new DatabaseSchema();

    public async Task<ModuleOutcome<CreatedChangeRequest>> CreateQuickAsync(string userId, QuickChangeRequestInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.ProjectId) || string.IsNullOrWhiteSpace(input.SchemaJson))
            return ModuleOutcome<CreatedChangeRequest>.Invalid("ProjectId and SchemaJson are required.");

        // 05 §6: şema yazma/CR açma → Editor ve üstü.
        if (!await _authority.CanEditAsync(input.ProjectId, userId, ct))
            return ModuleOutcome<CreatedChangeRequest>.NotFound(NotFoundProject);
        var project = await _projects.GetHeaderAsync(input.ProjectId, ct)
            ?? throw new InvalidOperationException("Project disappeared after the access check.");

        DatabaseSchema headSchema;
        try { headSchema = ParseSchema(input.SchemaJson); }
        catch (JsonException) { return ModuleOutcome<CreatedChangeRequest>.Invalid("SchemaJson is not a valid DatabaseSchema."); }

        // Yarışa karşı sertleştirilmiş tek kopya: Studio sözleşmesi.
        var branch = await _branches.GetOrCreateDefaultAsync(input.ProjectId, userId, ct);

        // Taban, sürüm eklenmeden önceki son sürüm. Sürüm numarası yarışı sözleşmenin içinde: aynı anda iki
        // "Request Review" ikisini de kaydeder; taban bir önceki numaraya ait olur.
        var headVersion = await _branches.AddVersionAsync(branch.Id, input.SchemaJson, input.Message, (short)headSchema.Tables.Count, userId, ct);
        var previousVersion = headVersion.Version > 1
            ? await _branches.GetVersionAtAsync(branch.Id, headVersion.Version - 1, ct)
            : null;
        var baseSchema = previousVersion is not null ? ParseSchema(previousVersion.SchemaJson) : new DatabaseSchema();

        var impact = SchemaImpactAnalyzer.Analyze(baseSchema, headSchema, EngineOf(project.DbType));

        // Safe risk, proje bunu açıkça opt-in etmişse insan onayı beklemeden Approved açılır; diğer her risk PendingReview.
        // previousVersion KOŞULU ŞART: ilk talepte karşılaştırılacak taban yok, boş şemaya karşı diff alınıyor ve her şey
        // "eklendi" görünüyor: tablo silen bir talep bile Safe çıkardı. Taban yoksa risk seviyesi bir şey kanıtlamıyor.
        var autoApprove = previousVersion is not null
                       && impact.OverallRisk == RiskLevel.Safe
                       && project.AutoApproveSafeChanges;
        var now = DateTime.UtcNow;

        var changeRequest = new ChangeRequest
        {
            ProjectId = input.ProjectId,
            BranchId = branch.Id,
            BaseVersionId = previousVersion?.Id,
            HeadVersionId = headVersion.Id,
            Title = input.Title ?? $"Schema update — v{headVersion.Version}",
            Status = autoApprove ? ChangeRequestStatus.Approved : ChangeRequestStatus.PendingReview,
            RiskLevel = impact.OverallRisk,
            ImpactReportJson = JsonSerializer.Serialize(impact),
            CreatedByUserId = userId,
            CreatedAt = now,
            ResolvedAt = autoApprove ? now : null,
            // Onay kuralının dayanağı BURADA sabitlenir (bkz. ChangeRequest.VotingMembersAtCreation, docs/last-review M-1).
            VotingMembersAtCreation = await _authority.CountVotingMembersAsync(input.ProjectId, ct),
        };
        _db.ChangeRequests.Add(changeRequest);

        _db.ChangeRequestAuditLogs.Add(new ChangeRequestAuditLog
        {
            ChangeRequestId = changeRequest.Id,
            ProjectId = changeRequest.ProjectId,
            ChangeRequestTitle = changeRequest.Title,
            Action = ChangeRequestAuditAction.Created,
            ActorUserId = userId,
            Details = $"Risk={impact.OverallRisk}",
            CreatedAt = now,
        });

        if (autoApprove)
        {
            _db.ChangeRequestAuditLogs.Add(new ChangeRequestAuditLog
            {
                ChangeRequestId = changeRequest.Id,
                ProjectId = changeRequest.ProjectId,
                ChangeRequestTitle = changeRequest.Title,
                Action = ChangeRequestAuditAction.AutoApproved,
                ActorUserId = null, // sistem-güdümlü: hiçbir insan onaylamadı
                Details = "Safe risk + project.AutoApproveSafeChanges enabled",
                // Listenin tek sıralama anahtarı CreatedAt; "Created"tan sonraya düşmesi ZORUNLU. 1 tick (100ns) YETMEZ:
                // timestamptz mikrosaniye çözünürlüğünde saklanır. 10 tick = tam 1 mikrosaniye.
                CreatedAt = now.AddTicks(10),
            });
        }

        await _db.SaveChangesAsync(ct);
        return ModuleOutcome<CreatedChangeRequest>.Ok(new CreatedChangeRequest(changeRequest.Id));
    }

    public async Task<ModuleOutcome<IReadOnlyList<AuditEntryView>>> GetAuditAsync(string userId, string id, CancellationToken ct = default)
    {
        var projectId = await _db.ChangeRequests.AsNoTracking().Where(c => c.Id == id).Select(c => c.ProjectId).FirstOrDefaultAsync(ct);
        if (projectId is null) return ModuleOutcome<IReadOnlyList<AuditEntryView>>.NotFound(NotFoundCr);

        // 05 §6: okuma için Viewer dahil her org üyesi yeterli.
        if (!await _authority.CanViewAsync(projectId, userId, ct))
            return ModuleOutcome<IReadOnlyList<AuditEntryView>>.NotFound(NotFoundCr);

        var rows = await _db.ChangeRequestAuditLogs.AsNoTracking()
            .Where(a => a.ChangeRequestId == id)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);
        var names = await _identity.GetUserNamesAsync(rows.Where(r => r.ActorUserId != null).Select(r => r.ActorUserId!).Distinct().ToList(), ct);

        IReadOnlyList<AuditEntryView> entries = rows.Select(a => new AuditEntryView(
            a.Id, a.Action, a.ActorUserId,
            a.ActorUserId is not null && names.TryGetValue(a.ActorUserId, out var name) ? name : null,
            a.Details, a.CreatedAt)).ToList();
        return ModuleOutcome<IReadOnlyList<AuditEntryView>>.Ok(entries);
    }

    private static int EffectiveTeamSize(ChangeRequest cr, int liveTeamSize) =>
        cr.VotingMembersAtCreation > 0
            ? Math.Max(cr.VotingMembersAtCreation, liveTeamSize)
            : liveTeamSize;

    public async Task<ModuleOutcome<ChangeRequestDetail>> GetDetailAsync(string userId, string id, CancellationToken ct = default)
    {
        var cr = await _db.ChangeRequests.Include(c => c.Approvals).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cr is null) return ModuleOutcome<ChangeRequestDetail>.NotFound(NotFoundCr);

        if (!await _authority.CanViewAsync(cr.ProjectId, userId, ct))
            return ModuleOutcome<ChangeRequestDetail>.NotFound(NotFoundCr);
        var project = await _projects.GetHeaderAsync(cr.ProjectId, ct)
            ?? throw new InvalidOperationException("Project disappeared after the access check.");

        var impact = JsonSerializer.Deserialize<ImpactReport>(cr.ImpactReportJson);

        // Sürüm ve branch Studio'nun: FK DB'de korunuyor, satır burada sözleşmeyle okunuyor.
        var headVersion = await _branches.GetVersionAsync(cr.HeadVersionId, ct)
            ?? throw new InvalidOperationException("Head version is missing.");
        var baseVersion = cr.BaseVersionId is null ? null : await _branches.GetVersionAsync(cr.BaseVersionId, ct);
        var branch = await _branches.GetAsync(cr.BranchId, ct)
            ?? throw new InvalidOperationException("Branch is missing.");

        var headSchema = ParseSchema(headVersion.SchemaJson);
        var baseSchema = baseVersion is not null ? ParseSchema(baseVersion.SchemaJson) : new DatabaseSchema();
        var engine = EngineOf(project.DbType);

        async Task<MigrationResult?> TryGenerateMigrationAsync()
        {
            try { return await _migrationService.GenerateMigrationAsync(baseSchema, headSchema, engine); }
            catch { return null; }
        }

        async Task<string?> TryExplainImpactAsync()
        {
            try { return await _aiService.ExplainImpactAsync(impact!); }
            catch { return null; }
        }

        // SAKLANMIŞ sonuç varsa MODEL HİÇ ÇAĞRILMIYOR (docs/last-review M-4): bu uç her görüntülemede iki LLM çağrısı
        // yapıyordu, Viewer'a açıktı ve kotaya yazılmıyordu. Girdi (base/head şemaları) değişmediği için sonuç
        // saklanır; bütçesi yoksa çağrı yapılmaz ve sekmeler boş görünür.
        var migration = cr.MigrationResultJson is not null
            ? JsonSerializer.Deserialize<MigrationResult>(cr.MigrationResultJson, SchemaJsonOptions)
            : null;
        var aiExplanation = cr.AiExplanation;

        if (migration is null || aiExplanation is null)
        {
            var reservation = await _quota.TryReserveAsync(userId, ImpactExplanationTokenEstimate, ct);
            if (reservation == AiQuotaDecision.Allowed)
            {
                var needsMigration = migration is null;
                var needsExplanation = aiExplanation is null;
                var migrationTask = migration is null ? TryGenerateMigrationAsync() : Task.FromResult<MigrationResult?>(migration);
                var explanationTask = aiExplanation is null ? TryExplainImpactAsync() : Task.FromResult<string?>(aiExplanation);
                await Task.WhenAll(migrationTask, explanationTask);

                migration = migrationTask.Result;
                aiExplanation = explanationTask.Result;

                // Başarısız çağrının payı iade edilir: kullanıcı sonuç alamadığı iş için bütçe kaybetmesin.
                var attempted = (needsMigration ? 1 : 0) + (needsExplanation ? 1 : 0);
                var failed = (needsMigration && migration is null ? 1 : 0) + (needsExplanation && aiExplanation is null ? 1 : 0);
                if (failed > 0)
                    await _quota.RefundAsync(userId, ImpactExplanationTokenEstimate * failed / attempted, ct);

                var changed = false;
                if (migration is not null && cr.MigrationResultJson is null)
                {
                    cr.MigrationResultJson = JsonSerializer.Serialize(migration, SchemaJsonOptions);
                    changed = true;
                }
                if (aiExplanation is not null && cr.AiExplanation is null)
                {
                    cr.AiExplanation = aiExplanation;
                    changed = true;
                }
                if (changed) await _db.SaveChangesAsync(ct);
            }
        }

        var teamSize = EffectiveTeamSize(cr, await _authority.CountVotingMembersAsync(cr.ProjectId, ct));
        var required = ChangeRequestApprovalPolicy.EffectiveRequiredApprovals(cr.RiskLevel, teamSize);

        var voterIds = cr.Approvals.Where(a => a.UserId != null).Select(a => a.UserId!).Distinct().ToList();
        var names = await _identity.GetUserNamesAsync(voterIds, ct);

        var detail = new ChangeRequestDetail(
            cr.Id, cr.ProjectId, cr.BranchId, branch.Name, cr.Title, cr.Status, cr.RiskLevel, cr.CreatedByUserId, cr.CreatedAt, cr.ResolvedAt,
            new VersionView(headVersion.Id, headVersion.Version, headVersion.TableCount, headVersion.CreatedAt),
            baseVersion is null ? null : new VersionView(baseVersion.Id, baseVersion.Version, baseVersion.TableCount, baseVersion.CreatedAt),
            impact, aiExplanation, migration,
            cr.TestRunAt is null ? null : new TestRunView(cr.TestRunSupported, cr.TestRunSuccess, cr.TestRunMessage, cr.TestRunFailedStatement, cr.TestRunDurationMs, cr.TestRunAt),
            required,
            cr.Approvals.Count(a => a.Decision == ApprovalDecision.Approved),
            cr.Approvals.Count(a => a.Decision == ApprovalDecision.Rejected),
            cr.Approvals.Select(a => new ApprovalView(
                a.Id, a.UserId,
                // Hesabı silinen oy verenin satırı kalır, adı "Deleted user" olur.
                a.UserId is not null && names.TryGetValue(a.UserId, out var name) ? name : "Deleted user",
                a.Decision, a.Comment, a.CreatedAt)).ToList());
        return ModuleOutcome<ChangeRequestDetail>.Ok(detail);
    }

    public async Task<ModuleOutcome<IReadOnlyList<ChangeRequestListItem>>> ListForProjectAsync(string userId, string projectId, CancellationToken ct = default)
    {
        if (!await _authority.CanViewAsync(projectId, userId, ct))
            return ModuleOutcome<IReadOnlyList<ChangeRequestListItem>>.NotFound(NotFoundProject);

        var entities = await _db.ChangeRequests.AsNoTracking()
            .Include(c => c.Approvals)
            .Where(c => c.ProjectId == projectId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        var teamSize = await _authority.CountVotingMembersAsync(projectId, ct);
        var branches = await _branches.GetManyAsync(entities.Select(c => c.BranchId).Distinct().ToList(), ct);
        var versions = await _branches.GetVersionHeadersAsync(entities.Select(c => c.HeadVersionId).Distinct().ToList(), ct);

        IReadOnlyList<ChangeRequestListItem> list = entities.Select(c => new ChangeRequestListItem(
            c.Id, c.Title, c.Status, c.RiskLevel, branches[c.BranchId].Name, versions[c.HeadVersionId].TableCount,
            c.CreatedAt, c.ResolvedAt,
            c.Approvals.Count(a => a.Decision == ApprovalDecision.Approved),
            ChangeRequestApprovalPolicy.EffectiveRequiredApprovals(c.RiskLevel, EffectiveTeamSize(c, teamSize)))).ToList();
        return ModuleOutcome<IReadOnlyList<ChangeRequestListItem>>.Ok(list);
    }

    /// <summary>
    /// Oylama TEK işlemde ve CR satırı kilitli yürür (<c>SELECT ... FOR UPDATE</c>): iki onaylayıcı, onay ile red ya da
    /// aynı kullanıcının çift gönderimi sırayla işlenir, her biri kararı güncel durumdan verir. Kilitsiz sürümde red,
    /// az önce onaylanan isteği "Rejected" yapabiliyordu (çözülmüş isteğin yeniden çözülmesi).
    /// </summary>
    public async Task<ModuleOutcome<DecisionResult>> DecideAsync(string userId, string id, ApprovalDecision decision, string? comment, CancellationToken ct = default)
    {
        var projectId = await _db.ChangeRequests.AsNoTracking().Where(c => c.Id == id).Select(c => c.ProjectId).FirstOrDefaultAsync(ct);
        if (projectId is null) return ModuleOutcome<DecisionResult>.NotFound(NotFoundCr);

        if (!await _authority.CanEditAsync(projectId, userId, ct))
            return ModuleOutcome<DecisionResult>.NotFound(NotFoundCr);

        var liveTeamSize = await _authority.CountVotingMembersAsync(projectId, ct);

        try
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                await using var tx = await _db.Database.BeginTransactionAsync(ct);

                if (_db.Database.IsNpgsql())
                {
                    await _db.Database.ExecuteSqlInterpolatedAsync(
                        $"""SELECT 1 FROM governance."ChangeRequests" WHERE "Id" = {id} FOR UPDATE""", ct);
                }

                var cr = await _db.ChangeRequests.Include(c => c.Approvals).FirstOrDefaultAsync(c => c.Id == id, ct);
                if (cr is null) return ModuleOutcome<DecisionResult>.NotFound(NotFoundCr);

                var teamSize = EffectiveTeamSize(cr, liveTeamSize);
                var evaluation = ChangeRequestApprovalPolicy.EvaluateVote(
                    cr.RiskLevel, cr.Status, cr.CreatedByUserId, userId, decision,
                    cr.Approvals.Count(a => a.Decision == ApprovalDecision.Approved),
                    cr.Approvals.Any(a => a.UserId == userId), teamSize);

                switch (evaluation.Outcome)
                {
                    case ChangeRequestApprovalPolicy.VoteOutcome.RejectedAlreadyResolved:
                        return ModuleOutcome<DecisionResult>.Conflict("This change request has already been decided.");
                    case ChangeRequestApprovalPolicy.VoteOutcome.RejectedAlreadyVoted:
                        return ModuleOutcome<DecisionResult>.Conflict("You have already voted on this change request.");
                    case ChangeRequestApprovalPolicy.VoteOutcome.RejectedSelfApprovalNotAllowed:
                        return ModuleOutcome<DecisionResult>.Forbidden();
                }

                var now = DateTime.UtcNow;
                _db.ChangeRequestApprovals.Add(new ChangeRequestApproval
                {
                    ChangeRequestId = cr.Id,
                    UserId = userId,
                    Decision = decision,
                    Comment = comment,
                    CreatedAt = now,
                });

                _db.ChangeRequestAuditLogs.Add(new ChangeRequestAuditLog
                {
                    ChangeRequestId = cr.Id,
                    ProjectId = cr.ProjectId,
                    ChangeRequestTitle = cr.Title,
                    Action = decision == ApprovalDecision.Approved ? ChangeRequestAuditAction.Approved : ChangeRequestAuditAction.Rejected,
                    ActorUserId = userId,
                    Details = comment,
                    CreatedAt = now,
                });

                if (evaluation.NewStatus is not null)
                {
                    cr.Status = evaluation.NewStatus.Value;
                    cr.ResolvedAt = now;
                }

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return ModuleOutcome<DecisionResult>.Ok(new DecisionResult(cr.Id, cr.Status));
            });
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            // (CR, kullanıcı) benzersiz indeksi son savunma: kilit yolunu atlayan bir yazım olsa bile çift oy girmez.
            return ModuleOutcome<DecisionResult>.Conflict("You have already voted on this change request.");
        }
    }

    public async Task<ModuleOutcome<TestRunResult>> RunTestsAsync(string userId, string id, CancellationToken ct = default)
    {
        var cr = await _db.ChangeRequests.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cr is null) return ModuleOutcome<TestRunResult>.NotFound(NotFoundCr);

        if (!await _authority.CanEditAsync(cr.ProjectId, userId, ct))
            return ModuleOutcome<TestRunResult>.NotFound(NotFoundCr);
        var project = await _projects.GetHeaderAsync(cr.ProjectId, ct)
            ?? throw new InvalidOperationException("Project disappeared after the access check.");

        var headVersion = await _branches.GetVersionAsync(cr.HeadVersionId, ct)
            ?? throw new InvalidOperationException("Head version is missing.");
        var result = await _testRunner.RunAsync(ParseSchema(headVersion.SchemaJson), EngineOf(project.DbType), ct);

        // Son koşu CR satırında tutulur: koşu geçmişi istenen bir özellik değil, her zaman "en son sonuç" okunuyor.
        cr.TestRunSupported = result.Supported;
        cr.TestRunSuccess = result.Success;
        cr.TestRunMessage = result.EngineMessage;
        cr.TestRunFailedStatement = result.FailedStatement;
        cr.TestRunDurationMs = result.DurationMs;
        cr.TestRunAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return ModuleOutcome<TestRunResult>.Ok(result);
    }

    public async Task<ModuleOutcome<AffectedCodeScan>> ScanAffectedCodeAsync(string userId, string id, IReadOnlyList<AffectedCodeFile> files, CancellationToken ct = default)
    {
        var cr = await _db.ChangeRequests.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cr is null) return ModuleOutcome<AffectedCodeScan>.NotFound(NotFoundCr);

        if (!await _authority.CanViewAsync(cr.ProjectId, userId, ct))
            return ModuleOutcome<AffectedCodeScan>.NotFound(NotFoundCr);

        var impact = JsonSerializer.Deserialize<ImpactReport>(cr.ImpactReportJson)
            ?? throw new InvalidOperationException("Stored ImpactReport could not be deserialized.");

        var identifiers = AffectedCodeScanner.ExtractCandidateIdentifiers(impact);
        var byName = files
            .Where(f => !string.IsNullOrWhiteSpace(f.FileName))
            .ToDictionary(f => f.FileName, f => f.Content);
        var matches = AffectedCodeScanner.Scan(identifiers, byName);

        return ModuleOutcome<AffectedCodeScan>.Ok(new AffectedCodeScan(identifiers, matches, byName.Count));
    }

    private static bool IsUniqueViolation(Exception ex) =>
        ex is DbUpdateException { InnerException: DbException { SqlState: "23505" } or DbException { ErrorCode: 19 } };
}
