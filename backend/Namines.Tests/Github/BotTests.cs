using Namines.Core.Enums;
using Namines.Core.Github;
using Namines.Core.Models;

namespace Namines.Tests.Github;

/// <summary>
/// Namines Bot (new-phase/11-MIGRATIONS-BRANCHING.md §7).
///
/// İki ayrı sorumluluk test ediliyor ve ikisi de sessizce yanlış olabilir:
/// <b>imza doğrulama</b> (yanlışsa uç herkese açık) ve <b>yorum metni</b>
/// (yanlışsa insan yanlış merge kararı verir).
/// </summary>
public class BotTests
{
    private const string Secret = "webhook-secret";

    // ── İmza ─────────────────────────────────────────────────────────────────

    [Fact]
    public void A_correct_signature_is_accepted()
    {
        const string payload = "{\"action\":\"opened\"}";
        var signature = "sha256=" + GithubWebhook.Compute(Secret, payload);

        Assert.True(GithubWebhook.IsSignatureValid(Secret, payload, signature));
    }

    [Fact]
    public void A_tampered_payload_is_rejected()
    {
        const string payload = "{\"action\":\"opened\"}";
        var signature = "sha256=" + GithubWebhook.Compute(Secret, payload);

        Assert.False(GithubWebhook.IsSignatureValid(Secret, "{\"action\":\"closed\"}", signature));
    }

    [Fact]
    public void A_wrong_secret_is_rejected()
    {
        const string payload = "{}";
        var signature = "sha256=" + GithubWebhook.Compute("other-secret", payload);

        Assert.False(GithubWebhook.IsSignatureValid(Secret, payload, signature));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_missing_secret_rejects_everything(string? secret)
    {
        // "Sır yoksa doğrulamayı atla" davranışı, yapılandırmayı unutan bir
        // kurulumda ucu tamamen açık bırakırdı.
        const string payload = "{}";
        var signature = "sha256=" + GithubWebhook.Compute(Secret, payload);

        Assert.False(GithubWebhook.IsSignatureValid(secret, payload, signature));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("deadbeef")]
    [InlineData("sha1=deadbeef")]
    public void A_malformed_signature_header_is_rejected(string? header)
    {
        Assert.False(GithubWebhook.IsSignatureValid(Secret, "{}", header));
    }

    // ── Komutlar ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/namines plan", "plan")]
    [InlineData("/namines preview", "preview")]
    [InlineData("/NAMINES Approve", "approve")]
    [InlineData("/namines rollback-plan", "rollback-plan")]
    public void Known_commands_are_parsed(string comment, string expected)
    {
        Assert.Equal(expected, BotCommandParser.Parse(comment)!.Name);
    }

    [Fact]
    public void An_unknown_command_is_not_guessed()
    {
        // "aprove" → "approve" tahmini, yıkıcı bir değişikliğin yazım hatasıyla
        // onaylanması demek olurdu.
        Assert.Null(BotCommandParser.Parse("/namines aprove"));
    }

    [Fact]
    public void A_command_in_the_middle_of_a_sentence_is_ignored()
    {
        // Alıntı yapan ya da öneri veren bir yorum kazara komut tetiklememeli.
        Assert.Null(BotCommandParser.Parse("you should try: /namines approve"));
    }

    [Fact]
    public void A_command_on_its_own_line_within_a_longer_comment_is_found()
    {
        var comment = "Looks good to me.\n\n/namines plan\n\nThanks!";

        Assert.Equal("plan", BotCommandParser.Parse(comment)!.Name);
    }

    [Fact]
    public void A_bare_prefix_asks_for_help()
    {
        Assert.Equal("help", BotCommandParser.Parse("/namines")!.Name);
    }

    [Fact]
    public void A_comment_without_a_command_returns_null()
    {
        Assert.Null(BotCommandParser.Parse("Nice work on this migration."));
    }

    // ── PR yorumu ────────────────────────────────────────────────────────────

    private static ImpactReport Report(RiskLevel risk, int dataLoss = 0, int breaking = 0) =>
        new(
            AffectedTables: new List<AffectedTable> { new("users", ChangeKind.Modified, Array.Empty<string>()) },
            AffectedRelations: Array.Empty<AffectedRelation>(),
            AffectedIndexes: Array.Empty<AffectedIndex>(),
            BreakingChanges: Enumerable.Range(0, breaking)
                .Select(i => new BreakingChange($"breaking {i}", BreakingChangeKind.ColumnRemoved, "users"))
                .ToList(),
            DataLossRisks: Enumerable.Range(0, dataLoss)
                .Select(i => new DataLossRisk("users", $"col{i}", "The column and its data are dropped."))
                .ToList(),
            LockRisks: Array.Empty<MigrationLockRisk>(),
            IndexSuggestions: Array.Empty<MissingIndexSuggestion>(),
            Rollback: new RollbackAssessment(false, "DROP COLUMN cannot be undone."),
            OverallRisk: risk);

    [Theory]
    [InlineData(RiskLevel.Safe, "success")]
    [InlineData(RiskLevel.Risky, "neutral")]
    [InlineData(RiskLevel.Breaking, "failure")]
    [InlineData(RiskLevel.Destructive, "failure")]
    public void The_check_conclusion_follows_the_risk(RiskLevel risk, string expected)
    {
        // Destructive'e "neutral" demek merge'ü engellemez ve check'i süse
        // çevirir; özelliğin tüm amacı bunu engellemek.
        Assert.Equal(expected, PullRequestReviewComposer.ConclusionFor(risk));
    }

    [Fact]
    public void Data_loss_is_named_column_by_column()
    {
        // "3 risk var" demek yetmez; incelemeci hangi kolonun gideceğini görmeli.
        var review = PullRequestReviewComposer.Compose(Report(RiskLevel.Destructive, dataLoss: 2));

        Assert.Contains("users.col0", review.Body);
        Assert.Contains("users.col1", review.Body);
    }

    [Fact]
    public void An_irreversible_change_says_so()
    {
        var review = PullRequestReviewComposer.Compose(Report(RiskLevel.Destructive, dataLoss: 1));

        Assert.Contains("Not reversible", review.Body);
        Assert.Contains("DROP COLUMN cannot be undone.", review.Body);
    }

    [Fact]
    public void A_safe_change_reads_as_safe_and_passes()
    {
        var review = PullRequestReviewComposer.Compose(Report(RiskLevel.Safe));

        Assert.Equal("success", review.Conclusion);
        Assert.Contains("[SAFE]", review.Body);
        Assert.DoesNotContain("### Data loss", review.Body);
    }

    [Fact]
    public void The_preview_database_is_mentioned_only_when_there_is_one()
    {
        var without = PullRequestReviewComposer.Compose(Report(RiskLevel.Safe));
        var with = PullRequestReviewComposer.Compose(Report(RiskLevel.Safe), "postgres://localhost:5433/db");

        Assert.DoesNotContain("Preview database", without.Body);
        Assert.Contains("postgres://localhost:5433/db", with.Body);
    }

    [Fact]
    public void The_comment_says_the_findings_are_not_from_a_language_model()
    {
        // İncelemeci bu tabloya güvenip merge kararı veriyor; bulguların
        // deterministik olduğunu bilmesi, güvenin dayanağı.
        var review = PullRequestReviewComposer.Compose(Report(RiskLevel.Breaking, breaking: 1));

        Assert.Contains("rule engine", review.Body);
    }
}
