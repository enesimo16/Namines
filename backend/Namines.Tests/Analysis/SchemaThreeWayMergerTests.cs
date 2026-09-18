using System.Linq;
using Namines.Core.Analysis;
using Namines.Core.Models;

namespace Namines.Tests.Analysis;

/// <summary>github/03-COKLU-GELISTIRICI-MERGE.md — 3-yollu birleştirme kararları.</summary>
public class SchemaThreeWayMergerTests
{
    private static SchemaColumn Column(string uuid, string name, string type = "int") =>
        new() { Id = uuid, StableUuid = uuid, Name = name, Type = type };

    private static SchemaTable Table(string uuid, string name, params SchemaColumn[] columns) =>
        new() { Id = uuid, StableUuid = uuid, Name = name, Columns = columns.ToList() };

    private static DatabaseSchema Schema(params SchemaTable[] tables) =>
        new() { Tables = tables.ToList() };

    private static DatabaseSchema Clone(DatabaseSchema schema) =>
        System.Text.Json.JsonSerializer.Deserialize<DatabaseSchema>(
            System.Text.Json.JsonSerializer.Serialize(schema))!;

    [Fact]
    public void A_change_only_one_side_made_is_merged_without_asking()
    {
        // Bugünkü iki yollu diff'in yapamadığı şey tam olarak bu: ortak ata
        // olmadan "yalnızca onlar değiştirdi" ile "ikimiz de değiştirdik"
        // ayırt edilemiyor ve kullanıcıya HER fark soruluyor.
        var baseSchema = Schema(Table("t1", "users", Column("c1", "email", "varchar")));
        var ours = Clone(baseSchema);
        var theirs = Clone(baseSchema);
        theirs.Tables[0].Columns[0].Type = "text";

        var result = SchemaThreeWayMerger.Merge(baseSchema, ours, theirs);

        Assert.Empty(result.Conflicts);
        Assert.Equal("text", result.Merged.Tables[0].Columns[0].Type);
        Assert.NotEmpty(result.AutoMerged);
    }

    [Fact]
    public void The_same_change_on_both_sides_is_not_a_conflict()
    {
        var baseSchema = Schema(Table("t1", "users", Column("c1", "email", "varchar")));
        var ours = Clone(baseSchema); ours.Tables[0].Columns[0].Type = "text";
        var theirs = Clone(baseSchema); theirs.Tables[0].Columns[0].Type = "text";

        var result = SchemaThreeWayMerger.Merge(baseSchema, ours, theirs);

        Assert.Empty(result.Conflicts);
        Assert.Equal("text", result.Merged.Tables[0].Columns[0].Type);
    }

    [Fact]
    public void Different_changes_to_the_same_column_are_a_conflict()
    {
        var baseSchema = Schema(Table("t1", "users", Column("c1", "status", "varchar")));
        var ours = Clone(baseSchema); ours.Tables[0].Columns[0].Type = "text";
        var theirs = Clone(baseSchema); theirs.Tables[0].Columns[0].Type = "int";

        var result = SchemaThreeWayMerger.Merge(baseSchema, ours, theirs);

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(MergeConflictKind.ColumnModified, conflict.Kind);
        Assert.Equal("status", conflict.ColumnName);
        Assert.False(conflict.Blocking);   // insan seçebilir
    }

    [Fact]
    public void Two_people_adding_the_same_column_name_is_caught_as_a_collision()
    {
        // BU, KULLANICININ ANLATTIĞI SENARYO. İki branch'te eklenen kolonların
        // StableUuid'leri FARKLI olur; yalnızca uuid'e bakan bir birleştirici
        // ikisini de ayrı kolon sanıp EKLER ve şema iki "status" kolonuyla
        // bozulur. Ad çakışması ayrıca aranmak zorunda.
        var baseSchema = Schema(Table("t1", "users"));
        var ours = Schema(Table("t1", "users", Column("c-ayse", "status", "varchar")));
        var theirs = Schema(Table("t1", "users", Column("c-mehmet", "status", "int")));

        var result = SchemaThreeWayMerger.Merge(baseSchema, ours, theirs);

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(MergeConflictKind.NameCollision, conflict.Kind);
        Assert.Equal("status", conflict.ColumnName);

        // Birleşen şemada İKİ tane "status" kalmamalı — bozuk şema üretmektense
        // çakışmayı bildirip tek bir taraf koymak doğru.
        Assert.Single(result.Merged.Tables[0].Columns.Where(c => c.Name == "status"));

        // Ve çakışan kolon "otomatik birleşti" diye SAYILMAMALI: canlı
        // doğrulamada tam olarak bu görüldü — rapor hem "status otomatik
        // birleşti" hem "status çakıştı" diyordu.
        Assert.DoesNotContain(result.AutoMerged, note => note.Contains("status"));
    }

    [Fact]
    public void An_added_column_is_described_as_added_not_changed()
    {
        // Kullanıcıya gösterilen metin: canlı doğrulamada yeni eklenen bir
        // kolon "changed" diye anlatılıyordu.
        var baseSchema = Schema(Table("t1", "users"));
        var ours = Clone(baseSchema);
        var theirs = Schema(Table("t1", "users", Column("c2", "phone", "varchar")));

        var result = SchemaThreeWayMerger.Merge(baseSchema, ours, theirs);

        Assert.Contains(result.AutoMerged, note => note.Contains("phone") && note.Contains("added"));
    }

