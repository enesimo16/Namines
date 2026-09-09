using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Enums;

namespace Namines.Core.Interfaces;

/// <param name="PartialApplyPossible">
/// Betik yarıda kaldıysa, hedef motorda <b>kısmî uygulama</b> mümkün mü.
///
/// Çalıştırma tek bir transaction içinde yapılıyor, ama DDL her motorda
/// transaction'a girmiyor: MySQL, MariaDB ve Oracle'da <c>CREATE TABLE</c> gibi
/// ifadeler <b>örtük commit</b> yapar ve geri alma onları geri almaz. Bunu
/// çağırana söylemezsek arayüz "hepsi ya da hiçbiri" garantisi veriyormuş gibi
/// görünür — verilmeyen bir garanti, garantisizlikten kötüdür.
/// </param>
public record ExecutionResult(
    bool Success,
    string? ErrorMessage,
    int StatementsExecuted,
    bool PartialApplyPossible = false);

public interface IDatabaseExecutor
{
    Task<ExecutionResult> ExecuteScriptAsync(
        string connectionString, string ddlScript, DatabaseType dbType,
        CancellationToken cancellationToken = default);

    Task<bool> TestConnectionAsync(
        string connectionString, DatabaseType dbType,
        CancellationToken cancellationToken = default);
}
