using System.Text;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;
using Xunit;

namespace Namines.Tests.Ddl;

public class TriggerProcedureSqlTests
{
    private static DatabaseSchema SchemaWithPostgresTrigger() => new()
    {
        SchemaId = "s1",
        Name = "Test",
        Triggers =
        {
            new SchemaTrigger
            {
                Id = "trg1",
                TableId = "t1",
                Timing = "After",
                Event = "Insert",
                TargetEngine = DatabaseType.PostgreSQL,
                Body = "BEGIN RAISE NOTICE 'x'; RETURN NEW; END;"
            }
        }
    };

    [Fact]
    public void Trigger_is_appended_when_target_engine_matches()
    {
        var sb = new StringBuilder();
        TriggerProcedureSql.Append(sb, SchemaWithPostgresTrigger(), DatabaseType.PostgreSQL);

        Assert.Contains("RAISE NOTICE", sb.ToString());
    }

    [Fact]
    public void Trigger_is_skipped_when_target_engine_does_not_match()
    {
        var sb = new StringBuilder();
        TriggerProcedureSql.Append(sb, SchemaWithPostgresTrigger(), DatabaseType.MSSQL);

        Assert.DoesNotContain("RAISE NOTICE", sb.ToString());
    }

    [Fact]
    public void Empty_triggers_and_procedures_produce_no_output()
    {
        var sb = new StringBuilder();
        TriggerProcedureSql.Append(sb, new DatabaseSchema(), DatabaseType.PostgreSQL);

        Assert.Equal(string.Empty, sb.ToString());
    }

    [Fact]
    public void Stored_procedure_is_appended_when_target_engine_matches()
    {
        var schema = new DatabaseSchema
        {
            StoredProcedures =
            {
                new SchemaStoredProcedure
                {
                    Id = "sp1",
                    Name = "DoThing",
                    TargetEngine = DatabaseType.MySQL,
                    Body = "BEGIN SELECT 1; END;"
                }
            }
        };

        var sb = new StringBuilder();
        TriggerProcedureSql.Append(sb, schema, DatabaseType.MySQL);

        Assert.Contains("DoThing", sb.ToString());
    }
}
