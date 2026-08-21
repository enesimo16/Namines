using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;

namespace Namines.API.Controllers;

public class QuickChangeRequestRequest
{
    public string ProjectId { get; set; } = string.Empty;
    public string SchemaJson { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Message { get; set; }
}

public class DecideChangeRequestRequest
{
    public ApprovalDecision Decision { get; set; }
    public string? Comment { get; set; }
}

public class CodeFileInput
{
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class ScanAffectedCodeRequest
{
    public List<CodeFileInput> Files { get; set; } = new();
}

public class SetAutoApproveSafeRequest
{
    public bool Enabled { get; set; }
}

/// <summary>
/// "Database PR" — new-phase/29-DATABASE-CHANGE-REVIEW.md. G8 (SchemaImpactAnalyzer),
/// G9 (risk sınıflandırması) ve G10'un (Branch/SchemaVersion) hepsini birleştiren yüzey.
///
/// <see cref="CreateQuick"/> bilinçli bir kapsam sadeleştirmesi: doc, kullanıcının önce
/// bir branch açıp üzerinde çalıştığı tam bir Git-benzeri akış varsayıyor. Faz 0'da
/// frontend'de böyle bir branch-yönetim UI'ı yok (canvas'taki BranchControlPanel hâlâ
/// TAMAMEN istemci-taraflı eski model — bkz. 30-SERVER-SIDE-BRANCHING.md §1, bağlanması
/// G17'nin işi). Bu yüzden "Request Review" tek bir aksiyon: proje için sunucu-taraflı
/// bir "main" branch'i yoksa oluşturur, mevcut canvas şemasını yeni bir SchemaVersion
/// olarak commit'ler, ve bir önceki versiyona karşı otomatik CR açar. Kullanıcı hiçbir
/// branch kavramıyla uğraşmaz — ama altyapı G10'un gerçek tabloları üzerinde çalışır.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChangeRequestController : ControllerBase
{
    private readonly AuthDbContext _context;
    private readonly IMigrationService _migrationService;
    private readonly IBranchTestRunner _testRunner;
    private readonly IAIService _aiService;

    // BranchController'daki aynı gerekçe: frontend enum'ları string yazıyor,
    // dönüştürücü olmadan Deserialize<DatabaseSchema> patlıyor.
    private static readonly JsonSerializerOptions SchemaJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ChangeRequestController(AuthDbContext context, IMigrationService migrationService, IBranchTestRunner testRunner, IAIService aiService)
    {
        _context = context;
        _migrationService = migrationService;
        _testRunner = testRunner;
        _aiService = aiService;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpPost("quick")]
    public async Task<IActionResult> CreateQuick([FromBody] QuickChangeRequestRequest request)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.ProjectId) || string.IsNullOrWhiteSpace(request.SchemaJson))
            return BadRequest(new { error = "ProjectId ve SchemaJson zorunludur." });

        // 05 §6: şema yazma/CR açma → Editor ve üstü.
        if (!await _context.CanEditAsync(request.ProjectId, userId))
            return NotFound(new { error = "Proje bulunamadı veya bu kullanıcıya ait değil." });
        var project = await _context.CloudProjects.FirstAsync(p => p.Id == request.ProjectId);

