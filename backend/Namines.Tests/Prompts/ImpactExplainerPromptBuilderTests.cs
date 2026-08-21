using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Core.Prompts;

namespace Namines.Tests.Prompts;

/// <summary>G15 — AI Impact Explainer. Prompt-üretim mantığı saf, AI çağrısı gerektirmez.</summary>
public class ImpactExplainerPromptBuilderTests
{
    private static ImpactReport SafeImpact() => new(
        AffectedTables: new[] { new AffectedTable("Posts", ChangeKind.Added, System.Array.Empty<string>()) },
        AffectedRelations: System.Array.Empty<AffectedRelation>(),
        AffectedIndexes: System.Array.Empty<AffectedIndex>(),
        BreakingChanges: System.Array.Empty<BreakingChange>(),
        DataLossRisks: System.Array.Empty<DataLossRisk>(),
        LockRisks: System.Array.Empty<MigrationLockRisk>(),
        IndexSuggestions: System.Array.Empty<MissingIndexSuggestion>(),
        Rollback: new RollbackAssessment(true, null),
        OverallRisk: RiskLevel.Safe);

    private static ImpactReport BreakingImpact() => new(
        AffectedTables: new[] { new AffectedTable("Users", ChangeKind.RenamedFrom, new[] { "Email" }, "Users_Old") },
        AffectedRelations: System.Array.Empty<AffectedRelation>(),
        AffectedIndexes: System.Array.Empty<AffectedIndex>(),
        BreakingChanges: new[] { new BreakingChange("Column Email removed from Users", BreakingChangeKind.ColumnRemoved, "Users", "Email", "Add a default value first") },
        DataLossRisks: new[] { new DataLossRisk("Users", "Email", "Column will be dropped, existing data is lost") },
        LockRisks: new[] { new MigrationLockRisk("ALTER TABLE ADD COLUMN NOT NULL", LockSeverity.Blocking, "Orders", "Add nullable first, backfill, then set NOT NULL") },
        IndexSuggestions: System.Array.Empty<MissingIndexSuggestion>(),
        Rollback: new RollbackAssessment(false, "Data loss makes rollback impossible"),
        OverallRisk: RiskLevel.Breaking);

    [Fact]
    public void System_prompt_forbids_inventing_new_findings()
    {
        var (systemPrompt, _) = ImpactExplainerPromptBuilder.Build(SafeImpact());
        Assert.Contains("NEVER invent", systemPrompt);
    }

    [Fact]
    public void User_prompt_includes_overall_risk_level()
    {
        var (_, userPrompt) = ImpactExplainerPromptBuilder.Build(BreakingImpact());
        Assert.Contains("Breaking", userPrompt);
    }

    [Fact]
    public void User_prompt_includes_actual_table_and_column_names_from_breaking_changes()
    {
        var (_, userPrompt) = ImpactExplainerPromptBuilder.Build(BreakingImpact());

        Assert.Contains("Users", userPrompt);
        Assert.Contains("Email", userPrompt);
        Assert.Contains("Add a default value first", userPrompt);
    }

    [Fact]
    public void User_prompt_includes_data_loss_and_lock_risk_details()
    {
        var (_, userPrompt) = ImpactExplainerPromptBuilder.Build(BreakingImpact());

        Assert.Contains("Column will be dropped, existing data is lost", userPrompt);
        Assert.Contains("Blocking", userPrompt);
        Assert.Contains("Add nullable first, backfill, then set NOT NULL", userPrompt);
    }

    [Fact]
    public void User_prompt_reports_irreversible_rollback_with_reason()
    {
        var (_, userPrompt) = ImpactExplainerPromptBuilder.Build(BreakingImpact());
        Assert.Contains("cannot be automatically rolled back", userPrompt);
        Assert.Contains("Data loss makes rollback impossible", userPrompt);
    }

    [Fact]
    public void Safe_impact_explicitly_states_no_risks_found_instead_of_omitting_sections()
    {
        var (_, userPrompt) = ImpactExplainerPromptBuilder.Build(SafeImpact());

        Assert.Contains("No breaking changes, data loss risks, or lock risks were found.", userPrompt);
        Assert.DoesNotContain("Breaking changes:", userPrompt);
        Assert.DoesNotContain("Data loss risks:", userPrompt);
    }

    [Fact]
    public void Reversible_rollback_is_stated_plainly()
    {
        var (_, userPrompt) = ImpactExplainerPromptBuilder.Build(SafeImpact());
        Assert.Contains("This change can be safely rolled back.", userPrompt);
    }
}
