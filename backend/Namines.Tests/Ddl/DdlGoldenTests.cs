using Namines.Core.Enums;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Tests.Fixtures;
using static VerifyXunit.Verifier;

namespace Namines.Tests.Ddl;

/// <summary>
/// Golden-file (snapshot) testleri.
///
/// Her fixture × her motor için üretilen DDL, <c>Golden/</c> altında kayıtlı
/// <c>.verified.sql</c> dosyasıyla karşılaştırılır. Çıktı değişirse test kırılır,
/// yanına <c>.received.sql</c> yazılır ve geliştirici diff'i inceleyip bilinçli
/// olarak onaylar.
///
/// NEDEN ÖNEMLİ: Bu projenin ürünü kod üretimidir — yani doğruluk ürünün kendisidir.
/// Bu testler olmadan bir üreticide yapılan değişikliğin diğer 5 motoru veya başka
/// bir senaryoyu bozup bozmadığı görünmez.
///
/// DİKKAT: Bu snapshot'lar "doğru DDL"i değil, "BUGÜNKÜ DDL"i temsil eder.
/// İçlerinde bilinen hatalar var (her FK'da sabit ON DELETE CASCADE, index üretimi yok).
/// Amaç baseline oluşturmak; düzeltmeler G3 ve sonrasında yapılıp diff'ler onaylanacak.
/// </summary>
public class DdlGoldenTests
{
    /// <summary>Golden dosyası üretilen motorlar (Db2/Firebird/Spanner/Redshift başka bir üreticiye takma ad).</summary>
    private static readonly DatabaseType[] Engines =
    [
        DatabaseType.MSSQL,
        DatabaseType.PostgreSQL,
        DatabaseType.MySQL,
        DatabaseType.MariaDB,
        DatabaseType.SQLite,
        DatabaseType.Oracle
    ];

    public static TheoryData<string, DatabaseType> Cases()
    {
        var data = new TheoryData<string, DatabaseType>();
        foreach (var (name, _) in SchemaFixtures.All())
            foreach (var engine in Engines)
                data.Add(name, engine);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Ddl_matches_golden_file(string fixtureName, DatabaseType engine)
    {
        var schema = SchemaFixtures.ByName(fixtureName);
        var generator = new DdlGeneratorFactory().GetGenerator(engine);

        var ddl = generator.Generate(schema);

        var settings = new VerifySettings();
        settings.UseDirectory(Path.Combine("..", "Golden", engine.ToString()));
        settings.UseFileName(fixtureName);
        settings.DisableDiff();

        // Target(extension, data) → golden dosya .sql uzantısıyla yazılır.
        // Bu kozmetik değil: diff incelerken SQL vurgulaması olması işi hızlandırır.
        return Verify(new Target("sql", ddl), settings);
    }

    /// <summary>
    /// Derleme deterministik olmalı: aynı girdi → aynı byte'lar.
    ///
    /// Bu olmadan golden dosyalar gürültülü olur ve git diff'leri güvenilmez hale gelir.
    /// (Mevcut modelde Guid.NewGuid() varsayılanları var; fixture'lar StableUuid'i açıkça
    /// verdiği için üretim deterministik olmalıdır — bu test onu garanti eder.)
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Ddl_generation_is_deterministic(string fixtureName, DatabaseType engine)
    {
        var factory = new DdlGeneratorFactory();

        var first = factory.GetGenerator(engine).Generate(SchemaFixtures.ByName(fixtureName));
        var second = factory.GetGenerator(engine).Generate(SchemaFixtures.ByName(fixtureName));

        Assert.Equal(first, second);
    }

    /// <summary>
    /// Üretilen DDL boş olmamalı ve en az bir CREATE TABLE içermeli.
    /// Golden dosyalar yanlışlıkla boş bir çıktıyı "onaylamasın" diye temel emniyet ağı.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Ddl_is_not_empty(string fixtureName, DatabaseType engine)
    {
        var ddl = new DdlGeneratorFactory().GetGenerator(engine).Generate(SchemaFixtures.ByName(fixtureName));

        Assert.False(string.IsNullOrWhiteSpace(ddl));
        Assert.Contains("CREATE TABLE", ddl, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Şemadaki her tablo çıktıda görünmeli — sessizce düşen tablo olmamalı.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void All_tables_appear_in_ddl(string fixtureName, DatabaseType engine)
    {
        var schema = SchemaFixtures.ByName(fixtureName);
        var ddl = new DdlGeneratorFactory().GetGenerator(engine).Generate(schema);

        foreach (var table in schema.Tables)
        {
            Assert.True(
                ddl.Contains(table.Name, StringComparison.OrdinalIgnoreCase),
                $"'{table.Name}' tablosu {engine} çıktısında yok.");
        }
    }
}
