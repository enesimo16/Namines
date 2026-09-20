using System.Text.Json;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// <see cref="SchemaJsonOptions"/> "kayıtlı şemayı okuyan HER yerin paylaştığı
/// tek ayar" olduğunu iddia ediyor. Eksik bir dönüştürücü bu iddiayı sessizce
/// yalanlıyordu ve sonuçları yalnızca uzakta görülüyordu:
///
/// <list type="bullet">
/// <item>LaunchController.Download → "The project's stored schema is not valid."</item>
/// <item>AuthController'ın Namines Flow diff'i → try/catch'e düşüp SESSİZCE
/// atlanıyor, yani ilişkisi olan bir projede sunucu tarafı otomasyon
/// (webhook/Slack/DBA) hiç tetiklenmiyordu.</item>
/// </list>
///
/// Bu testler ayarın gerçekten ÜRETİMDEKİ şemayı okuyabildiğini doğruluyor.
/// </summary>
public class SchemaJsonOptionsTests
{
    [Fact]
    public void Iliskili_sema_okunabiliyor_enum_string_olarak_geliyor()
    {
        // Frontend `ReferentialAction`'ı string yazıyor. Dönüştürücü olmadan
        // burası JsonException fırlatıyordu — ilişkisi OLAN her proje.
        const string json = """
            {
              "schemaId": "s1",
              "name": "shop",
              "tables": [{ "id": "t1", "name": "orders", "columns": [] }],
              "relations": [{
                "id": "r1", "type": "OneToMany",
                "sourceTableId": "t1", "sourceColumnId": "c1",
                "targetTableId": "t2", "targetColumnId": "c2",
                "onDelete": "Cascade", "onUpdate": "NoAction"
              }]
            }
            """;

        var schema = JsonSerializer.Deserialize<DatabaseSchema>(json, SchemaJsonOptions.Default);

        Assert.NotNull(schema);
        var relation = Assert.Single(schema!.Relations);
        Assert.Equal(ReferentialAction.Cascade, relation.OnDelete);
        Assert.Equal(ReferentialAction.NoAction, relation.OnUpdate);
    }

    [Fact]
    public void Metin_alanina_gelen_sayi_tolere_ediliyor()
    {
        // Enum dönüştürücüsü eklenirken `TolerantStringConverter`'ın
        // kaybolmadığını doğruluyor: model çıktısı bir METİN alanına sayı ya da
        // bool yazabiliyor (`"defaultValue": 0`).
        //
        // Dikkat: dönüştürücü `JsonConverter<string>` — yalnızca METİN alanlarına
        // uygulanıyor. Sayısal bir alana gelen `"255"` (ör. `length`) hâlâ
        // ayrıştırma hatası veriyor; sınıfın açıklamasında bunun aksini söyleyen
        // örnek düzeltildi.
        const string json = """
            {
              "schemaId": "s1", "name": "shop",
              "tables": [{
                "id": "t1", "name": "orders",
                "columns": [{ "id": "c1", "name": "total", "type": "INT", "defaultValue": 0 }]
              }],
              "relations": []
            }
            """;

        var schema = JsonSerializer.Deserialize<DatabaseSchema>(json, SchemaJsonOptions.Default);

        Assert.NotNull(schema);
        Assert.Equal("0", Assert.Single(Assert.Single(schema!.Tables).Columns).DefaultValue);
    }

    [Fact]
    public void Ozellik_adlari_buyuk_kucuk_harf_duyarsiz()
    {
        const string json = """{ "SchemaId": "s1", "Name": "shop", "Tables": [], "Relations": [] }""";

        var schema = JsonSerializer.Deserialize<DatabaseSchema>(json, SchemaJsonOptions.Default);

        Assert.Equal("shop", schema!.Name);
    }
}
