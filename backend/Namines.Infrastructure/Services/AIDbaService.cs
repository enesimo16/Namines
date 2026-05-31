using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Infrastructure.AI;

namespace Namines.Infrastructure.Services;

public class AIDbaService : IAIDbaService
{
    private readonly GroqAIService _groqService;

    public AIDbaService(GroqAIService groqService)
    {
        _groqService = groqService;
    }

    public async Task<DbaAnalysisResult> AnalyzeSchemaAsync(DatabaseSchema schema, DatabaseType dbType)
    {
        var result = new DbaAnalysisResult();
        var issues = new List<DbaIssue>();

        if (schema?.Tables == null || schema.Tables.Count == 0)
        {
            return new DbaAnalysisResult
            {
                TotalScore = 100,
                OverallAssessment = "Analiz edilecek tablo bulunamadı. Lütfen önce şemanıza tablo ekleyin."
            };
        }

        // AŞAMA A: Yerel Linter Kuralları (Local Rule Engine)
        RunLocalRules(schema, issues);

        // AŞAMA B: AI Destekli Derin DBA Analizi (Groq API)
        try
        {
            var aiIssues = await _groqService.AnalyzeSchemaDbaAsync(schema, dbType);
            if (aiIssues != null && aiIssues.Count > 0)
            {
                foreach (var aiIssue in aiIssues)
                {
                    aiIssue.Source = "AI";
                    issues.Add(aiIssue);
                }
            }
        }
        catch
        {
            // Fallback gracefully: if Groq fails or rate limits, local linter issues are still returned
            issues.Add(new DbaIssue
            {
                RuleId = "DBA-SYS-001",
                Severity = DbaIssueSeverity.Info,
                Message = "AI DBA linter servisi şu anda meşgul. Yerel kurallarla temel linter analizi tamamlandı.",
                Suggestion = "AI analizi için lütfen kısa süre sonra tekrar deneyin.",
                Source = "System"
            });
        }

        // AŞAMA C: Skorlama ve Birleştirme
        result.Issues = issues;
        result.TotalScore = CalculateScore(issues);
        result.OverallAssessment = GenerateAssessmentText(result.TotalScore);

        return result;
    }

