using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.BuildingBlocks.Application;
using Namines.Infrastructure.Data;

namespace Namines.API.Controllers;

public class CreateBranchRequest
{
    public string ProjectId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ParentBranchId { get; set; }
    public int? ForkedFromVersion { get; set; }
    public bool IsDefault { get; set; }
}

public class CommitSchemaVersionRequest
{
    public string SchemaJson { get; set; } = string.Empty;
    public string? Message { get; set; }
}

/// <summary>
/// Sunucu-otoriteli branch/şema-versiyon uç noktaları — new-phase/30-SERVER-SIDE-BRANCHING.md
/// §3 Adım 1'in API yüzeyi. <see cref="GetOrCreateDefaultBranch"/> Adım 2'nin köprüsü —
/// CanvasHub'ın (realtime) roomId'si artık buradan gelen branch ID'sine eşlenebiliyor
/// (bkz. frontend hooks/useMultiplayer.ts, G17).
///
/// Mevcut AuthController deseniyle tutarlı: ayrı bir servis katmanı yok, doğrudan
/// AuthDbContext + sahiplik kontrolü (CloudProject.UserId == giriş yapan kullanıcı).
/// </summary>
public sealed record ProvisionBranchDatabaseRequest(DatabaseType Engine = DatabaseType.PostgreSQL);
public sealed record SeedBranchDatabaseRequest(int RowsPerTable = 25);

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BranchController : ControllerBase
{
    private readonly IBranchService _branches;
    private readonly IStudioBranches _versions;
    private readonly IBranchDatabaseProvisioner _databases;
    private readonly AuthDbContext _billing;

    // Frontend enum'ları (ör. ReferentialAction) JSON'a string olarak yazıyor
    // ("NoAction", "Cascade"...) — dönüştürücü olmadan Deserialize<DatabaseSchema>
    // bu alanlarda JsonException fırlatır.
    private static readonly JsonSerializerOptions SchemaJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public BranchController(IBranchService branches, IStudioBranches versions, IBranchDatabaseProvisioner databases, AuthDbContext billing)
    {
        _branches = branches;
        _versions = versions;
        _databases = databases;
        _billing = billing;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private IActionResult Fail<T>(ModuleOutcome<T> outcome) => outcome.Kind switch
    {
        ModuleOutcomeKind.NotFound => NotFound(new { error = outcome.Error }),
        ModuleOutcomeKind.Invalid => BadRequest(new { error = outcome.Error }),
        ModuleOutcomeKind.Conflict => Conflict(new { error = outcome.Error }),
        _ => StatusCode(500),
    };

    [HttpPost]
    public async Task<IActionResult> CreateBranch([FromBody] CreateBranchRequest request)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var outcome = await _branches.CreateAsync(userId, new CreateBranchCommand(
            request.ProjectId, request.Name, request.ParentBranchId, request.ForkedFromVersion, request.IsDefault));
        return outcome.IsOk ? Ok(outcome.Value) : Fail(outcome);
    }

    [HttpGet("project/{projectId}")]
    public async Task<IActionResult> ListBranches(string projectId)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var outcome = await _branches.ListAsync(userId, projectId);
        return outcome.IsOk ? Ok(outcome.Value) : Fail(outcome);
    }

    /// <summary>
    /// G17 — new-phase/30-SERVER-SIDE-BRANCHING.md §3 Adım 2: "CanvasHub'ın bugünkü
    /// roomId kavramı branch_id'ye eşlenir." Frontend, gerçek-zamanlı işbirliği odasının
    /// kimliği olarak rastgele bir string üretmek yerine bunu çağırıp projenin varsayılan
    /// ("main") branch'inin ID'sini oda kimliği olarak kullanır — aynı projenin aynı
    /// branch'i üzerinde çalışan iki kullanıcı otomatik olarak aynı odada buluşur.
    ///
    /// ChangeRequestController.CreateQuick'teki "yoksa oluştur" deseniyle aynı — burada
    /// da proje senkronize edildiğinde (POST /api/auth/sync) henüz bir Branch satırı
    /// açılmıyor, ilk gerçek ihtiyaç anında (ilk canlı işbirliği bağlantısı) oluşturuluyor.
    /// </summary>
    /// <summary>
    /// Varsayılan branch'i DÖNDÜRÜR — yoksa 404. <b>Hiçbir şey oluşturmaz.</b>
    ///
    /// <b>Bulunan açık (docs/last-review L-1):</b> bu uç bir <c>GET</c> olduğu hâlde
    /// satır OLUŞTURUYORDU, ve <c>GET</c>'ler CSRF korumasından muaf (muaf olmaları
    /// doğru — güvenli olduğu VARSAYILAN bir fiil). Oluşturma idempotent ve kullanıcının
    /// zaten sahip olduğu bir kaynak için olduğundan saldırganın kazanacağı bir şey
    /// yoktu, ama "GET yazmaz" kuralının istisnası olmak, bir sonraki değişikliğin o
    /// istisnayı büyütmesine zemin hazırlar.
    ///
    /// Oluşturma artık <see cref="EnsureDefaultBranch"/>'te (<c>POST</c>).
    /// </summary>
    [HttpGet("project/{projectId}/default")]
    public async Task<IActionResult> GetDefaultBranch(string projectId)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var outcome = await _branches.GetDefaultAsync(userId, projectId);
        if (!outcome.IsOk) return Fail(outcome);
        var branch = outcome.Value!;
        return Ok(new { branch.Id, branch.ProjectId, branch.Name });
    }

    /// <summary>
    /// Varsayılan branch'i döndürür, yoksa OLUŞTURUR. <c>POST</c>, çünkü yazabilir
    /// (docs/last-review L-1).
    ///
    /// Yanıt <c>200</c> (vardı) ya da <c>201</c> (oluşturuldu): çağıranın hangisinin
    /// olduğunu bilmesi, "ilk kez açıldı" durumunu loglamasını/ölçmesini mümkün kılıyor.
    /// </summary>
    [HttpPost("project/{projectId}/default")]
    public async Task<IActionResult> EnsureDefaultBranch(string projectId)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var outcome = await _branches.EnsureDefaultAsync(userId, projectId);
        if (!outcome.IsOk) return Fail(outcome);
        var (branch, created) = outcome.Value;
        var body = new { branch.Id, branch.ProjectId, branch.Name };

        return created ? StatusCode(StatusCodes.Status201Created, body) : Ok(body);
    }

    [HttpPost("{branchId}/versions")]
    [RequestSizeLimit(SchemaRequestLimits.SingleSchemaBytes)]
    public async Task<IActionResult> CommitSchemaVersion(string branchId, [FromBody] CommitSchemaVersionRequest request)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var outcome = await _branches.CommitAsync(userId, branchId, request.SchemaJson, request.Message);
        return outcome.IsOk ? Ok(outcome.Value) : Fail(outcome);
    }

    [HttpGet("{branchId}/versions")]
    public async Task<IActionResult> ListSchemaVersions(string branchId)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var outcome = await _branches.ListVersionsAsync(userId, branchId);
        return outcome.IsOk ? Ok(outcome.Value) : Fail(outcome);
    }

    [HttpGet("versions/{versionId}")]
    public async Task<IActionResult> GetSchemaVersion(string versionId)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var outcome = await _branches.GetVersionAsync(userId, versionId);
        if (!outcome.IsOk) return Fail(outcome);

        // Yanıtın tarihsel biçimi varlığın kendisiydi: boş navigasyon alanları da anahtar olarak vardı.
        var v = outcome.Value!;
        return Ok(new
        {
            v.Id,
            v.ProjectId,
            Project = (object?)null,
            v.BranchId,
            Branch = (object?)null,
            v.Version,
            v.Checksum,
            v.SchemaJson,
            v.Message,
            v.TableCount,
            v.AuthorUserId,
            AuthorUser = (object?)null,
            v.CreatedAt,
        });
    }

    // ── Branch veritabanı (06 §4) ────────────────────────────────────────────

    /// <summary>
    /// Branch için canlı bir veritabanı ayağa kaldırır ve son şema sürümünü uygular.
    ///
    /// Yetki sınırı bilinçli olarak <c>CanEditAsync</c> (Editor+), okuma değil:
    /// bu uç host'ta bir container başlatıyor ve kaynak tüketiyor. Salt-okuma
    /// yetkisi olan birinin bunu tetikleyebilmesi, izleyicilere sunucu kaynağı
    /// harcatmak demek olurdu.
    /// </summary>
    [HttpPost("{branchId}/database")]
    public async Task<IActionResult> ProvisionDatabase(string branchId, [FromBody] ProvisionBranchDatabaseRequest request, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var authorized = await _branches.AuthorizeBranchAsync(userId, branchId, cancellationToken);
        if (!authorized.IsOk) return Fail(authorized);

        var latest = await _versions.GetLatestVersionAsync(branchId, cancellationToken);

        // Şemasız bir branch için boş bir veritabanı açmak kaynak harcar ve
        // kullanıcıya hiçbir şey vermez.
        if (latest is null)
            return BadRequest(new { error = "This branch has no schema version yet." });

        // ── Plan kotası (06 §10) ─────────────────────────────────────────────
        // Kontrol, container AÇILMADAN önce: kotayı aştıktan sonra kapatmak, açılış
        // maliyetini zaten ödemek demek olurdu. Zaten açık olan bir veritabanını
        // yeniden istemek kotaya sayılmaz — sağlayıcı onu yeni açmıyor, mevcudu
        // döndürüyor.
        // Veritabanı ZATEN açık mı? Hem kota hem faturalama bu cevaba bağlı ve
        // ikisi AYNI cevabı kullanmak zorunda.
        var alreadyExisted = await _databases.GetAsync(branchId, cancellationToken) is not null;

        var quotaFailure = await CheckBranchDatabaseQuotaAsync(
            userId, branchId, alreadyExisted, cancellationToken);
        if (quotaFailure is not null) return quotaFailure;

        DatabaseSchema schema;
        try
        {
            schema = JsonSerializer.Deserialize<DatabaseSchema>(latest.SchemaJson, SchemaJsonOptions)
                     ?? new DatabaseSchema();
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "The branch schema could not be read." });
        }

        try
        {
            var database = await _databases.ProvisionAsync(branchId, schema, request.Engine, cancellationToken);

            // Kayıt, işlem GERÇEKTEN olduktan sonra: başarısız bir sağlamayı
            // faturalandırmak, olmamış bir şey için para almaktır.
            //
            // <b>Bulunan açık (docs/last-review M-10):</b> ölçüm KOŞULSUZ
            // yazılıyordu, oysa kota kapısı "zaten açık olan bir veritabanını
            // yeniden istemek kotaya sayılmaz" diyerek bilerek atlanıyordu. İki
            // taraf aynı olayı farklı sayıyordu: aynı sayfayı yenileyen bir
            // kullanıcı her seferinde YENİ bir branch veritabanı faturalanıyordu,
            // sağlayıcı hiçbir şey açmadığı hâlde. Kotanın gerekçesi doğruydu,
            // faturalamanın onu takip etmemesi yanlıştı.
            if (!alreadyExisted)
                await _billing.RecordAsync(userId, UsageResource.BranchDatabase, 1, branchId, cancellationToken);

            return Ok(Describe(database));
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(503, new { error = ex.Message });
        }
    }

    /// <summary>
    /// İki branch'i ORTAK ATALARINA bakarak birleştirmenin önizlemesi
    /// (github/03-COKLU-GELISTIRICI-MERGE.md).
    ///
    /// <b>Uygulamıyor, yalnızca söylüyor.</b> Önce "ne olacağını doğru
    /// anlatmak", sonra "yapmak" — tersi, yanlış bir birleştirmeyi kalıcı
    /// hâle getirir.
    ///
    /// Ortak ata için yeni bir alan gerekmedi: <see cref="Branch.ParentBranchId"/>
    /// ve <see cref="Branch.ForkedFromVersion"/> fork anında zaten yazılıyor.
    /// </summary>
    [HttpPost("{branchId}/merge/preview")]
    public async Task<IActionResult> MergePreview(string branchId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var preview = await _branches.PreviewMergeAsync(userId, branchId, cancellationToken);
        if (!preview.IsOk) return Fail(preview);
        var (sourceBranch, targetBranch, baseVersion, result) = preview.Value!;

        return Ok(new
        {
            sourceBranch,
            targetBranch,
            baseVersion,
            autoMerged = result.AutoMerged,
            // `kind` STRING olarak geçiyor: sunucu enum'unu istemcide bire bir
            // kopyalamak bu projede bir kez kırıldı. Tanınmayan bir değer
            // istemcide yanlış değil, bilinmeyen olur.
            conflicts = result.Conflicts.Select(c => new
            {
                c.Id,
                kind = c.Kind.ToString(),
                c.TableName,
                c.ColumnName,
                ours = c.OursValue,
                theirs = c.TheirsValue,
                c.Blocking,
                c.Explanation,
            }),
            blocked = result.Conflicts.Any(c => c.Blocking),
            merged = result.Merged,
        });
    }

    [HttpGet("{branchId}/database")]
    public async Task<IActionResult> GetDatabase(string branchId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var authorized = await _branches.AuthorizeBranchAsync(userId, branchId, cancellationToken);
        if (!authorized.IsOk) return Fail(authorized);

        var database = await _databases.GetAsync(branchId, cancellationToken);
        return database is null
            ? NotFound(new { error = "This branch has no live database." })
            : Ok(Describe(database));
    }

    /// <summary>
    /// Branch veritabanını örnek veriyle doldurur.
    ///
    /// Sağlamadan AYRI: şeması olan ama boş bir veritabanı pek işe yaramıyor, ama
    /// tohumlama da her zaman istenmez. Kararı kullanıcı verir.
    /// </summary>
    [HttpPost("{branchId}/database/seed")]
    public async Task<IActionResult> SeedDatabase(string branchId, [FromBody] SeedBranchDatabaseRequest request, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var authorized = await _branches.AuthorizeBranchAsync(userId, branchId, cancellationToken);
        if (!authorized.IsOk) return Fail(authorized);

        var latest = await _versions.GetLatestVersionAsync(branchId, cancellationToken);

        if (latest is null)
            return BadRequest(new { error = "This branch has no schema version yet." });

        DatabaseSchema schema;
        try
        {
            schema = JsonSerializer.Deserialize<DatabaseSchema>(latest.SchemaJson, SchemaJsonOptions)
                     ?? new DatabaseSchema();
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "The branch schema could not be read." });
        }

        // Üst sınır bilinçli: tek istekle branch veritabanını şişirmek kolay olmamalı.
        var rows = Math.Clamp(request.RowsPerTable, 1, 500);

        try
        {
            var inserted = await _databases.SeedAsync(branchId, schema, rows, cancellationToken);
            return Ok(new { rowsInserted = inserted });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{branchId}/database")]
    public async Task<IActionResult> DestroyDatabase(string branchId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var authorized = await _branches.AuthorizeBranchAsync(userId, branchId, cancellationToken);
        if (!authorized.IsOk) return Fail(authorized);

        await _databases.DestroyAsync(branchId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Kullanıcının açık branch veritabanı sayısını planının sınırıyla karşılaştırır.
    ///
    /// Sayım KULLANICININ branch'leriyle sınırlı: host'taki tüm container'ları saymak,
    /// başka bir kullanıcının açtığı veritabanı yüzünden kota dolmuş göstermek olurdu.
    /// </summary>
    /// <param name="alreadyExisted">
    /// Veritabanı çağrıdan ÖNCE zaten açık mıydı. Çağıran tarafından veriliyor,
    /// burada tekrar sorgulanmıyor: aynı soruya iki kez cevap aramak, kota ile
    /// faturalamanın farklı cevaplar görmesine kapı açardı (docs/last-review M-10).
    /// </param>
    private async Task<IActionResult?> CheckBranchDatabaseQuotaAsync(
        string userId, string branchId, bool alreadyExisted, CancellationToken ct)
    {
        if (alreadyExisted) return null;

        // Kotayı ölçüm motoru veriyor (22 §5): dahil miktar içindeyse geç, aşıldıysa
        // yalnızca aşırı kullanım AÇIKSA ve harcama tavanı dolmadıysa geç. Sabit bir
        // sınır, ödeme yapmaya HAZIR bir kullanıcıyı da durdururdu.
        var decision = await _billing.EvaluateAsync(userId, UsageResource.BranchDatabase, 1, ct);

        if (!decision.Allowed)
            return StatusCode(402, new
            {
                error = decision.Reason,
                overageCostUsd = decision.OverageCostUsd,
            });

        return null;
    }

    /// <summary>
    /// Bağlantı bilgisini yanıta çevirir. Parola DAHİL edilir — bu veritabanının
    /// tüm amacı geliştiricinin ona bağlanabilmesi; parolayı gizlemek özelliği
    /// kullanılamaz kılardı. Yetki kontrolü zaten yukarıda yapıldı ve veritabanı
    /// yalnızca sunucunun loopback'inde dinliyor.
    /// </summary>
    private static object Describe(BranchDatabase database) => new
    {
        database.BranchId,
        engine = database.Engine.ToString(),
        database.Host,
        database.Port,
        database.Database,
        database.Username,
        database.Password,
        database.ConnectionString,
        database.ExpiresAt,
    };
}
