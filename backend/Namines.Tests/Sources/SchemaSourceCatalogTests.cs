using System.Linq;
using Namines.Core.Sources;
using Namines.Infrastructure.Services;

namespace Namines.Tests.Sources;

/// <summary>github/06-EKLENTI-MIMARISI.md — katalog sözleşmesi.</summary>
public class SchemaSourceCatalogTests
{
    private static readonly ISchemaSourceCatalog Catalog = new StaticSchemaSourceCatalog();

    [Fact]
    public void Catalog_lists_every_source_the_product_has_today()
    {
        var ids = Catalog.All().Select(s => s.Id).ToArray();

        Assert.Contains("github", ids);
        Assert.Contains("dbconnect", ids);
        Assert.Contains("openapi", ids);
        Assert.Contains("code", ids);
        Assert.Contains("jsonshape", ids);
        Assert.Contains("image", ids);
        Assert.Contains("starter", ids);
    }

    [Fact]
    public void Voice_is_not_a_source()
    {
        // Ses prompt METNİ üretiyor, şema değil. Menüye koymak, kullanıcıya
        // seçtiğinde şema geleceğini vaat etmek olurdu.
        Assert.DoesNotContain("voice", Catalog.All().Select(s => s.Id));
    }

    [Fact]
    public void Watching_a_source_requires_connecting_to_it()
    {
        // 06 §4'ün "Bağla" / "İçe aktar" ayrımı: drift takibi ancak sürekli
        // bir ilişki varsa mümkün. Tek seferlik bir içe aktarmada izlenecek
        // bir şey yok — bu testin koruduğu şey menünün gruplaması değil,
        // kullanıcıya verilen sözün tutarlılığı.
        foreach (var source in Catalog.All().Where(s => s.Capabilities.HasFlag(SchemaSourceCapability.Watch)))
            Assert.Equal(SchemaSourceKind.Connect, source.Kind);
    }

    [Fact]
    public void Inferred_sources_are_marked_as_guesses()
    {
        // second-phase/06-VERI-KAYNAKLARI.md: "çıkarım olduğu her ekranda
        // söylenmeli". Bayrak katalogdan geliyor ki UI unutamasın.
        Assert.True(Catalog.All().Single(s => s.Id == "openapi").ProducesGuess);
        Assert.True(Catalog.All().Single(s => s.Id == "jsonshape").ProducesGuess);
        Assert.True(Catalog.All().Single(s => s.Id == "image").ProducesGuess);

        Assert.False(Catalog.All().Single(s => s.Id == "dbconnect").ProducesGuess);
        Assert.False(Catalog.All().Single(s => s.Id == "code").ProducesGuess);
    }

    [Fact]
    public void Github_promises_only_what_it_can_do_today()
    {
        // F1 tek seferlik bir içe aktarma: drift takibi (Watch) ve geri yazma
        // (WriteBack) F3/F5'te geliyor. Bugün "Connect" grubuna koymak,
        // kullanıcıya tutulmayacak bir söz vermek olurdu.
        var github = Catalog.All().Single(s => s.Id == "github");

        Assert.Equal(SchemaSourceKind.Import, github.Kind);
        Assert.False(github.Capabilities.HasFlag(SchemaSourceCapability.Watch));
        Assert.False(github.Capabilities.HasFlag(SchemaSourceCapability.WriteBack));
        Assert.False(github.ProducesGuess);   // ayrıştırıcılar deterministik
    }

    [Fact]
    public void Every_source_can_be_shown_to_a_user()
    {
        foreach (var source in Catalog.All())
        {
            Assert.False(string.IsNullOrWhiteSpace(source.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(source.Description));
            Assert.True(source.Capabilities.HasFlag(SchemaSourceCapability.Import));
        }
    }
}
