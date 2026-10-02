using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Namines.API.Controllers;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;
using Xunit;

namespace Namines.Tests.Controllers;

/// <summary>
/// <c>POST /api/compile/sql</c> — geçersiz tanımlayıcının HTTP karşılığı.
///
/// <b>Bu testler LOCALHOST doğrulamasında bulunan bir kusur için yazıldı.</b>
/// C-1 düzeltmesi <see cref="SqlIdentifier"/>'a kontrol karakterlerini reddetme
/// kuralı ekledi (haklı olarak), ama fırlatılan <c>ArgumentException</c> hiçbir
/// yerde yakalanmıyordu: uç <b>HTTP 500</b> dönüyor ve gövdede iç ayrıntıyı
/// (<c>Parameter 'identifier'</c>) sızdırıyordu. Geçersiz bir tablo adı bir
/// İSTEMCİ hatası, sunucu arızası değil.
///
/// <b>Hiçbir birim testi bunu göstermemişti</b>, çünkü hepsi üreticiyi doğrudan
/// çağırıyor ve HTTP katmanından geçmiyordu — "testler yeşil" ile "gerçekten
/// çalışıyor" arasındaki farkın bir örneği daha.
/// </summary>
public class CompileInvalidIdentifierTests
{
    /// <summary>
    /// CompileSql yalnızca DDL üreticisini kullanıyor; diğer bağımlılıklar bu
    /// yolda hiç dokunulmadığı için <c>null!</c> geçiliyor.
    /// </summary>
    private static CompileController NewController() =>
        new(new DdlGeneratorFactory(), efCoreGenerator: null!, prismaGenerator: null!, eject: null!);

    private static CompileRequest RequestWith(string tableName) => new()
    {
        DbType = DatabaseType.PostgreSQL,
        Schema = new DatabaseSchema
        {
            Name = "audit",
            Tables =
            {
                new SchemaTable
                {
                    Id = "t1", Name = tableName,
                    Columns = { new SchemaColumn { Id = "c1", Name = "id", Type = "INT", IsPK = true } },
                },
            },
        },
    };

    [Theory]
    [InlineData("orders\n; DROP TABLE users")]
    [InlineData("orders\t; DROP TABLE users")]
    [InlineData("orders\0x")]
    [InlineData("")]
    public void Gecersiz_tanimlayici_500_DEGIL_400_donuyor(string tableName)
    {
        var result = NewController().CompileSql(RequestWith(tableName));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, bad.StatusCode);
    }

    [Fact]
    public void Hata_mesaji_IC_AYRINTI_sizdirmiyor()
    {
        // ".NET"in ArgumentException'a eklediği "(Parameter 'identifier')" eki
        // çağıran için anlamsız bir iç ayrıntı.
        var result = NewController().CompileSql(RequestWith("orders\nx"));

        var body = Assert.IsType<BadRequestObjectResult>(result).Value!.ToString()!;
        Assert.DoesNotContain("Parameter", body);
        // Ama kullanıcıya NE yapacağını söyleyen kısım KALMALI.
        Assert.Contains("control character", body);
    }

    [Fact]
    public void Ayirici_iceren_GECERLI_ad_hala_derleniyor()
    {
        // Kaçışı olan ad reddedilmemeli — C-1'in düzeltmesi tam olarak onu
        // güvenle üretmek içindi.
        var result = NewController().CompileSql(RequestWith("orders\"x"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var sql = ok.Value!.GetType().GetProperty("sql")!.GetValue(ok.Value)!.ToString()!;

        Assert.Contains("\"orders\"\"x\"", sql);
    }
}
