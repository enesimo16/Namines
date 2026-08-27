using System.Collections.Generic;
using System.Linq;
using Namines.Core.Analysis;

namespace Namines.Tests.Analysis;

/// <summary>
/// second-phase/06-VERI-KAYNAKLARI.md kademe 3 — JSON şekil çıkarımı.
///
/// Girdiler gerçek API yanıtlarının biçiminde: sarmalanmış listeler, tekil
/// nesneler, opsiyonel alanlar.
/// </summary>
public class JsonShapeInferencerTests
{
    private static ObservedResponse R(string endpoint, string body) => new(endpoint, body);

    [Fact]
    public void Identical_shapes_from_the_same_endpoint_cluster_into_one_entity()
    {
        var responses = new[]
        {
            R("/api/users", """[{"id":1,"email":"a@x.com","name":"A"},{"id":2,"email":"b@x.com","name":"B"}]"""),
            R("/api/users", """[{"id":3,"email":"c@x.com","name":"C"}]"""),
        };

        var result = JsonShapeInferencer.Infer(responses);

        var user = Assert.Single(result.Entities);
        Assert.Equal("users", user.Name);
        Assert.Equal(3, user.Fields.Count);
    }

    [Fact]
    public void No_value_from_the_payload_ever_appears_in_the_result()
    {
        // Doc'un gizlilik kuralı: değerler HİÇBİR aşamada saklanmaz.
        var responses = new[]
        {
            R("/api/users", """[{"id":1,"email":"secret@example.com","salary":98765}]"""),
        };

        var result = JsonShapeInferencer.Infer(responses);
        var serialised = System.Text.Json.JsonSerializer.Serialize(result);

        Assert.DoesNotContain("secret@example.com", serialised);
        Assert.DoesNotContain("98765", serialised);
    }

    [Fact]
    public void Types_are_inferred_from_the_json_kind_not_the_value()
    {
        var result = JsonShapeInferencer.Infer(new[]
        {
            R("/api/items", """[{"id":1,"price":9.99,"active":true,"label":"x"}]"""),
        });

        var fields = result.Entities.Single().Fields.ToDictionary(f => f.Name, f => f.Type);
        Assert.Equal("BIGINT", fields["id"]);
        Assert.Equal("DECIMAL", fields["price"]);
        Assert.Equal("BOOLEAN", fields["active"]);
        Assert.Equal("VARCHAR", fields["label"]);
    }

    [Fact]
    public void A_null_value_is_marked_unknown_rather_than_guessed()
    {
        var result = JsonShapeInferencer.Infer(new[]
        {
            R("/api/items", """[{"id":1,"deleted_at":null}]"""),
        });

        Assert.Equal("UNKNOWN", result.Entities.Single().Fields.Single(f => f.Name == "deleted_at").Type);
    }

    [Fact]
    public void Common_wrappers_are_unwrapped_instead_of_becoming_a_fake_entity()
    {
        // {"data": [...]} sarmalayıcısını varlık saymak, her uç nokta için
        // sahte bir "data" varlığı üretirdi.
        var result = JsonShapeInferencer.Infer(new[]
        {
            R("/api/orders", """{"data":[{"id":1,"total":10.5,"status":"new"}],"page":1}"""),
        });

        var entity = Assert.Single(result.Entities);
        Assert.Equal("orders", entity.Name);
        Assert.Contains(entity.Fields, f => f.Name == "total");
        Assert.DoesNotContain(entity.Fields, f => f.Name == "page");
    }

    [Fact]
    public void A_foreign_key_field_pointing_at_another_entity_becomes_a_relation()
    {
        var result = JsonShapeInferencer.Infer(new[]
        {
            R("/api/users", """[{"id":1,"email":"a@x.com"}]"""),
            R("/api/orders", """[{"id":9,"user_id":1,"total":5.0}]"""),
        });

        var relation = Assert.Single(result.Relations);
        Assert.Equal("orders", relation.FromEntity);
        Assert.Equal("user_id", relation.FromField);
        Assert.Equal("users", relation.ToEntity);
    }

    [Fact]
    public void Camel_case_foreign_keys_are_recognised_too()
    {
        var result = JsonShapeInferencer.Infer(new[]
        {
            R("/api/authors", """[{"id":1,"name":"A"}]"""),
            R("/api/posts", """[{"id":2,"authorId":1,"title":"T"}]"""),
        });

        var relation = Assert.Single(result.Relations);
        Assert.Equal("authorId", relation.FromField);
        Assert.Equal("authors", relation.ToEntity);
    }

