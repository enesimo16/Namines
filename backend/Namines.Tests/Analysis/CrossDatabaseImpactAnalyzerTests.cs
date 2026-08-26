using System.Linq;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;

namespace Namines.Tests.Analysis;

/// <summary>second-phase/10-COKLU-DB.md</summary>
public class CrossDatabaseImpactAnalyzerTests
{
    private static CrossDatabaseRelation Relation(
        string id, string sourceProject, string sourceTable, string sourceCol,
        string targetProject, string targetTable, string targetCol) => new()
    {
        Id = id,
        SourceProjectId = sourceProject, SourceTableId = sourceTable, SourceColumnId = sourceCol,
        TargetProjectId = targetProject, TargetTableId = targetTable, TargetColumnId = targetCol,
        CreatedByUserId = "u1",
    };

    [Fact]
    public void Deleting_the_source_table_of_a_relation_is_flagged_as_outgoing()
    {
        var relations = new[] { Relation("r1", "orders-db", "t_orders", "c_userid", "auth-db", "t_users", "c_id") };

        var impacts = CrossDatabaseImpactAnalyzer.FindAffected(relations, "orders-db", "t_orders");

        var impact = Assert.Single(impacts);
        Assert.Equal("outgoing", impact.Direction);
        Assert.Equal("auth-db", impact.OtherProjectId);
    }

    [Fact]
    public void Deleting_the_target_table_of_a_relation_is_flagged_as_incoming()
    {
        var relations = new[] { Relation("r1", "orders-db", "t_orders", "c_userid", "auth-db", "t_users", "c_id") };

        var impacts = CrossDatabaseImpactAnalyzer.FindAffected(relations, "auth-db", "t_users");

        var impact = Assert.Single(impacts);
        Assert.Equal("incoming", impact.Direction);
        Assert.Equal("orders-db", impact.OtherProjectId);
    }

    [Fact]
    public void A_table_with_no_cross_database_relations_is_never_flagged()
    {
        var relations = new[] { Relation("r1", "orders-db", "t_orders", "c_userid", "auth-db", "t_users", "c_id") };

        var impacts = CrossDatabaseImpactAnalyzer.FindAffected(relations, "billing-db", "t_invoices");

        Assert.Empty(impacts);
    }

    [Fact]
    public void Column_scoped_check_only_matches_the_exact_column()
    {
        var relations = new[] { Relation("r1", "orders-db", "t_orders", "c_userid", "auth-db", "t_users", "c_id") };

        var affectedColumn = CrossDatabaseImpactAnalyzer.FindAffected(relations, "orders-db", "t_orders", "c_userid");
        var unaffectedColumn = CrossDatabaseImpactAnalyzer.FindAffected(relations, "orders-db", "t_orders", "c_total");

        Assert.Single(affectedColumn);
        Assert.Empty(unaffectedColumn);
    }

    [Fact]
    public void Whole_table_deletion_ignores_column_scope_and_matches_any_column_on_that_table()
    {
        var relations = new[]
        {
            Relation("r1", "orders-db", "t_orders", "c_userid", "auth-db", "t_users", "c_id"),
            Relation("r2", "orders-db", "t_orders", "c_sellerid", "auth-db", "t_users", "c_id"),
        };

        // columnId verilmedi -> tablo seviyesinde silme, hepsi etkilenir.
        var impacts = CrossDatabaseImpactAnalyzer.FindAffected(relations, "orders-db", "t_orders");

        Assert.Equal(2, impacts.Count);
    }

    [Fact]
    public void Linked_project_ids_include_both_directions_and_are_deduplicated()
    {
        var relations = new[]
        {
            Relation("r1", "orders-db", "t_orders", "c_userid", "auth-db", "t_users", "c_id"),
            Relation("r2", "orders-db", "t_orders", "c_sellerid", "auth-db", "t_users", "c_id"),
            Relation("r3", "billing-db", "t_invoices", "c_orderid", "orders-db", "t_orders", "c_id"),
        };

        var linked = CrossDatabaseImpactAnalyzer.LinkedProjectIds(relations, "orders-db");

        Assert.Equal(2, linked.Count);
        Assert.Contains("auth-db", linked);
        Assert.Contains("billing-db", linked);
    }
}
