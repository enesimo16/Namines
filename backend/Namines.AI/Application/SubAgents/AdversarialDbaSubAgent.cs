using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Namines.AI.Domain;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Core.Nsl;

namespace Namines.AI.Application;

public sealed record DbaAuditInput(
    DatabaseSchema Schema,
    DatabaseType Engine,
    bool UseAiDeepAudit,
    string? ModelHint = null,
    string? ProviderHint = null);

public sealed record DbaAuditOutput(
    IReadOnlyList<DbaIssue> Issues,
    IReadOnlyList<MinusFindingItem> MinusFindings);

/// <summary>
/// Performans, FinOps (maliyet), normalizasyon ve güvenlik/PII kurallarını denetleyen DBA alt-ajanı.
/// Yapısal kontrolleri 0-token yerel kurallarla, derin mimari değerlendirmeyi NSL-sıkıştırılmış
/// bağlam üzerinden AI ile yapar.
/// </summary>
public sealed class AdversarialDbaSubAgent : ISubAgent<DbaAuditInput, DbaAuditOutput>
{
    private readonly ILlmCompletionEngine _llm;

    public AdversarialDbaSubAgent(ILlmCompletionEngine llm)
    {
        _llm = llm;
    }

    public AgentKind Kind => AgentKind.DbaFinOpsSecurityCritic;

    public async Task<DbaAuditOutput> ExecuteAsync(
        AgentInstance self,
        DbaAuditInput task,
        CancellationToken ct = default)
    {
        var issues = new List<DbaIssue>();
        var findings = new List<MinusFindingItem>();

        RunLocalDbaAudit(task.Schema, issues, findings);

        if (task.UseAiDeepAudit && task.Schema.Tables.Count > 0 && _llm.IsConfiguredFor(task.ProviderHint))
        {
            await RunAiDbaAuditAsync(task, issues, findings, ct).ConfigureAwait(false);
        }

        return new DbaAuditOutput(issues, findings);
    }

    private static void RunLocalDbaAudit(
        DatabaseSchema schema,
        List<DbaIssue> issues,
        List<MinusFindingItem> findings)
    {
        var fkColIds = schema.Relations.Select(r => r.SourceColumnId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var table in schema.Tables)
        {
            if (!table.Columns.Any(c => c.IsPK))
            {
                issues.Add(new DbaIssue
                {
                    RuleId = "DBA001",
                    TableName = table.Name,
                    Severity = DbaIssueSeverity.Error,
                    Category = "Design",
                    Source = "Local",
                    Message = $"'{table.Name}' tablosunda Primary Key tanımlı değil.",
                    Suggestion = "uuid veya bigint türünde birincil anahtar kolonu ekleyin.",
                });
                findings.Add(new MinusFindingItem(
                    $"dba-pk-{table.Name}", "DBA001", MinusSeverity.Blocker, table.Name, null,
                    $"{table.Name} Primary Key içermiyor.",
                    $"{table.Name} tablosuna id UUID PK kolonu ekle.",
                    "DbaFinOpsSecurityCritic", IsDeterministicAutoFixable: true));
            }

            foreach (var col in table.Columns)
            {
                var isFk = col.IsFK || fkColIds.Contains(col.Id);
                var hasIndex = table.Indexes.Any(idx =>
                    idx.Columns.Count > 0 &&
                    string.Equals(idx.Columns[0].ColumnId, col.Id, StringComparison.OrdinalIgnoreCase));

                if (isFk && !hasIndex && !col.IsPK)
                {
                    issues.Add(new DbaIssue
                    {
                        RuleId = "DBA002",
                        TableName = table.Name,
                        ColumnName = col.Name,
                        Severity = DbaIssueSeverity.Warning,
                        Category = "Performance",
                        Source = "Local",
                        Message = $"Foreign key kolonu '{table.Name}.{col.Name}' üzerinde indeks yok.",
                        Suggestion = $"idx_{table.Name}_{col.Name} B-Tree indeksi ekleyin.",
                    });
                    findings.Add(new MinusFindingItem(
                        $"dba-idx-{table.Name}-{col.Name}", "DBA002", MinusSeverity.DbaOptimization, table.Name, col.Name,
                        $"{table.Name}.{col.Name} FK kolonu indekssiz.",
                        $"{table.Name}.{col.Name} üzerine index tanımla.",
                        "DbaFinOpsSecurityCritic", IsDeterministicAutoFixable: true));
                }

                if (string.Equals(col.Name, "password", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new DbaIssue
                    {
                        RuleId = "DBA010",
                        TableName = table.Name,
                        ColumnName = col.Name,
                        Severity = DbaIssueSeverity.Warning,
                        Category = "Security",
                        Source = "Local",
                        Message = $"'{table.Name}.{col.Name}' ham parola adını taşıyor; hash alan adı tercih edilmeli.",
                        Suggestion = "Kolonu password_hash olarak adlandırın ve Argon2id/bcrypt hash saklayın.",
                    });
                }

                if (col.Type.Equals("VARCHAR", StringComparison.OrdinalIgnoreCase) && col.Length is null or <= 0)
                {
                    issues.Add(new DbaIssue
                    {
                        RuleId = "DBA008",
                        TableName = table.Name,
                        ColumnName = col.Name,
                        Severity = DbaIssueSeverity.Info,
                        Category = "FinOps",
                        Source = "Local",
                        Message = $"'{table.Name}.{col.Name}' boyutsuz VARCHAR kullanıyor.",
                        Suggestion = "Açık üst sınır (ör. VARCHAR(255)) vererek motor uyumluluğu ve depolama kontrolü sağlayın.",
                    });
                }
            }
        }
    }

