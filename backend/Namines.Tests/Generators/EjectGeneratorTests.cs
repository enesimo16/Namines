using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Infrastructure.Generators.Eject;

namespace Namines.Tests.Generators;

/// <summary>
/// Eject hedefleri (new-phase/12-CODEGEN-EJECT.md).
///
/// 15 hedef var ve hepsini ayrı ayrı derletmek mümkün değil (Go, Python, Java
/// derleyicileri gerekirdi). Bu yüzden testler iki katmanda:
///
/// 1. <b>Her hedef için ortak sözleşme</b> — çıktı boş değil, dosya adı var,
///    tablo adı geçiyor. Bir üretici sessizce boş dosya döndürürse bu yakalar.
/// 2. <b>Hedefe özgü tuzaklar</b> — her dilin kendine has, sessizce yanlış
///    çalışan noktası. Asıl değer burada: "derlenmiyor" görünür bir hatadır,
///    "yanlış kolona bağlanıyor" değildir.
/// </summary>
public class EjectGeneratorTests
{
    private static readonly EjectGeneratorRegistry Registry = new(new DdlGeneratorFactory());

    private static DatabaseSchema Schema() => new()
    {
        Name = "shop",
        Tables =
        {
            new SchemaTable
            {
                Id = "t1", Name = "users",
                Columns =
                {
                    new SchemaColumn { Id = "c1", Name = "id", Type = "INT", IsPK = true },
                    new SchemaColumn { Id = "c2", Name = "email", Type = "VARCHAR", Length = 255 },
                    new SchemaColumn { Id = "c3", Name = "created_at", Type = "DATETIME" },
                    new SchemaColumn { Id = "c4", Name = "note", Type = "TEXT", IsNullable = true },
                },
                Checks = { new SchemaCheck { Id = "k1", Expression = "email <> ''" } },
            },
            new SchemaTable
            {
                Id = "t2", Name = "orders",
                Columns =
                {
                    new SchemaColumn { Id = "c5", Name = "id", Type = "INT", IsPK = true },
                    new SchemaColumn { Id = "c6", Name = "user_id", Type = "INT" },
                    new SchemaColumn { Id = "c7", Name = "total", Type = "DECIMAL" },
                },
            },
        },
        Relations =
        {
            new SchemaRelation
            {
                Id = "r1", SourceTableId = "t2", SourceColumnId = "c6",
                TargetTableId = "t1", TargetColumnId = "c1",
                OnDelete = ReferentialAction.Cascade,
            },
        },
    };

    public static TheoryData<string> AllTargets()
    {
        var data = new TheoryData<string>();
        foreach (var generator in new EjectGeneratorRegistry(new DdlGeneratorFactory()).All)
            data.Add(generator.Target);
        return data;
    }

    // ── Ortak sözleşme ───────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllTargets))]
    public void Every_target_produces_non_empty_files(string target)
    {
        var result = Registry.Get(target).Generate(Schema(), DatabaseType.PostgreSQL);

        Assert.NotEmpty(result.Files);
        foreach (var (name, content) in result.Files)
        {
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.False(string.IsNullOrWhiteSpace(content), $"{target} produced an empty {name}");
        }
    }

