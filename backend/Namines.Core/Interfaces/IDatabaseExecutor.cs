using System.Threading.Tasks;
using Namines.Core.Enums;

namespace Namines.Core.Interfaces;

public record ExecutionResult(bool Success, string? ErrorMessage, int StatementsExecuted);

public interface IDatabaseExecutor
{
    Task<ExecutionResult> ExecuteScriptAsync(string connectionString, string ddlScript, DatabaseType dbType);
    Task<bool> TestConnectionAsync(string connectionString, DatabaseType dbType);
}
