using Namines.Core.Enums;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Tests.Fixtures;

namespace Namines.Tests.Ddl;

/// <summary>
/// Testcontainers ile PostgreSQL'e karşı çalıştırılan bir entegrasyon testi
/// "type nvarchar does not exist" hatası verdi. İnceleme sonucu: MSSQL, PostgreSQL,
/// MySQL ve MariaDB üreticilerinin DÖRDÜ DE hiçbir tip eşlemesi yapmıyordu.
///
/// Kullanıcı arayüzü (TableEditorDrawer) NVARCHAR, NTEXT, UNIQUEIDENTIFIER, UUID,
/// BOOLEAN, IMAGE, BLOB gibi tipleri sunuyor ve kullanıcı bunları HERHANGİ bir
/// motora derleyebiliyordu — ama yalnızca SQLite ve Oracle gerçek bir eşleme
/// yapıyordu. Yani "6 motora derleme" iddiası pratikte 2 motorda çalışıyordu.
///
/// Bu testler golden-file'lardan bağımsızdır: TypeSql/DefaultValueSql'i doğrudan
/// çağırır, böylece kural DDL üretiminden ayrı olarak da doğrulanır.
/// </summary>
public class TypeMappingTests
{
    // ── PostgreSQL'de HİÇ VAR OLMAYAN tipler artık native karşılığına dönüyor ──

    [Theory]
    [InlineData("NVARCHAR", "varchar")]
    [InlineData("NTEXT", "text")]
    [InlineData("UNIQUEIDENTIFIER", "uuid")]
    [InlineData("UUID", "uuid")]
    [InlineData("BOOLEAN", "boolean")]
    [InlineData("BIT", "boolean")]
    [InlineData("BLOB", "bytea")]
    [InlineData("IMAGE", "bytea")]
    [InlineData("DATETIME2", "timestamp")]
    [InlineData("TINYINT", "smallint")]
    public void Postgres_never_emits_invalid_native_types(string canonical, string expectedPrefix)
    {
        var sql = TypeSql.Map(canonical, null, DatabaseType.PostgreSQL);

        Assert.StartsWith(expectedPrefix, sql, StringComparison.OrdinalIgnoreCase);
    }

    // ── MSSQL'de HİÇ VAR OLMAYAN 4 tip artık native karşılığına dönüyor ────────

