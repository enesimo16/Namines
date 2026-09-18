using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Github;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.API.Controllers;

/// <param name="RepoUrl">Kullanıcının yapıştırdığı adres.</param>
/// <param name="Branch">Boşsa deponun kendi varsayılan dalı kullanılır.</param>
/// <param name="CompareWith">
/// Doluysa çıkarılan şema BUNA karşı karşılaştırılır — "kodun şunu diyor,
/// elimizdeki şema şunu diyor".
/// </param>
public sealed record ScanRepositoryRequest(
    string RepoUrl,
    string? Branch,
    DatabaseSchema? CompareWith,
    DatabaseType DbType = DatabaseType.PostgreSQL);

/// <summary>
/// github/01-DEPO-TARAMA.md — depo linkinden şema.
///
/// <b>Giriş ZORUNLU</b> (<see cref="CodeSchemaController"/> ile aynı gerekçe ve
/// bir fazlası): bu uç sunucuyu dışarıya çıkarıyor. Kimliksiz bırakmak, onu
/// herkesin kullanabildiği bir istek aracına çevirirdi. Nereye çıkılabileceğini
/// de <see cref="GithubRepositoryUrl"/>'in host beyaz listesi sınırlıyor.
///
/// <b>Depo klonlanmaz</b>, hiçbir dosya yazılmaz, hiçbir SQL çalıştırılmaz —
/// yalnızca metin okunur (second-phase/11'in iki açık yasağı).
/// </summary>
[Authorize]
[EnableRateLimiting("sensitive")]
[ApiController]
[Route("api/github")]
public class GithubScanController : ControllerBase
{
    private readonly IRepositoryScanner _scanner;

    public GithubScanController(IRepositoryScanner scanner) => _scanner = scanner;

    [HttpPost("scan")]
    public async Task<IActionResult> Scan(
        [FromBody] ScanRepositoryRequest request, CancellationToken cancellationToken)
    {
        if (!GithubRepositoryUrl.TryParse(request?.RepoUrl, out var repository))
            return BadRequest(new { message = "That is not a GitHub repository URL. Expected github.com/owner/name." });

        try
        {
            // installationId null: bu kademe yalnızca PUBLIC depo okur. Private
            // depo, GitHub App kurulumuyla F2'de geliyor; yarım bir kimlik yolu
            // uydurmak yerine hata mesajı kullanıcıyı oraya yönlendiriyor.
            var scan = await _scanner.ScanAsync(
                repository!, request!.Branch, installationId: null, cancellationToken);

            return Ok(new
            {
                format = scan.Format,
                branch = scan.Branch,
                schema = scan.Schema,
                // Dürüst kısmi rapor: ne okundu, ne okunmadı ve NEDEN.
                parsedFiles = scan.ParsedFiles,
                skipped = scan.Skipped.Select(s => new { name = s.Name, reason = s.Reason }),
                // Ağaç kesildiyse "deponda şema yok" demek yanlış olur.
                treeTruncated = scan.TreeTruncated,
                // Okunan dosyalardan çıkan şemanın KENDİSİYLE ilgili uyarılar
                // (atlanan dosyalardan ayrı): ör. birbirinden bağımsız
                // projelerin şemaları birleştiğinde çıkan ad tekrarları.
                warnings = scan.Warnings,
                drift = request.CompareWith is null
                    ? null
                    : SchemaDriftResponse.Build(scan.Schema, request.CompareWith, request.DbType),
            });
        }
        catch (GithubRepositoryUnavailableException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (GithubRateLimitedException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (CodeSchemaExtractor.UnknownFormatException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
