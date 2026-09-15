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
}
