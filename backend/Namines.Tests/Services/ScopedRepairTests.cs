using System.Collections.Generic;
using System.Linq;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// Task 8: onarım turu artık BÜTÜN şemayı değil, yalnızca bulgunun adını
/// verdiği tabloları kapsıyor. Bu, 50-60 tablolu bir şemanın tek bir onarım
/// turunda token tavanına çarpıp kesilmesini (ya da tabloları sessizce
/// kaybetmesini) önlüyor — ama modelin göremediği bir tabloya yabancı anahtar
/// yazma riskini de yeniden AÇMAMALI: diğer tabloların isimleri hâlâ talimata
/// ekleniyor (bkz. GroqSchemaDraftSource.RepairAsync), yalnızca tam içerikleri
/// gönderilmiyor.
/// </summary>
public class ScopedRepairTests
{
    private static SchemaTable Table(string name, string id) =>
        new() { Id = id, Name = name };

    // ---- SchemaMerge.SpliceTables ----------------------------------------

    [Fact]
    public void SpliceTables_replaces_only_the_name_matched_table()
    {
        var full = new DatabaseSchema { SchemaId = "s", Name = "S" };
        full.Tables.Add(Table("orders", "t1"));
        full.Tables.Add(Table("customers", "t2"));

        var repairedSubset = new DatabaseSchema { SchemaId = "s", Name = "S" };
        var revisedOrders = Table("orders", "t1");
        revisedOrders.Columns.Add(new SchemaColumn { Id = "c1", Name = "id", Type = "int", IsPK = true });
        repairedSubset.Tables.Add(revisedOrders);

        var spliced = SchemaMerge.SpliceTables(full, repairedSubset);

        Assert.Equal(2, spliced.Tables.Count);
        var orders = spliced.Tables.Single(t => t.Name == "orders");
        Assert.Single(orders.Columns);
        Assert.Equal("id", orders.Columns[0].Name);

        // "customers" was not in the repaired subset — it must come back
        // EXACTLY as it was in the full schema, not dropped.
        Assert.Contains(spliced.Tables, t => t.Name == "customers");
    }

    [Fact]
    public void SpliceTables_preserves_triggers_enums_and_procedures_not_in_the_subset()
    {
        var full = new DatabaseSchema { SchemaId = "s", Name = "S" };
        full.Tables.Add(Table("orders", "t1"));
        full.Triggers.Add(new SchemaTrigger
        {
            Id = "trg1",
            TableId = "t1",
            Timing = "After",
            Event = "Insert",
            TargetEngine = DatabaseType.PostgreSQL,
            Body = "CREATE TRIGGER x ...;"
        });
        full.Enums.Add(new SchemaEnum { Id = "e1", Name = "Status", Values = { "A", "B" } });
        full.StoredProcedures.Add(new SchemaStoredProcedure
        {
            Id = "sp1",
            Name = "P",
            TargetEngine = DatabaseType.MySQL,
            Body = "BEGIN END;"
        });

        var repairedSubset = new DatabaseSchema { SchemaId = "s", Name = "S" };
        repairedSubset.Tables.Add(Table("orders", "t1"));
        // Model did not touch triggers/enums/procedures — it omits them.

        var spliced = SchemaMerge.SpliceTables(full, repairedSubset);

        Assert.Single(spliced.Triggers);
        Assert.Single(spliced.Enums);
        Assert.Single(spliced.StoredProcedures);
    }

    [Fact]
    public void SpliceTables_adds_a_table_the_model_introduced_that_is_not_in_the_full_schema()
    {
        var full = new DatabaseSchema { SchemaId = "s", Name = "S" };
        full.Tables.Add(Table("orders", "t1"));

        var repairedSubset = new DatabaseSchema { SchemaId = "s", Name = "S" };
        repairedSubset.Tables.Add(Table("orders", "t1"));
        repairedSubset.Tables.Add(Table("order_audit_log", "t99")); // brand new table

        var spliced = SchemaMerge.SpliceTables(full, repairedSubset);

        Assert.Equal(2, spliced.Tables.Count);
        Assert.Contains(spliced.Tables, t => t.Name == "order_audit_log");
    }

