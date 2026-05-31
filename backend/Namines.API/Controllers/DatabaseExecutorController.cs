using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Namines.Core.Enums;
using Namines.Core.Interfaces;

namespace Namines.API.Controllers;

[ApiController]
[Route("api/executor")]
public class DatabaseExecutorController : ControllerBase
{
    private readonly IDatabaseExecutor _executor;

    public DatabaseExecutorController(IDatabaseExecutor executor)
    {
        _executor = executor;
    }

    [HttpPost("test-connection")]
    public async Task<IActionResult> TestConnection([FromBody] ExecutorRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString))
            return BadRequest("Bağlantı dizesi (Connection string) boş olamaz.");

        var success = await _executor.TestConnectionAsync(request.ConnectionString, request.DbType);
        
        // Memory security: Güvenlik amaçlı connection string null'a çekiliyor (opsiyonel GC dostu)
        request.ConnectionString = null;

        if (success)
            return Ok(new { success = true, message = "Bağlantı başarılı!" });
        else
            return BadRequest(new { success = false, message = "Bağlantı başarısız. Lütfen bilgileri kontrol edin." });
    }

    [HttpPost("execute")]
    public async Task<IActionResult> ExecuteScript([FromBody] ExecutorRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString) || string.IsNullOrWhiteSpace(request.Script))
            return BadRequest("Connection string veya çalıştırılacak script boş olamaz.");

        var result = await _executor.ExecuteScriptAsync(request.ConnectionString, request.Script, request.DbType);

        // Security: clear
        request.ConnectionString = null;

        if (result.Success)
            return Ok(new { success = true, message = $"{result.StatementsExecuted} işlem başarıyla çalıştırıldı." });
        else
            return BadRequest(new { success = false, message = result.ErrorMessage, statementsExecuted = result.StatementsExecuted });
    }
}

public class ExecutorRequest
{
    public string ConnectionString { get; set; } = string.Empty;
    public string Script { get; set; } = string.Empty;
    public DatabaseType DbType { get; set; }
}
