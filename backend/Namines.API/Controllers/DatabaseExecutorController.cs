using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;

namespace Namines.API.Controllers;

// Hibrit güvenlik: keyfi SQL çalıştırma tehlikeli → login + rate-limit zorunlu.
[Authorize]
[EnableRateLimiting("sensitive")]
[ApiController]
[Route("api/executor")]
public class DatabaseExecutorController : ControllerBase
{
    /// <summary>
    /// Denetim kaydında saklanan betik ön ekinin uzunluğu. Betiğin TAMAMI
    /// bilerek saklanmıyor — gerekçe <see cref="SqlExecutionAudit"/>'te.
    /// </summary>
    private const int ScriptPreviewLength = 500;

    /// <summary>
    /// Denetim kaydında "riskli" işareti koymak için taranan kelimeler.
    ///
    /// Kaba bir sinyal: bir yorumun içindeki "DROP" da işaretlenir. Yetki kararı
    /// buna DAYANMIYOR — yalnızca kaydı sonradan tararken önceliklendirmeyi
    /// mümkün kılıyor. Yanlış pozitif burada zararsız, yanlış negatif değil;
    /// bu yüzden geniş tutuldu.
    /// </summary>
    private static readonly string[] DestructiveKeywords =
        { "DROP ", "TRUNCATE ", "DELETE ", "ALTER ", "REVOKE ", "GRANT " };

    private readonly IDatabaseExecutor _executor;
    private readonly AuthDbContext _context;
    private readonly ILogger<DatabaseExecutorController> _logger;

