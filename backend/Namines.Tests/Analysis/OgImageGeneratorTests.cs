using System.Xml.Linq;
using Namines.Core.Analysis;
using Namines.Core.Models;

namespace Namines.Tests.Analysis;

/// <summary>
/// Sosyal önizleme görseli (new-phase/23-GTM.md §2 Döngü 1).
///
/// Bu üreticinin sessizce bozulma biçimi belli: <b>geçersiz XML</b>. Bozuk bir
/// SVG, önizlemede hiç görsel olmamasından kötü — bağlantı kırık görünür. O yüzden
/// testlerin çoğu metin araması değil, gerçek XML ayrıştırması.
/// </summary>
public class OgImageGeneratorTests
{
    private static DatabaseSchema Schema(string name, params string[] tables)
    {
        var schema = new DatabaseSchema { Name = name };
        foreach (var t in tables)
        {
            schema.Tables.Add(new SchemaTable
            {
                Id = t,
                Name = t,
                Columns = { new SchemaColumn { Name = "id", Type = "int", IsPK = true } },
            });
        }
        return schema;
    }

    private static XDocument Parse(string svg) => XDocument.Parse(svg);

    [Fact]
    public void The_output_is_well_formed_svg_at_the_social_preview_size()
    {
        var doc = Parse(OgImageGenerator.Generate(Schema("Shop", "users", "orders"), "PostgreSQL"));

        Assert.Equal("svg", doc.Root!.Name.LocalName);
        Assert.Equal("1200", doc.Root.Attribute("width")!.Value);
        Assert.Equal("630", doc.Root.Attribute("height")!.Value);
    }

    [Fact]
    public void A_table_name_with_xml_metacharacters_does_not_break_the_document()
    {
        // Kaçış unutulursa burada bir XmlException patlar — ve üretimde bozuk bir
        // önizleme görseli olurdu.
        var svg = OgImageGenerator.Generate(Schema("A & B <script>", "user<s>", "a&b"), "MySQL");

        var doc = Parse(svg);
        var texts = doc.Descendants().Where(e => e.Name.LocalName == "text").Select(e => e.Value).ToList();

        Assert.Contains(texts, t => t.Contains("A & B"));
        Assert.Contains(texts, t => t.Contains("a&b"));
    }

    [Fact]
    public void The_counts_shown_are_the_real_counts()
    {
        var schema = Schema("Blog", "posts", "comments");
        schema.Tables[0].Columns.Add(new SchemaColumn { Name = "title", Type = "text" });
        schema.Relations.Add(new SchemaRelation
        {
            SourceTableId = "comments",
            TargetTableId = "posts",
            Type = "one-to-many",
        });

        var texts = Parse(OgImageGenerator.Generate(schema, "PostgreSQL"))
            .Descendants().Where(e => e.Name.LocalName == "text").Select(e => e.Value).ToList();

        var index = texts.IndexOf("TABLES");
        Assert.Equal("2", texts[index - 1]);
        Assert.Equal("3", texts[texts.IndexOf("COLUMNS") - 1]);   // 2 + 1 eklenen
        Assert.Equal("1", texts[texts.IndexOf("RELATIONS") - 1]);
    }

    [Fact]
    public void A_long_name_is_truncated_rather_than_overflowing()
    {
        var name = new string('x', 200);

        var title = Parse(OgImageGenerator.Generate(Schema(name, "t"), "SQLite"))
            .Descendants().First(e => e.Name.LocalName == "text" && e.Value.StartsWith("xxx")).Value;

        // Kırpılmazsa metin görselin dışına taşar ve önizleme okunmaz olur.
        Assert.True(title.Length <= 24, $"Title was {title.Length} characters.");
        Assert.EndsWith("…", title);
    }

    [Fact]
    public void The_same_schema_always_produces_the_same_image()
    {
        // Deterministik olmazsa her istekte farklı bayt üretilir; uzun süreli
        // önbellek (max-age=3600) anlamsızlaşır ve sosyal ağlar görseli
        // tutarsız gösterir.
        var schema = Schema("Shop", "users", "orders", "products");

        Assert.Equal(
            OgImageGenerator.Generate(schema, "PostgreSQL"),
            OgImageGenerator.Generate(schema, "PostgreSQL"));
    }

    [Fact]
    public void An_empty_schema_still_renders()
    {
        // Yeni açılmış, henüz tablosu olmayan bir proje paylaşılabilir; burada
        // patlamak paylaşım sayfasını tamamen görselsiz bırakırdı.
        var doc = Parse(OgImageGenerator.Generate(new DatabaseSchema(), "PostgreSQL"));

        var texts = doc.Descendants().Where(e => e.Name.LocalName == "text").Select(e => e.Value).ToList();
        Assert.Contains("Untitled schema", texts);
        Assert.Equal("0", texts[texts.IndexOf("TABLES") - 1]);
    }

    [Fact]
    public void Table_name_chips_stay_inside_the_canvas()
    {
        var schema = Schema("Wide",
            "aaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbb", "cccccccccccccccc",
            "dddddddddddddddd", "eeeeeeeeeeeeeeee");

        var doc = Parse(OgImageGenerator.Generate(schema, "PostgreSQL"));

        var chips = doc.Descendants()
            .Where(e => e.Name.LocalName == "rect" && e.Attribute("rx") != null)
            .ToList();

        Assert.NotEmpty(chips);
        foreach (var chip in chips)
        {
            var right = int.Parse(chip.Attribute("x")!.Value) + int.Parse(chip.Attribute("width")!.Value);
            Assert.True(right <= 1200, $"A chip ended at x={right}, outside the 1200px canvas.");
        }
    }
}
