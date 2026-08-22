using System.Text.Json;
using System.Text.Json.Nodes;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Core.Nsl;

namespace Namines.Tests.Nsl;

/// <summary>
/// Kanonik JSON ara temsili (04 §3).
///
/// <b>Round-trip yine ana test:</b> yaz → oku → karşılaştır. Bir biçimin sessizce
/// veri düşürmesi en pahalı arıza türüdür ve tek tek alan kontrolü, yazıcının
/// hiç yazmadığı bir alanı gözden kaçırmaya açıktır.
/// </summary>
public class CanonicalIrTests
{
    private static DatabaseSchema Sample()
    {
        var schema = new DatabaseSchema { Name = "shop" };

        schema.Enums.Add(new SchemaEnum
        {
            Id = "e1", Name = "order_status", Values = { "pending", "paid" },
        });

        var users = new SchemaTable
        {
            Id = "t1", Name = "users",
            Columns =
            {
                new SchemaColumn { Id = "c1", Name = "id", Type = "INT", IsPK = true },
                new SchemaColumn { Id = "c2", Name = "email", Type = "VARCHAR", Length = 255, Collation = "tr-TR-x-icu" },
                new SchemaColumn { Id = "c3", Name = "tags", Type = "TEXT", IsArray = true, IsNullable = true },
            },
            Checks = { new SchemaCheck { Id = "k1", Name = "ck_email", Expression = "email <> ''" } },
        };
        users.Uniques.Add(new SchemaUnique { Id = "u1", Name = "uq_email", ColumnIds = { "c2" } });
        users.Indexes.Add(new SchemaIndex
        {
            Id = "i1", Name = "ix_email", IsUnique = false,
            Columns = { new SchemaIndexColumn { ColumnId = "c2", Descending = true } },
        });

        var orders = new SchemaTable
        {
            Id = "t2", Name = "orders",
            Columns =
            {
                new SchemaColumn { Id = "c4", Name = "id", Type = "INT", IsPK = true },
                new SchemaColumn { Id = "c5", Name = "user_id", Type = "INT" },
                new SchemaColumn { Id = "c6", Name = "status", Type = "TEXT", EnumRef = "order_status" },
                new SchemaColumn { Id = "c7", Name = "total", Type = "DECIMAL", Generated = "qty * price" },
                new SchemaColumn { Id = "c8", Name = "code", Type = "INT", IsPK = false, Identity = false },
            },
        };

        schema.Tables.Add(users);
        schema.Tables.Add(orders);
        schema.Relations.Add(new SchemaRelation
        {
            Id = "r1", Type = "many-to-one",
            SourceTableId = "t2", SourceColumnId = "c5",
            TargetTableId = "t1", TargetColumnId = "c1",
            OnDelete = ReferentialAction.Cascade,
        });

        return schema;
    }

    private static DatabaseSchema RoundTrip() => CanonicalIr.Read(CanonicalIr.Write(Sample()));

    [Fact]
    public void The_version_is_the_first_thing_in_the_file()
    {
        // Sürümsüz bir biçim, ileride değiştiğinde eski dosyaları sessizce
        // yanlış okur — ve bunu ancak veriler bozulunca fark edersin.
        var json = CanonicalIr.Write(Sample());

        Assert.Equal("1.0", JsonNode.Parse(json)!["nsl"]!.GetValue<string>());
        Assert.StartsWith("{\n  \"nsl\"", json.Replace("\r\n", "\n"));
    }

    [Fact]
    public void A_file_from_another_version_is_refused()
    {
        // Farklı sürümdeki bir dosyayı bu sürümün kurallarıyla okumak, alanları
        // yanlış yorumlamak demektir.
        var json = CanonicalIr.Write(Sample()).Replace("\"1.0\"", "\"2.0\"");

        var error = Assert.Throws<NslParseException>(() => CanonicalIr.Read(json));
        Assert.Contains("2.0", error.Message);
    }

    [Fact]
    public void Tables_columns_and_relations_survive_a_round_trip()
    {
        var parsed = RoundTrip();

        Assert.Equal(new[] { "users", "orders" }, parsed.Tables.Select(t => t.Name));
        Assert.Equal(new[] { "id", "email", "tags" }, parsed.Tables[0].Columns.Select(c => c.Name));
        Assert.Single(parsed.Relations);
        Assert.Equal(ReferentialAction.Cascade, parsed.Relations[0].OnDelete);
    }

    [Fact]
    public void The_column_features_survive_a_round_trip()
    {
        var parsed = RoundTrip();
        var users = parsed.Tables[0];
        var orders = parsed.Tables[1];

        Assert.Equal(255, users.Columns[1].Length);
        Assert.Equal("tr-TR-x-icu", users.Columns[1].Collation);
        Assert.True(users.Columns[2].IsArray);
        Assert.Equal("order_status", orders.Columns[2].EnumRef);
        Assert.Equal("qty * price", orders.Columns[3].Generated);
    }

