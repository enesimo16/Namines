using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Tests.Analysis;

/// <summary>G13 — new-phase/28-IMPACT-ANALYSIS-ENGINE.md §5. Saf fonksiyon, DB/HTTP gerektirmez.</summary>
public class AffectedCodeScannerTests
{
    private static ImpactReport ImpactWithBreakingColumnRename(string oldName, string newName, string tableName) =>
        new(
            AffectedTables: new[] { new AffectedTable(tableName, ChangeKind.RenamedFrom, new[] { newName }, oldName) },
            AffectedRelations: System.Array.Empty<AffectedRelation>(),
            AffectedIndexes: System.Array.Empty<AffectedIndex>(),
            BreakingChanges: new[] { new BreakingChange($"{oldName} renamed to {newName}", BreakingChangeKind.ColumnRenamed, tableName, oldName) },
            DataLossRisks: System.Array.Empty<DataLossRisk>(),
            LockRisks: System.Array.Empty<MigrationLockRisk>(),
            IndexSuggestions: System.Array.Empty<MissingIndexSuggestion>(),
            Rollback: new RollbackAssessment(true, null),
            OverallRisk: RiskLevel.Breaking);

    [Fact]
    public void Extracts_breaking_change_column_and_table_names()
    {
        var impact = ImpactWithBreakingColumnRename("Email", "EmailAddress", "Users");

        var identifiers = AffectedCodeScanner.ExtractCandidateIdentifiers(impact);

        Assert.Contains("Email", identifiers);
        Assert.Contains("Users", identifiers);
    }

    [Fact]
    public void Finds_matches_across_multiple_files_with_line_numbers()
    {
        var impact = ImpactWithBreakingColumnRename("Email", "EmailAddress", "Users");
        var identifiers = AffectedCodeScanner.ExtractCandidateIdentifiers(impact);

        var files = new Dictionary<string, string>
        {
            ["UserService.cs"] = "public class UserService\n{\n    public string Email { get; set; }\n}",
            ["unrelated.cs"] = "public class Product\n{\n    public decimal Price { get; set; }\n}"
        };

        var matches = AffectedCodeScanner.Scan(identifiers, files);

        Assert.Single(matches);
        Assert.Equal("UserService.cs", matches[0].FileName);
        Assert.Equal(3, matches[0].LineNumber);
        Assert.Equal("Email", matches[0].MatchedIdentifier);
    }

    [Fact]
    public void Does_not_match_substrings_only_whole_words()
    {
        var impact = ImpactWithBreakingColumnRename("Id", "UserId", "Users");
        var identifiers = AffectedCodeScanner.ExtractCandidateIdentifiers(impact);

        var files = new Dictionary<string, string> { ["f.cs"] = "public int ValidId { get; set; }" };
        var matches = AffectedCodeScanner.Scan(identifiers, files);

        // "Id" kelime sınırıyla arandığı için "ValidId" içindeki "Id" eşleşmemeli.
        Assert.Empty(matches);
    }

    [Fact]
    public void No_identifiers_means_no_matches_even_with_files()
    {
        var impact = new ImpactReport(
            System.Array.Empty<AffectedTable>(), System.Array.Empty<AffectedRelation>(), System.Array.Empty<AffectedIndex>(),
            System.Array.Empty<BreakingChange>(), System.Array.Empty<DataLossRisk>(), System.Array.Empty<MigrationLockRisk>(),
            System.Array.Empty<MissingIndexSuggestion>(), new RollbackAssessment(true, null), RiskLevel.Safe);

        var matches = AffectedCodeScanner.Scan(AffectedCodeScanner.ExtractCandidateIdentifiers(impact), new Dictionary<string, string> { ["f.cs"] = "anything" });

        Assert.Empty(matches);
    }
}
