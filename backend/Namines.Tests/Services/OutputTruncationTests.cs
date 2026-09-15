using System.Text.Json;
using Namines.Core.Interfaces;
using Namines.Infrastructure.AI;
using Xunit;

namespace Namines.Tests.Services;

public class OutputTruncationTests
{
    private static JsonElement Response(string finishReason, string content) =>
        JsonSerializer.Deserialize<JsonElement>($$$"""
        {"choices":[{"finish_reason":"{{{finishReason}}}","message":{"content":{{{JsonSerializer.Serialize(content)}}}}}]}
        """);

    [Fact]
    public void Tamamlanmis_yanit_icerigi_donuyor()
    {
        var content = GroqResponseReader.ReadContentOrThrow(Response("stop", "{\"tables\":[]}"), 16000);

        Assert.Equal("{\"tables\":[]}", content);
    }

    [Fact]
    public void Uzunluk_sinirinda_kesilen_yanit_AYRI_bir_istisna_uretiyor()
    {
        // Genel bir JsonException olarak birakmak, cagiranin sicakligi
        // artirip YENIDEN denemesine yol aciyordu: uzunluk hatasinin caresi
        // sicaklik degil, kullaniciya gercek sebebi soylemek.
        var ex = Assert.Throws<AiOutputTruncatedException>(
            () => GroqResponseReader.ReadContentOrThrow(Response("length", "{\"tables\":[{\"na"), 16000));

        Assert.Equal(16000, ex.MaxTokens);
    }

    [Fact]
    public void Kesilme_mesaji_gercek_sebebi_soyluyor()
    {
        var ex = Assert.Throws<AiOutputTruncatedException>(
            () => GroqResponseReader.ReadContentOrThrow(Response("length", "kesik"), 6000));

        Assert.Contains("6000", ex.Message);
    }

    /// <summary>
    /// GroqAIService.CompleteAsync'in (Plan/Repair/DraftChunk'ın paylaştığı ham
    /// sohbet yolu) gönderdiği ile aynı şekil: "content" yok, "tool_calls" var.
    /// </summary>
    private static JsonElement ToolCallResponse(string finishReason) =>
        JsonSerializer.Deserialize<JsonElement>($$$"""
        {"choices":[{"finish_reason":"{{{finishReason}}}","message":{"content":null,"tool_calls":[
            {"id":"call_1","type":"function","function":{"name":"validate_schema","arguments":"{}"}}
        ]}}]}
        """);

    [Fact]
    public void CompleteAsync_seklindeki_yanitta_uzunluk_kesilmesi_istisna_uretiyor()
    {
        // Task 6 fix-round: CompleteAsync (PlanAsync, RepairWithToolsAsync ve
        // DraftChunkAsync'in paylaştığı yol) artık ReadContentOrThrow'dan
        // geçiyor — bir parça/plan/onarım yanıtı tavana çarparsa bu da
        // AiOutputTruncatedException olarak yüzeye çıkmalı, belirsiz bir JSON
        // ayrıştırma hatası olarak değil.
        var ex = Assert.Throws<AiOutputTruncatedException>(
            () => GroqResponseReader.ReadContentOrThrow(Response("length", "{\"tables\":[{\"na"), 6000));

        Assert.Equal(6000, ex.MaxTokens);
    }

    [Fact]
    public void Arac_cagrisi_turunda_finish_reason_tool_calls_kesilme_SAYILMIYOR()
    {
        // "tool_calls" meşru bir tamamlanma nedeni — model içerik yazmadan
        // araç çağırdığında sağlayıcı bunu bildiriyor. Bunu kesilme sayıp
        // istisna fırlatmak, onarım turundaki araç döngüsünü kırardı.
        var content = GroqResponseReader.ReadContentOrThrow(ToolCallResponse("tool_calls"), 6000);

        Assert.Equal(string.Empty, content);
    }
}
