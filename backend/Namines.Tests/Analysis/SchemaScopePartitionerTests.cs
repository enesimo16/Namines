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
}