    private void RunLocalRules(DatabaseSchema schema, List<DbaIssue> issues)
    {
        foreach (var table in schema.Tables)
        {
            // KURAL 1: DBA-003 - Tabloda Primary Key (PK) eksik
            var hasPk = table.Columns.Any(c => c.IsPK);
            if (!hasPk)
            {
                issues.Add(new DbaIssue
                {
                    RuleId = "DBA-003",
                    TableName = table.Name,
                    Severity = DbaIssueSeverity.Error,
                    Message = $"'{table.Name}' tablosunda Primary Key (Birincil Anahtar) tanımlanmamış.",
                    Suggestion = "Her tabloda satırları benzersiz şekilde tanımlayan bir Primary Key (örneğin otomatik artan bir 'Id' kolonu) bulunmalıdır.",
                    Source = "Local",
                    Category = "Performance"
                });
            }

            foreach (var col in table.Columns)
            {
                // KURAL 2: DBA-002 - NVARCHAR(MAX) veya TEXT tipi sınırsız kullanımı (FinOps)
                if ((string.Equals(col.Type, "nvarchar", StringComparison.OrdinalIgnoreCase) || string.Equals(col.Type, "varchar", StringComparison.OrdinalIgnoreCase)) && 
                    (col.Length == null || col.Length == -1 || string.Equals(col.Length.ToString(), "max", StringComparison.OrdinalIgnoreCase)))
                {
                    issues.Add(new DbaIssue
                    {
                        RuleId = "DBA-002",
                        TableName = table.Name,
                        ColumnName = col.Name,
                        Severity = DbaIssueSeverity.Warning,
                        Message = $"'{table.Name}' tablosundaki '{col.Name}' sütunu için sınırsız uzunluk (NVARCHAR(MAX)) kullanılmış.",
                        Suggestion = "AWS RDS veya Azure SQL üzerinde sınırsız genişlikteki alanlar yüksek IOPS tüketir, disk alanını şişirir ve bulut faturalarını aylık %40'a varan oranda artırabilir. Boyutu NVARCHAR(255) veya NVARCHAR(500) ile sınırlandırmanız önerilir.",
                        Source = "Local",
                        Category = "FinOps"
                    });
                }

                // KURAL 3: DBA-001 - Foreign Key (FK) sütunlarında INDEX eksik (Performance)
                if (col.IsFK && (col.DefaultValue == null || !col.DefaultValue.Contains("Indexed")))
                {
                    issues.Add(new DbaIssue
                    {
                        RuleId = "DBA-001",
                        TableName = table.Name,
                        ColumnName = col.Name,
                        Severity = DbaIssueSeverity.Warning,
                        Message = $"'{table.Name}' tablosundaki '{col.Name}' Foreign Key (Yabancı Anahtar) sütunu üzerinde INDEX tanımlanmamış.",
                        Suggestion = "Yabancı anahtarlar sık sık JOIN işlemlerinde kullanılır. Sorgu performansını artırmak için bu sütun üzerinde bir NON-CLUSTERED INDEX oluşturmalısınız.",
                        Source = "Local",
                        Category = "Performance"
                    });
                }

                // KURAL 4: DBA-SEC-001 - KVKK/PII Hassas Veri Algılama (Security)
                var colLower = col.Name.ToLowerInvariant();
                if ((colLower.Contains("sifre") || colLower.Contains("password") || colLower.Contains("tckn") || 
                     colLower.Contains("identityno") || colLower.Contains("kredikarti") || colLower.Contains("creditcard") || 
                     colLower.Contains("phone") || colLower.Contains("telefon") || colLower.Contains("email") || 
                     colLower.Contains("address") || colLower.Contains("adres")) && 
                    (col.DefaultValue == null || (!col.DefaultValue.Contains("Secured") && !col.DefaultValue.Contains("Encrypted"))))
                {
                    issues.Add(new DbaIssue
                    {
                        RuleId = "DBA-SEC-001",
                        TableName = table.Name,
                        ColumnName = col.Name,
                        Severity = DbaIssueSeverity.Warning,
                        Message = $"'{table.Name}' tablosundaki '{col.Name}' sütununda KVKK/GDPR kapsamında kritik kişisel veri (PII) veya hassas bilgi algılandı.",
                        Suggestion = "Hassas kişisel verilerin doğrudan düz metin olarak saklanması güvenlik ihlali riski barındırır. C# tarafında '[ProtectedPersonalData]' niteliği kullanmalı, şifreler için Salted BCrypt Hashing tercih etmeli veya SQL Server dynamic data masking kurgulamalısınız.",
                        Source = "Local",
                        Category = "Security"
                    });
                }
            }
        }
    }

    private int CalculateScore(List<DbaIssue> issues)
    {
        int score = 100;
        foreach (var issue in issues)
        {
            score -= issue.Severity switch
            {
                DbaIssueSeverity.Error => 15,
                DbaIssueSeverity.Warning => 8,
                DbaIssueSeverity.Info => 3,
                _ => 0
            };
        }
        return Math.Max(0, score);
    }

    private string GenerateAssessmentText(int score)
    {
        if (score >= 90)
            return "Mükemmel veritabanı tasarımı! Performans riski bulunmuyor. Şema yapısı kurumsal standartlara son derece uygun.";
        if (score >= 70)
            return "İyi bir tasarım. Küçük performans iyileştirmeleri ve indexlemelerle daha verimli hale gelecektir.";
        if (score >= 50)
            return "Orta düzey veritabanı tasarımı. FK indexleri eksik veya sınırsız alan kullanımları sorgu hızlarını olumsuz etkileyebilir.";
        
        return "İyileştirilmesi gereken kritik performans ve yapısal sorunlar var. SQL veri bütünlüğü ve index optimizasyonu için önerileri uygulamanız şiddetle tavsiye edilir.";
    }
}
