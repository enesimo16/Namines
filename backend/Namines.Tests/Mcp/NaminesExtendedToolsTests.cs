using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Flow.Services;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Infrastructure.Generators.Eject;
using Namines.Infrastructure.Generators.PrismaGenerator;
using Namines.Mcp;
using Xunit;

namespace Namines.Tests.Mcp;

public class NaminesExtendedToolsTests
{
    private static NaminesTools CreateTools()
    {
        var ddlFactory = new DdlGeneratorFactory();
        var ejectRegistry = new EjectGeneratorRegistry(ddlFactory);

        return new NaminesTools(
            introspection: null!,
            testRunner: null!,
            ddlFactory: ddlFactory,
            prisma: new PrismaGeneratorService(),
            ejectRegistry: ejectRegistry,
            gateway: null!,
            executor: null!,
            flowMatcher: new FlowRuleMatcher(),
            cloud: null!,
            connectionGuard: new McpConnectionGuard(
                new ConfigurationBuilder().Build(),
                NullLogger<McpConnectionGuard>.Instance));
    }

    private const string SampleSchemaJson = """
    {
      "name": "ecommerce_app",
      "tables": [
        {
          "id": "t1",
          "name": "users",
          "stableUuid": "uuid_users",
          "columns": [
            { "id": "c1", "name": "id", "type": "uuid", "isPK": true },
            { "id": "c2", "name": "email", "type": "varchar", "length": 255 }
          ]
        },
        {
          "id": "t2",
          "name": "orders",
          "stableUuid": "uuid_orders",
          "columns": [
            { "id": "c3", "name": "id", "type": "uuid", "isPK": true },
            { "id": "c4", "name": "user_id", "type": "uuid", "isFK": true },
            { "id": "c5", "name": "amount", "type": "decimal" }
          ]
        }
      ],
      "relations": [
        {
          "id": "r1",
          "sourceTableId": "t2",
          "sourceColumnId": "c4",
          "targetTableId": "t1",
          "targetColumnId": "c1"
        }
      ]
    }
    """;

    [Fact]
    public void List_artifact_targets_returns_all_registered_targets()
    {
        var tools = CreateTools();
        var json = tools.ListArtifactTargets();

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.GetArrayLength() >= 15);
    }

    [Theory]
    [InlineData("orm.drizzle")]
    [InlineData("types.typescript")]
    [InlineData("types.zod")]
    [InlineData("types.pydantic")]
    [InlineData("orm.sqlalchemy")]
    public void Generate_artifact_produces_files_for_supported_targets(string target)
    {
        var tools = CreateTools();
        var json = tools.GenerateArtifact(SampleSchemaJson, target, "PostgreSQL");

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("files", out var files));
        Assert.Equal(JsonValueKind.Object, files.ValueKind);
    }

    [Fact]
    public void Hub_search_blueprints_filters_by_query()
    {
        var tools = CreateTools();
        var json = tools.HubSearchBlueprints("fintech");

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.GetArrayLength() > 0);

        var first = doc.RootElement[0];
        Assert.Contains("fintech", first.GetProperty("slug").GetString()!.ToLowerInvariant());
    }

    [Fact]
    public void Hub_get_blueprint_retrieves_complete_schema()
    {
        var tools = CreateTools();
        var json = tools.HubGetBlueprint("fintech-ledger-engine");

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("fintech-ledger-engine", doc.RootElement.GetProperty("slug").GetString());
        Assert.True(doc.RootElement.TryGetProperty("schema", out var schema));
        Assert.True(schema.GetProperty("tables").GetArrayLength() >= 4);
    }

    [Fact]
    public void Flow_match_rules_detects_table_added()
    {
        var tools = CreateTools();
        var baseSchema = "{\"name\":\"app\",\"tables\":[]}";
        var targetSchema = SampleSchemaJson;

        var json = tools.FlowMatchRules(baseSchema, targetSchema);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("matchedRulesCount").GetInt32() > 0);
    }
}
