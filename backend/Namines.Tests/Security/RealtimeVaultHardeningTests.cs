using System.Net;
using Namines.Vault.Providers;
using Xunit;

namespace Namines.Tests.Security;

/// <summary>
/// A-6: Vault host sabitleme — doğrulanan liste ile sabitlenen adres aynı çözümlemeden
/// gelmeli ve güvensiz çözümleme ada geri düşmemeli (ad konteynerde yeniden çözülür).
/// </summary>
public sealed class VaultHostPinningTests
{
    [Fact]
    public void Public_resolution_pins_the_first_validated_address()
    {
        var pinned = DbConnectionParts.ResolveAndValidate("db.example.test",
            _ => new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Parse("1.1.1.1") }, null);

        Assert.Equal("8.8.8.8", pinned);
    }

    [Fact]
    public void A_single_non_public_address_in_the_resolved_list_rejects_the_host()
    {
        Assert.Throws<ArgumentException>(() => DbConnectionParts.ResolveAndValidate("db.example.test",
            _ => new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Parse("10.0.0.5") }, null));
    }

    [Fact]
    public void Metadata_address_is_rejected_instead_of_falling_back_to_the_hostname()
    {
        Assert.Throws<ArgumentException>(() => DbConnectionParts.ResolveAndValidate("rebind.example.test",
            _ => new[] { IPAddress.Parse("169.254.169.254") }, null));
    }

    private sealed class FixedPolicy(bool allow) : Namines.Core.Security.IDbHostAccessPolicy
    {
        public bool IsHostAllowed(string? host, out string denyReason) { denyReason = ""; return allow; }
    }

    [Fact]
    public void Private_resolution_is_accepted_when_the_policy_allows_private_hosts()
    {
        var pinned = DbConnectionParts.ResolveAndValidate("db.internal.test",
            _ => new[] { IPAddress.Parse("10.0.0.5") }, new FixedPolicy(true));

        Assert.Null(pinned); // eski davranış: ad geçer
    }

    [Fact]
    public void Private_resolution_is_rejected_when_the_policy_disallows_private_hosts()
    {
        Assert.Throws<ArgumentException>(() => DbConnectionParts.ResolveAndValidate("db.internal.test",
            _ => new[] { IPAddress.Parse("10.0.0.5") }, new FixedPolicy(false)));
    }
}

/// <summary>A-15: kod aralığındaki değerler ve öneri metinleri kaçırılmalı.</summary>
public sealed class PullRequestReviewCodeSpanTests
{
    private static string Compose(string table, string reason, string op, string? previewUrl = null)
    {
        var report = new Namines.Core.Models.ImpactReport(
            AffectedTables: Array.Empty<Namines.Core.Models.AffectedTable>(),
            AffectedRelations: Array.Empty<Namines.Core.Models.AffectedRelation>(),
            AffectedIndexes: Array.Empty<Namines.Core.Models.AffectedIndex>(),
            BreakingChanges: Array.Empty<Namines.Core.Models.BreakingChange>(),
            DataLossRisks: Array.Empty<Namines.Core.Models.DataLossRisk>(),
            LockRisks: new[] { new Namines.Core.Models.MigrationLockRisk(op, Namines.Core.Enums.LockSeverity.Brief, table) },
            IndexSuggestions: new[] { new Namines.Core.Models.MissingIndexSuggestion(table, "c", reason) },
            Rollback: new Namines.Core.Models.RollbackAssessment(true, null),
            OverallRisk: Namines.Core.Enums.RiskLevel.Safe);
        return Namines.Core.Github.PullRequestReviewComposer.Compose(report, previewUrl).Body;
    }

    [Fact]
    public void Backtick_in_a_code_span_value_cannot_break_out()
    {
        var body = Compose("t", "ok", "ALTER` **sahte** `x");

        // Değerdeki tek backtick için çit 2 backtick olmalı; ham tek-backtick çıkışı olmamalı.
        Assert.Contains("``ALTER` **sahte** `x``", body);
    }

    [Fact]
    public void Index_suggestion_text_is_escaped()
    {
        var body = Compose("t`**x**", "[a](https://kotu.example.com)", "ADD");

        Assert.DoesNotContain("[a](https://kotu.example.com)", body);
        Assert.Contains("``t`**x**``", body);
    }

    [Fact]
    public void Preview_url_with_backticks_stays_inside_the_code_span()
    {
        var body = Compose("t", "r", "ADD", "postgres://x`y");

        Assert.Contains("``postgres://x`y``", body);
    }
}

/// <summary>A-16: bitişik yazılmış yazma anahtar kelimeleri okuma sayılmamalı.</summary>
public sealed class SqlStatementKindTokenizationTests
{
    [Theory]
    [InlineData("SELECT * INTO/**/yeni FROM users")]
    [InlineData("SELECT*INTO yeni FROM users")]
    [InlineData("SELECT 1;DROP TABLE x")]
    [InlineData("WITH a AS (SELECT 1)INSERT INTO t SELECT 1")]
    public void Adjacent_write_keywords_are_not_read(string sql) =>
        Assert.NotEqual(Namines.Core.Analysis.SqlKind.Read, Namines.Core.Analysis.SqlStatementKind.Classify(sql));
}

/// <summary>A-20: SQL Server Failover Partner da doğrulanacak bir host.</summary>
public sealed class FailoverPartnerHostTests
{
    [Fact]
    public void Introspection_includes_the_failover_partner()
    {
        var hosts = Namines.Infrastructure.Services.DbIntrospectionService.ExtractHosts(
            "Server=93.184.216.34;Failover Partner=169.254.169.254;Database=x;User Id=u;Password=p", "MSSQL");

        Assert.Contains("169.254.169.254", hosts);
        Assert.Contains("93.184.216.34", hosts);
    }
}