    public DatabaseExecutorController(
        IDatabaseExecutor executor,
        AuthDbContext context,
        ILogger<DatabaseExecutorController> logger)
    {
        _executor = executor;
        _context = context;
        _logger = logger;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpPost("test-connection")]
    public async Task<IActionResult> TestConnection(
        [FromBody] ExecutorRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString))
            return BadRequest(new { success = false, message = "The connection string cannot be empty." });

        var success = await _executor.TestConnectionAsync(request.ConnectionString, request.DbType, ct);

        return success
            ? Ok(new { success = true, message = "Connection successful." })
            : BadRequest(new { success = false, message = "Connection failed. Check the connection details and try again." });
    }

    /// <summary>
    /// Kullanıcının verdiği bağlantıya karşı bir SQL betiği çalıştırır.
    ///
    /// <b>Bu uç, ürünün ChangeRequest yönetişim zincirinin DIŞINDA.</b> Yetenek
    /// meşru — "ürettiğim şemayı kendi veritabanıma bas" çekirdek akış. Meşru
    /// olmayan, bunun iz bırakmamasıydı: yıkıcı bir DDL'den sonra "kim, ne
    /// zaman, nerede" sorusunun cevabı yoktu.
    ///
    /// Artık her çalıştırma — <b>başarısız olanlar dahil</b> — kaydediliyor.
    /// Başarısızları kaydetmek özellikle önemli: bir saldırı denemesi tam olarak
    /// başarısız çalıştırmalar dizisi gibi görünür.
    /// </summary>
    [HttpPost("execute")]
    public async Task<IActionResult> ExecuteScript(
        [FromBody] ExecutorRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.ConnectionString) || string.IsNullOrWhiteSpace(request.Script))
            return BadRequest(new { success = false, message = "The connection string and the script to run cannot be empty." });

        var result = await _executor.ExecuteScriptAsync(
            request.ConnectionString, request.Script, request.DbType, ct);

        // Kayıt, yanıt döndürülmeden ÖNCE yazılıyor: sonra yazmak, isteğin
        // yarıda kesildiği durumda işlemin izsiz kalması demekti.
        await WriteAuditAsync(userId, request, result, ct);

        if (result.Success)
            return Ok(new
            {
                success = true,
                message = $"{result.StatementsExecuted} statements executed successfully.",
            });

        return BadRequest(new
        {
            success = false,
            message = result.ErrorMessage,
            statementsExecuted = result.StatementsExecuted,
            // Kısmî uygulama uyarısı yanıtta: MySQL/MariaDB/Oracle DDL'i örtük
            // commit'ler, yani "geri alındı" demek yalan olurdu.
            partialApplyPossible = result.PartialApplyPossible,
        });
    }

    private async Task WriteAuditAsync(
        string userId, ExecutorRequest request, ExecutionResult result, CancellationToken ct)
    {
        var script = request.Script ?? string.Empty;
        var (host, database) = DescribeTarget(request.ConnectionString, request.DbType);

        var entry = new SqlExecutionAudit
        {
            UserId = userId,
            ProjectId = string.IsNullOrWhiteSpace(request.ProjectId) ? null : request.ProjectId,
            TargetHost = host,
            TargetDatabase = database,
            DbType = request.DbType.ToString(),
            ScriptHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script))),
            ScriptPreview = script.Length <= ScriptPreviewLength ? script : script[..ScriptPreviewLength],
            ScriptLength = script.Length,
            ContainsDestructiveKeyword = DestructiveKeywords.Any(
                k => script.Contains(k, StringComparison.OrdinalIgnoreCase)),
            Success = result.Success,
            StatementsExecuted = result.StatementsExecuted,
            PartialApplyPossible = result.PartialApplyPossible,
            Error = result.ErrorMessage,
        };

        try
        {
            _context.SqlExecutionAudits.Add(entry);
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Kayıt yazılamazsa işlem GERİ ALINMAZ — o çoktan hedefte uygulandı;
            // burada geri alınacak bir şey yok. Ama sessiz kalınmaz: denetim
            // kaydının boşluğu, boşluğun kendisinden daha tehlikelidir.
            _logger.LogError(ex,
                "SQL calistirma denetim kaydi YAZILAMADI. Kullanici={UserId} Hedef={Host}/{Database} Hash={Hash}",
                userId, host, database, entry.ScriptHash);
        }
    }

    /// <summary>
    /// Bağlantı dizesinden yalnızca host ve veritabanı adını çıkarır.
    ///
    /// <b>Parola ve kullanıcı adı bilerek alınmıyor.</b> Denetim kaydı genellikle
    /// ana veriden uzun yaşar ve daha çok kişi tarafından okunur; oraya bir
    /// kimlik bilgisi yazmak, sızıntı yüzeyini uzun vadeye yaymaktır.
    ///
    /// Ayrıştırma başarısız olursa alanlar null kalır — kayıt yine de yazılır.
    /// Eksik bir kayıt, hiç kayıt olmamasından iyidir.
    /// </summary>
    private static (string? Host, string? Database) DescribeTarget(string? connectionString, DatabaseType dbType)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return (null, null);

        string? host = null, database = null;

        foreach (var pair in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0) continue;

            var key = pair[..separator].Trim();
            var value = pair[(separator + 1)..].Trim();

            if (key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Server", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Data Source", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Address", StringComparison.OrdinalIgnoreCase))
                host = value;
            else if (key.Equals("Database", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase))
                database = value;
        }

        // SQLite'ta "Data Source" bir dosya yolu; kullanıcının disk düzenini
        // denetim kaydına yazmamak için yalnızca dosya adı tutuluyor.
        if (dbType == DatabaseType.SQLite && host is not null)
        {
            host = System.IO.Path.GetFileName(host);
        }

        return (host, database);
    }
}

public class ExecutorRequest
{
    public string? ConnectionString { get; set; } = string.Empty;
    public string Script { get; set; } = string.Empty;
    public DatabaseType DbType { get; set; }

    /// <summary>
    /// İsteğe bağlı proje bağı. Zorunlu DEĞİL: derleme akışı, henüz bir projeye
    /// bağlanmamış bir şemayı kullanıcının veritabanına basmayı destekliyor.
    /// Verildiğinde denetim kaydı projeye de bağlanır ve kayıt proje bazında
    /// okunabilir hâle gelir.
    /// </summary>
    public string? ProjectId { get; set; }
}
