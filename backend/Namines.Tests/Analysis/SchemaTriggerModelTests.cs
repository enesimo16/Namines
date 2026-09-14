using Namines.Core.Enums;
using Namines.Core.Models;
using Xunit;

namespace Namines.Tests.Analysis;

public class SchemaTriggerModelTests
{
    [Fact]
    public void DatabaseSchema_defaults_to_empty_triggers_and_procedures()
    {
        var schema = new DatabaseSchema();

        Assert.Empty(schema.Triggers);
        Assert.Empty(schema.StoredProcedures);
    }

    [Fact]
    public void SchemaTrigger_carries_engine_gated_raw_body()
    {
        var trigger = new SchemaTrigger
        {
            Id = "trg1",
            TableId = "t_orders",
            Timing = "After",
            Event = "Insert",
            TargetEngine = DatabaseType.PostgreSQL,
            Body = "BEGIN RAISE NOTICE 'order inserted'; RETURN NEW; END;"
        };

        Assert.Equal(DatabaseType.PostgreSQL, trigger.TargetEngine);
        Assert.Equal("t_orders", trigger.TableId);
    }

    [Fact]
    public void SchemaStoredProcedure_carries_parameters()
    {
        var proc = new SchemaStoredProcedure
        {
            Id = "sp1",
            Name = "RecalculateOrderTotal",
            TargetEngine = DatabaseType.PostgreSQL,
            Parameters = { new SchemaStoredProcedureParameter { Name = "order_id", Type = "INT" } },
            Body = "BEGIN UPDATE orders SET total = 0 WHERE id = order_id; END;"
        };

        Assert.Single(proc.Parameters);
        Assert.Equal("order_id", proc.Parameters[0].Name);
    }
}
