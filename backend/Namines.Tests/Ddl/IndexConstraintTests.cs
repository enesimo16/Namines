using Namines.Core.Enums;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Tests.Fixtures;

namespace Namines.Tests.Ddl;

/// <summary>
/// Index / UNIQUE / CHECK üretimi.
///
/// Faz 1'de bu kavramlar modelde HİÇ YOKTU — üretilen şemalar index'siz geliyordu.
/// Yabancı anahtar kolonunda index olmaması, üretimdeki en yaygın performans hatasıdır.
/// Üstelik AI tercihleri ekranında "Enable Auto-Indexing Suggestions" seçeneği vardı
/// ama modelde index diye bir kavram olmadığı için o ayar hiçbir şey yapmıyordu.
/// </summary>
public class IndexConstraintTests
{
    private static readonly DatabaseType[] AllEngines =
    [
        DatabaseType.MSSQL, DatabaseType.PostgreSQL, DatabaseType.MySQL,
        DatabaseType.MariaDB, DatabaseType.SQLite, DatabaseType.Oracle
    ];

    public static TheoryData<DatabaseType> Engines()
    {
        var data = new TheoryData<DatabaseType>();
        foreach (var e in AllEngines) data.Add(e);
        return data;
    }

    private static string Ddl(DatabaseType engine) =>
        new DdlGeneratorFactory().GetGenerator(engine).Generate(SchemaFixtures.IndexesAndConstraints());

    // ── Temel üretim ─────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Engines))]
    public void Indexes_are_emitted(DatabaseType engine)
    {
        var ddl = Ddl(engine);

        Assert.Contains("CREATE INDEX", ddl, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Unique_index_is_emitted(DatabaseType engine)
    {
        var ddl = Ddl(engine);

        Assert.Contains("CREATE UNIQUE INDEX", ddl, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Unique_constraint_is_emitted(DatabaseType engine)
    {
        var ddl = Ddl(engine);

        Assert.Contains("UQ_Users_Email", ddl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNIQUE", ddl, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Check_constraint_is_emitted(DatabaseType engine)
    {
        var ddl = Ddl(engine);

        Assert.Contains("CHECK", ddl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Total >= 0", ddl);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Fk_column_gets_its_index(DatabaseType engine)
    {
        // En yaygın performans hatası: FK kolonunda index olmaması.
        var ddl = Ddl(engine);

        Assert.Contains("IX_Orders_UserId", ddl, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Composite_index_preserves_column_order_and_direction(DatabaseType engine)
    {
        var ddl = Ddl(engine);

        // CountryCode ASC, CreatedAt DESC — sıra ve yön sorgu planını belirler.
        Assert.Contains("CreatedAt", ddl);
        Assert.Contains("DESC", ddl, StringComparison.OrdinalIgnoreCase);
    }

    // ── Motor yetenek farkları ───────────────────────────────────────────────

    [Theory]
    [InlineData(DatabaseType.MSSQL)]
    [InlineData(DatabaseType.PostgreSQL)]
    [InlineData(DatabaseType.SQLite)]
    public void Partial_index_is_emitted_where_supported(DatabaseType engine)
    {
        var ddl = Ddl(engine);

        Assert.Contains("WHERE DeletedAt IS NULL", ddl, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DatabaseType.MySQL)]
    [InlineData(DatabaseType.MariaDB)]
    [InlineData(DatabaseType.Oracle)]
    public void Partial_index_is_flagged_not_silently_dropped(DatabaseType engine)
    {
        // Koşulu sessizce düşürmek, index'i kullanıcının istemediği bir şeye çevirirdi.
        // Bu motorlarda kısmi index yok; çıktıda açıklama olarak görünmeli.
        var ddl = Ddl(engine);

        Assert.Contains("kısmi index desteklemiyor", ddl);
        Assert.DoesNotContain("WHERE DeletedAt IS NULL;", ddl, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DatabaseType.MSSQL)]
    [InlineData(DatabaseType.PostgreSQL)]
    public void Include_columns_emitted_where_supported(DatabaseType engine)
    {
        var ddl = Ddl(engine);

        Assert.Contains("INCLUDE (", ddl, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DatabaseType.MySQL)]
    [InlineData(DatabaseType.MariaDB)]
    [InlineData(DatabaseType.SQLite)]
    [InlineData(DatabaseType.Oracle)]
    public void Include_columns_flagged_where_unsupported(DatabaseType engine)
    {
        var ddl = Ddl(engine);

        Assert.Contains("desteklemiyor", ddl);
    }

    [Fact]
    public void Oracle_index_names_fit_30_chars()
    {
        // Oracle 12c ve öncesinde tanımlayıcılar 30 karakterle sınırlı.
        var ddl = Ddl(DatabaseType.Oracle);

        var names = System.Text.RegularExpressions.Regex
            .Matches(ddl, @"CREATE (?:UNIQUE )?INDEX ""([^""]+)""")
            .Select(m => m.Groups[1].Value);

        Assert.All(names, n => Assert.True(n.Length <= 30, $"'{n}' {n.Length} karakter — Oracle limiti 30."));
    }

    // ── Geriye uyumluluk ─────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Engines))]
    public void Schemas_without_indexes_are_unaffected(DatabaseType engine)
    {
        // Eski kayıtlarda Indexes/Uniques/Checks alanları yok → boş liste.
        // Çıktıda hiçbir index/constraint satırı olmamalı.
        var ddl = new DdlGeneratorFactory().GetGenerator(engine).Generate(SchemaFixtures.ECommerce());

        Assert.DoesNotContain("CREATE INDEX", ddl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE UNIQUE INDEX", ddl, StringComparison.OrdinalIgnoreCase);
    }
}