    private async Task RunAiDbaAuditAsync(
        DbaAuditInput task,
        List<DbaIssue> issues,
        List<MinusFindingItem> findings,
        CancellationToken ct)
    {
        // Tüm ham JSON yerine NSL ile sıkıştırılmış şema göndererek ~%80 input token tasarrufu sağla.
        var nsl = NslWriter.Write(task.Schema, task.Engine);
        if (nsl.Length > 24_000)
            nsl = nsl[..24_000] + "\n// ...(truncated for DBA summary)";

        var system =
            "You are a Principal Database Administrator auditing a relational schema for Performance, FinOps, Normalization, and Security.\n" +
            "Return STRICT JSON array ONLY: [{\"ruleId\":\"AI-DBA-01\",\"tableName\":\"...\",\"columnName\":null,\"severity\":\"Warning\",\"category\":\"Performance\",\"message\":\"...\",\"suggestion\":\"...\"}].\n" +
            "Emit at most 8 high-signal issues that are NOT trivial missing-PK/FK-index checks.";

        var user = $"Engine: {task.Engine}\nSchema (NSL 1.0):\n{nsl}";

        try
        {
            var resp = await _llm.CompleteAsync(
                new LlmRequest(
                    SystemPrompt: system,
                    UserPrompt: user,
                    Temperature: 0.1,
                    ModelHint: task.ModelHint,
                    ProviderHint: task.ProviderHint,
                    JsonResponse: true,
                    FeatureKey: "dba_analyze"),
                ct).ConfigureAwait(false);

            var start = resp.Content.IndexOf('[');
            var end = resp.Content.LastIndexOf(']');
            if (start < 0 || end <= start)
                return;

            var json = JsonSanitizerPreprocessor.Sanitize(resp.Content[start..(end + 1)]);
            var aiIssues = JsonSerializer.Deserialize<List<DbaIssue>>(json, SchemaJsonOptions.Default);
            if (aiIssues is null)
                return;

            var seq = 0;
            foreach (var item in aiIssues.Take(10))
            {
                seq++;
                item.Source = "AI";
                issues.Add(item);
                findings.Add(new MinusFindingItem(
                    Id: $"ai-dba-{seq}",
                    Code: string.IsNullOrWhiteSpace(item.RuleId) ? $"AI-DBA-{seq:00}" : item.RuleId,
                    Severity: item.Severity == DbaIssueSeverity.Error ? MinusSeverity.Structural : MinusSeverity.DbaOptimization,
                    TableName: string.IsNullOrWhiteSpace(item.TableName) ? null : item.TableName,
                    ColumnName: item.ColumnName,
                    ProblemDescription: item.Message,
                    ActionableDirective: item.Suggestion ?? item.Message,
                    SourceSubAgent: "DbaFinOpsSecurityCritic"));
            }
        }
        catch
        {
            // AI DBA derin denetimi başarısız olsa dahi yerel 0-token bulgular korunur.
        }
    }
}
