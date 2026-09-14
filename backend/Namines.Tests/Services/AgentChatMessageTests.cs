using Namines.Core.Interfaces;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// Tool-calling mesaj tipleri — OpenAI uyumlu tel biçimine çevrilmeden önceki
/// saf model. AI çağrısı gerektirmez.
/// </summary>
public class AgentChatMessageTests
{
    [Fact]
    public void A_tool_result_message_carries_the_call_id_it_answers()
    {
        // Çağrı id'si olmadan sağlayıcı, cevabın hangi araca ait olduğunu
        // bilemez ve turu reddeder.
        var message = new AgentChatMessage("tool", "{\"ok\":true}", null, "call_1");

        Assert.Equal("tool", message.Role);
        Assert.Equal("call_1", message.ToolCallId);
    }

    [Fact]
    public void An_assistant_message_can_carry_tool_calls_without_content()
    {
        var call = new AgentToolCall("call_1", "validate_schema", "{}");
        var message = new AgentChatMessage("assistant", null, new[] { call });

        Assert.Null(message.Content);
        Assert.Single(message.ToolCalls!);
        Assert.Equal("validate_schema", message.ToolCalls![0].Name);
    }

    [Fact]
    public void A_response_with_no_tool_calls_exposes_an_empty_list()
    {
        var response = new AgentChatResponse("done", System.Array.Empty<AgentToolCall>());

        Assert.Empty(response.ToolCalls);
        Assert.Equal("done", response.Content);
    }

    [Fact]
    public void A_tool_definition_keeps_its_parameter_schema_as_raw_json()
    {
        // Şema METİN olarak taşınıyor; tel biçimine çevrilirken JSON NESNESİ
        // olarak gömülmesi gerekiyor. Metin olarak gönderilirse sağlayıcı
        // aracı hiç tanımıyor.
        var definition = new AgentToolDefinition(
            "preview_ddl", "Compile the schema.", """{"type":"object","properties":{}}""");

        var parsed = System.Text.Json.JsonDocument.Parse(definition.ParametersJsonSchema);

        Assert.Equal(System.Text.Json.JsonValueKind.Object, parsed.RootElement.ValueKind);
    }
}
