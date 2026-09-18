using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Namines.Core.Analysis;
using Namines.Core.Models;

namespace Namines.API.Controllers;

/// <param name="Base">Ortak ata — iki branch'in ayrıldığı andaki şema.</param>
/// <param name="Ours">Üzerine birleşilen taraf (hedef).</param>
/// <param name="Theirs">Birleşen taraf (kaynak).</param>
public sealed record MergePreviewRequest(
    DatabaseSchema Base,
    DatabaseSchema Ours,
    DatabaseSchema Theirs);

/// <summary>
/// Üç şemayı ortak atalarına göre birleştirmenin önizlemesi
/// (github/03-COKLU-GELISTIRICI-MERGE.md).
///
/// <b>Neden durumsuz ve <see cref="BranchController"/>'dan ayrı:</b> canvas'ın
/// branch'leri tarayıcıda yaşıyor (<c>ProjectSnapshot.branches</c>) ve sunucuda
/// bir kimlikleri yok. Sunucu-taraflı branch'lere bağlı uç
/// (<c>/api/branch/{id}/merge/preview</c>) onlar için çalışmıyor. Bu uç üç
/// şemayı gövdede aldığı için iki branch sistemini birleştirmeyi beklemeden
/// canvas'ın birleştirmesini üç yollu hâle getiriyor.
///
/// <b>Anonim</b> — <see cref="MigrationController"/>/<see cref="LintController"/>
/// ile aynı sınıf: saf dönüşüm, kalıcı durum yok, veritabanına bağlanmıyor.
/// Kimlik istemek misafir kullanıcıların bugün sahip olduğu birleştirmeyi
/// ellerinden alırdı.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting("sensitive")]
[ApiController]
[Route("api/[controller]")]
public class MergeController : ControllerBase
{
    [HttpPost("preview")]
    public IActionResult Preview([FromBody] MergePreviewRequest request)
    {
        // Üçü de zorunlu: ortak ata olmadan yapılan şey üç yollu birleştirme
        // değil, iki yollu bir diff'tir. Eksik bir ata ile devam edip sonucu
        // "otomatik birleşti" diye sunmak, kullanıcıya olmayan bir güvence
        // vermek olurdu.
        if (request?.Base is null || request.Ours is null || request.Theirs is null)
            return BadRequest(new { error = "base, ours and theirs are all required — a three-way merge needs the common ancestor." });

        var result = SchemaThreeWayMerger.Merge(request.Base, request.Ours, request.Theirs);

        return Ok(new
        {
            autoMerged = result.AutoMerged,
            // `kind` STRING: sunucu enum'unu istemcide bire bir kopyalamak bu
            // projede bir kez kırıldı. Tanınmayan bir değer istemcide yanlış
            // değil, bilinmeyen olur.
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
}