    // ---- RepairScope.TableNamesIn -----------------------------------------

    [Fact]
    public void TableNamesIn_matches_whole_word_case_insensitively()
    {
        var schema = new DatabaseSchema { SchemaId = "s", Name = "S" };
        schema.Tables.Add(Table("orders", "t1"));
        schema.Tables.Add(Table("order_items", "t2"));

        var findings = new List<string> { "[rule] NSL004: Table 'ORDERS' has no primary key" };

        var scoped = RepairScope.TableNamesIn(findings, schema);

        Assert.Single(scoped);
        Assert.Equal("orders", scoped[0]);
    }

    [Fact]
    public void TableNamesIn_does_not_match_a_table_name_that_is_only_a_substring_of_another()
    {
        var schema = new DatabaseSchema { SchemaId = "s", Name = "S" };
        schema.Tables.Add(Table("orders", "t1"));
        schema.Tables.Add(Table("order_items", "t2"));

        // The finding names order_items only — "orders" must NOT be picked up
        // just because it is a substring of order_items.
        var findings = new List<string> { "Table 'order_items' violates NSL010" };

        var scoped = RepairScope.TableNamesIn(findings, schema);

        Assert.Single(scoped);
        Assert.Equal("order_items", scoped[0]);
    }

    [Fact]
    public void TableNamesIn_deduplicates_tables_named_across_multiple_findings()
    {
        var schema = new DatabaseSchema { SchemaId = "s", Name = "S" };
        schema.Tables.Add(Table("orders", "t1"));
        schema.Tables.Add(Table("customers", "t2"));

        var findings = new List<string>
        {
            "Table 'orders' has no primary key",
            "Table 'orders' column 'total' should not be nullable",
            "Table 'customers' has an orphaned index",
        };

        var scoped = RepairScope.TableNamesIn(findings, schema);

        Assert.Equal(2, scoped.Count);
        Assert.Contains("orders", scoped);
        Assert.Contains("customers", scoped);
    }

    [Fact]
    public void TableNamesIn_returns_empty_list_when_no_table_name_can_be_extracted()
    {
        var schema = new DatabaseSchema { SchemaId = "s", Name = "S" };
        schema.Tables.Add(Table("orders", "t1"));

        var findings = new List<string> { "Schema-level: naming convention should be snake_case." };

        var scoped = RepairScope.TableNamesIn(findings, schema);

        Assert.Empty(scoped);
    }

    // ---- AgentQuotaReservation.TokensForScope ------------------------------

    [Fact]
    public void TokensForScope_grows_linearly_with_table_count()
    {
        var small = AgentQuotaReservation.TokensForScope(tableCount: 5, repairRounds: 0);
        var large = AgentQuotaReservation.TokensForScope(tableCount: 50, repairRounds: 0);

        // 50 tables is 10x 5 tables; the difference attributable to table
        // count alone should scale by the same linear factor.
        Assert.Equal((large - small), (50 - 5) * (small - AgentQuotaReservation.TokensForScope(0, 0)) / 5);
    }

    [Fact]
    public void TokensForScope_increases_as_repair_rounds_increase()
    {
        var noRepair = AgentQuotaReservation.TokensForScope(tableCount: 20, repairRounds: 0);
        var oneRepair = AgentQuotaReservation.TokensForScope(tableCount: 20, repairRounds: 1);
        var twoRepairs = AgentQuotaReservation.TokensForScope(tableCount: 20, repairRounds: 2);

        Assert.True(oneRepair > noRepair);
        Assert.True(twoRepairs > oneRepair);
    }

    [Fact]
    public void TokensForScope_returns_a_positive_floor_even_at_zero_tables_and_zero_rounds()
    {
        Assert.True(AgentQuotaReservation.TokensForScope(tableCount: 0, repairRounds: 0) > 0);
    }
}
