using Namines.Infrastructure.Services;

namespace Namines.Tests.Services;

/// <summary>
/// OpenAPI 3.x / Swagger 2.0 dokümanından varlık adayı çıkarımı —
/// second-phase/06-VERI-KAYNAKLARI.md kademe 2. Ağa hiç dokunmuyor,
/// bkz. <see cref="GraphQlSchemaParserTests"/> ile aynı gerekçe.
/// </summary>
public class OpenApiSchemaParserTests
{
    [Fact]
    public void Recognises_openapi_v3_documents()
    {
        Assert.True(OpenApiSchemaParser.LooksLikeOpenApiDocument("""{"openapi":"3.0.0"}"""));
    }

    [Fact]
    public void Recognises_swagger_v2_documents()
    {
        Assert.True(OpenApiSchemaParser.LooksLikeOpenApiDocument("""{"swagger":"2.0"}"""));
    }

    [Fact]
    public void Does_not_mistake_an_arbitrary_json_document_for_a_spec()
    {
        // Örn. bir GraphQL yanıtı ya da alakasız bir JSON API burada
        // yanlışlıkla OpenAPI sayılmamalı — ApiSpecExtractor bunu GraphQL
        // denemesinden SONRA çağırıyor, ikisi çakışırsa yanlış kademe seçilir.
        Assert.False(OpenApiSchemaParser.LooksLikeOpenApiDocument("""{"data":{"__schema":{}}}"""));
    }

    [Fact]
    public void Extracts_v3_schemas_with_field_and_relation_counts()
    {
        var json = """
            {"openapi":"3.0.0","components":{"schemas":{
                "Product":{"type":"object","properties":{
                    "id":{"type":"string"},
                    "title":{"type":"string"},
                    "seller":{"$ref":"#/components/schemas/Seller"}
                }},
                "Seller":{"type":"object","properties":{"id":{"type":"string"}}}
            }}}
            """;

        var result = OpenApiSchemaParser.Parse(json);

        var product = result.Single(t => t.Name == "Product");
        Assert.Contains("3 alan", product.Reason);
        Assert.Contains("1 ilişki", product.Reason);
    }

    [Fact]
    public void Extracts_v2_definitions_the_same_way()
    {
        // Swagger 2.0'da "definitions" kullanılıyor, "components.schemas" değil.
        var json = """
            {"swagger":"2.0","definitions":{
                "Product":{"type":"object","properties":{"id":{"type":"string"}}}
            }}
            """;

        var result = OpenApiSchemaParser.Parse(json);

        Assert.Single(result);
        Assert.Equal("Product", result[0].Name);
    }

    [Fact]
    public void Array_of_ref_items_counts_as_a_relation()
    {
        // "reviews: array of Review" -- bire-çok ilişkinin OpenAPI'deki biçimi.
        var json = """
            {"openapi":"3.0.0","components":{"schemas":{
                "Product":{"type":"object","properties":{
                    "reviews":{"type":"array","items":{"$ref":"#/components/schemas/Review"}}
                }},
                "Review":{"type":"object","properties":{"id":{"type":"string"}}}
            }}}
            """;

        var result = OpenApiSchemaParser.Parse(json);

        var product = result.Single(t => t.Name == "Product");
        Assert.Contains("1 ilişki", product.Reason);
    }

    [Fact]
    public void A_reference_to_an_unknown_schema_does_not_count_as_a_relation()
    {
        // $ref harici bir dosyaya ya da tanımsız bir şemaya işaret edebilir;
        // schemaNames kümesinde yoksa "ilişki" saymak yanlış bir sinyal olurdu.
        var json = """
            {"openapi":"3.0.0","components":{"schemas":{
                "Product":{"type":"object","properties":{
                    "external":{"$ref":"#/components/schemas/Unknown"}
                }}
            }}}
            """;

        var result = OpenApiSchemaParser.Parse(json);

        var product = result.Single(t => t.Name == "Product");
        Assert.DoesNotContain("ilişki", product.Reason);
    }

    [Fact]
    public void Schemas_without_properties_are_not_table_candidates()
    {
        // ör. yalnızca bir enum ya da salt tip takma adı ("type": "string").
        var json = """
            {"openapi":"3.0.0","components":{"schemas":{
                "Status":{"type":"string","enum":["active","inactive"]}
            }}}
            """;

        Assert.Empty(OpenApiSchemaParser.Parse(json));
    }

    [Fact]
    public void Every_reason_is_labelled_as_a_guess()
    {
        var json = """
            {"openapi":"3.0.0","components":{"schemas":{
                "Product":{"type":"object","properties":{"id":{"type":"string"}}}
            }}}
            """;

        var result = OpenApiSchemaParser.Parse(json);

        Assert.All(result, t => Assert.Contains("tahmin", t.Reason));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"openapi":"3.0.0"}""")]
    public void Malformed_or_schema_less_documents_return_an_empty_list_not_an_exception(string body)
    {
        Assert.Empty(OpenApiSchemaParser.Parse(body));
    }
}
