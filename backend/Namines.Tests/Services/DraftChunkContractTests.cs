using System.Linq;
using System.Threading.Tasks;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// <see cref="ISchemaDraftSource.DraftChunkAsync"/> sözleşmesi: verilen
/// <see cref="SchemaChunk"/> ve tüm tablo adları listesiyle çağrılmalı, ve
/// yalnızca o parçanın sahiplendiği tabloları üreten bir şema dönmeli.
/// </summary>
public class DraftChunkContractTests
{
    [Fact]
    public async Task Parca_cagrisi_istenen_chunk_ve_baglami_kaydediyor()
    {
        var fake = new FakeSchemaDraftSource();
        var chunk = new SchemaChunk("orders (1/2)", new[] { "orders", "order_items" });
        var allTableNames = new[] { "orders", "order_items", "users", "products" };

        await fake.DraftChunkAsync("bir e-ticaret şeması", DatabaseType.PostgreSQL, chunk, allTableNames);

        var recordedChunk = Assert.Single(fake.ChunkCalls);
        Assert.Same(chunk, recordedChunk);

        var recordedContext = Assert.Single(fake.ChunkContexts);
        Assert.Equal(allTableNames, recordedContext);
    }

    [Fact]
    public async Task Parca_cagrisi_yalnizca_sahiplenilen_tablolari_ureten_sema_donuyor()
    {
        var fake = new FakeSchemaDraftSource();
        var chunk = new SchemaChunk("catalog", new[] { "products", "categories" });
        var allTableNames = new[] { "products", "categories", "orders" };

        var schema = await fake.DraftChunkAsync(
            "bir e-ticaret şeması", DatabaseType.PostgreSQL, chunk, allTableNames);

        Assert.Equal(new[] { "products", "categories" }, schema.Tables.Select(t => t.Name));

        // Her tabloya PK verilmiş olmalı: aksi hâlde NslValidator bulgu üretir
        // ve hat onarım döngüsüne girer.
        Assert.All(schema.Tables, t => Assert.Contains(t.Columns, c => c.IsPK));
    }

    [Fact]
    public async Task Basarisiz_olarak_isaretlenen_etiket_istisna_firlatiyor()
    {
        var fake = new FakeSchemaDraftSource();
        fake.FailingChunkLabels.Add("broken");
        var chunk = new SchemaChunk("broken", new[] { "x" });

        await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => fake.DraftChunkAsync("prompt", DatabaseType.PostgreSQL, chunk, new[] { "x" }));
    }
}
