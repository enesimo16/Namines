using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;

namespace Namines.Tests.Ddl;

/// <summary>
/// Tanımlayıcı alıntılaması — <c>docs/last-review</c> C-1.
///
/// <b>Bulunan açık:</b> altı motor üreticisinin ve <see cref="EnumSql"/>'in
/// <c>Quote</c>'u ayırıcıyı İKİLEMİYORDU (<c>$"\"{identifier}\""</c>), ve altı
/// üreticide birincil anahtar kolonları <c>Quote</c>'u tamamen atlayıp satır içinde
/// interpolate ediliyordu. Şema istemciden geldiği için (CompileController şemayı
/// gövdeden alıp yalnızca null kontrolüyle üreticiye veriyor) bir tablo adı
/// alıntılanmış tanımlayıcıyı kapatıp ifadeye devam edebiliyordu. Üretilen betik
/// <c>/api/docker/run</c> ile sandbox container'ında SUPERUSER olarak çalışıyor.
///
/// Bu testler Docker gerektirmiyor: üreticinin metin çıktısına bakıyorlar.
/// </summary>
public class IdentifierEscapingTests
{
    private static readonly DatabaseType[] AllEngines =
    {
        DatabaseType.PostgreSQL, DatabaseType.MySQL, DatabaseType.MariaDB,
        DatabaseType.MSSQL, DatabaseType.Oracle, DatabaseType.SQLite,
    };

    /// <summary>Motorun kendi ayırıcısı — kaçışın ikilemesi gereken karakter.</summary>
    private static string Delimiter(DatabaseType engine) => engine switch
    {
        DatabaseType.MSSQL => "]",
        DatabaseType.MySQL or DatabaseType.MariaDB => "`",
        _ => "\"",
    };

    private static DatabaseSchema SchemaWith(string tableName, string columnName) => new()
    {
        Name = "audit",
        Tables =
        {
            new SchemaTable
            {
                Id = "t1", Name = tableName,
                Columns =
                {
                    new SchemaColumn { Id = "c1", Name = columnName, Type = "INT", IsPK = true },
                    new SchemaColumn { Id = "c2", Name = "note", Type = "VARCHAR" },
                },
            },
        },
    };

    private static string Ddl(DatabaseType engine, DatabaseSchema schema) =>
        new DdlGeneratorFactory().GetGenerator(engine).Generate(schema);

    [Fact]
    public void Every_engine_doubles_its_own_delimiter_in_a_table_name()
    {
        foreach (var engine in AllEngines)
        {
            var delimiter = Delimiter(engine);
            // Ada ayırıcıyı GÖMÜYORUZ ki çıktıda ikilenmiş hâli aranabilsin.
            var schema = SchemaWith($"orders{delimiter}x", "id");

            var ddl = Ddl(engine, schema);

            Assert.Contains($"orders{delimiter}{delimiter}x", ddl);
            // Tek (ikilenmemiş) ayırıcı, tanımlayıcıyı erken kapatırdı.
            Assert.DoesNotContain($"orders{delimiter}x", ddl);
        }
    }

    [Fact]
    public void Every_engine_doubles_its_own_delimiter_in_a_column_name()
    {
        foreach (var engine in AllEngines)
        {
            var delimiter = Delimiter(engine);
            var schema = SchemaWith("orders", $"id{delimiter}x");

            var ddl = Ddl(engine, schema);

            Assert.Contains($"id{delimiter}{delimiter}x", ddl);
            Assert.DoesNotContain($"id{delimiter}x", ddl);
        }
    }

    /// <summary>
    /// Birincil anahtar kolonları ve kısıt adı, üreticilerde <c>Quote</c>'u ATLAYIP
    /// satır içinde interpolate ediliyordu — tablo/kolon adı düzeltilse bile bu yol
    /// açık kalırdı. PK kolonu yukarıdaki testte zaten PK; burada kısıt ADININ da
    /// (tablo adından türüyor) kaçırıldığı doğrulanıyor.
    /// </summary>
    [Theory]
    [InlineData(DatabaseType.PostgreSQL)]
    [InlineData(DatabaseType.MSSQL)]
    public void Primary_key_constraint_name_is_escaped_too(DatabaseType engine)
    {
        var delimiter = Delimiter(engine);
        var schema = SchemaWith($"orders{delimiter}x", "id");

        var ddl = Ddl(engine, schema);

        Assert.Contains($"PK_orders{delimiter}{delimiter}x", ddl);
    }

