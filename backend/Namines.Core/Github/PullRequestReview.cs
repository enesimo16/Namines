using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Github;

/// <param name="Conclusion">
/// GitHub check sonucu: <c>success</c> | <c>neutral</c> | <c>failure</c>.
/// Sabit metin DEĞİL de sözleşme: GitHub bu değerleri tanıyor, farklı bir kelime
/// göndermek check'i hiç oluşturmamaya eşdeğer.
/// </param>
public sealed record PullRequestReview(string Conclusion, string Title, string Summary, string Body);

/// <summary>
/// Şema etki raporunu bir PR yorumuna ve status check'ine çevirir
/// (new-phase/11-MIGRATIONS-BRANCHING.md §7).
///
/// <b>Yeni bulgu üretmiyor</b> — <see cref="Analysis.SchemaImpactAnalyzer"/>'ın
/// çıktısını insan diline çeviriyor. Burada ayrı bir risk değerlendirmesi yapmak,
/// aynı değişikliğin Studio'da ve PR'da farklı risk göstermesi demek olurdu.
///
/// <b>Neden Markdown üretimi ayrı bir sınıf:</b> GitHub'a gönderme adımı kimlik
/// bilgisi ve ağ gerektiriyor, metin üretimi gerektirmiyor. Ayrıldıklarında metin
/// tamamen test edilebilir kalıyor — ki asıl değer orada: yanlış bir risk tablosu,
/// insanı yanlış bir merge kararına götürür.
/// </summary>
public static class PullRequestReviewComposer
{
    /// <summary>
    /// Risk seviyesi → check sonucu.
    ///
    /// <b>Destructive ve Breaking FAILURE.</b> "neutral" seçmek merge'ü engellemez
    /// ve check'i süse çevirir; bu özelliğin tüm amacı, veri kaybettirecek bir
    /// değişikliğin insan onayı olmadan geçmemesi.
    /// </summary>
    public static string ConclusionFor(RiskLevel risk) => risk switch
    {
        RiskLevel.Destructive or RiskLevel.Breaking => "failure",
        RiskLevel.Risky => "neutral",
        _ => "success",
    };

    public static PullRequestReview Compose(ImpactReport report, string? previewDatabaseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(report);

        var sb = new StringBuilder();

        sb.AppendLine($"## {Badge(report.OverallRisk)} Namines schema review");
        sb.AppendLine();
        sb.AppendLine(Headline(report));
        sb.AppendLine();

        sb.AppendLine("| Signal | Count |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Affected tables | {report.AffectedTables.Count} |");
        sb.AppendLine($"| Breaking changes | {report.BreakingChanges.Count} |");
        sb.AppendLine($"| Data-loss risks | {report.DataLossRisks.Count} |");
        sb.AppendLine($"| Lock risks | {report.LockRisks.Count} |");
        sb.AppendLine();

        // Veri kaybı EN ÜSTTE ve kısaltılmadan: bir incelemeci yorumun tamamını
        // okumayabilir ama ilk bölümü okur. "3 risk var" demek yetmez, hangi
        // kolonun gideceğini söylemek gerekir.
        if (report.DataLossRisks.Count > 0)
        {
            sb.AppendLine("### Data loss");
            sb.AppendLine();
            foreach (var risk in report.DataLossRisks)
            {
                var where = risk.ColumnName is null ? risk.TableName : $"{risk.TableName}.{risk.ColumnName}";
                sb.AppendLine($"- **{where}** — {risk.Reason}");
            }
            sb.AppendLine();
        }

        if (report.BreakingChanges.Count > 0)
        {
            sb.AppendLine("### Breaking changes");
            sb.AppendLine();
            foreach (var change in report.BreakingChanges)
            {
                sb.AppendLine($"- {change.Description}");
                if (!string.IsNullOrWhiteSpace(change.SuggestedMitigation))
                    sb.AppendLine($"  - _Mitigation:_ {change.SuggestedMitigation}");
            }
            sb.AppendLine();
        }

        if (report.LockRisks.Count > 0)
        {
            sb.AppendLine("### Migration locks");
            sb.AppendLine();
            foreach (var risk in report.LockRisks)
                sb.AppendLine($"- `{risk.Operation}` on **{risk.TableName}** — {risk.Severity} lock");
            sb.AppendLine();
        }

        sb.AppendLine("### Rollback");
        sb.AppendLine();
        sb.AppendLine(report.Rollback.IsReversible
            ? "This change can be rolled back."
            : $"**Not reversible.** {report.Rollback.Reason}");
        sb.AppendLine();

        if (report.IndexSuggestions.Count > 0)
        {
            sb.AppendLine("### Suggested indexes");
            sb.AppendLine();
            foreach (var suggestion in report.IndexSuggestions)
                sb.AppendLine($"- `{suggestion.TableName}` — {suggestion.Reason}");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(previewDatabaseUrl))
        {
            sb.AppendLine("### Preview database");
            sb.AppendLine();
            sb.AppendLine($"A throwaway database with this schema is available: `{previewDatabaseUrl}`");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("Findings are computed by a rule engine, not a language model.");
        sb.AppendLine("Comment `/namines plan` for the migration plan, or `/namines rollback-plan`.");

        return new PullRequestReview(
            ConclusionFor(report.OverallRisk),
            $"Schema review: {report.OverallRisk}",
            Headline(report),
            sb.ToString());
    }

    private static string Headline(ImpactReport report) => report.OverallRisk switch
    {
        RiskLevel.Destructive =>
            $"This change destroys data in {report.DataLossRisks.Count} place(s). " +
            "It must not merge without an explicit human decision.",
        RiskLevel.Breaking =>
            $"This change breaks {report.BreakingChanges.Count} existing contract(s). " +
            "Dependent code will stop working unless it is updated first.",
        RiskLevel.Risky =>
            "This change is safe for data but may lock tables during migration. " +
            "Check the timing against your traffic.",
        _ => "No breaking changes and no data loss.",
    };

    /// <summary>
    /// Metin rozeti, emoji değil: GitHub'da emoji her temada aynı okunmuyor ve
    /// ekran okuyucuda anlamsız bir ad ("skull") olarak seslendiriliyor.
    /// </summary>
    private static string Badge(RiskLevel risk) => risk switch
    {
        RiskLevel.Destructive => "[DESTRUCTIVE]",
        RiskLevel.Breaking => "[BREAKING]",
        RiskLevel.Risky => "[RISKY]",
        _ => "[SAFE]",
    };
}
