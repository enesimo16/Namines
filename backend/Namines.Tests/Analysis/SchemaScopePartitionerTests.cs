using System.Linq;
using Namines.Core.Analysis;
using Xunit;

namespace Namines.Tests.Analysis;

public class SchemaScopePartitionerTests
{
    private static SchemaScopePlan Plan(params (string Domain, int Tables)[] domains)
    {
        var json = new System.Text.StringBuilder("""{"schemaName":"X","domains":[""");
        for (var d = 0; d < domains.Length; d++)
        {
            if (d > 0) json.Append(',');
            var tables = Enumerable.Range(0, domains[d].Tables)
                .Select(i => $"\"{domains[d].Domain}_t{i}\"");
            json.Append($$"""{"name":"{{domains[d].Domain}}","tables":[{{string.Join(",", tables)}}]}""");
        }
        json.Append("]}");
        return SchemaScopePlan.TryParse(json.ToString())!;
    }

    [Fact]
    public void Esik_altinda_parcalanmiyor()
    {
        Assert.False(SchemaScopePartitioner.ShouldPartition(Plan(("A", 12))));
        Assert.True(SchemaScopePartitioner.ShouldPartition(Plan(("A", 13))));
    }

    [Fact]
    public void Kucuk_alanlar_tek_cagriya_gruplaniyor()
    {
        // 3+3+3 = 9 tablo hedefin altinda: tek parca olmali, uc degil.
        var chunks = SchemaScopePartitioner.Partition(Plan(("A", 3), ("B", 3), ("C", 3)));

        Assert.Single(chunks);
        Assert.Equal(9, chunks[0].OwnedTables.Count);
    }

    [Fact]
    public void Buyuk_bir_alan_boluniyor()
    {
        var chunks = SchemaScopePartitioner.Partition(Plan(("Big", 25)));

        Assert.True(chunks.Count >= 3, "25 tablo tek cagriya sigmamali");
        Assert.All(chunks, c => Assert.True(
            c.OwnedTables.Count <= SchemaScopePartitioner.TargetTablesPerChunk,
            "hicbir parca hedefi asmamali"));
    }

    [Fact]
    public void Hicbir_tablo_kaybolmuyor_ve_tekrarlanmiyor()
    {
        var plan = Plan(("A", 7), ("B", 11), ("C", 4), ("D", 20));
        var chunks = SchemaScopePartitioner.Partition(plan);

        var owned = chunks.SelectMany(c => c.OwnedTables).ToList();

        Assert.Equal(plan.TableCount, owned.Count);
        Assert.Equal(plan.AllTableNames.OrderBy(x => x), owned.OrderBy(x => x));
    }

    [Fact]
    public void Her_parcanin_okunabilir_bir_etiketi_var()
    {
        var chunks = SchemaScopePartitioner.Partition(Plan(("Identity", 5), ("Catalog", 20)));

        Assert.All(chunks, c => Assert.False(string.IsNullOrWhiteSpace(c.Label)));
    }

    // ── Final whole-branch review I2: eşik/hedef tavana göre KÜÇÜLEBİLMELİ ──
    //
    // Free'nin tavanı 6.000 token; spec'in tablo başı ~600 token ölçümüyle bu
    // ~10 tablo demek. Eski sabit eşik (12), 11-12 tablolu bir Free planını
    // TEK ÇAĞRIYA düşürüyordu — bölme mekanizması tam da bunu çözerdi ama
    // devreye hiç girmiyordu.

    [Fact]
    public void Dusuk_tavanda_esik_kuculur_free_11_tablo_artik_bolunuyor()
    {
        var plan = Plan(("A", 11));

        // Tavan verilmezse (eski davranış, geriye dönük uyum) 11 hâlâ eşiğin altında.
        Assert.False(SchemaScopePartitioner.ShouldPartition(plan));

        // Free tavanı (6.000 ≈ 10 tablo) verildiğinde 11 tablo artık BÖLÜNMELİ.
        Assert.True(SchemaScopePartitioner.ShouldPartition(plan, maxOutputTokens: 6_000));
    }

    [Fact]
    public void Yuksek_tavan_bugunku_sabitleri_asmiyor_pro_team_degismiyor()
    {
        var plan12 = Plan(("A", 12));
        var plan13 = Plan(("A", 13));

        // Pro (16.000) ve Team (32.000) tavanları bugünkü sabit eşikten (12)
        // DAHA BÜYÜK bir eşik üretmemeli — yalnızca daha düşük tavanlar
        // eşiği küçültebilir, hiçbiri onu büyütmemeli.
        Assert.False(SchemaScopePartitioner.ShouldPartition(plan12, maxOutputTokens: 16_000));
        Assert.False(SchemaScopePartitioner.ShouldPartition(plan12, maxOutputTokens: 32_000));
        Assert.True(SchemaScopePartitioner.ShouldPartition(plan13, maxOutputTokens: 16_000));
        Assert.True(SchemaScopePartitioner.ShouldPartition(plan13, maxOutputTokens: 32_000));
    }

    [Fact]
    public void Parca_hedefi_de_tavandan_turetiliyor_ama_bugunku_sabiti_asmiyor()
    {
        // Çok düşük bir tavanda (ör. 3.000 ≈ 5 tablo) parça hedefi TargetTablesPerChunk'ın (9) ALTINA inmeli.
        var chunks = SchemaScopePartitioner.Partition(Plan(("Big", 25)), maxOutputTokens: 3_000);

        Assert.All(chunks, c => Assert.True(c.OwnedTables.Count <= 5,
            "3.000 token tavanında bir parça 5 tablodan fazlasını hedeflememeli (~600 token/tablo)."));

        // Pro/Team tavanında (>= eski sabitlerin gerektirdiği) hedef bugünküyle AYNI (9) kalmalı.
        var chunksAtProCeiling = SchemaScopePartitioner.Partition(Plan(("Big", 25)), maxOutputTokens: 16_000);
        Assert.All(chunksAtProCeiling, c => Assert.True(
            c.OwnedTables.Count <= SchemaScopePartitioner.TargetTablesPerChunk));
        Assert.Contains(chunksAtProCeiling, c => c.OwnedTables.Count == SchemaScopePartitioner.TargetTablesPerChunk);
    }

    [Fact]
    public void Tavan_sifirsa_ya_da_verilmezse_bugunku_sabitler_aynen_kullaniliyor()
    {
        var plan = Plan(("A", 13));

        var chunksNoCeiling = SchemaScopePartitioner.Partition(plan);
        var chunksZeroCeiling = SchemaScopePartitioner.Partition(plan, maxOutputTokens: 0);

        Assert.Equal(chunksNoCeiling.Count, chunksZeroCeiling.Count);
        Assert.Equal(SchemaScopePartitioner.ShouldPartition(plan), SchemaScopePartitioner.ShouldPartition(plan, 0));
    }
}
