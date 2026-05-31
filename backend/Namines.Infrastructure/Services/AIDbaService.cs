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
                OverallAssessment = "No tables found to analyze. Please add tables to your schema first."
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
                Message = "AI DBA linter service is currently busy. Basic linter analysis completed using local rules.",
                Suggestion = "Please try again shortly for AI analysis.",
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
                    Message = $"Primary Key is not defined in the '{table.Name}' table.",
                    Suggestion = "Each table should have a Primary Key (e.g. an auto-incrementing 'Id' column) to uniquely identify rows.",
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
                        Message = $"Unlimited length (NVARCHAR(MAX)) is used for column '{col.Name}' in table '{table.Name}'.",
                        Suggestion = "Unlimited width fields consume high IOPS on AWS RDS or Azure SQL, bloat disk space, and can increase monthly cloud bills by up to 40%. It is recommended to restrict the length to NVARCHAR(255) or NVARCHAR(500).",
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
                        Message = $"No INDEX is defined on the '{col.Name}' Foreign Key column in the '{table.Name}' table.",
                        Suggestion = "Foreign keys are frequently used in JOIN operations. You should create a NON-CLUSTERED INDEX on this column to improve query performance.",
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
                        Message = $"Critical personal data (PII) or sensitive information under KVKK/GDPR was detected in column '{col.Name}' of table '{table.Name}'.",
                        Suggestion = "Storing sensitive personal data directly as plain text poses a security breach risk. You should use the '[ProtectedPersonalData]' attribute in C#, select Salted BCrypt Hashing for passwords, or configure SQL Server dynamic data masking.",
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
            return "Excellent database design! No performance risk. The schema structure complies exceptionally with enterprise standards.";
        if (score >= 70)
            return "Good design. It will become more efficient with minor performance improvements and indexing.";
        if (score >= 50)
            return "Average database design. Missing FK indexes or using unlimited length columns may negatively affect query speeds.";
        
        return "There are critical performance and structural issues that need improvement. It is strongly recommended to apply the suggestions for SQL data integrity and index optimization.";
    }
}