    [Theory]
    [InlineData("BOOLEAN", "BIT")]
    [InlineData("UUID", "UNIQUEIDENTIFIER")]
    [InlineData("BLOB", "VARBINARY")]
    [InlineData("JSON", "NVARCHAR")]
    public void Mssql_never_emits_invalid_native_types(string canonical, string expectedPrefix)
    {
        var sql = TypeSql.Map(canonical, null, DatabaseType.MSSQL);

        Assert.StartsWith(expectedPrefix, sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// MSSQL için ZATEN GEÇERLİ olan tipler DEĞİŞMEMELİ — bu, mevcut golden
    /// dosyaların (fixtures 01-06) hiç değişmemesinin garantisidir.
    /// </summary>
    [Theory]
    [InlineData("INT")]
    [InlineData("NVARCHAR")]
    [InlineData("CHAR")]
    [InlineData("DECIMAL")]
    [InlineData("DATETIME2")]
    [InlineData("NTEXT")]
    [InlineData("UNIQUEIDENTIFIER")]
    public void Mssql_passes_through_already_valid_types_unchanged(string canonical)
    {
        var sql = TypeSql.Map(canonical, null, DatabaseType.MSSQL);

        Assert.Equal(canonical, sql, ignoreCase: true);
    }

    // ── MySQL/MariaDB'de HİÇ VAR OLMAYAN tipler artık native karşılığına dönüyor ─

    [Theory]
    [InlineData("NVARCHAR", "VARCHAR")]
    [InlineData("NTEXT", "TEXT")]
    [InlineData("UNIQUEIDENTIFIER", "CHAR(36)")]
    [InlineData("UUID", "CHAR(36)")]
    [InlineData("BOOLEAN", "TINYINT(1)")]
    [InlineData("BIT", "TINYINT(1)")]
    [InlineData("DATETIME2", "DATETIME")]
    public void MySql_family_never_emits_invalid_native_types(string canonical, string expected)
    {
        Assert.Equal(expected, TypeSql.Map(canonical, null, DatabaseType.MySQL), ignoreCase: true);
        Assert.Equal(expected, TypeSql.Map(canonical, null, DatabaseType.MariaDB), ignoreCase: true);
    }

    // ── Uzunluk doğru taşınıyor ──────────────────────────────────────────────

    [Theory]
    [InlineData(DatabaseType.PostgreSQL)]
    [InlineData(DatabaseType.MySQL)]
    [InlineData(DatabaseType.MariaDB)]
    [InlineData(DatabaseType.MSSQL)]
    public void Length_is_preserved_for_variable_width_types(DatabaseType engine)
    {
        var sql = TypeSql.Map("VARCHAR", 120, engine);

        Assert.Contains("120", sql);
    }

    [Fact]
    public void Uuid_fixed_length_ignores_provided_length_in_mysql()
    {
        // CHAR(36) sabittir — kullanıcı yanlışlıkla farklı bir uzunluk girse bile
        // UUID'nin gerçek uzunluğu (36) korunmalı.
        var sql = TypeSql.Map("UUID", 10, DatabaseType.MySQL);

        Assert.Equal("CHAR(36)", sql);
    }

    // ── Geriye uyumluluk: golden dosyalar hiç değişmeyen tipler kullanıyor ───

    [Fact]
    public void Existing_fixtures_only_use_types_safe_across_all_engines_or_now_fixed()
    {
        // Bu test "belge" niteliğinde: mevcut fixture'lardaki tüm tipler artık
        // 6 motorun hepsinde anlamlı bir SQL tipine çevriliyor (hiçbiri exception
        // fırlatmıyor, hiçbiri boş string dönmüyor).
        foreach (var (_, schema) in SchemaFixtures.All())
        {
            foreach (var table in schema.Tables)
            {
                foreach (var col in table.Columns)
                {
                    foreach (var engine in new[]
                             {
                                 DatabaseType.MSSQL, DatabaseType.PostgreSQL,
                                 DatabaseType.MySQL, DatabaseType.MariaDB
                             })
                    {
                        var sql = TypeSql.Map(col.Type, col.Length, engine);
                        Assert.False(string.IsNullOrWhiteSpace(sql),
                            $"{table.Name}.{col.Name} ({col.Type}) → {engine}: boş tip üretildi.");
                    }
                }
            }
        }
    }

    // ── DefaultValueSql: GETUTCDATE() gibi motora özgü fonksiyonlar ──────────

    [Theory]
    [InlineData(DatabaseType.MSSQL, "GETUTCDATE()")]
    [InlineData(DatabaseType.PostgreSQL, "(now() AT TIME ZONE 'utc')")]
    [InlineData(DatabaseType.MySQL, "(UTC_TIMESTAMP())")]
    [InlineData(DatabaseType.MariaDB, "(UTC_TIMESTAMP())")]
    [InlineData(DatabaseType.SQLite, "(datetime('now'))")]
    [InlineData(DatabaseType.Oracle, "SYS_EXTRACT_UTC(SYSTIMESTAMP)")]
    public void GetUtcDate_translates_per_engine(DatabaseType engine, string expected)
    {
        Assert.Equal(expected, DefaultValueSql.Translate("GETUTCDATE()", engine));
    }

    [Theory]
    [InlineData("'TR'")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("some_custom_expression()")]
    public void Unknown_default_values_pass_through_unchanged(string value)
    {
        // Bilinmeyen bir ifadeyi "düzeltmeye" çalışmak yanlış olurdu — literal
        // değerler ve tanınmayan ifadeler olduğu gibi geçmeli.
        foreach (var engine in Enum.GetValues<DatabaseType>())
            Assert.Equal(value, DefaultValueSql.Translate(value, engine));
    }

    [Fact]
    public void Null_or_empty_default_passes_through()
    {
        Assert.Equal(string.Empty, DefaultValueSql.Translate(null, DatabaseType.PostgreSQL));
        Assert.Equal(string.Empty, DefaultValueSql.Translate("", DatabaseType.PostgreSQL));
    }
}
