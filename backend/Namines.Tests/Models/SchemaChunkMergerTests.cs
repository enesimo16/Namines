using System.Collections.Generic;
using System.Linq;
using Namines.Core.Models;
using Xunit;

namespace Namines.Tests.Models;

public class SchemaChunkMergerTests
{
    private static DatabaseSchema Chunk(params SchemaTable[] tables)
    {
        var s = new DatabaseSchema();
        foreach (var t in tables) s.Tables.Add(t);
        return s;
    }

    private static SchemaTable Table(string id, string name) =>
        new() { Id = id, Name = name };

    [Fact]
    public void Tum_parcalarin_tablolari_birlesiyor()
    {
        var result = SchemaChunkMerger.Merge("Shop", new[]
        {
            Chunk(Table("t_users", "users"), Table("t_roles", "roles")),
            Chunk(Table("t_products", "products")),
        });

        Assert.Equal(3, result.Schema.Tables.Count);
        Assert.Equal("Shop", result.Schema.Name);
    }

    [Fact]
    public void Ayni_isimli_tablo_tekillestiriliyor_ve_not_uretiyor()
    {
        var result = SchemaChunkMerger.Merge("X", new[]
        {
            Chunk(Table("t_users", "users")),
            Chunk(Table("t_users_2", "Users")),
        });

        Assert.Single(result.Schema.Tables);
        Assert.Contains(result.Notes, n => n.Contains("users"));
    }

    [Fact]
    public void Alanlar_arasi_iliski_id_si_isimle_cozuluyor()
    {
        // Parca B, A'nin urettigi gercek id'yi goremez; sozlesmeye gore
        // "t_users" yazar. A tabloyu baska bir id ile uretmis olsa bile
        // merge bunu isimle eslestirip yeniden yazmali.
        var a = Chunk(Table("users_tbl_01", "users"));
        var b = Chunk(Table("t_orders", "orders"));
        b.Relations.Add(new SchemaRelation
        {
            Id = "r1", Type = "OneToMany",
            SourceTableId = "t_orders", TargetTableId = "t_users",
        });

        var result = SchemaChunkMerger.Merge("X", new[] { a, b });

        var relation = Assert.Single(result.Schema.Relations);
        Assert.Equal("users_tbl_01", relation.TargetTableId);
    }

    [Fact]
    public void Cozulemeyen_iliski_dusuruluyor_ve_not_uretiyor()
    {
        var chunk = Chunk(Table("t_orders", "orders"));
        chunk.Relations.Add(new SchemaRelation
        {
            Id = "r1", Type = "OneToMany",
            SourceTableId = "t_orders", TargetTableId = "t_hicyok",
        });

        var result = SchemaChunkMerger.Merge("X", new[] { chunk });

        Assert.Empty(result.Schema.Relations);
        Assert.Contains(result.Notes, n => n.Contains("hicyok"));
    }

    [Fact]
    public void Temiz_birlesmede_not_uretilmiyor()
    {
        var result = SchemaChunkMerger.Merge("X", new[]
        {
            Chunk(Table("t_users", "users")),
            Chunk(Table("t_orders", "orders")),
        });

        Assert.Empty(result.Notes);
    }

    [Fact]
    public void Bos_parca_listesi_bos_sema_veriyor_patlamiyor()
    {
        var result = SchemaChunkMerger.Merge("X", new List<DatabaseSchema>());

        Assert.Empty(result.Schema.Tables);
    }
}
