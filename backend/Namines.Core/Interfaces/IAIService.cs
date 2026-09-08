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
    /// Elle çizilmiş veritabanı şeması görselini analiz eder ve DatabaseSchema şemasına dönüştürür.
    /// </summary>
    Task<DatabaseSchema> AnalyzeImageAsync(byte[] imageBytes, string mimeType);

    /// <summary>
    /// G15 — AI Impact Explainer (new-phase/28-IMPACT-ANALYSIS-ENGINE.md §1).
    /// SchemaImpactAnalyzer'ın ürettiği deterministik <see cref="ImpactReport"/>'u
    /// insan diline çevirir. AI kendi başına YENİ bir bulgu üretmez — sadece verilen
    /// yapıyı özetler/açıklar (doc'un kuralı: "motor kanıtladı, AI özetledi").
    /// </summary>
    Task<string> ExplainImpactAsync(ImpactReport impact);
}