    [Theory]
    [MemberData(nameof(AllTargets))]
    public void Every_target_mentions_the_tables(string target)
    {
        var result = Registry.Get(target).Generate(Schema(), DatabaseType.PostgreSQL);
        var all = string.Join("\n", result.Files.Values);

        Assert.Contains("users", all, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("orders", all, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(AllTargets))]
    public void Every_target_survives_an_empty_schema(string target)
    {
        // Boş şema geçerli bir durum (kullanıcı henüz tablo eklememiş) ve
        // üreticinin patlaması değil, boş ama geçerli çıktı vermesi gerekir.
        var result = Registry.Get(target).Generate(new DatabaseSchema { Name = "empty" }, DatabaseType.PostgreSQL);
        Assert.NotNull(result);
    }

    [Fact]
    public void An_unknown_target_lists_the_valid_ones()
    {
        var ex = Assert.Throws<NotSupportedException>(() => Registry.Get("orm.nonexistent"));

        Assert.Contains("orm.drizzle", ex.Message);
        Assert.Contains("types.typescript", ex.Message);
    }

    [Fact]
    public void Targets_that_cannot_express_checks_report_it()
    {
        // CHECK kısıtı hiçbir ORM/tip hedefinde yok. Sessizce düşürmek, üretilen
        // dosyayı veritabanından daha gevşek yapar.
        foreach (var target in new[] { "types.typescript", "orm.drizzle", "orm.django" })
        {
            var result = Registry.Get(target).Generate(Schema(), DatabaseType.PostgreSQL);
            Assert.Contains(result.Warnings, w => w.Contains("CHECK"));
        }
    }

    // ── Hedefe özgü tuzaklar ─────────────────────────────────────────────────

    [Fact]
    public void Typescript_maps_bigint_to_string_not_number()
    {
        // number 2^53 üstünü sessizce yuvarlar; BIGINT'i number yapmak veriyi bozar.
        var schema = Schema();
        schema.Tables[0].Columns.Add(new SchemaColumn { Id = "big", Name = "counter", Type = "BIGINT" });

        var ts = Registry.Get("types.typescript").Generate(schema, DatabaseType.PostgreSQL).Files["types.ts"];

        Assert.Contains("counter: string", ts);
    }

    [Fact]
    public void Typescript_quotes_identifiers_that_are_not_valid()
    {
        var schema = Schema();
        schema.Tables[0].Columns.Add(new SchemaColumn { Id = "odd", Name = "order-id", Type = "INT" });

        var ts = Registry.Get("types.typescript").Generate(schema, DatabaseType.PostgreSQL).Files["types.ts"];

        Assert.Contains("\"order-id\":", ts);
    }

    [Fact]
    public void Zod_carries_the_column_length_that_typescript_cannot()
    {
        // Zod'un TypeScript tiplerine göre kazandırdığı şey tam olarak bu.
        var zod = Registry.Get("types.zod").Generate(Schema(), DatabaseType.PostgreSQL).Files["schemas.ts"];

        Assert.Contains("max(255)", zod);
        // Nullable "alan yok" değil "değer null" demek.
        Assert.Contains("nullable()", zod);
    }

    [Fact]
    public void Csharp_maps_json_property_names()
    {
        // Kolon adı PascalCase'e çevrildiği için eşleme olmadan "created_at"
        // sessizce null gelirdi.
        var files = Registry.Get("types.csharp").Generate(Schema(), DatabaseType.PostgreSQL).Files;

        Assert.Contains("JsonPropertyName(\"created_at\")", files["Users.cs"]);
    }

    [Fact]
    public void Drizzle_refuses_engines_it_does_not_support()
    {
        // Sessizce pg-core yazmak derlenebilir ama TAMAMEN yanlış bir dosya üretirdi.
        var ex = Assert.Throws<NotSupportedException>(() =>
            Registry.Get("orm.drizzle").Generate(Schema(), DatabaseType.Oracle));

        Assert.Contains("Oracle", ex.Message);
    }

    [Theory]
    [InlineData(DatabaseType.PostgreSQL, "pg-core")]
    [InlineData(DatabaseType.MySQL, "mysql-core")]
    [InlineData(DatabaseType.SQLite, "sqlite-core")]
    public void Drizzle_imports_the_dialect_module(DatabaseType engine, string module)
    {
        var ts = Registry.Get("orm.drizzle").Generate(Schema(), engine).Files["schema.ts"];
        Assert.Contains($"drizzle-orm/{module}", ts);
    }

    [Fact]
    public void Django_keeps_the_real_table_name()
    {
        // db_table olmadan Django tabloyu "app_users" gibi bir adla arar.
        var py = Registry.Get("orm.django").Generate(Schema(), DatabaseType.PostgreSQL).Files["models.py"];

        Assert.Contains("db_table = \"users\"", py);
        Assert.Contains("on_delete=models.CASCADE", py);
    }

    [Fact]
    public void Django_gives_decimal_fields_the_arguments_it_requires()
    {
        // max_digits/decimal_places olmadan Django model doğrulaması hata verir.
        var py = Registry.Get("orm.django").Generate(Schema(), DatabaseType.PostgreSQL).Files["models.py"];

        Assert.Contains("max_digits=", py);
        Assert.Contains("decimal_places=", py);
    }

    [Fact]
    public void Sqlalchemy_uses_the_2_0_mapped_style()
    {
        var py = Registry.Get("orm.sqlalchemy").Generate(Schema(), DatabaseType.PostgreSQL).Files["models.py"];

        Assert.Contains("Mapped[", py);
        Assert.Contains("mapped_column(", py);
        Assert.Contains("ForeignKey(\"users.id\")", py);
    }

    [Fact]
    public void Pydantic_aliases_snake_cased_names_back_to_the_column()
    {
        var py = Registry.Get("types.python").Generate(Schema(), DatabaseType.PostgreSQL).Files["models.py"];
        Assert.Contains("class Users(BaseModel)", py);
    }

    [Fact]
    public void Gorm_exports_every_field_and_pins_the_table_name()
    {
        // Küçük harfle başlayan bir Go alanı dışa açık olmaz ve sessizce hiç
        // doldurulmaz; GORM de tablo adını çoğullar.
        var go = Registry.Get("orm.gorm").Generate(Schema(), DatabaseType.PostgreSQL).Files["models.go"];

        Assert.Contains("Id int", go);
        Assert.Contains("column:created_at", go);
        Assert.Contains("return \"users\"", go);
    }

    [Fact]
    public void Gorm_uses_pointers_for_nullable_columns()
    {
        // Pointer olmadan "NULL" ile "boş metin" ayırt edilemez.
        var go = Registry.Get("orm.gorm").Generate(Schema(), DatabaseType.PostgreSQL).Files["models.go"];
        Assert.Contains("*string", go);
    }

    [Fact]
    public void Sequelize_disables_the_defaults_that_would_break_the_schema()
    {
        // Sequelize varsayılan olarak tablo adını çoğullar ve createdAt/updatedAt
        // kolonları olduğunu varsayar; ikisi de bizim şemamızda doğru değil.
        var js = Registry.Get("orm.sequelize").Generate(Schema(), DatabaseType.PostgreSQL).Files["models.js"];

        Assert.Contains("freezeTableName: true", js);
        Assert.Contains("timestamps: false", js);
    }

    [Fact]
    public void Typeorm_names_every_table_and_column_explicitly()
    {
        var files = Registry.Get("orm.typeorm").Generate(Schema(), DatabaseType.PostgreSQL).Files;

        Assert.Contains("@Entity({ name: \"users\" })", files["Users.ts"]);
        Assert.Contains("name: \"created_at\"", files["Users.ts"]);
    }

    [Fact]
    public void Graphql_declares_custom_scalars_it_uses()
    {
        // DateTime'ı bildirmeden kullanmak SDL'i geçersiz kılar.
        var sdl = Registry.Get("contract.graphql").Generate(Schema(), DatabaseType.PostgreSQL).Files["schema.graphql"];

        Assert.Contains("scalar DateTime", sdl);
        Assert.Contains("type Query", sdl);
    }

    [Fact]
    public void Graphql_marks_nullable_fields_without_the_bang()
    {
        var sdl = Registry.Get("contract.graphql").Generate(Schema(), DatabaseType.PostgreSQL).Files["schema.graphql"];

        Assert.Contains("note: String\n", sdl.Replace("\r\n", "\n"));
        Assert.Contains("email: String!", sdl);
    }

    [Fact]
    public void Json_schema_separates_integers_from_decimals()
    {
        var json = Registry.Get("contract.jsonschema").Generate(Schema(), DatabaseType.PostgreSQL).Files["schema.json"];

        Assert.Contains("\"integer\"", json);
        Assert.Contains("\"number\"", json);
    }

    [Fact]
    public void Protobuf_warns_about_field_number_stability()
    {
        // Alan numarası protobuf'ta sözleşmenin kendisi; kolon sırasından
        // türetilmesi gerçek bir risk ve söylenmeli.
        var result = Registry.Get("contract.protobuf").Generate(Schema(), DatabaseType.PostgreSQL);

        Assert.Contains(result.Warnings, w => w.Contains("Field numbers"));
        Assert.Contains("syntax = \"proto3\"", result.Files["schema.proto"]);
    }

    [Fact]
    public void Flyway_uses_the_version_naming_convention()
    {
        var files = Registry.Get("mig.flyway").Generate(Schema(), DatabaseType.PostgreSQL).Files;

        Assert.Single(files);
        Assert.StartsWith("V1__", files.Keys.Single());
        Assert.EndsWith(".sql", files.Keys.Single());
    }

    [Fact]
    public void Liquibase_escapes_a_cdata_terminator_in_the_ddl()
    {
        // "]]>" dizisi CDATA bölümünü erkenden kapatır ve XML'i bozar.
        var schema = Schema();
        schema.Tables[0].Checks.Add(new SchemaCheck { Id = "k2", Expression = "note <> ']]>'" });

        var xml = Registry.Get("mig.liquibase").Generate(schema, DatabaseType.PostgreSQL).Files["changelog.xml"];

        // Sözleşme "]]> hiç geçmesin" DEĞİL: standart kaçış (]]]]><![CDATA[>) zaten
        // o diziyi içerir — CDATA'yı kapatıp yeniden açarak çalışır. Asıl sözleşme
        // XML'in GEÇERLİ kalması ve değerin bozulmadan çıkması; ayrıştırarak sınıyoruz.
        var document = System.Xml.Linq.XDocument.Parse(xml);
        var sql = document.Descendants()
            .First(e => e.Name.LocalName == "sql")
            .Value;

        Assert.Contains("]]>", sql);
    }

    // ── Console eject (07 §8) ────────────────────────────────────────────────

    [Fact]
    public void Console_produces_a_runnable_next_app()
    {
        var files = Registry.Get("console.nextjs").Generate(Schema(), DatabaseType.PostgreSQL).Files;

        // Bu dosyalar olmadan `npm install && npm run dev` çalışmaz.
        Assert.Contains("package.json", files.Keys);
        Assert.Contains("tsconfig.json", files.Keys);
        Assert.Contains("app/layout.tsx", files.Keys);
        Assert.Contains("app/page.tsx", files.Keys);
        Assert.Contains("app/[table]/page.tsx", files.Keys);
    }

    [Fact]
    public void Console_keeps_the_api_key_on_the_server()
    {
        // NEXT_PUBLIC_ öneki anahtarı tarayıcıya indirir ve her ziyaretçiye
        // anahtarın eriştiği tabloları açar.
        var files = Registry.Get("console.nextjs").Generate(Schema(), DatabaseType.PostgreSQL).Files;

        Assert.DoesNotContain("NEXT_PUBLIC_NAMINES_API_KEY", files[".env.example"]);
        Assert.Contains("server-only", files["lib/gateway.ts"]);
    }

    [Fact]
    public void Console_talks_to_the_gateway_not_the_database()
    {
        // Doğrudan bağlanmak, tablo izinlerini ve PII maskelemesini atlardı.
        var gateway = Registry.Get("console.nextjs").Generate(Schema(), DatabaseType.PostgreSQL)
            .Files["lib/gateway.ts"];

        Assert.Contains("/api/gateway/", gateway);
        Assert.Contains("X-Namines-Key", gateway);
    }

    [Fact]
    public void Console_detects_a_junction_table()
    {
        // Bileşik anahtarın tamamı yabancı anahtarsa tablo kendi sayfasını
        // hak etmiyor; 07 §3.2'nin otomatik desen seçimi.
        var schema = Schema();
        schema.Tables.Add(new SchemaTable
        {
            Id = "t3", Name = "user_roles",
            Columns =
            {
                new SchemaColumn { Id = "c8", Name = "user_id", Type = "INT", IsPK = true },
                new SchemaColumn { Id = "c9", Name = "role_id", Type = "INT", IsPK = true },
            },
        });
        schema.Tables.Add(new SchemaTable
        {
            Id = "t4", Name = "roles",
            Columns = { new SchemaColumn { Id = "c10", Name = "id", Type = "INT", IsPK = true } },
        });
        schema.Relations.Add(new SchemaRelation
        {
            Id = "r2", SourceTableId = "t3", SourceColumnId = "c8", TargetTableId = "t1", TargetColumnId = "c1",
        });
        schema.Relations.Add(new SchemaRelation
        {
            Id = "r3", SourceTableId = "t3", SourceColumnId = "c9", TargetTableId = "t4", TargetColumnId = "c10",
        });

        var result = Registry.Get("console.nextjs").Generate(schema, DatabaseType.PostgreSQL);

        Assert.Contains("\"junction\"", result.Files["lib/schema.ts"]);
        Assert.Contains(result.Warnings, w => w.Contains("junction"));
    }

    [Fact]
    public void Console_marks_a_keyless_table_read_only()
    {
        // Anahtarsız satırı güvenle hedefleyemeyiz; Gateway zaten anahtarsız
        // yazmayı reddediyor, panel de düzenleme sunmamalı.
        var schema = Schema();
        schema.Tables.Add(new SchemaTable
        {
            Id = "t5", Name = "audit_log",
            Columns = { new SchemaColumn { Id = "c11", Name = "message", Type = "TEXT" } },
        });

        var result = Registry.Get("console.nextjs").Generate(schema, DatabaseType.PostgreSQL);

        Assert.Contains("\"readonly\"", result.Files["lib/schema.ts"]);
        Assert.Contains(result.Warnings, w => w.Contains("no primary key"));
    }

    [Fact]
    public void Console_picks_a_human_readable_label_column()
    {
        // Yabancı anahtar gösterirken ham id yerine bu kolon gösterilecek.
        var metadata = Registry.Get("console.nextjs").Generate(Schema(), DatabaseType.PostgreSQL)
            .Files["lib/schema.ts"];

        Assert.Contains("\"labelColumn\": \"email\"", metadata);
    }
}
