using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// TryRead, modelin sık yaptığı tip uyuşmazlıklarını (string beklenen yerde
/// sayı/bool) TOLERE etmeli — GroqAIService'in kendi tek-turlu yolu bunun
/// için <c>TolerantStringConverter</c> kullanıyor; araç döngüsünün cevabını
/// okuyan bu sınıf aynı toleransı kaybederse, model doğru bir şema
/// döndürdüğünde bile "okunamadı" sayılıp turu çöpe gider.
/// </summary>
public class SchemaJsonReaderTests
{
    [Fact]
    public void Reads_a_well_formed_schema()
    {
        const string json = """
            {
              "schemaId": "s1",
              "name": "Shop",
              "tables": [
                { "id": "t1", "name": "Users", "columns": [
                  { "id": "c1", "name": "Id", "type": "INT", "isPK": true }
                ] }
              ]
            }
            """;

        var schema = SchemaJsonReader.TryRead(json);

        Assert.NotNull(schema);
        Assert.Single(schema!.Tables);
    }

    [Fact]
    public void Tolerates_a_numeric_value_in_a_string_field()
    {
        // Model "defaultValue": 0 gibi bir sayı döndürebilir; alan string.
        // TolerantStringConverter olmadan bu JsonException fırlatır ve okunabilir
        // bir cevap "okunamadı" sayılıp modelin turu çöpe gider.
        const string json = """
            {
              "schemaId": "s1",
              "name": "Shop",
              "tables": [
                { "id": "t1", "name": "Orders", "columns": [
                  { "id": "c1", "name": "Id", "type": "INT", "isPK": true, "defaultValue": 0 }
                ] }
              ]
            }
            """;

        var schema = SchemaJsonReader.TryRead(json);

        Assert.NotNull(schema);
        Assert.Equal("0", schema!.Tables[0].Columns[0].DefaultValue);
    }

    [Fact]
    public void Tolerates_a_boolean_value_in_a_string_field()
    {
        const string json = """
            {
              "schemaId": "s1",
              "name": "Shop",
              "tables": [
                { "id": "t1", "name": "Orders", "columns": [
                  { "id": "c1", "name": "Active", "type": "BOOLEAN", "defaultValue": true }
                ] }
              ]
            }
            """;

        var schema = SchemaJsonReader.TryRead(json);

        Assert.NotNull(schema);
        Assert.Equal("true", schema!.Tables[0].Columns[0].DefaultValue);
    }

    [Fact]
    public void Returns_null_for_a_schema_with_no_tables()
    {
        const string json = """{ "schemaId": "s1", "name": "Empty", "tables": [] }""";

        Assert.Null(SchemaJsonReader.TryRead(json));
    }

    [Fact]
    public void Returns_null_for_unparseable_text()
    {
        Assert.Null(SchemaJsonReader.TryRead("I couldn't find a matching JSON object."));
    }

    [Fact]
    public void Returns_null_for_empty_input()
    {
        Assert.Null(SchemaJsonReader.TryRead(""));
        Assert.Null(SchemaJsonReader.TryRead(null));
    }
}
