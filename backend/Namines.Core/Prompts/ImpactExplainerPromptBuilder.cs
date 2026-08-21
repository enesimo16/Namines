using System.Linq;
using System.Text;
using Namines.Core.Models;

namespace Namines.Core.Prompts;

/// <summary>
/// G15 — AI Impact Explainer (new-phase/28-IMPACT-ANALYSIS-ENGINE.md §1). Prompt'un tek
/// işi <see cref="ImpactReport"/>'un ZATEN İÇERDİĞİ bulguları insan diline çevirmek —
/// sistem talimatı AI'a yeni bulgu üretmesini AÇIKÇA yasaklıyor ("motor kanıtladı, AI
/// özetledi" ilkesi, doc'un kendi cümlesi).
/// </summary>
public static class ImpactExplainerPromptBuilder
{
    public static (string SystemPrompt, string UserPrompt) Build(ImpactReport impact)
    {
        const string systemPrompt = """
            You are explaining a database schema change to a non-technical reviewer (a product
            manager or founder, not a DBA). You will be given a STRUCTURED, already-computed
            analysis of the change — table/column/relation diffs, breaking changes, data loss
            risks, lock risks, and a risk level.

            STRICT RULE: You may only explain, summarize, and give plain-language context for
            the findings you are given. You must NEVER invent a new finding, risk, or table/
            column that is not listed below — the structural analysis is deterministic and
            already complete; your job is translation, not analysis.

            Write 2-4 short paragraphs, plain text (no markdown headers, no bullet lists).
            Open with what changed and the overall risk level in one plain sentence. If there
            are breaking changes or data loss risks, explain concretely what could go wrong in
            production and why, referencing the actual table/column names given. If everything
            is safe, say so plainly and briefly — don't manufacture caveats.
            """;

        var sb = new StringBuilder();
        sb.AppendLine($"Overall risk level: {impact.OverallRisk}");
        sb.AppendLine();

        if (impact.AffectedTables.Count > 0)
        {
            sb.AppendLine("Affected tables:");
            foreach (var t in impact.AffectedTables)
                sb.AppendLine($"- {t.TableName} ({t.Kind}){(t.ChangedColumns.Count > 0 ? $": columns {string.Join(", ", t.ChangedColumns)}" : "")}");
            sb.AppendLine();
        }

        if (impact.BreakingChanges.Count > 0)
        {
            sb.AppendLine("Breaking changes:");
            foreach (var b in impact.BreakingChanges)
                sb.AppendLine($"- [{b.Kind}] {b.Description}{(b.SuggestedMitigation is not null ? $" (mitigation: {b.SuggestedMitigation})" : "")}");
            sb.AppendLine();
        }

        if (impact.DataLossRisks.Count > 0)
        {
            sb.AppendLine("Data loss risks:");
            foreach (var d in impact.DataLossRisks)
                sb.AppendLine($"- {d.TableName}{(d.ColumnName is not null ? $".{d.ColumnName}" : "")}: {d.Reason}");
            sb.AppendLine();
        }

        if (impact.LockRisks.Count > 0)
        {
            sb.AppendLine("Migration lock risks:");
            foreach (var l in impact.LockRisks)
                sb.AppendLine($"- [{l.Severity}] {l.Operation}{(l.TableName is not null ? $" on {l.TableName}" : "")}{(l.SaferAlternative is not null ? $" — safer alternative: {l.SaferAlternative}" : "")}");
            sb.AppendLine();
        }

        if (impact.IndexSuggestions.Count > 0)
        {
            sb.AppendLine("Missing index suggestions:");
            foreach (var s in impact.IndexSuggestions)
                sb.AppendLine($"- {s.TableName}.{s.ColumnName}: {s.Reason}");
            sb.AppendLine();
        }

        sb.AppendLine($"Rollback: {(impact.Rollback.IsReversible ? "This change can be safely rolled back." : $"This change cannot be automatically rolled back. {impact.Rollback.Reason}")}");

        if (impact.BreakingChanges.Count == 0 && impact.DataLossRisks.Count == 0 && impact.LockRisks.Count == 0)
            sb.AppendLine("No breaking changes, data loss risks, or lock risks were found.");

        return (systemPrompt, sb.ToString());
    }
}
