using System.Text;
using System.Text.RegularExpressions;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;

namespace Namines.Tests.Ddl;

/// <summary>
/// Kısıt/index adlarının motor sınırına sığdırılması ve kesme sonrası yeniden
/// tekilleştirilmesi (bkz. <see cref="ObjectNameRegistry"/>). Eski <c>Fit</c> yalnızca
/// Oracle'da 30'a kesiyor, kesilen adlar yeniden çakışıyor, PostgreSQL (63) ve
/// MySQL/MariaDB (64) sınırı hiç uygulanmıyordu.
/// </summary>
public class ObjectNameLimitTests
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

    // ── Kayıt defteri birim davranışı ────────────────────────────────────────

    [Theory]
    [InlineData(DatabaseType.PostgreSQL, 63)]
    [InlineData(DatabaseType.MySQL, 64)]
    [InlineData(DatabaseType.MariaDB, 64)]
    [InlineData(DatabaseType.MSSQL, 128)]
    [InlineData(DatabaseType.Oracle, 30)]
    public void Limits_match_the_engines(DatabaseType engine, int expected)
    {
        Assert.Equal(expected, ObjectNameRegistry.MaxLength(engine));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void A_name_that_fits_is_returned_untouched(DatabaseType engine)
    {
        Assert.Equal("UQ_Users_Email", ObjectNameRegistry.Shorten("UQ_Users_Email", engine));
    }

    [Fact]
    public void A_name_at_exactly_the_limit_is_untouched()
    {
        var name = new string('a', 63);

        Assert.Equal(name, ObjectNameRegistry.Shorten(name, DatabaseType.PostgreSQL));
        Assert.NotEqual(name + "b", ObjectNameRegistry.Shorten(name + "b", DatabaseType.PostgreSQL));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void An_overlong_name_is_cut_to_the_limit_with_a_stable_hash(DatabaseType engine)
    {
        var name = "IX_" + new string('x', 300);

        var first = ObjectNameRegistry.Shorten(name, engine);
        var second = ObjectNameRegistry.Shorten(name, engine);

        if (engine == DatabaseType.SQLite)
        {
            Assert.Equal(name, first); // SQLite sınır koymaz
            return;
        }

        Assert.Equal(first, second); // deterministik
        Assert.True(Encoding.UTF8.GetByteCount(first) <= ObjectNameRegistry.MaxLength(engine));
        Assert.Matches(new Regex(@"_[0-9a-f]{8}$"), first);
    }

    [Fact]
    public void Two_long_names_with_the_same_prefix_stay_distinct_after_truncation()
    {
        var a = new string('p', 80) + "_a";
        var b = new string('p', 80) + "_b";

        Assert.NotEqual(
            ObjectNameRegistry.Shorten(a, DatabaseType.PostgreSQL),
            ObjectNameRegistry.Shorten(b, DatabaseType.PostgreSQL));
    }

    [Fact]
    public void Postgres_counts_bytes_not_characters()
    {
        // 40 adet 'ş' = 40 karakter ama 80 bayt; PostgreSQL 63 BAYT'ta keser.
        var name = new string('ş', 40);

        var shortened = ObjectNameRegistry.Shorten(name, DatabaseType.PostgreSQL);

        Assert.True(Encoding.UTF8.GetByteCount(shortened) <= 63);
        // Çok baytlı karakter ortadan bölünmemeli.
        Assert.DoesNotContain('�', shortened);
    }

    [Fact]
    public void Colliding_names_get_a_counter_and_still_fit()
    {
        var registry = new ObjectNameRegistry(DatabaseType.PostgreSQL);
        var longName = new string('q', 100);

        var first = registry.ClaimConstraint("t", longName);
        var second = registry.ClaimConstraint("t", longName);

        Assert.NotEqual(first, second);
        Assert.All(new[] { first, second }, n => Assert.True(n.Length <= 63));
    }

    [Fact]
    public void Same_short_name_twice_gets_the_old_numeric_suffix()
    {
        var registry = new ObjectNameRegistry(DatabaseType.MSSQL);

        Assert.Equal("UQ_a", registry.ClaimConstraint("t", "UQ_a", createsIndex: true));
        Assert.Equal("UQ_a_2", registry.ClaimConstraint("t", "UQ_a", createsIndex: true));
    }

    [Fact]
    public void Index_name_scope_follows_the_engine()
    {
        // MySQL: index adı tablo başına -> iki tabloda aynı ad geçerli, değişmemeli.
        var mysql = new ObjectNameRegistry(DatabaseType.MySQL);
        Assert.Equal("ix_created", mysql.ClaimIndex("a", "ix_created"));
        Assert.Equal("ix_created", mysql.ClaimIndex("b", "ix_created"));

        // PostgreSQL: index adı şema genelinde -> ikincisi çakışır ve ayrışmalı.
        var pg = new ObjectNameRegistry(DatabaseType.PostgreSQL);
        Assert.Equal("ix_created", pg.ClaimIndex("a", "ix_created"));
        Assert.Equal("ix_created_2", pg.ClaimIndex("b", "ix_created"));
    }

    [Fact]
    public void A_unique_constraint_and_an_index_cannot_share_a_name_on_the_same_table()
    {
        // UNIQUE kısıtı aynı adlı bir index yaratır.
        var registry = new ObjectNameRegistry(DatabaseType.MySQL);

        Assert.Equal("uq_email", registry.ClaimConstraint("users", "uq_email", createsIndex: true));
        Assert.Equal("uq_email_2", registry.ClaimIndex("users", "uq_email"));
    }

    // ── Üretilen DDL ─────────────────────────────────────────────────────────

    private static DatabaseSchema LongNameSchema()
    {
        // İki tablo aynı 70 karakterlik önekle başlıyor: eski Oracle kesmesi (30) ve yeni
        // sınırlar altında üretilen varsayılan adlar kesildikten sonra çakışır.
        var prefix = new string('t', 70);

        SchemaTable Table(string suffix, string id)
        {
            var t = new SchemaTable
            {
                Id = id,
                Name = prefix + suffix,
                Columns =
                {
                    new SchemaColumn { Id = id + "c1", Name = "id", Type = "INT", IsPK = true },
                    new SchemaColumn { Id = id + "c2", Name = "state", Type = "TEXT", EnumRef = "status" },
                    new SchemaColumn { Id = id + "c3", Name = "email", Type = "VARCHAR", Length = 50 },
                    new SchemaColumn { Id = id + "c4", Name = "owner_id", Type = "INT" },
                },
            };
            t.Uniques.Add(new SchemaUnique { Id = id + "u", ColumnIds = { id + "c3" } });
            t.Checks.Add(new SchemaCheck { Id = id + "k", Expression = "id > 0" });
            t.Indexes.Add(new SchemaIndex { Id = id + "i", Columns = { new SchemaIndexColumn { ColumnId = id + "c3" } } });
            return t;
        }

        var schema = new DatabaseSchema
        {
            Name = "long",
            Enums = { new SchemaEnum { Id = "e", Name = "status", Values = { "a", "b" } } },
            Tables = { Table("_one", "a"), Table("_two", "b") },
        };
        schema.Relations.Add(new SchemaRelation
        {
            Id = "r", SourceTableId = "a", SourceColumnId = "ac4", TargetTableId = "b", TargetColumnId = "bc1",
        });
        return schema;
    }

    private static List<string> Names(string ddl)
    {
        // CONSTRAINT <q>ad<q> ve [UNIQUE] INDEX <q>ad<q>; ayırıcı motora göre " ` [
        var matches = Regex.Matches(ddl, @"(?:CONSTRAINT|INDEX)\s+[""`\[]([^""`\]]+)[""`\]]");
        return matches.Select(m => m.Groups[1].Value).ToList();
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Generated_names_fit_the_engine_limit_and_are_unique(DatabaseType engine)
    {
        var ddl = new DdlGeneratorFactory().GetGenerator(engine).Generate(LongNameSchema());
        var names = Names(ddl);

        Assert.NotEmpty(names);

        var limit = ObjectNameRegistry.MaxLength(engine);
        // Tablo adları da 70+ karakter; yalnızca kısıt/index adları kısıtlanır.
        Assert.All(names, n =>
            Assert.True(Encoding.UTF8.GetByteCount(n) <= limit, $"{engine}: '{n}' sınırı ({limit}) aşıyor."));

        // SQLite'ta kısıt adı yalnızca CHECK/UNIQUE'te var ve sınırsız; yine de tekil olmalı.
        var distinct = names.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Assert.Equal(names.Count, distinct);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Name_shortening_is_deterministic(DatabaseType engine)
    {
        var factory = new DdlGeneratorFactory();

        Assert.Equal(
            factory.GetGenerator(engine).Generate(LongNameSchema()),
            factory.GetGenerator(engine).Generate(LongNameSchema()));
    }

    [Fact]
    public void Enum_check_names_are_registered_with_other_constraints()
    {
        // Kullanıcının CHECK'i enum CHECK'inin adını taşıyor: tekilleşmeli (eskiden enum adı kayıt dışıydı).
        var schema = new DatabaseSchema
        {
            Name = "s",
            Enums = { new SchemaEnum { Id = "e", Name = "status", Values = { "a" } } },
            Tables =
            {
                new SchemaTable
                {
                    Id = "t", Name = "orders",
                    Columns =
                    {
                        new SchemaColumn { Id = "c1", Name = "id", Type = "INT", IsPK = true },
                        new SchemaColumn { Id = "c2", Name = "state", Type = "TEXT", EnumRef = "status" },
                    },
                    Checks = { new SchemaCheck { Id = "k", Name = "CK_orders_state", Expression = "id > 0" } },
                },
            },
        };

        var ddl = new DdlGeneratorFactory().GetGenerator(DatabaseType.SQLite).Generate(schema);

        Assert.Contains("CONSTRAINT \"CK_orders_state\" CHECK (\"state\" IN", ddl);
        Assert.Contains("CONSTRAINT \"CK_orders_state_2\" CHECK (id > 0)", ddl);
    }

    [Fact]
    public void Normal_names_are_not_rewritten()
    {
        var ddl = new DdlGeneratorFactory().GetGenerator(DatabaseType.Oracle).Generate(LongNameSchemaWithShortNames());

        Assert.Contains("\"PK_orders\"", ddl);
        Assert.Contains("\"UQ_orders_email\"", ddl);
        Assert.Contains("\"IX_orders_email\"", ddl);
        Assert.Contains("\"FK_orders_users_owner_id\"", ddl);
        Assert.DoesNotMatch(new Regex(@"_[0-9a-f]{8}"""), ddl);
    }

    private static DatabaseSchema LongNameSchemaWithShortNames()
    {
        var schema = new DatabaseSchema
        {
            Name = "short",
            Tables =
            {
                new SchemaTable
                {
                    Id = "o", Name = "orders",
                    Columns =
                    {
                        new SchemaColumn { Id = "o1", Name = "id", Type = "INT", IsPK = true },
                        new SchemaColumn { Id = "o2", Name = "email", Type = "VARCHAR", Length = 50 },
                        new SchemaColumn { Id = "o3", Name = "owner_id", Type = "INT" },
                    },
                    Uniques = { new SchemaUnique { Id = "u", ColumnIds = { "o2" } } },
                    Indexes = { new SchemaIndex { Id = "i", Columns = { new SchemaIndexColumn { ColumnId = "o2" } } } },
                },
                new SchemaTable
                {
                    Id = "u", Name = "users",
                    Columns = { new SchemaColumn { Id = "u1", Name = "id", Type = "INT", IsPK = true } },
                },
            },
        };
        schema.Relations.Add(new SchemaRelation
        {
            Id = "r", SourceTableId = "o", SourceColumnId = "o3", TargetTableId = "u", TargetColumnId = "u1",
        });
        return schema;
    }
}
