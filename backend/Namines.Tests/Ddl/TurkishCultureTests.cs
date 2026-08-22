using System.Globalization;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;

namespace Namines.Tests.Ddl;

/// <summary>
/// Türkçe kültürde tip karşılaştırmaları (Turkish-I sorunu).
///
/// <b>Bu testler gerçek bir hatadan sonra yazıldı ve hata GELİŞTİRME MAKİNESİNDE
/// üretimdeydi.</b> Türkçe kültürde <c>"int".ToUpper()</c> sonucu <c>"INT"</c>
/// değil <c>"İNT"</c>'tir (noktalı büyük İ) ve <c>"ID".ToLower()</c> sonucu
/// <c>"id"</c> değil <c>"ıd"</c>'dir. Kod ASCII sabitlerle karşılaştırdığı için
/// bu karşılaştırmaların hepsi <b>sessizce başarısız</b> oluyordu:
/// küçük harfle <c>"int"</c> yazılmış bir birincil anahtar PostgreSQL'de
/// <c>SERIAL</c> yerine düz <c>integer</c> üretiyor, yani otomatik artan
/// olmuyordu. Canlı API'ye istek atılarak bulundu.
///
/// Testler kültürü AÇIKÇA değiştiriyor: CI çoğunlukla invariant kültürde çalışır
/// ve orada bu hata hiç görünmez — yani "testler geçiyor" hiçbir şey kanıtlamazdı.
/// </summary>
public class TurkishCultureTests : IDisposable
{
    private readonly CultureInfo _original = CultureInfo.CurrentCulture;

    public TurkishCultureTests() => CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

    public void Dispose() => CultureInfo.CurrentCulture = _original;

    private static DatabaseSchema Schema(string type) => new()
    {
        Name = "shop",
        Tables =
        {
            new SchemaTable
            {
                Id = "t1", Name = "orders",
                Columns = { new SchemaColumn { Id = "c1", Name = "id", Type = type, IsPK = true } },
            },
        },
    };

    private static string Ddl(DatabaseType engine, DatabaseSchema schema) =>
        new DdlGeneratorFactory().GetGenerator(engine).Generate(schema);

    [Theory]
    [InlineData("int")]
    [InlineData("INT")]
    [InlineData("Int")]
    public void A_lowercase_int_key_still_becomes_serial(string type)
    {
        // "int" yazan kullanıcı ile "INT" yazan kullanıcı aynı şemayı tarif eder.
        // Türkçe kültürde ilki otomatik artan olmuyordu.
        Assert.Contains("SERIAL", Ddl(DatabaseType.PostgreSQL, Schema(type)));
    }

    [Theory]
    [InlineData(DatabaseType.MSSQL, "IDENTITY(1,1)")]
    [InlineData(DatabaseType.MySQL, "AUTO_INCREMENT")]
    [InlineData(DatabaseType.MariaDB, "AUTO_INCREMENT")]
    [InlineData(DatabaseType.Oracle, "GENERATED ALWAYS AS IDENTITY")]
    [InlineData(DatabaseType.SQLite, "AUTOINCREMENT")]
    public void Every_engine_recognises_a_lowercase_integer_key(DatabaseType engine, string marker)
    {
        Assert.Contains(marker, Ddl(engine, Schema("int")));
    }

    [Fact]
    public void Type_mapping_does_not_depend_on_the_culture()
    {
        // Aynı şema, aynı çıktı — kültür ne olursa olsun. Aksi hâlde iki
        // geliştirici aynı şemadan farklı DDL üretir ve fark, biri üretime
        // çıkana kadar görünmez.
        var turkish = Ddl(DatabaseType.PostgreSQL, Schema("int"));

        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var invariant = Ddl(DatabaseType.PostgreSQL, Schema("int"));

        Assert.Equal(invariant, turkish);
    }
}