    [Fact]
    public void Enum_type_names_are_escaped()
    {
        // EnumSql kendi Quote'unu taşıyordu ve o da ikilemiyordu.
        foreach (var engine in new[] { DatabaseType.PostgreSQL, DatabaseType.MSSQL })
        {
            var delimiter = Delimiter(engine);
            var schema = SchemaWith("orders", "id");
            schema.Enums.Add(new SchemaEnum
            {
                Name = $"status{delimiter}x",
                Values = { "new", "shipped" },
            });

            var ddl = Ddl(engine, schema);

            Assert.DoesNotContain($"status{delimiter}x", ddl);
        }
    }

    /// <summary>
    /// Kontrol karakterleri ikilenerek etkisizleştirilemez: bir satır sonu üretilen
    /// betikteki <c>--</c> yorumunu erken kapatabilir. Sessizce temizlemek yerine
    /// reddediliyor — temizlemek, kullanıcının yazdığından farklı adla tablo açardı.
    /// </summary>
    [Theory]
    [InlineData("orders\nusers")]
    [InlineData("orders\rusers")]
    [InlineData("orders\tusers")]
    [InlineData("orders\0users")]
    public void Control_characters_in_an_identifier_are_rejected(string tableName)
    {
        foreach (var engine in AllEngines)
        {
            var schema = SchemaWith(tableName, "id");

            Assert.Throws<ArgumentException>(() => Ddl(engine, schema));
        }
    }

    [Fact]
    public void An_ordinary_schema_still_compiles_on_every_engine()
    {
        // Kaçış eklemek normal şemaları bozmamalı.
        foreach (var engine in AllEngines)
        {
            var ddl = Ddl(engine, SchemaWith("orders", "id"));

            Assert.Contains("orders", ddl);
            Assert.Contains("note", ddl);
        }
    }

    /// <summary>
    /// Tam bir şema: yabancı anahtar, index, UNIQUE ve enum — her birinin ADI ayrı
    /// bir tanımlayıcı ve her biri ayrı bir kod yolundan alıntılanıyor.
    ///
    /// <b>Bu test gerçek bir eksik buldu:</b> yalnızca tablo+kolon bakan basit
    /// şemayla altı motorun beşi temiz görünüyordu, ama Oracle'ın birincil anahtar
    /// KISIT adı <c>Quote</c>'u atlayıp satır içinde yazılıyordu
    /// (<c>CONSTRAINT "PK_orders"x"</c>) — yani tablo adı doğru kaçırılsa bile
    /// tanımlayıcı kısıt adında kapanıyordu. Kaçış eklerken "hepsini yakaladım"
    /// varsayımı bu yüzden test edilmeden kabul edilmemeli.
    /// </summary>
    [Fact]
    public void No_engine_leaks_a_single_delimiter_from_any_kind_of_name()
    {
        foreach (var engine in AllEngines)
        {
            var delimiter = Delimiter(engine);
            string N(string prefix) => prefix + delimiter + "x";

            var schema = new DatabaseSchema
            {
                Name = "audit",
                Enums = { new SchemaEnum { Id = "e1", Name = N("status"), Values = { "new", "shipped" } } },
                Tables =
                {
                    new SchemaTable
                    {
                        Id = "t1", Name = N("customers"),
                        Columns = { new SchemaColumn { Id = "c1", Name = N("cid"), Type = "INT", IsPK = true } },
                    },
                    new SchemaTable
                    {
                        Id = "t2", Name = N("orders"),
                        Columns =
                        {
                            new SchemaColumn { Id = "c2", Name = N("oid"), Type = "INT", IsPK = true },
                            new SchemaColumn { Id = "c3", Name = N("owner"), Type = "INT", IsFK = true },
                            new SchemaColumn { Id = "c4", Name = N("state"), Type = "TEXT", EnumRef = N("status") },
                            new SchemaColumn { Id = "c5", Name = N("note"), Type = "VARCHAR", IsNullable = true },
                        },
                        Indexes =
                        {
                            new SchemaIndex
                            {
                                Id = "i1", Name = N("IX_owner"),
                                Columns = { new SchemaIndexColumn { ColumnId = "c3" } },
                            },
                        },
                        Uniques = { new SchemaUnique { Id = "u1", Name = N("UQ_note"), ColumnIds = { "c5" } } },
                    },
                },
                Relations =
                {
                    new SchemaRelation
                    {
                        Id = "r1", Type = "one-to-many",
                        SourceTableId = "t2", SourceColumnId = "c3",
                        TargetTableId = "t1", TargetColumnId = "c1",
                    },
                },
            };

            var ddl = Ddl(engine, schema);

            // Tek (ikilenmemiş) ayırıcı hiçbir yerde kalmamalı: kaldığı yer,
            // tanımlayıcının erken kapandığı ve gerisinin SQL olarak okunduğu yerdir.
            foreach (var prefix in new[] { "customers", "orders", "cid", "oid", "owner", "state", "note", "status", "IX_owner", "UQ_note" })
                Assert.DoesNotContain(prefix + delimiter + "x", ddl);
        }
    }
}
