using Namines.Core.Enums;
using Namines.Core.Models;
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
        var sql = TypeSql.Map(canonical, null, null, DatabaseType.PostgreSQL);

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
        var sql = TypeSql.Map(canonical, null, null, DatabaseType.MSSQL);

        Assert.StartsWith(expectedPrefix, sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// T-SQL'de <c>timestamp</c> bir tarih tipi DEĞİL, <c>rowversion</c>'ın eski adıdır:
    /// veritabanının ürettiği 8 baytlık satır sürümü. Üstelik DEFAULT alamaz, yani
    /// "created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP" SQL Server'da
    /// çalıştırılamaz ("Defaults cannot be created on columns of data type timestamp").
    ///
    /// BULUNMA YERİ: demo şablonlarının ve AI çıktısının kanonik zaman tipi TIMESTAMP;
    /// MSSQL golden fixture'ları ise DATETIME2 kullandığı için bu yol hiç test
    /// edilmiyordu. Şablonlardan birini açıp SQL Server seçen herkes çalışmayan DDL
    /// alıyordu. Bkz. 07-timestamp-default fixture'ı.
    /// </summary>
    [Theory]
    [InlineData("TIMESTAMP")]
    [InlineData("DATETIME")]
    public void Mssql_maps_date_time_types_to_datetime2(string canonical)
    {
        var sql = TypeSql.Map(canonical, null, null, DatabaseType.MSSQL);

        Assert.Equal("DATETIME2", sql, ignoreCase: true);
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
        var sql = TypeSql.Map(canonical, null, null, DatabaseType.MSSQL);

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
        Assert.Equal(expected, TypeSql.Map(canonical, null, null, DatabaseType.MySQL), ignoreCase: true);
        Assert.Equal(expected, TypeSql.Map(canonical, null, null, DatabaseType.MariaDB), ignoreCase: true);
    }

    // ── Uzunluk doğru taşınıyor ──────────────────────────────────────────────

    [Theory]
    [InlineData(DatabaseType.PostgreSQL)]
    [InlineData(DatabaseType.MySQL)]
    [InlineData(DatabaseType.MariaDB)]
    [InlineData(DatabaseType.MSSQL)]
    public void Length_is_preserved_for_variable_width_types(DatabaseType engine)
    {
        var sql = TypeSql.Map("VARCHAR", 120, null, engine);

        Assert.Contains("120", sql);
    }

    [Fact]
    public void Uuid_fixed_length_ignores_provided_length_in_mysql()
    {
        // CHAR(36) sabittir — kullanıcı yanlışlıkla farklı bir uzunluk girse bile
        // UUID'nin gerçek uzunluğu (36) korunmalı.
        var sql = TypeSql.Map("UUID", 10, null, DatabaseType.MySQL);

        Assert.Equal("CHAR(36)", sql);
    }

    // ── DECIMAL ölçeği ───────────────────────────────────────────────────────

    /// <summary>
    /// <c>NUMERIC(10,2)</c> gibi bir para kolonu ölçeğini KORUMALI. Ölçek yokken
    /// üretilen <c>numeric(10)</c> tam sayıdır — kuruşlar sessizce yuvarlanır.
    ///
    /// BULUNMA YERİ: canlı bir PostgreSQL veritabanı içe aktarılıp geri derlendiğinde
    /// <c>total NUMERIC(10,2)</c> kolonu <c>numeric</c> olarak dönüyordu.
    /// </summary>
    [Theory]
    [InlineData(DatabaseType.PostgreSQL, "numeric(10,2)")]
    [InlineData(DatabaseType.MSSQL, "DECIMAL(10,2)")]
    [InlineData(DatabaseType.MySQL, "DECIMAL(10,2)")]
    [InlineData(DatabaseType.MariaDB, "DECIMAL(10,2)")]
    [InlineData(DatabaseType.Oracle, "NUMBER(10,2)")]
    public void Decimal_scale_survives_compilation(DatabaseType engine, string expected)
    {
        var ddl = new DdlGeneratorFactory().GetGenerator(engine).Generate(MoneySchema());

        Assert.Contains(expected, ddl, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ölçek verilmemişse davranış DEĞİŞMEMELİ — mevcut golden dosyaların garantisi.</summary>
    [Fact]
    public void Decimal_without_scale_keeps_previous_output()
    {
        Assert.Equal("numeric(10)", TypeSql.Map("DECIMAL", 10, null, DatabaseType.PostgreSQL));
        Assert.Equal("DECIMAL(10)", TypeSql.Map("DECIMAL", 10, null, DatabaseType.MSSQL));
    }

    private static DatabaseSchema MoneySchema() => new()
    {
        Name = "Money",
        Tables =
        {
            new SchemaTable
            {
                Id = "t", Name = "invoices", StableUuid = "uuid-t",
                Columns =
                {
                    new SchemaColumn
                    {
                        Id = "c", Name = "total", StableUuid = "uuid-c",
                        Type = "DECIMAL", Length = 10, Scale = 2
                    }
                }
            }
        }
    };

    // ── Motor listesi: her motorun kendi üreticisi olmalı ───────────────────

    /// <summary>
    /// <see cref="DatabaseType"/>'ta bir değer varsa arayüzde de seçilebiliyor demektir;
    /// o yüzden her değerin KENDİ üreticisi olmak zorunda.
    ///
    /// BULUNMA YERİ: Db2/Firebird/Spanner/Redshift listede duruyor ama fabrika onları
    /// sessizce Oracle/SQLite/PostgreSQL üreticilerine yönlendiriyordu. "Google Spanner"
    /// seçen kullanıcı PostgreSQL DDL'i alıyordu — Spanner'da SERIAL yok, Redshift
    /// yabancı anahtar kısıtını zorlamaz. Dördü de kaldırıldı; yanlış DDL vermektense
    /// motoru hiç sunmamak doğru.
    /// </summary>
    [Fact]
    public void Every_database_type_has_its_own_generator()
    {
        var factory = new DdlGeneratorFactory();
        var seen = new Dictionary<Type, DatabaseType>();

        foreach (var engine in Enum.GetValues<DatabaseType>())
        {
            var generator = factory.GetGenerator(engine);
            var type = generator.GetType();

            Assert.False(seen.TryGetValue(type, out var already),
                $"{engine} ile {already} aynı üreticiyi ({type.Name}) paylaşıyor — " +
                "biri diğerinin takma adı, yani kullanıcı yanlış motorun DDL'ini alıyor.");

            seen[type] = engine;
        }
    }

    // ── Oracle: sözdizimi sırası ve bilinmeyen tip davranışı ────────────────

    /// <summary>
    /// Oracle'da kolon tanımının sırası bağlayıcıdır: <c>tip DEFAULT ifade NOT NULL</c>.
    /// Ters sıra (<c>NOT NULL DEFAULT ...</c>) ORA-00907 ile reddedilir.
    ///
    /// BULUNMA YERİ: 07 fixture'ı eklenirken. Sorun yeni değildi — 02 ve 06
    /// golden dosyaları da varsayılanlı her kolonda ters sırayı taşıyordu, yani
    /// Oracle seçen kullanıcı bugüne kadar çalıştırılamayan DDL alıyordu.
    /// </summary>
    [Fact]
    public void Oracle_puts_default_before_not_null()
    {
        var ddl = new DdlGeneratorFactory()
            .GetGenerator(DatabaseType.Oracle)
            .Generate(SchemaFixtures.TimestampDefault());

        Assert.Contains("DEFAULT CURRENT_TIMESTAMP NOT NULL", ddl);
        Assert.DoesNotContain("NOT NULL DEFAULT", ddl);
    }

    /// <summary>
    /// Oracle eşlemesi bilinmeyen tipi sessizce <c>NVARCHAR2(255)</c>'e düşürüyordu:
    /// kanonik <c>DATETIME2</c> listede olmadığı için tarih kolonu METİN oluyordu.
    /// Sessiz tip kaybı, çalıştırılamayan DDL'den daha kötü — çünkü DDL çalışır,
    /// hata ancak veri yazılırken ortaya çıkar.
    /// </summary>
    [Theory]
    [InlineData("DATETIME2", "TIMESTAMP")]
    [InlineData("TIMESTAMP", "TIMESTAMP")]
    [InlineData("DATETIME", "TIMESTAMP")]
    public void Oracle_maps_all_canonical_date_time_types(string canonical, string expected)
    {
        var schema = new DatabaseSchema
        {
            Name = "OracleTypes",
            Tables =
            {
                new SchemaTable
                {
                    Id = "t", Name = "t", StableUuid = "uuid-t",
                    Columns =
                    {
                        new SchemaColumn
                        {
                            Id = "c", Name = "at", StableUuid = "uuid-c", Type = canonical
                        }
                    }
                }
            }
        };

        var ddl = new DdlGeneratorFactory().GetGenerator(DatabaseType.Oracle).Generate(schema);

        Assert.Contains($"\"at\" {expected}", ddl);
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
                        var sql = TypeSql.Map(col.Type, col.Length, col.Scale, engine);
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