        DatabaseSchema headSchema;
        try
        {
            headSchema = JsonSerializer.Deserialize<DatabaseSchema>(request.SchemaJson,
                SchemaJsonOptions) ?? new DatabaseSchema();
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "SchemaJson geçerli bir DatabaseSchema değil." });
        }

        // Yarışa karşı sertleştirilmiş tek kopya — bkz. BranchProvisioning.
        var branch = await _context.GetOrCreateDefaultBranchAsync(request.ProjectId, userId);

        // Versiyon numarası oku-sonra-yaz yarışı: (BranchId, Version) unique. Aynı branch'te
        // iki kişi aynı anda "Request Review" derse ikisi de aynı numarayı hesaplar ve
        // ikincisi unique index'e çarpar. 500 vermek yerine yeniden okuyup bir sonraki
        // numarayla tekrar dener — kullanıcının commit'i kaybolmaz.
        SchemaVersion headVersion = null!;
        SchemaVersion? previousVersion = null;
        DatabaseSchema baseSchema = new();

        const int maxVersionAttempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            previousVersion = await _context.SchemaVersions
                .Where(v => v.BranchId == branch.Id)
                .OrderByDescending(v => v.Version)
                .FirstOrDefaultAsync();

            baseSchema = previousVersion is not null
                ? JsonSerializer.Deserialize<DatabaseSchema>(previousVersion.SchemaJson,
                    SchemaJsonOptions) ?? new DatabaseSchema()
                : new DatabaseSchema();

            headVersion = new SchemaVersion
            {
                ProjectId = request.ProjectId,
                BranchId = branch.Id,
                Version = (previousVersion?.Version ?? 0) + 1,
                Checksum = System.Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.SchemaJson))
                ).ToLowerInvariant(),
                SchemaJson = request.SchemaJson,
                Message = request.Message,
                TableCount = (short)headSchema.Tables.Count,
                AuthorUserId = userId,
                CreatedAt = DateTime.UtcNow
            };
            await _context.SchemaVersions.AddAsync(headVersion);

            try
            {
                await _context.SaveChangesAsync();
                break;
            }
            catch (DbUpdateException) when (attempt < maxVersionAttempts)
            {
                _context.Entry(headVersion).State = EntityState.Detached;
            }
        }

        var engine = Enum.TryParse<DatabaseType>(project.DbType, ignoreCase: true, out var parsedEngine)
            ? parsedEngine
            : DatabaseType.PostgreSQL;

        var impact = SchemaImpactAnalyzer.Analyze(baseSchema, headSchema, engine);

        // G16 — new-phase/29-DATABASE-CHANGE-REVIEW.md §3: Safe risk, proje bunu açıkça
        // opt-in etmişse insan onayı beklemeden Approved olarak açılır. Diğer her risk
        // seviyesi (Risky dahil) her zaman PendingReview'dan başlar.
        var autoApprove = impact.OverallRisk == RiskLevel.Safe && project.AutoApproveSafeChanges;
        var now = DateTime.UtcNow;

        var changeRequest = new ChangeRequest
        {
            ProjectId = request.ProjectId,
            BranchId = branch.Id,
            BaseVersionId = previousVersion?.Id,
            HeadVersionId = headVersion.Id,
            Title = request.Title ?? $"Schema update — v{headVersion.Version}",
            Status = autoApprove ? ChangeRequestStatus.Approved : ChangeRequestStatus.PendingReview,
            RiskLevel = impact.OverallRisk,
            ImpactReportJson = JsonSerializer.Serialize(impact),
            CreatedByUserId = userId,
            CreatedAt = now,
            ResolvedAt = autoApprove ? now : null
        };
        await _context.ChangeRequests.AddAsync(changeRequest);

        await _context.ChangeRequestAuditLogs.AddAsync(new ChangeRequestAuditLog
        {
            ChangeRequestId = changeRequest.Id,
            Action = ChangeRequestAuditAction.Created,
            ActorUserId = userId,
            Details = $"Risk={impact.OverallRisk}",
            CreatedAt = now
        });

        if (autoApprove)
        {
            await _context.ChangeRequestAuditLogs.AddAsync(new ChangeRequestAuditLog
            {
                ChangeRequestId = changeRequest.Id,
                Action = ChangeRequestAuditAction.AutoApproved,
                ActorUserId = null, // sistem-güdümlü — hiçbir insan onaylamadı
                Details = "Safe risk + project.AutoApproveSafeChanges enabled",
                // GetAuditLog'un tek sıralama anahtarı CreatedAt; Postgres eşit değerlerde
                // sıra garanti etmez, o yüzden "Created"tan sonraya düşmesi ZORUNLU.
                // 1 tick (100ns) YETMEZ: timestamptz mikrosaniye çözünürlüğünde saklanır,
                // sub-mikrosaniye fark yazarken kırpılır ve iki satır aynı değere düşerdi
                // (gerçek DB'ye karşı doğrulandı). 10 tick = tam 1 mikrosaniye.
                CreatedAt = now.AddTicks(10)
            });
        }

        await _context.SaveChangesAsync();

        return Ok(new { changeRequest.Id });
    }

    /// <summary>G16 — proje sahibi, Safe risk'li değişikliklerin insan onayı beklemeden
    /// otomatik onaylanıp onaylanmayacağını değiştirir.</summary>
    [HttpPut("project/{projectId}/auto-approve-safe")]
    public async Task<IActionResult> SetAutoApproveSafe(string projectId, [FromBody] SetAutoApproveSafeRequest request)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        // Onay zorunluluğunu GEVŞETEN bir ayar — Editor'a bırakılmaz, Admin/Owner gerekir.
        if (!await _context.CanManageMembersAsync(projectId, userId))
            return NotFound(new { error = "Proje bulunamadı veya bu kullanıcıya ait değil." });
        var project = await _context.CloudProjects.FirstAsync(p => p.Id == projectId);

        project.AutoApproveSafeChanges = request.Enabled;
        await _context.SaveChangesAsync();

        return Ok(new { project.Id, project.AutoApproveSafeChanges });
    }

    /// <summary>G16 — bir CR'ın yaşam döngüsü boyunca durum geçişlerinin (kim/ne zaman/neden)
    /// append-only kaydı. İnsan oyları için bkz. GetDetail'in Approvals alanı — bu ayrı
    /// uç nokta, otomatik onay gibi insan oyu olmayan olayları da aynı zaman çizelgesinde
    /// gösterir.</summary>
    [HttpGet("{id}/audit")]
    public async Task<IActionResult> GetAuditLog(string id)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var cr = await _context.ChangeRequests.FirstOrDefaultAsync(c => c.Id == id);
        if (cr is null) return NotFound(new { error = "Change request bulunamadı." });

        // 05 §6: okuma için Viewer dahil her org üyesi yeterli.
        if (!await _context.CanViewAsync(cr.ProjectId, userId))
            return NotFound(new { error = "Change request bulunamadı." });
        var project = await _context.CloudProjects.FirstAsync(p => p.Id == cr.ProjectId);

        var entries = await _context.ChangeRequestAuditLogs
            .Include(a => a.ActorUser)
            .Where(a => a.ChangeRequestId == id)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new
            {
                a.Id,
                a.Action,
                a.ActorUserId,
                ActorUsername = a.ActorUser != null ? a.ActorUser.UserName : null,
                a.Details,
                a.CreatedAt
            })
            .ToListAsync();

        return Ok(entries);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDetail(string id)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var cr = await _context.ChangeRequests
            .Include(c => c.Branch)
            .Include(c => c.HeadVersion)
            .Include(c => c.BaseVersion)
            .Include(c => c.Approvals).ThenInclude(a => a.User)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (cr is null) return NotFound(new { error = "Change request bulunamadı." });

        // 05 §6: okuma için Viewer dahil her org üyesi yeterli.
        if (!await _context.CanViewAsync(cr.ProjectId, userId))
            return NotFound(new { error = "Change request bulunamadı." });
        var project = await _context.CloudProjects.FirstAsync(p => p.Id == cr.ProjectId);

        var impact = JsonSerializer.Deserialize<ImpactReport>(cr.ImpactReportJson);

        var headSchema = JsonSerializer.Deserialize<DatabaseSchema>(cr.HeadVersion.SchemaJson,
            SchemaJsonOptions) ?? new DatabaseSchema();
        var baseSchema = cr.BaseVersion is not null
            ? JsonSerializer.Deserialize<DatabaseSchema>(cr.BaseVersion.SchemaJson,
                SchemaJsonOptions) ?? new DatabaseSchema()
            : new DatabaseSchema();

        var engine = Enum.TryParse<DatabaseType>(project.DbType, ignoreCase: true, out var parsedEngine)
            ? parsedEngine
            : DatabaseType.PostgreSQL;

        // SQL sekmesi: elimizdeki tek gerçek "diff'ten kod üretimi" motoru EF Core migration
        // üreticisi — ham ALTER-diff DDL üreten ayrı bir motor henüz yok (bilinçli, bkz. sınıf
        // yorumu). Deterministik olduğu için burada yeniden hesaplanıyor, ayrıca saklanmıyor.
        //
        // Migration üretimi ve G15'in AI Impact Explainer'ı birbirinden bağımsız iki ayrı AI
        // çağrısı — art arda await etmek yerine paralel çalıştırılır, sayfa yükleme süresi
        // ikisinin TOPLAMI değil daha YAVAŞ olanı kadar sürer. Her biri kendi hatasını yutar
        // (AI anahtarı/servis yoksa ilgili sekme boş görünür, diğerleri etkilenmez).
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

        var migrationTask = TryGenerateMigrationAsync();
        var explanationTask = TryExplainImpactAsync();
        await Task.WhenAll(migrationTask, explanationTask);
        var migration = migrationTask.Result;
        var aiExplanation = explanationTask.Result;

        // UI'ın gösterdiği sayı, Decide'ın uyguladığı sayıyla AYNI olmalı — yoksa
        // "1/2 onay" yazarken CR onaylanmış görünür (kafa karıştırıcı ve güvensiz).
        var teamSize = await _context.CountVotingMembersAsync(cr.ProjectId);
        var required = ChangeRequestApprovalPolicy.EffectiveRequiredApprovals(cr.RiskLevel, teamSize);
        var approvedCount = cr.Approvals.Count(a => a.Decision == ApprovalDecision.Approved);
        var rejectedCount = cr.Approvals.Count(a => a.Decision == ApprovalDecision.Rejected);

        return Ok(new
        {
            cr.Id,
            cr.ProjectId,
            cr.BranchId,
            BranchName = cr.Branch.Name,
            cr.Title,
            cr.Status,
            cr.RiskLevel,
            cr.CreatedByUserId,
            cr.CreatedAt,
            cr.ResolvedAt,
            HeadVersion = new { cr.HeadVersion.Id, cr.HeadVersion.Version, cr.HeadVersion.TableCount, cr.HeadVersion.CreatedAt },
            BaseVersion = cr.BaseVersion is null ? null : new { cr.BaseVersion.Id, cr.BaseVersion.Version, cr.BaseVersion.TableCount, cr.BaseVersion.CreatedAt },
            Impact = impact,
            AiExplanation = aiExplanation,
            Migration = migration,
            TestRun = cr.TestRunAt is null ? null : new
            {
                cr.TestRunSupported,
                cr.TestRunSuccess,
                cr.TestRunMessage,
                cr.TestRunFailedStatement,
                cr.TestRunDurationMs,
                cr.TestRunAt
            },
            RequiredApprovals = required,
            ApprovedCount = approvedCount,
            RejectedCount = rejectedCount,
            Approvals = cr.Approvals.Select(a => new
            {
                a.Id,
                a.UserId,
                Username = a.User.UserName,
                a.Decision,
                a.Comment,
                a.CreatedAt
            })
        });
    }

    [HttpGet("project/{projectId}")]
    public async Task<IActionResult> ListForProject(string projectId)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!await _context.CanViewAsync(projectId, userId))
            return NotFound(new { error = "Proje bulunamadı veya bu kullanıcıya ait değil." });

        var entities = await _context.ChangeRequests
            .Include(c => c.Branch)
            .Include(c => c.HeadVersion)
            .Include(c => c.Approvals)
            .Where(c => c.ProjectId == projectId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        var listTeamSize = await _context.CountVotingMembersAsync(projectId);

        // ChangeRequestApprovalPolicy.EffectiveRequiredApprovals bir SQL çevirisi olmayan saf C#
        // fonksiyonu — EF'in .Select() projeksiyonunda client-evaluation'a izin verilmediği
        // için önce materialize edilip sonra bellek içinde eşleniyor.
        var list = entities.Select(c => new
        {
            c.Id,
            c.Title,
            c.Status,
            c.RiskLevel,
            BranchName = c.Branch.Name,
            TableCount = c.HeadVersion.TableCount,
            c.CreatedAt,
            c.ResolvedAt,
            ApprovedCount = c.Approvals.Count(a => a.Decision == ApprovalDecision.Approved),
            RequiredApprovals = ChangeRequestApprovalPolicy.EffectiveRequiredApprovals(c.RiskLevel, listTeamSize)
        });

        return Ok(list);
    }

    [HttpPost("{id}/decide")]
    public async Task<IActionResult> Decide(string id, [FromBody] DecideChangeRequestRequest request)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var cr = await _context.ChangeRequests
            .Include(c => c.Approvals)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (cr is null) return NotFound(new { error = "Change request bulunamadı." });

        // Oylama bir YAZMA eylemi — 05 §6'da viewer "yorum yapabilir" ama onaylayamaz.
        // Bu kontrol sayesinde artık org'un BAŞKA bir Editor/Admin üyesi de buraya
        // ulaşabiliyor; Destructive/Breaking'in "2 farklı kişi" kuralı ancak böyle
        // uygulanabilir hâle geliyor (önceden yalnızca proje sahibi erişebildiği için
        // kural matematiksel olarak imkânsızdı).
        if (!await _context.CanEditAsync(cr.ProjectId, userId))
            return NotFound(new { error = "Change request bulunamadı." });

        var approvedCountBefore = cr.Approvals.Count(a => a.Decision == ApprovalDecision.Approved);
        var alreadyVoted = cr.Approvals.Any(a => a.UserId == userId);

        // Onay kuralı ekip büyüklüğüne uyarlanır: 3+ kişide 2 onay, 2 kişide 1 onay,
        // tek kişide yazarın kendi onayı (aksi hâlde CR sonsuza kadar kilitlenirdi).
        var teamSize = await _context.CountVotingMembersAsync(cr.ProjectId);

        var evaluation = ChangeRequestApprovalPolicy.EvaluateVote(
            cr.RiskLevel, cr.Status, cr.CreatedByUserId, userId, request.Decision,
            approvedCountBefore, alreadyVoted, teamSize);

        switch (evaluation.Outcome)
        {
            case ChangeRequestApprovalPolicy.VoteOutcome.RejectedAlreadyResolved:
                return Conflict(new { error = "Bu change request zaten karara bağlanmış." });
            case ChangeRequestApprovalPolicy.VoteOutcome.RejectedAlreadyVoted:
                return Conflict(new { error = "Bu change request için zaten oy kullandınız." });
            case ChangeRequestApprovalPolicy.VoteOutcome.RejectedSelfApprovalNotAllowed:
                return Forbid();
        }

        var approval = new ChangeRequestApproval
        {
            ChangeRequestId = cr.Id,
            UserId = userId,
            Decision = request.Decision,
            Comment = request.Comment,
            CreatedAt = DateTime.UtcNow
        };
        await _context.ChangeRequestApprovals.AddAsync(approval);

        // Audit kaydı, oyun CR'ın DURUMUNU değiştirip değiştirmediğinden BAĞIMSIZ yazılır.
        // Önceden bu blok `if (NewStatus is not null)` içindeydi: Destructive/Breaking bir
        // CR'da 2 onay gerektiği için ilk onaylayan durumu değiştirmiyor, dolayısıyla
        // audit'e hiç girmiyordu — tam da iki-kişi kuralının uygulandığı en riskli
        // kategoride onaylayanların yarısı görünmez oluyordu.
        await _context.ChangeRequestAuditLogs.AddAsync(new ChangeRequestAuditLog
        {
            ChangeRequestId = cr.Id,
            Action = request.Decision == ApprovalDecision.Approved
                ? ChangeRequestAuditAction.Approved
                : ChangeRequestAuditAction.Rejected,
            ActorUserId = userId,
            Details = request.Comment,
            CreatedAt = DateTime.UtcNow
        });

        if (evaluation.NewStatus is not null)
        {
            cr.Status = evaluation.NewStatus.Value;
            cr.ResolvedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        return Ok(new { cr.Id, cr.Status });
    }

    /// <summary>
    /// "Run Tests" — new-phase/29-DATABASE-CHANGE-REVIEW.md §4. Impact Analysis'in
    /// TAHMİN ettiğini burada gerçek bir ephemeral motor container'ında ÇALIŞTIRIP kanıtlıyoruz.
    /// Senkron: container açılışı ~5-20sn sürebilir, MVP için bir job kuyruğu kurmuyoruz
    /// (bkz. new-phase/30-SERVER-SIDE-BRANCHING.md §3 Adım 3 — "ucuz, hızlı bir MVP köprüsü").
    /// </summary>
    // Her çağrı 2-2.5 GB'lık bir DB container'ı açıyor — sınırsız eşzamanlılık Docker
    // host'unu tüketir. DatabaseExecutorController/GatewayController ile aynı politika
    // (kullanıcı başına dakikada 5). Controller'ın TAMAMINA değil sadece bu pahalı
    // aksiyona uygulanır; liste/detay okumaları normal hızda kalmalı.
    [EnableRateLimiting("sensitive")]
    [HttpPost("{id}/run-tests")]
    public async Task<IActionResult> RunTests(string id, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var cr = await _context.ChangeRequests
            .Include(c => c.HeadVersion)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cr is null) return NotFound(new { error = "Change request bulunamadı." });

        // Gerçek container açıyor (pahalı) — Viewer tetikleyemez.
        if (!await _context.CanEditAsync(cr.ProjectId, userId, cancellationToken))
            return NotFound(new { error = "Change request bulunamadı." });
        var project = await _context.CloudProjects.FirstAsync(p => p.Id == cr.ProjectId, cancellationToken);

        var headSchema = JsonSerializer.Deserialize<DatabaseSchema>(cr.HeadVersion.SchemaJson, SchemaJsonOptions) ?? new DatabaseSchema();
        var engine = Enum.TryParse<DatabaseType>(project.DbType, ignoreCase: true, out var parsedEngine)
            ? parsedEngine
            : DatabaseType.PostgreSQL;

        var result = await _testRunner.RunAsync(headSchema, engine, cancellationToken);

        cr.TestRunSupported = result.Supported;
        cr.TestRunSuccess = result.Success;
        cr.TestRunMessage = result.EngineMessage;
        cr.TestRunFailedStatement = result.FailedStatement;
        cr.TestRunDurationMs = result.DurationMs;
        cr.TestRunAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// G13 — "Etkilenen API/UI statik tahmini" (new-phase/28-IMPACT-ANALYSIS-ENGINE.md §5).
    /// Kullanıcının yapıştırdığı/yüklediği dosyaları, bu CR'ın ImpactReport'undaki değişen
    /// tablo/kolon adlarına karşı tarar. TAHMİNDİR — kesin değil (bkz. AffectedCodeScanner
    /// sınıf yorumu). Kalıcı değil: sonuç saklanmaz, her çağrıda yeniden hesaplanır.
    /// </summary>
    [HttpPost("{id}/scan-affected-code")]
    public async Task<IActionResult> ScanAffectedCode(string id, [FromBody] ScanAffectedCodeRequest request)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var cr = await _context.ChangeRequests.FirstOrDefaultAsync(c => c.Id == id);
        if (cr is null) return NotFound(new { error = "Change request bulunamadı." });

        // 05 §6: okuma için Viewer dahil her org üyesi yeterli.
        if (!await _context.CanViewAsync(cr.ProjectId, userId))
            return NotFound(new { error = "Change request bulunamadı." });
        var project = await _context.CloudProjects.FirstAsync(p => p.Id == cr.ProjectId);

        var impact = JsonSerializer.Deserialize<ImpactReport>(cr.ImpactReportJson)
            ?? throw new InvalidOperationException("Stored ImpactReport could not be deserialized.");

        var identifiers = AffectedCodeScanner.ExtractCandidateIdentifiers(impact);
        var files = request.Files
            .Where(f => !string.IsNullOrWhiteSpace(f.FileName))
            .ToDictionary(f => f.FileName, f => f.Content);
        var matches = AffectedCodeScanner.Scan(identifiers, files);

        return Ok(new { candidateIdentifiers = identifiers, matches, filesScanned = files.Count });
    }
}
