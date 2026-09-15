using System.Collections.Generic;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Prompts;
using Xunit;

namespace Namines.Tests.Prompts;

public class ChunkPromptTests
{
    private static readonly SchemaChunk Chunk = new("Catalog", new[] { "products", "variants" });
    private static readonly string[] AllTables = { "users", "products", "variants", "orders" };

    [Fact]
    public void Parca_promptu_yalnizca_kendi_tablolarini_tanimlamasini_soyluyor()
    {
        var prompt = SchemaPromptBuilder.BuildChunkUserPrompt("bir magaza", DatabaseType.PostgreSQL, Chunk, AllTables);

        Assert.Contains("products", prompt);
        Assert.Contains("variants", prompt);
        Assert.Contains("ONLY", prompt);
    }

    [Fact]
    public void Parca_promptu_diger_tablolarin_isimlerini_baglam_olarak_veriyor()
    {
        // Bunlar olmadan parca, alanlar arasi yabanci anahtari yazamaz.
        var prompt = SchemaPromptBuilder.BuildChunkUserPrompt("bir magaza", DatabaseType.PostgreSQL, Chunk, AllTables);

        Assert.Contains("users", prompt);
        Assert.Contains("orders", prompt);
    }

    [Fact]
    public void Parca_promptu_deterministik_id_sozlesmesini_yaziyor()
    {
        var prompt = SchemaPromptBuilder.BuildChunkUserPrompt("bir magaza", DatabaseType.PostgreSQL, Chunk, AllTables);

        Assert.Contains("t_", prompt);
        Assert.Contains("c_", prompt);
    }

    [Fact]
    public void Plan_promptu_JSON_istiyor_ve_satir_siniri_koymuyor()
    {
        var prompt = AgentPlanPromptBuilder.BuildSystemPrompt();

        Assert.Contains("JSON", prompt);
        Assert.Contains("domains", prompt);
        // Eski "at most 12 lines" siniri, buyuk isteklerde kapsami
        // taslak turundan ONCE kilitleyen sebepti.
        Assert.DoesNotContain("12 lines", prompt);
    }

    [Fact]
    public void Plan_promptu_kapsamli_istekte_eksiksiz_liste_istiyor()
    {
        var prompt = AgentPlanPromptBuilder.BuildSystemPrompt();

        Assert.Contains("comprehensive", prompt, System.StringComparison.OrdinalIgnoreCase);
    }
}
