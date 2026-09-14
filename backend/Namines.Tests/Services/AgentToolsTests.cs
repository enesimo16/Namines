using System;
using System.Linq;
using System.Text.Json;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Infrastructure.AI.Agent;
using Namines.Infrastructure.Generators.DdlGenerator;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// Modelin çağırabildiği araçlar.
///
/// <b>Araçların sözleşmesi:</b> hiçbiri istisna fırlatmaz. Bir aracın patlaması
/// tüm turu düşürürdü; oysa model hata METNİNİ okuyup düzeltebilir. Testler bu
/// sözleşmeyi ve her aracın gerçekten bir şey söylediğini kilitliyor.
/// </summary>
public class AgentToolsTests
{
    private static DatabaseSchema Valid()
    {
        var schema = new DatabaseSchema { SchemaId = "s", Name = "S" };
        var table = new SchemaTable { Id = "t1", Name = "Orders" };
        table.Columns.Add(new SchemaColumn { Id = "c1", Name = "Id", Type = "INT", IsPK = true });
        table.Columns.Add(new SchemaColumn { Id = "c2", Name = "Total", Type = "DECIMAL" });
        schema.Tables.Add(table);
        return schema;
    }

    private static AgentTools Tools(DatabaseSchema? schema, DatabaseType engine = DatabaseType.PostgreSQL) =>
        new(schema, engine, new DdlGeneratorFactory());

    [Fact]
    public void Three_tools_are_offered_to_the_model()
    {
        var names = Tools(Valid()).Definitions.Select(d => d.Name).ToList();

        Assert.Contains("validate_schema", names);
        Assert.Contains("get_column_info", names);
        Assert.Contains("preview_ddl", names);
    }

    [Fact]
    public void Every_tool_parameter_schema_is_a_valid_json_object()
    {
        // Bozuk bir şema, sağlayıcıya gönderilirken patlar ve turu düşürür.
        foreach (var definition in Tools(Valid()).Definitions)
        {
            using var parsed = JsonDocument.Parse(definition.ParametersJsonSchema);
            Assert.Equal(JsonValueKind.Object, parsed.RootElement.ValueKind);
        }
    }

    [Fact]
    public void get_column_info_reports_the_columns_of_a_known_table()
    {
        var result = Tools(Valid()).Invoke(
            new AgentToolCall("1", "get_column_info", """{"tableName":"Orders"}"""));

        Assert.Contains("Total", result);
        Assert.Contains("DECIMAL", result);
    }

    [Fact]
    public void get_column_info_lists_the_known_tables_when_the_name_is_wrong()
    {
        // Yalnızca "bulunamadı" demek, modelin aynı yanlış adı tekrar denemesine
        // yol açıyor; doğru adları göstermek turu kurtarıyor.
        var result = Tools(Valid()).Invoke(
            new AgentToolCall("1", "get_column_info", """{"tableName":"Nope"}"""));

        Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Orders", result);
    }

    [Fact]
    public void validate_schema_reports_no_errors_for_a_valid_schema()
    {
        var result = Tools(Valid()).Invoke(new AgentToolCall("1", "validate_schema", "{}"));

        Assert.Contains("no errors", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void validate_schema_reports_the_engine_mismatch_rule_by_code()
    {
        var schema = Valid();
        schema.Triggers.Add(new SchemaTrigger
        {
            Id = "trg1",
            TableId = "t1",
            Timing = "After",
            Event = "Insert",
            TargetEngine = DatabaseType.MySQL,
            Body = "CREATE TRIGGER x ...;"
        });

        var result = Tools(schema).Invoke(new AgentToolCall("1", "validate_schema", "{}"));

        Assert.Contains("NSL024", result);
    }

    [Fact]
    public void validate_schema_can_check_a_schema_passed_as_an_argument()
    {
        // Model henüz YAZMADIĞI bir şemayı kontrol edebilmeli; bağlamdaki şema
        // her zaman bir önceki tur.
        var candidate = JsonSerializer.Serialize(new DatabaseSchema { SchemaId = "x", Name = "X" });
        var arguments = JsonSerializer.Serialize(new { schema = candidate });

        var result = Tools(Valid()).Invoke(new AgentToolCall("1", "validate_schema", arguments));

        Assert.Contains("no errors", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void preview_ddl_returns_generated_sql()
    {
        var result = Tools(Valid()).Invoke(new AgentToolCall("1", "preview_ddl", "{}"));

        Assert.Contains("CREATE TABLE", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void preview_ddl_returns_the_failure_message_instead_of_throwing()
    {
        // SQLite hesaplanan birincil anahtarı reddeder — üretici istisna fırlatır.
        var schema = new DatabaseSchema { SchemaId = "s", Name = "S" };
        var table = new SchemaTable { Id = "t1", Name = "Orders" };
        table.Columns.Add(new SchemaColumn
        {
            Id = "c1", Name = "Id", Type = "INT", IsPK = true, Generated = "1 + 1"
        });
        schema.Tables.Add(table);

        var result = Tools(schema, DatabaseType.SQLite).Invoke(
            new AgentToolCall("1", "preview_ddl", "{}"));

        Assert.Contains("SQLite", result);
    }

    [Fact]
    public void An_unknown_tool_name_is_reported_rather_than_throwing()
    {
        var result = Tools(Valid()).Invoke(new AgentToolCall("1", "delete_everything", "{}"));

        Assert.Contains("Unknown tool", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Malformed_tool_arguments_are_reported_rather_than_throwing()
    {
        var result = Tools(Valid()).Invoke(new AgentToolCall("1", "get_column_info", "not json at all"));

        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    [Fact]
    public void Tools_say_so_when_there_is_no_schema_yet()
    {
        // Taslak turunda bağlam boş olabilir; araç sessizce boş dönmemeli.
        var result = Tools(null).Invoke(new AgentToolCall("1", "validate_schema", "{}"));

        Assert.Contains("no schema", result, StringComparison.OrdinalIgnoreCase);
    }
}
