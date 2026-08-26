using Namines.Infrastructure.Services;

namespace Namines.Tests.Services;

/// <summary>
/// GraphQL introspection yanıtından varlık adayı çıkarımı —
/// second-phase/06-VERI-KAYNAKLARI.md kademe 1.
///
/// <b>Ağa hiç dokunmuyor</b> — sabit JSON metinleriyle çalışıyor. Ağ tarafı
/// (SsrfGuard, timeout, boyut sınırı) <c>ApiSpecExtractor</c>'da ve gerçek bir
/// ağ çağrısı olmadan test edilemiyor; burası saf ayrıştırma mantığını kilitliyor.
/// </summary>
public class GraphQlSchemaParserTests
{
    [Fact]
    public void Object_types_with_fields_become_table_candidates()
    {
        var json = """
            {"data":{"__schema":{"types":[
                {"name":"Product","kind":"OBJECT","fields":[
                    {"name":"id","type":{"name":"ID","kind":"SCALAR"}},
                    {"name":"title","type":{"name":"String","kind":"SCALAR"}}
                ]}
            ]}}}
            """;

        var result = GraphQlSchemaParser.Parse(json);

        Assert.Single(result);
        Assert.Equal("Product", result[0].Name);
        Assert.Contains("2 alan", result[0].Reason);
    }

    [Fact]
    public void Query_mutation_and_introspection_types_are_not_table_candidates()
    {
        // Kök tipler ve __ ile başlayan introspection dahili adları veri
        // modeli değil, API'nin kendi iskeleti.
        var json = """
            {"data":{"__schema":{"types":[
                {"name":"Query","kind":"OBJECT","fields":[{"name":"products","type":{"name":null,"kind":"LIST"}}]},
                {"name":"__Type","kind":"OBJECT","fields":[{"name":"name","type":{"name":"String","kind":"SCALAR"}}]},
                {"name":"Product","kind":"OBJECT","fields":[{"name":"id","type":{"name":"ID","kind":"SCALAR"}}]}
            ]}}}
            """;

        var result = GraphQlSchemaParser.Parse(json);

        Assert.Single(result);
        Assert.Equal("Product", result[0].Name);
    }

    [Fact]
    public void Non_object_kinds_are_not_table_candidates()
    {
        // ENUM, SCALAR, INPUT_OBJECT birer veri tablosu değil.
        var json = """
            {"data":{"__schema":{"types":[
                {"name":"Status","kind":"ENUM"},
                {"name":"String","kind":"SCALAR"},
                {"name":"ProductInput","kind":"INPUT_OBJECT","fields":[{"name":"title","type":{"name":"String","kind":"SCALAR"}}]}
            ]}}}
            """;

        var result = GraphQlSchemaParser.Parse(json);

        Assert.Empty(result);
    }

    [Fact]
    public void A_field_referencing_another_object_type_counts_as_a_relation()
    {
        var json = """
            {"data":{"__schema":{"types":[
                {"name":"Product","kind":"OBJECT","fields":[
                    {"name":"id","type":{"name":"ID","kind":"SCALAR"}},
                    {"name":"seller","type":{"name":"Seller","kind":"OBJECT"}}
                ]},
                {"name":"Seller","kind":"OBJECT","fields":[{"name":"id","type":{"name":"ID","kind":"SCALAR"}}]}
            ]}}}
            """;

        var result = GraphQlSchemaParser.Parse(json);

        var product = result.Single(t => t.Name == "Product");
        Assert.Contains("1 ilişki", product.Reason);
    }

    [Fact]
    public void A_list_and_non_null_wrapped_relation_is_still_detected()
    {
        // "[Seller!]!" gibi sarmalanmış tipler pratikte en yaygın örnek —
        // ilişkiyi kaçırmak, üretilen şemada yabancı anahtarın atlanması demek.
        var json = """
            {"data":{"__schema":{"types":[
                {"name":"Product","kind":"OBJECT","fields":[
                    {"name":"reviews","type":{"name":null,"kind":"NON_NULL","ofType":{"name":null,"kind":"LIST","ofType":{"name":"Review","kind":"OBJECT"}}}}
                ]},
                {"name":"Review","kind":"OBJECT","fields":[{"name":"id","type":{"name":"ID","kind":"SCALAR"}}]}
            ]}}}
            """;

        var result = GraphQlSchemaParser.Parse(json);

        var product = result.Single(t => t.Name == "Product");
        Assert.Contains("1 ilişki", product.Reason);
    }

    [Fact]
    public void Every_reason_is_labelled_as_a_guess()
    {
        // Çıkarılan şey sitenin GERÇEK veritabanı değil, API'sinin dışa açtığı
        // görünüm. Bunu gizlemek eski özelliğin yalanının yerine yenisini
        // koymak olurdu.
        var json = """
            {"data":{"__schema":{"types":[
                {"name":"Product","kind":"OBJECT","fields":[{"name":"id","type":{"name":"ID","kind":"SCALAR"}}]}
            ]}}}
            """;

        var result = GraphQlSchemaParser.Parse(json);

        Assert.All(result, t => Assert.Contains("tahmin", t.Reason));
    }

    [Fact]
    public void Fieldless_types_are_not_table_candidates()
    {
        var json = """
            {"data":{"__schema":{"types":[
                {"name":"Empty","kind":"OBJECT","fields":[]}
            ]}}}
            """;

        Assert.Empty(GraphQlSchemaParser.Parse(json));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("""{"data":{}}""")]
    [InlineData("""{"errors":[{"message":"introspection disabled"}]}""")]
    public void Malformed_or_non_introspection_responses_return_an_empty_list_not_an_exception(string body)
    {
        // Bu, callera "bir alttaki kademeye düş" sinyali; hata fırlatmak
        // ApiSpecExtractor'ın zincirini kırardı.
        var result = GraphQlSchemaParser.Parse(body);
        Assert.Empty(result);
    }
}
