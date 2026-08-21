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
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
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
    private readonly AuthDbContext _context;
    private readonly IBranchDatabaseProvisioner _databases;

    // Frontend enum'ları (ör. ReferentialAction) JSON'a string olarak yazıyor
    // ("NoAction", "Cascade"...) — dönüştürücü olmadan Deserialize<DatabaseSchema>
    // bu alanlarda JsonException fırlatır.
    private static readonly JsonSerializerOptions SchemaJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public BranchController(AuthDbContext context, IBranchDatabaseProvisioner databases)
    {
        _context = context;
        _databases = databases;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // 05 §6: yetki sınırı artık tek sahip değil, organizasyon üyeliği (bkz. OrgAccess).
    // Branch/versiyon uçları şema YAZAR — Editor ve üstü gerekir.
    private async Task<bool> UserOwnsProjectAsync(string projectId, string userId) =>
        await _context.CanEditAsync(projectId, userId);

    [HttpPost]
    public async Task<IActionResult> CreateBranch([FromBody] CreateBranchRequest request)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.ProjectId) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "ProjectId ve Name zorunludur." });

        if (!await UserOwnsProjectAsync(request.ProjectId, userId))
            return NotFound(new { error = "Proje bulunamadı veya bu kullanıcıya ait değil." });

        var nameTaken = await _context.Branches
            .AnyAsync(b => b.ProjectId == request.ProjectId && b.Name == request.Name);
        if (nameTaken)
            return Conflict(new { error = $"'{request.Name}' adında bir branch bu projede zaten var." });

        if (request.ParentBranchId is not null)
        {
            var parentExists = await _context.Branches
                .AnyAsync(b => b.Id == request.ParentBranchId && b.ProjectId == request.ProjectId);
            if (!parentExists)
                return BadRequest(new { error = "ParentBranchId bu projede bulunamadı." });
        }

        var branch = new Branch
        {
            ProjectId = request.ProjectId,
            Name = request.Name,
            ParentBranchId = request.ParentBranchId,
            ForkedFromVersion = request.ForkedFromVersion,
            IsDefault = request.IsDefault,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        // Kısmi unique index (ProjectId WHERE IsDefault) DB seviyesinde tek default'u
        // garanti eder, ama kullanıcıya çıplak bir constraint-violation 500'ü göstermek
        // yerine önce eskisini açıkça devre dışı bırakıyoruz — aynı SaveChanges'te tutarlı.
        if (request.IsDefault)
        {
            var previousDefault = await _context.Branches
                .Where(b => b.ProjectId == request.ProjectId && b.IsDefault)
                .ToListAsync();
            foreach (var b in previousDefault) b.IsDefault = false;
        }

        await _context.Branches.AddAsync(branch);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            branch.Id,
            branch.ProjectId,
            branch.Name,
            branch.ParentBranchId,
            branch.ForkedFromVersion,
            branch.IsDefault,
            branch.CreatedByUserId,
            branch.CreatedAt,
            branch.ClosedAt
        });
    }

    [HttpGet("project/{projectId}")]
    public async Task<IActionResult> ListBranches(string projectId)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!await UserOwnsProjectAsync(projectId, userId))
            return NotFound(new { error = "Proje bulunamadı veya bu kullanıcıya ait değil." });

        var branches = await _context.Branches
            .Where(b => b.ProjectId == projectId)
            .OrderByDescending(b => b.IsDefault)
            .ThenBy(b => b.CreatedAt)
            .Select(b => new
            {
                b.Id,
                b.ProjectId,
                b.Name,
                b.ParentBranchId,
                b.ForkedFromVersion,
                b.IsDefault,
                b.CreatedByUserId,
                b.CreatedAt,
                b.ClosedAt
            })
            .ToListAsync();

        return Ok(branches);
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
    [HttpGet("project/{projectId}/default")]
    public async Task<IActionResult> GetOrCreateDefaultBranch(string projectId)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!await UserOwnsProjectAsync(projectId, userId))
            return NotFound(new { error = "Proje bulunamadı veya bu kullanıcıya ait değil." });

        // Yarışa karşı sertleştirilmiş tek kopya — bkz. BranchProvisioning.
        var branch = await _context.GetOrCreateDefaultBranchAsync(projectId, userId);

        return Ok(new { branch.Id, branch.ProjectId, branch.Name });
    }

    [HttpPost("{branchId}/versions")]
    public async Task<IActionResult> CommitSchemaVersion(string branchId, [FromBody] CommitSchemaVersionRequest request)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.SchemaJson))
            return BadRequest(new { error = "SchemaJson boş olamaz." });

        var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == branchId);
        if (branch is null) return NotFound(new { error = "Branch bulunamadı." });
        if (!await UserOwnsProjectAsync(branch.ProjectId, userId))
            return NotFound(new { error = "Branch bulunamadı." });
        if (branch.ClosedAt is not null)
            return Conflict(new { error = "Kapatılmış bir branch'e yeni versiyon eklenemez." });

        short tableCount = 0;
        try
        {
            var parsed = JsonSerializer.Deserialize<DatabaseSchema>(request.SchemaJson, SchemaJsonOptions);
            tableCount = (short)(parsed?.Tables?.Count ?? 0);
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "SchemaJson geçerli bir DatabaseSchema değil." });
        }

        var lastVersion = await _context.SchemaVersions
            .Where(v => v.BranchId == branchId)
            .OrderByDescending(v => v.Version)
            .Select(v => v.Version)
            .FirstOrDefaultAsync();

        var version = new SchemaVersion
        {
            ProjectId = branch.ProjectId,
            BranchId = branchId,
            Version = lastVersion + 1,
            Checksum = ComputeChecksum(request.SchemaJson),
            SchemaJson = request.SchemaJson,
            Message = request.Message,
            TableCount = tableCount,
            AuthorUserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        await _context.SchemaVersions.AddAsync(version);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            version.Id,
            version.ProjectId,
            version.BranchId,
            version.Version,
            version.Checksum,
            version.Message,
            version.TableCount,
            version.AuthorUserId,
            version.CreatedAt
        });
    }

    [HttpGet("{branchId}/versions")]
    public async Task<IActionResult> ListSchemaVersions(string branchId)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == branchId);
        if (branch is null) return NotFound(new { error = "Branch bulunamadı." });
        if (!await UserOwnsProjectAsync(branch.ProjectId, userId))
            return NotFound(new { error = "Branch bulunamadı." });

        // SchemaJson kasıtlı olarak dışarıda bırakılıyor — liste görünümü büyük blob'ları
        // taşımamalı, tek bir versiyonun içeriği ayrı uç noktadan çekilir.
        var versions = await _context.SchemaVersions
            .Where(v => v.BranchId == branchId)
            .OrderByDescending(v => v.Version)
            .Select(v => new
            {
                v.Id,
                v.ProjectId,
                v.BranchId,
                v.Version,
                v.Checksum,
                v.Message,
                v.TableCount,
                v.AuthorUserId,
                v.CreatedAt
            })
            .ToListAsync();

        return Ok(versions);
    }

    [HttpGet("versions/{versionId}")]
    public async Task<IActionResult> GetSchemaVersion(string versionId)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var version = await _context.SchemaVersions.FirstOrDefaultAsync(v => v.Id == versionId);
        if (version is null) return NotFound(new { error = "Versiyon bulunamadı." });
        if (!await UserOwnsProjectAsync(version.ProjectId, userId))
            return NotFound(new { error = "Versiyon bulunamadı." });

        return Ok(version);
    }

    private static string ComputeChecksum(string schemaJson)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(schemaJson));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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

        var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken);
        if (branch is null) return NotFound(new { error = "Branch bulunamadı." });
        if (!await UserOwnsProjectAsync(branch.ProjectId, userId))
            return NotFound(new { error = "Branch bulunamadı." });

        var latest = await _context.SchemaVersions
            .Where(v => v.BranchId == branchId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(cancellationToken);

        // Şemasız bir branch için boş bir veritabanı açmak kaynak harcar ve
        // kullanıcıya hiçbir şey vermez.
        if (latest is null)
            return BadRequest(new { error = "Bu branch'te henüz bir şema sürümü yok." });

        DatabaseSchema schema;
        try
        {
            schema = JsonSerializer.Deserialize<DatabaseSchema>(latest.SchemaJson, SchemaJsonOptions)
                     ?? new DatabaseSchema();
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "Branch şeması okunamadı." });
        }

        try
        {
            var database = await _databases.ProvisionAsync(branchId, schema, request.Engine, cancellationToken);
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

    [HttpGet("{branchId}/database")]
    public async Task<IActionResult> GetDatabase(string branchId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken);
        if (branch is null) return NotFound(new { error = "Branch bulunamadı." });
        if (!await UserOwnsProjectAsync(branch.ProjectId, userId))
            return NotFound(new { error = "Branch bulunamadı." });

        var database = await _databases.GetAsync(branchId, cancellationToken);
        return database is null
            ? NotFound(new { error = "Bu branch'in canlı veritabanı yok." })
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

        var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken);
        if (branch is null) return NotFound(new { error = "Branch bulunamadı." });
        if (!await UserOwnsProjectAsync(branch.ProjectId, userId))
            return NotFound(new { error = "Branch bulunamadı." });

        var latest = await _context.SchemaVersions
            .Where(v => v.BranchId == branchId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
            return BadRequest(new { error = "Bu branch'te henüz bir şema sürümü yok." });

        DatabaseSchema schema;
        try
        {
            schema = JsonSerializer.Deserialize<DatabaseSchema>(latest.SchemaJson, SchemaJsonOptions)
                     ?? new DatabaseSchema();
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "Branch şeması okunamadı." });
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

        var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken);
        if (branch is null) return NotFound(new { error = "Branch bulunamadı." });
        if (!await UserOwnsProjectAsync(branch.ProjectId, userId))
            return NotFound(new { error = "Branch bulunamadı." });

        await _databases.DestroyAsync(branchId, cancellationToken);
        return NoContent();
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