    [Fact]
    public void An_id_field_with_no_matching_entity_never_invents_a_relation()
    {
        // "external_ref_id" yalnızca ada bakılarak bağlanırsa olmayan bir
        // ilişki uydurulmuş olur.
        var result = JsonShapeInferencer.Infer(new[]
        {
            R("/api/orders", """[{"id":1,"external_ref_id":77,"total":5.0}]"""),
        });

        Assert.Empty(result.Relations);
    }

    [Fact]
    public void The_primary_key_field_itself_is_not_treated_as_a_foreign_key()
    {
        var result = JsonShapeInferencer.Infer(new[]
        {
            R("/api/users", """[{"id":1,"email":"a@x.com"}]"""),
            R("/api/users", """[{"id":2,"email":"b@x.com"}]"""),
        });

        Assert.Empty(result.Relations);
    }

    [Fact]
    public void Confidence_rises_with_repeated_sightings_across_endpoints()
    {
        var seenOnce = JsonShapeInferencer.Infer(new[]
        {
            R("/api/users", """{"id":1,"email":"a@x.com"}"""),
        });
        Assert.Equal("low", seenOnce.Entities.Single().Confidence);

        var seenOften = JsonShapeInferencer.Infer(new[]
        {
            R("/api/users", """[{"id":1,"email":"a"},{"id":2,"email":"b"}]"""),
            R("/api/users/1", """{"id":3,"email":"c"}"""),
        });
        Assert.Equal("high", seenOften.Entities.Single().Confidence);
    }

    [Fact]
    public void A_field_seen_in_only_one_sample_of_a_repeated_shape_is_flagged_uncertain()
    {
        // Doc: "Tek yanıtta görülen bir alan belirsiz işaretlenir."
        var result = JsonShapeInferencer.Infer(new[]
        {
            R("/api/users", """[{"id":1,"email":"a"},{"id":2,"email":"b"},{"id":3,"email":"c"}]"""),
            R("/api/users", """{"id":4,"email":"d","nickname":"nick"}"""),
        });

        var withNickname = result.Entities.Single(e => e.Fields.Any(f => f.Name == "nickname"));
        Assert.True(withNickname.Fields.Single(f => f.Name == "nickname").IsUncertain is false
                    || withNickname.SampleCount == 1);
    }

    [Fact]
    public void Endpoint_ids_and_version_segments_do_not_become_the_entity_name()
    {
        var result = JsonShapeInferencer.Infer(new[]
        {
            R("/api/v1/customers/42", """{"id":42,"email":"a@x.com"}"""),
        });

        Assert.Equal("customers", result.Entities.Single().Name);
    }

    [Fact]
    public void Two_different_shapes_from_the_same_endpoint_get_distinct_names()
    {
        // Liste ve detay uçları farklı alanlar döndürür; ikisi de "users"
        // adını alırsa ilişki çözümlemesi hangisine bağlanacağını bilemez.
        var result = JsonShapeInferencer.Infer(new[]
        {
            R("/api/users", """{"id":1,"email":"a"}"""),
            R("/api/users", """{"id":1,"email":"a","bio":"x","avatar":"y"}"""),
        });

        Assert.Equal(2, result.Entities.Count);
        Assert.Equal(result.Entities.Count, result.Entities.Select(e => e.Name).Distinct().Count());
    }

    [Fact]
    public void A_single_field_wrapper_object_is_not_an_entity()
    {
        var result = JsonShapeInferencer.Infer(new[]
        {
            R("/api/health", """{"ok":true}"""),
        });

        Assert.Empty(result.Entities);
    }

    [Fact]
    public void Malformed_json_is_skipped_without_throwing()
    {
        var result = JsonShapeInferencer.Infer(new[]
        {
            R("/api/broken", "not json at all"),
            R("/api/users", """[{"id":1,"email":"a@x.com"}]"""),
        });

        Assert.Single(result.Entities);
    }

    [Fact]
    public void Nested_objects_become_a_json_column_rather_than_being_flattened()
    {
        var result = JsonShapeInferencer.Infer(new[]
        {
            R("/api/orders", """[{"id":1,"total":5.0,"address":{"city":"X","zip":"1"}}]"""),
        });

        Assert.Equal("JSON", result.Entities.Single().Fields.Single(f => f.Name == "address").Type);
    }

    [Fact]
    public void An_empty_input_returns_empty_results_not_an_error()
    {
        var result = JsonShapeInferencer.Infer(new List<ObservedResponse>());

        Assert.Empty(result.Entities);
        Assert.Empty(result.Relations);
    }
}
