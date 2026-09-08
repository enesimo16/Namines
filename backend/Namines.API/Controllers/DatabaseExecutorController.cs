using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.Tasks;
using Namines.Core.Enums;
using Namines.Core.Interfaces;

namespace Namines.API.Controllers;

// Hibrit güvenlik: keyfi SQL çalıştırma tehlikeli → login + rate-limit zorunlu.
[Authorize]
[EnableRateLimiting("sensitive")]
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
            return BadRequest("The connection string cannot be empty.");

        var success = await _executor.TestConnectionAsync(request.ConnectionString, request.DbType);
        
        // Memory security: Güvenlik amaçlı connection string null'a çekiliyor (opsiyonel GC dostu)
        request.ConnectionString = null;

        if (success)
            return Ok(new { success = true, message = "Connection successful." });
        else
            return BadRequest(new { success = false, message = "Connection failed. Check the connection details and try again." });
    }

    [HttpPost("execute")]
    public async Task<IActionResult> ExecuteScript([FromBody] ExecutorRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString) || string.IsNullOrWhiteSpace(request.Script))
            return BadRequest("The connection string and the script to run cannot be empty.");

        var result = await _executor.ExecuteScriptAsync(request.ConnectionString, request.Script, request.DbType);

        // Security: clear
        request.ConnectionString = null;

        if (result.Success)
            return Ok(new { success = true, message = $"{result.StatementsExecuted} statements executed successfully." });
        else
            return BadRequest(new { success = false, message = result.ErrorMessage, statementsExecuted = result.StatementsExecuted });
    }
}

public class ExecutorRequest
{
    // Nullable: handler'lar kullanım sonrası bunu bilerek null'a çekiyor (GC/güvenlik amaçlı).
    public string? ConnectionString { get; set; } = string.Empty;
    public string Script { get; set; } = string.Empty;
    public DatabaseType DbType { get; set; }
}
