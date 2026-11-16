using System.Collections.Generic;
using System.Threading.Tasks;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Interfaces;

/// <summary>
/// Namines yaşam döngüsü boyunca tüm yapay zekâ yeteneklerinin birleşik arayüzü.
/// Tüm tüketiciler (Controller ve Infrastructure servisleri) yalnızca bu sözleşmeye
/// bağımlıdır — hiçbir somut sağlayıcı sınıfına doğrudan bağlanmaz.
/// </summary>
public interface IAIService : IDbaSchemaAnalyzer
{
    Task<DatabaseSchema> GenerateSchemaAsync(GenerateRequest request);
    Task<DatabaseSchema> ReviseSchemaAsync(ReviseRequest request);
    Task<DatabaseSchema> ParseDbContextToSchemaAsync(string dbContextCode, DatabaseType dbType);
    Task<MigrationResult> GenerateMigrationCodeAsync(DatabaseSchema oldSchema, DatabaseSchema newSchema, DatabaseType dbType);
    Task<string> GenerateSmartSeedSqlAsync(DatabaseSchema schema, DatabaseType dbType, string domain, int rowCount);
    Task<string> GenerateMockDataAsync(DatabaseSchema schema);

    /// <summary>
    /// Verilen veritabanı şemasının iş amacını ve mimari özetini anlatan
    /// profesyonel bir Yönetici Özeti (Executive Summary) üretir.
    /// </summary>
    Task<string> GenerateProjectSummaryAsync(DatabaseSchema schema, string projectName);

    Task<string> GenerateStreamlitAppAsync(DatabaseSchema schema, DatabaseType dbType);

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

    /// <summary>
    /// Doğal dil sorusundan hedef motor lehçesine uygun salt-okunur SQL üretir.
    /// </summary>
    Task<string> GenerateSqlFromQuestionAsync(DatabaseSchema schema, DatabaseType dbType, string question);
}
