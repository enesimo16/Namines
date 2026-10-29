using System.Text.RegularExpressions;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;

namespace Namines.Tests.Security;

/// <summary>
/// Ham SQL parçalarının (ifade, DEFAULT, tip, index yöntemi, enum sabiti, yorum)
/// tek doğrulayıcıdan geçtiğini kanıtlar. Üretilen betik sandbox'ta psql/mysql
/// istemcisine verildiği için satır sonu + <c>\!</c> ya da <c>--</c> kuyruğu
/// container içinde komut çalıştırmaya kadar gidebiliyordu (bkz. SqlFragmentGuard).
/// </summary>
public class DdlFragmentHardeningTests
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

    private static DatabaseSchema Schema(Action<SchemaTable> configureTable, Action<SchemaColumn>? configureColumn = null)
    {
        var column = new SchemaColumn { Id = "c2", Name = "total", Type = "INT" };
        configureColumn?.Invoke(column);

        var table = new SchemaTable
        {
            Id = "t1",
            Name = "orders",
            Columns = { new SchemaColumn { Id = "c1", Name = "id", Type = "INT", IsPK = true }, column },
        };
        configureTable(table);

        return new DatabaseSchema { Name = "s", Tables = { table } };
    }

    private static string Ddl(DatabaseType engine, DatabaseSchema schema) =>
        new DdlGeneratorFactory().GetGenerator(engine).Generate(schema);

    // ── Hesaplanan kolon ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("1) STORED\n\\! id\n--")]
    [InlineData("a * 2\r\n\\! cmd")]
    [InlineData("a * 2) STORED --")]
    [InlineData("a * 2 /* x */")]
    [InlineData("a * 2; DROP TABLE x")]
    [InlineData("a\\! id")]
    public void Generated_expression_with_payload_is_rejected(string payload)
    {
        // SQLite hesaplanan kolon için PK'yı reddeder; PK olmayan kolon kullanılıyor.
        foreach (var engine in AllEngines)
            Assert.Throws<ArgumentException>(() =>
                Ddl(engine, Schema(_ => { }, c => c.Generated = payload)));
    }

    [Fact]
    public void Generated_expression_clean_value_is_unchanged()
    {
        var ddl = Ddl(DatabaseType.PostgreSQL, Schema(_ => { }, c => c.Generated = "id * 2"));

        Assert.Contains("GENERATED ALWAYS AS (id * 2) STORED", ddl);
    }

    [Fact]
    public void MySql_hash_comment_in_expression_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            Ddl(DatabaseType.MySQL, Schema(_ => { }, c => c.Generated = "id * 2 # rest")));
        // '#' MySQL dışında yorum değil (ör. PostgreSQL JSON operatörü #>).
        Assert.Contains("#>", Ddl(DatabaseType.PostgreSQL, Schema(_ => { }, c => c.Generated = "(id::text::jsonb #> '{a}')::int")));
    }

    // ── CHECK ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("a>0\n\\! cmd")]
    [InlineData("a>0\u0001")]
    [InlineData("a>0 -- trailing")]
    [InlineData("a>0 /* c */")]
    [InlineData("a>0\\")]
    public void Check_expression_with_payload_is_rejected(string payload)
    {
        foreach (var engine in AllEngines)
            Assert.Throws<ArgumentException>(() =>
                Ddl(engine, Schema(t => t.Checks.Add(new SchemaCheck { Id = "ck", Name = "ck", Expression = payload }))));
    }

    [Fact]
    public void Multi_line_expressions_are_accepted_and_emitted_on_one_line()
    {
        const string multi = "total >= 0\r\n  AND total\t< 100";

        foreach (var engine in AllEngines)
        {
            var ddl = Ddl(engine, Schema(t => t.Checks.Add(new SchemaCheck { Id = "ck", Name = "rng", Expression = multi })));
            Assert.Contains("CHECK (total >= 0 AND total < 100)", ddl);
        }

        Assert.Contains("GENERATED ALWAYS AS (id * 2 + 1) STORED",
            Ddl(DatabaseType.PostgreSQL, Schema(_ => { }, c => c.Generated = "id * 2\n + 1")));
        Assert.Contains("WHERE total > 0 AND total < 9",
            Ddl(DatabaseType.PostgreSQL, Schema(t => t.Indexes.Add(Index(i => i.Where = "total > 0\n AND total < 9")))));
        Assert.Contains("DEFAULT now() + interval '1 day'",
            Ddl(DatabaseType.PostgreSQL, Schema(_ => { }, c => c.DefaultValue = "now()\n + interval '1 day'")));
    }

    [Theory]
    [InlineData("public.citext", "public.citext")]
    [InlineData("myschema.my_enum", "myschema.my_enum")]
    [InlineData("timestamp with time zone", "timestamp with time zone")]
    [InlineData("character varying(255)", "character varying(255)")]
    [InlineData("numeric(10,2)", "numeric(10,2)")]
    [InlineData("text[]", "text[]")]
    public void Schema_qualified_and_multi_word_type_names_are_accepted(string type, string expected)
    {
        Assert.Contains(expected, Ddl(DatabaseType.PostgreSQL, Schema(_ => { }, c => c.Type = type)));
    }

    [Theory]
    [InlineData("public.citext; DROP TABLE x")]
    [InlineData("public.citext\n\\! id")]
    [InlineData("public..citext")]
    [InlineData("public.citext--")]
    public void Qualified_type_payloads_are_still_rejected(string type)
    {
        Assert.Throws<ArgumentException>(() => Ddl(DatabaseType.PostgreSQL, Schema(_ => { }, c => c.Type = type)));
    }

    [Fact]
    public void Check_expression_clean_value_is_unchanged()
    {
        var ddl = Ddl(DatabaseType.PostgreSQL,
            Schema(t => t.Checks.Add(new SchemaCheck { Id = "ck", Name = "positive", Expression = " total >= 0 " })));

        Assert.Contains("CONSTRAINT \"positive\" CHECK (total >= 0)", ddl);
    }

    // ── Index WHERE / yöntem ─────────────────────────────────────────────────

    private static SchemaIndex Index(Action<SchemaIndex> configure)
    {
        var index = new SchemaIndex
        {
            Id = "i1",
            Name = "ix_total",
            Columns = { new SchemaIndexColumn { ColumnId = "c2" } },
        };
        configure(index);
        return index;
    }

    [Theory]
    [InlineData("total > 0\n\\! id")]
    [InlineData("total > 0 --")]
    [InlineData("total > 0; DROP TABLE orders")]
    public void Index_where_with_payload_is_rejected_on_every_engine(string payload)
    {
        // Desteklemeyen motorda koşul yorum bloğuna girer; orada da doğrulanmalı
        // ("*/" ile yorumdan çıkıp SQL yazılabilirdi).
        foreach (var engine in AllEngines)
            Assert.Throws<ArgumentException>(() =>
                Ddl(engine, Schema(t => t.Indexes.Add(Index(i => i.Where = payload)))));
    }

    [Fact]
    public void Index_where_comment_cannot_be_closed_early()
    {
        Assert.Throws<ArgumentException>(() =>
            Ddl(DatabaseType.MySQL, Schema(t => t.Indexes.Add(Index(i => i.Where = "x */ DROP TABLE orders /*")))));
    }

    [Theory]
    [InlineData("btree; DROP TABLE orders")]
    [InlineData("btree\n\\! id")]
    [InlineData("nonsense")]
    public void Index_method_outside_allow_list_is_rejected(string method)
    {
        Assert.Throws<ArgumentException>(() =>
            Ddl(DatabaseType.PostgreSQL, Schema(t => t.Indexes.Add(Index(i => i.Method = method)))));
        Assert.Throws<ArgumentException>(() =>
            Ddl(DatabaseType.MySQL, Schema(t => t.Indexes.Add(Index(i => i.Method = method)))));
    }

    [Fact]
    public void Index_method_in_allow_list_is_written_in_each_engines_case()
    {
        Assert.Contains("USING gin", Ddl(DatabaseType.PostgreSQL, Schema(t => t.Indexes.Add(Index(i => i.Method = "GIN")))));
        Assert.Contains("USING HASH", Ddl(DatabaseType.MySQL, Schema(t => t.Indexes.Add(Index(i => i.Method = "hash")))));
    }

    [Fact]
    public void Unsupported_index_method_note_cannot_break_out_of_its_comment_line()
    {
        // Yöntemi yok sayan motorda ad yalnızca `--` notuna girer; satır sonu temizlenir.
        var ddl = Ddl(DatabaseType.SQLite, Schema(t => t.Indexes.Add(Index(i => i.Method = "x\nDROP TABLE orders;"))));

        Assert.DoesNotMatch(new Regex(@"^DROP TABLE", RegexOptions.Multiline), ddl);
    }

    [Fact]
    public void Include_columns_comment_cannot_be_closed_early()
    {
        var schema = Schema(t =>
        {
            t.Columns.Add(new SchemaColumn { Id = "c3", Name = "x */ DROP TABLE orders; /*", Type = "INT" });
            t.Indexes.Add(Index(i => i.IncludeColumnIds.Add("c3")));
        });

        var ddl = Ddl(DatabaseType.MySQL, schema);

        Assert.Contains("/* INCLUDE (x * / DROP TABLE orders; /*) — not supported by MySQL */", ddl);
    }

    // ── RLS ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("tenant_id = 1\n\\! id")]
    [InlineData("tenant_id = 1 -- x")]
    [InlineData("tenant_id = 1 /* x */")]
    public void Rls_expressions_use_the_shared_validator(string payload)
    {
        foreach (var (u, w) in new (string?, string?)[] { (payload, null), (null, payload) })
        {
            var table = new SchemaTable
            {
                Name = "orders",
                EnableRls = true,
                RlsPolicies = { new SchemaRlsPolicy { Name = "p", Command = "ALL", Using = u, WithCheck = w } },
            };

            Assert.Throws<ArgumentException>(() =>
                Infrastructure_Rls(table));
        }
    }

    private static string Infrastructure_Rls(SchemaTable table) =>
        Namines.Infrastructure.Generators.DdlGenerator.RlsSql.GenerateTableRls(
            table, DatabaseType.PostgreSQL, s => $"\"{s}\"");

    // ── DEFAULT ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("0) --")]
    [InlineData("1\n\\! id")]
    [InlineData("'x'; DROP TABLE orders")]
    [InlineData("'unterminated")]
    [InlineData("now() /* c */")]
    public void Unrecognised_default_with_payload_is_rejected(string payload)
    {
        foreach (var engine in AllEngines)
            Assert.Throws<ArgumentException>(() =>
                Ddl(engine, Schema(_ => { }, c => c.DefaultValue = payload)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1.5")]
    [InlineData("'TR'")]
    [InlineData("'it''s'")]
    [InlineData("NULL")]
    [InlineData("true")]
    [InlineData("CURRENT_TIMESTAMP")]
    [InlineData("now()")]
    public void Recognised_default_passes_through_unchanged(string value)
    {
        Assert.Contains($"DEFAULT {value}", Ddl(DatabaseType.PostgreSQL, Schema(_ => { }, c => c.DefaultValue = value)));
    }

    [Fact]
    public void MySql_string_default_with_backslash_is_rejected_but_postgres_keeps_it()
    {
        // MySQL ailesinde '\' kapanış tırnağını kaçırır; standart dize kullanan motorlarda sıradan bir karakter.
        Assert.Throws<ArgumentException>(() =>
            Ddl(DatabaseType.MySQL, Schema(_ => { }, c => c.DefaultValue = "'x\\'")));
        Assert.Contains(@"DEFAULT 'C:\temp'", Ddl(DatabaseType.PostgreSQL, Schema(_ => { }, c => c.DefaultValue = @"'C:\temp'")));
    }

    // ── Tip adı ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("INT); DROP TABLE orders; --")]
    [InlineData("INT\n\\! id")]
    [InlineData("foo(bar)")]
    [InlineData("a b; c")]
    public void Unmapped_type_with_payload_is_rejected(string type)
    {
        foreach (var engine in new[] { DatabaseType.MSSQL, DatabaseType.PostgreSQL, DatabaseType.MySQL, DatabaseType.MariaDB })
            Assert.Throws<ArgumentException>(() =>
                Ddl(engine, Schema(_ => { }, c => c.Type = type)));
    }

    [Theory]
    [InlineData("GEOGRAPHY")]
    [InlineData("DOUBLE PRECISION")]
    [InlineData("VARCHAR(MAX)")]
    [InlineData("NUMERIC(10,2)")]
    [InlineData("INT[]")]
    public void Plain_type_names_still_pass(string type)
    {
        var ddl = Ddl(DatabaseType.PostgreSQL, Schema(_ => { }, c => c.Type = type));

        Assert.Contains("total", ddl);
    }

    // ── Enum sabiti ──────────────────────────────────────────────────────────

    private static DatabaseSchema EnumSchema(string value) => new()
    {
        Name = "s",
        Enums = { new SchemaEnum { Id = "e1", Name = "status", Values = { "a", value } } },
        Tables =
        {
            new SchemaTable
            {
                Id = "t1", Name = "orders",
                Columns =
                {
                    new SchemaColumn { Id = "c1", Name = "id", Type = "INT", IsPK = true },
                    new SchemaColumn { Id = "c2", Name = "state", Type = "TEXT", EnumRef = "status" },
                },
            },
        },
    };

    [Theory]
    [InlineData(DatabaseType.MySQL)]
    [InlineData(DatabaseType.MariaDB)]
    public void Enum_value_backslash_is_doubled_for_mysql_family(DatabaseType engine)
    {
        // 'x\' ikilenmezse kapanış tırnağını yutar ve sonraki metin SQL olur.
        var ddl = Ddl(engine, EnumSchema("x\\"));

        Assert.Contains(@"ENUM('a', 'x\\')", ddl);
    }

    [Theory]
    [InlineData(DatabaseType.PostgreSQL)]
    [InlineData(DatabaseType.MSSQL)]
    [InlineData(DatabaseType.SQLite)]
    [InlineData(DatabaseType.Oracle)]
    public void Enum_value_backslash_is_literal_for_standard_string_engines(DatabaseType engine)
    {
        var ddl = Ddl(engine, EnumSchema("x\\"));

        Assert.Contains(@"'x\'", ddl);
        Assert.DoesNotContain(@"'x\\'", ddl);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Enum_value_with_line_break_is_rejected(DatabaseType engine)
    {
        Assert.Throws<ArgumentException>(() => Ddl(engine, EnumSchema("x'\n\\! id\n--")));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Enum_value_with_apostrophe_is_still_doubled(DatabaseType engine)
    {
        Assert.Contains("'can''t_ship'", Ddl(engine, EnumSchema("can't_ship")));
    }

    // ── Yorum satırları (trigger/prosedür adları) ────────────────────────────

    [Fact]
    public void Trigger_and_procedure_comment_text_cannot_end_the_comment()
    {
        var schema = new DatabaseSchema
        {
            Name = "s",
            Triggers =
            {
                new SchemaTrigger
                {
                    Id = "trg\n\\! id", Timing = "After\r\nDROP", Event = "Insert\n;",
                    TargetEngine = DatabaseType.PostgreSQL, Body = "BEGIN NULL; END;",
                },
            },
            StoredProcedures =
            {
                new SchemaStoredProcedure
                {
                    Id = "p1", Name = "proc\n\\! id", TargetEngine = DatabaseType.PostgreSQL, Body = "SELECT 1;",
                },
            },
        };

        var sb = new System.Text.StringBuilder();
        TriggerProcedureSql.Append(sb, schema, DatabaseType.PostgreSQL);
        var lines = sb.ToString().Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        // Her '--' satırı tek satır olmalı; sızan kuyruk '\!' ile başlayan ayrı satır olurdu.
        Assert.DoesNotContain(lines, l => l.StartsWith(@"\!"));
        Assert.DoesNotContain(lines, l => l.StartsWith("DROP"));
        Assert.Contains(lines, l => l.StartsWith("-- Trigger: trg"));
        Assert.Contains(lines, l => l.StartsWith("-- Stored Procedure: proc"));
    }

    [Fact]
    public void Trigger_and_procedure_bodies_stay_raw_by_design()
    {
        var sb = new System.Text.StringBuilder();
        TriggerProcedureSql.Append(sb, new DatabaseSchema
        {
            Triggers = { new SchemaTrigger { Id = "t", TargetEngine = DatabaseType.PostgreSQL, Body = "BEGIN\n  RETURN NEW; -- ok\nEND;" } },
        }, DatabaseType.PostgreSQL);

        Assert.Contains("BEGIN\n  RETURN NEW; -- ok\nEND;", sb.ToString().Replace("\r\n", "\n"));
    }
}
