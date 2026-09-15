using System.Collections.Generic;
using Namines.Core.Analysis;
using Namines.Core.Models;
using Xunit;

namespace Namines.Tests.Analysis;

public class AutomationRuleMatcherTests
{
    private static DatabaseSchema SchemaWith(params (string Id, string Name)[] tables)
    {
        var schema = new DatabaseSchema();
        foreach (var (id, name) in tables)
            schema.Tables.Add(new SchemaTable { Id = id, Name = name });
        return schema;
    }

    [Fact]
    public void Tablo_silinince_scopeTableId_ile_eslesen_kural_tetikleniyor()
    {
        var oldSchema = SchemaWith(("t1", "orders"));
        var newSchema = SchemaWith();
        var diff = new SchemaDiffResult { RemovedTables = { "orders" } };
        var rules = new List<AutomationRule>
        {
            new() { Id = "r1", ScopeTableId = "t1", TriggerType = "TableDeleted", ActionType = "Webhook", Enabled = true },
        };

        var matched = AutomationRuleMatcher.Match(diff, oldSchema, newSchema, rules);

        Assert.Single(matched);
        Assert.Equal("r1", matched[0].Id);
    }

    [Fact]
    public void Baska_tabloya_ait_kural_tetiklenmiyor()
    {
        var oldSchema = SchemaWith(("t1", "orders"), ("t2", "users"));
        var newSchema = SchemaWith(("t2", "users"));
        var diff = new SchemaDiffResult { RemovedTables = { "orders" } };
        var rules = new List<AutomationRule>
        {
            new() { Id = "r1", ScopeTableId = "t2", TriggerType = "TableDeleted", ActionType = "Webhook", Enabled = true },
        };

        var matched = AutomationRuleMatcher.Match(diff, oldSchema, newSchema, rules);

        Assert.Empty(matched);
    }

    [Fact]
    public void Devre_disi_kural_tetiklenmiyor()
    {
        var oldSchema = SchemaWith(("t1", "orders"));
        var newSchema = SchemaWith();
        var diff = new SchemaDiffResult { RemovedTables = { "orders" } };
        var rules = new List<AutomationRule>
        {
            new() { Id = "r1", ScopeTableId = "t1", TriggerType = "TableDeleted", ActionType = "Webhook", Enabled = false },
        };

        Assert.Empty(AutomationRuleMatcher.Match(diff, oldSchema, newSchema, rules));
    }

    [Fact]
    public void Proje_geneli_kural_scopeTableId_null_herhangi_bir_tablo_eklenince_tetikleniyor()
    {
        var oldSchema = SchemaWith();
        var newSchema = SchemaWith(("t1", "orders"));
        var diff = new SchemaDiffResult { AddedTables = { "orders" } };
        var rules = new List<AutomationRule>
        {
            new() { Id = "r1", ScopeTableId = null, TriggerType = "TableAdded", ActionType = "Toast", Enabled = true },
        };

        var matched = AutomationRuleMatcher.Match(diff, oldSchema, newSchema, rules);

        Assert.Single(matched);
    }

    [Fact]
    public void Kolon_eklenince_tabloya_bagli_kural_tetikleniyor()
    {
        var oldSchema = SchemaWith(("t1", "orders"));
        var newSchema = SchemaWith(("t1", "orders"));
        var diff = new SchemaDiffResult
        {
            ModifiedTables = { new TableDiffDetail { TableName = "orders", AddedColumns = { "total" } } },
        };
        var rules = new List<AutomationRule>
        {
            new() { Id = "r1", ScopeTableId = "t1", TriggerType = "ColumnAdded", ActionType = "DbaCheck", Enabled = true },
        };

        Assert.Single(AutomationRuleMatcher.Match(diff, oldSchema, newSchema, rules));
    }

    [Fact]
    public void Farkli_tetikleyici_tipindeki_kural_eslesmiyor()
    {
        var oldSchema = SchemaWith(("t1", "orders"));
        var newSchema = SchemaWith();
        var diff = new SchemaDiffResult { RemovedTables = { "orders" } };
        var rules = new List<AutomationRule>
        {
            new() { Id = "r1", ScopeTableId = "t1", TriggerType = "TableAdded", ActionType = "Webhook", Enabled = true },
        };

        Assert.Empty(AutomationRuleMatcher.Match(diff, oldSchema, newSchema, rules));
    }
}