    [Fact]
    public void Saying_nothing_about_identity_stays_saying_nothing()
    {
        // "identityEffective" çıkarımın SONUCU. Geri yazarken onu saklamak,
        // "söylenmedi" ile "evet dendi" arasındaki farkı yok ederdi — ve o fark,
        // kullanıcının anahtarı kendisinin atayabilmesinin tek dayanağı.
        var parsed = RoundTrip();

        Assert.Null(parsed.Tables[0].Columns[0].Identity);
        Assert.False(parsed.Tables[1].Columns[4].Identity);
    }

    [Fact]
    public void The_effective_identity_is_written_out_for_readers()
    {
        // Bu dosyayı okuyan bir araç "bu anahtarı kim atıyor?" sorusunu, çıkarım
        // kurallarını yeniden uygulamak zorunda kalmadan cevaplayabilmeli.
        var root = JsonNode.Parse(CanonicalIr.Write(Sample()))!;
        var idColumn = root["tables"]![0]!["columns"]![0]!;

        Assert.True(idColumn["identityEffective"]!.GetValue<bool>());
    }

    [Fact]
    public void Enums_keep_their_order()
    {
        // PostgreSQL enum değerlerini tanımlandıkları sırayla SIRALAR; sırayı
        // değiştirmek ORDER BY sonucunu değiştirir.
        var parsed = RoundTrip();

        Assert.Equal(new[] { "pending", "paid" }, parsed.Enums[0].Values);
    }

    [Fact]
    public void Constraints_survive_a_round_trip()
    {
        var users = RoundTrip().Tables[0];

        Assert.Equal("email <> ''", users.Checks[0].Expression);
        Assert.Single(users.Uniques);
        Assert.Single(users.Indexes);
        Assert.True(users.Indexes[0].Columns[0].Descending);
        // Kolon kimlikleri yeniden üretiliyor; bağın ADA göre yeniden kurulduğunu
        // doğrulamak, "kısıt duruyor ama boş bir kolona bakıyor" hâlini yakalar.
        Assert.Equal("email", users.Columns.First(c => c.Id == users.Uniques[0].ColumnIds[0]).Name);
    }

    [Fact]
    public void Writing_the_same_schema_twice_produces_the_same_bytes()
    {
        var schema = Sample();

        Assert.Equal(CanonicalIr.Write(schema), CanonicalIr.Write(schema));
    }

    [Fact]
    public void The_same_source_file_always_produces_the_same_ir()
    {
        // Asıl garanti bu: aynı KAYNAKTAN iki kez okumak aynı IR'ı vermeli.
        // Vermezse her kaydetme sahte bir diff üretir ve dosyanın anlamlı olduğu
        // tek an gürültünün içinde kaybolur. Bu, ancak kimlikler addan
        // türetildiği için tutuyor — rastgele üretilseydi iki okuma birbirinden
        // farklı görünürdü (SchemaIdentity'de düzeltilen hatanın aynısı).
        const string source = "table users {\n  id int pk\n  email varchar(255) not null\n}\n";

        Assert.Equal(
            CanonicalIr.Write(NslParser.Parse(source)),
            CanonicalIr.Write(NslParser.Parse(source)));
    }

    [Fact]
    public void The_ir_is_valid_json()
    {
        // Elle kurulan bir JSON'da tek bir kaçış hatası dosyayı okunamaz kılar.
        using var document = JsonDocument.Parse(CanonicalIr.Write(Sample()));

        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
    }

    [Fact]
    public void A_name_with_quotes_does_not_break_the_ir()
    {
        var schema = Sample();
        schema.Tables[0].Name = "we\"ird";

        Assert.Equal("we\"ird", CanonicalIr.Read(CanonicalIr.Write(schema)).Tables[0].Name);
    }

    [Fact]
    public void The_ir_and_the_nsl_text_describe_the_same_schema()
    {
        // İki biçim ayrışırsa hangisinin doğru olduğu belirsizleşir; ikisi de
        // "tek doğruluk kaynağı" iddiasını taşıyamaz.
        var viaIr = CanonicalIr.Read(CanonicalIr.Write(Sample()));
        var viaText = NslParser.Parse(NslWriter.Write(Sample()));

        Assert.Equal(
            viaText.Tables.Select(t => t.Name),
            viaIr.Tables.Select(t => t.Name));

        Assert.Equal(
            viaText.Tables[1].Columns.Select(c => c.Name),
            viaIr.Tables[1].Columns.Select(c => c.Name));

        Assert.Equal(
            viaText.Tables[1].Columns[2].EnumRef,
            viaIr.Tables[1].Columns[2].EnumRef);
    }
}
