using System.Threading.Tasks;
using Namines.Core.Models;

namespace Namines.Core.Interfaces;

public interface IAIService
{
    Task<DatabaseSchema> GenerateSchemaAsync(GenerateRequest request);
    Task<DatabaseSchema> ReviseSchemaAsync(ReviseRequest request);
    Task<string> GenerateMockDataAsync(DatabaseSchema schema);

    /// <summary>
    /// Verilen veritabanı şemasının iş amacını ve mimari özetini anlatan
    /// profesyonel bir Yönetici Özeti (Executive Summary) üretir.
    /// </summary>
    Task<string> GenerateProjectSummaryAsync(DatabaseSchema schema, string projectName);

    Task<string> GenerateStreamlitAppAsync(DatabaseSchema schema, Namines.Core.Enums.DatabaseType dbType);

    /// <summary>
    /// Fixes faulty Streamlit code based on error logs and database schema.
    /// Used by the self-healing mechanism when container crashes are detected.
    /// </summary>
    /// <param name="originalCode">The faulty Python code that caused the container to crash</param>
    /// <param name="errorLogs">Container error logs containing traceback and error messages</param>
    /// <param name="schema">Database schema for context</param>
    /// <param name="dbType">Database type (MSSQL, PostgreSQL, MySQL) for connection parameters</param>
    /// <returns>Corrected Python code</returns>
    Task<string> FixStreamlitAppAsync(string originalCode, string errorLogs, DatabaseSchema schema, Namines.Core.Enums.DatabaseType dbType);

    /// <summary>
    /// Elle çizilmiş veritabanı şeması görselini analiz eder ve DatabaseSchema şemasına dönüştürür.
    /// </summary>
    Task<DatabaseSchema> AnalyzeImageAsync(byte[] imageBytes, string mimeType);
}