    [Fact]
    public void Deleting_what_the_other_side_changed_blocks_the_merge()
    {
        // "Son yazan kazanır" burada veri kaybıdır: bir taraf kolonu silmiş,
        // diğeri ona güvenerek değiştirmiş. Otomatik seçim yapılmaz.
        var baseSchema = Schema(Table("t1", "users", Column("c1", "status", "varchar")));
        var ours = Schema(Table("t1", "users"));
        var theirs = Clone(baseSchema); theirs.Tables[0].Columns[0].Type = "text";

        var result = SchemaThreeWayMerger.Merge(baseSchema, ours, theirs);

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(MergeConflictKind.DeleteVsModify, conflict.Kind);
        Assert.True(conflict.Blocking);
    }

    [Fact]
    public void A_rename_on_one_side_and_a_column_added_on_the_other_merge_cleanly()
    {
        // Satır bazlı bir merge'in çözemediği, StableUuid sayesinde çözülen durum.
        var baseSchema = Schema(Table("t1", "users", Column("c1", "email", "varchar")));
        var ours = Clone(baseSchema); ours.Tables[0].Name = "accounts";
        var theirs = Clone(baseSchema); theirs.Tables[0].Columns.Add(Column("c2", "phone", "varchar"));

        var result = SchemaThreeWayMerger.Merge(baseSchema, ours, theirs);

        Assert.Empty(result.Conflicts);
        Assert.Equal("accounts", result.Merged.Tables[0].Name);
        Assert.Contains(result.Merged.Tables[0].Columns, c => c.Name == "phone");
    }

    [Fact]
    public void A_table_added_on_one_side_only_is_merged_in()
    {
        var baseSchema = Schema(Table("t1", "users"));
        var ours = Clone(baseSchema);
        var theirs = Schema(Table("t1", "users"), Table("t2", "orders"));

        var result = SchemaThreeWayMerger.Merge(baseSchema, ours, theirs);

        Assert.Empty(result.Conflicts);
        Assert.Equal(2, result.Merged.Tables.Count);
    }

    [Fact]
    public void Two_people_adding_a_table_with_the_same_name_is_a_collision()
    {
        var baseSchema = Schema();
        var ours = Schema(Table("t-ayse", "orders", Column("c1", "id")));
        var theirs = Schema(Table("t-mehmet", "orders", Column("c2", "id")));

        var result = SchemaThreeWayMerger.Merge(baseSchema, ours, theirs);

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(MergeConflictKind.NameCollision, conflict.Kind);
        Assert.Single(result.Merged.Tables);
    }

    [Fact]
    public void A_table_deleted_on_one_side_while_untouched_on_the_other_is_removed()
    {
        var baseSchema = Schema(Table("t1", "users"), Table("t2", "orders"));
        var ours = Clone(baseSchema);
        var theirs = Schema(Table("t1", "users"));

        var result = SchemaThreeWayMerger.Merge(baseSchema, ours, theirs);

        Assert.Empty(result.Conflicts);
        Assert.Single(result.Merged.Tables);
    }

    [Fact]
    public void A_relation_added_on_one_side_survives_the_merge()
    {
        // KOD İNCELEMESİNDE BULUNDU: birleşen şema `ours`'un klonuydu ve
        // yalnızca tabloları yeniden hesaplanıyordu — karşı tarafta eklenen
        // ilişki hiçbir çakışma ve hiçbir "otomatik birleşti" satırı olmadan
        // KAYBOLUYORDU.
        var baseSchema = Schema(
            Table("t1", "users", Column("c1", "id")),
            Table("t2", "orders", Column("c2", "user_id")));

        var ours = Clone(baseSchema);
        var theirs = Clone(baseSchema);
        theirs.Relations.Add(new SchemaRelation
        {
            Id = "r1", Type = "1:N",
            SourceTableId = "t2", SourceColumnId = "c2",
            TargetTableId = "t1", TargetColumnId = "c1",
        });

        var result = SchemaThreeWayMerger.Merge(baseSchema, ours, theirs);

        Assert.Contains(result.Merged.Relations, r => r.Id == "r1");
        Assert.NotEmpty(result.AutoMerged);
    }

    [Fact]
    public void A_relation_whose_table_did_not_survive_is_dropped()
    {
        // Uçları kalmayan bir ilişkiyi taşımak, hiçbir motorda çalışmayan bir
        // şema üretir.
        var baseSchema = Schema(
            Table("t1", "users", Column("c1", "id")),
            Table("t2", "orders", Column("c2", "user_id")));
        baseSchema.Relations.Add(new SchemaRelation
        {
            Id = "r1", SourceTableId = "t2", SourceColumnId = "c2",
            TargetTableId = "t1", TargetColumnId = "c1",
        });

        var ours = Clone(baseSchema);
        var theirs = Clone(baseSchema);
        theirs.Tables.RemoveAll(t => t.StableUuid == "t2");   // orders silindi

        var result = SchemaThreeWayMerger.Merge(baseSchema, ours, theirs);

        Assert.DoesNotContain(result.Merged.Relations, r => r.Id == "r1");
    }

    [Fact]
    public void An_enum_added_on_one_side_survives_the_merge()
    {
        var baseSchema = Schema(Table("t1", "users", Column("c1", "id")));
        var ours = Clone(baseSchema);
        var theirs = Clone(baseSchema);
        theirs.Enums.Add(new SchemaEnum { Id = "e1", Name = "order_status", StableUuid = "e1" });

        var result = SchemaThreeWayMerger.Merge(baseSchema, ours, theirs);

        Assert.Contains(result.Merged.Enums, e => e.Name == "order_status");
    }

    [Fact]
    public void Identical_branches_produce_nothing_to_do()
    {
        var baseSchema = Schema(Table("t1", "users", Column("c1", "email")));

        var result = SchemaThreeWayMerger.Merge(baseSchema, Clone(baseSchema), Clone(baseSchema));

        Assert.Empty(result.Conflicts);
        Assert.Empty(result.AutoMerged);
    }
}
