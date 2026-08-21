using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.PrismaGenerator;

namespace Namines.Tests.Generators;

/// <summary>
/// Prisma eject (new-phase/12-CODEGEN-EJECT.md).
///
/// Testlerin ikiye ayrılması bilinçli: metin iddiaları hızlı geri bildirim verir,
/// <see cref="RequiresPrismaFactAttribute"/> testleri ise çıktıyı GERÇEK Prisma
/// ayrıştırıcısına doğrulatır. G5'in dersi: makul görünen çıktı, kabul edilen çıktı
/// değildir.
/// </summary>
public class PrismaGeneratorTests
{
    private static readonly PrismaGeneratorService Generator = new();

    private static SchemaColumn Column(
        string id, string name, string type, bool pk = false, bool nullable = false,
        int? length = null, string? defaultValue = null) =>
        new()
        {
            Id = id, Name = name, Type = type, IsPK = pk, IsNullable = nullable,
            Length = length, DefaultValue = defaultValue,
        };

    private static DatabaseSchema BlogSchema()
    {
        var users = new SchemaTable
        {
            Id = "t-users", Name = "users",
            Columns =
            {
                Column("u-id", "id", "INT", pk: true),
                Column("u-email", "email", "VARCHAR", length: 255),
                Column("u-created", "created_at", "DATETIME", defaultValue: "CURRENT_TIMESTAMP"),
            },
            Uniques = { new SchemaUnique { Id = "uq-email", Name = "UQ_users_email", ColumnIds = { "u-email" } } },
        };

        var posts = new SchemaTable
        {
            Id = "t-posts", Name = "posts",
            Columns =
            {
                Column("p-id", "id", "INT", pk: true),
                Column("p-title", "title", "VARCHAR", length: 200),
                Column("p-author", "author_id", "INT"),
                Column("p-published", "is_published", "BIT", defaultValue: "0"),
            },
            Indexes =
            {
                new SchemaIndex
                {
                    Id = "ix-author", Name = "IX_posts_author",
                    Columns = { new SchemaIndexColumn { ColumnId = "p-author" } },
                },
            },
        };

        return new DatabaseSchema
        {
            Name = "blog",
            Tables = { users, posts },
            Relations =
            {
                new SchemaRelation
                {
                    Id = "r-1", SourceTableId = "t-posts", SourceColumnId = "p-author",
                    TargetTableId = "t-users", TargetColumnId = "u-id",
                    OnDelete = ReferentialAction.Cascade,
                },
            },
        };
    }

    // ── Temel yapı ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DatabaseType.PostgreSQL, "postgresql")]
    [InlineData(DatabaseType.MySQL, "mysql")]
    [InlineData(DatabaseType.MSSQL, "sqlserver")]
    [InlineData(DatabaseType.SQLite, "sqlite")]
    [InlineData(DatabaseType.MariaDB, "mysql")]
    public void Provider_matches_the_engine(DatabaseType engine, string expected)
    {
        var schema = Generator.Generate(BlogSchema(), engine).Files["schema.prisma"];
        Assert.Contains($"provider = \"{expected}\"", schema);
    }

    [Fact]
    public void Oracle_is_rejected_rather_than_silently_mapped_to_another_provider()
    {
        // Prisma'nın Oracle provider'ı yok. Sessizce postgresql yazmak ayrıştırılabilir
        // ama TAMAMEN yanlış bir dosya üretirdi ve kullanıcı bunu ancak canlıda görürdü.
        var ex = Assert.Throws<NotSupportedException>(
            () => Generator.Generate(BlogSchema(), DatabaseType.Oracle));

        Assert.Contains("Oracle", ex.Message);
        Assert.Contains("EF Core", ex.Message);
    }

    [Fact]
    public void Table_names_are_mapped_not_renamed_in_the_database()
    {
        // Model adı PascalCase olur ama @@map ile gerçek tablo adı korunur.
        // Eşleme yazılmasaydı `prisma db push` tabloyu yeniden adlandırırdı.
        var schema = Generator.Generate(BlogSchema(), DatabaseType.PostgreSQL).Files["schema.prisma"];

        // Model adı tablo adına SADIK kalır (users → Users), tekilleştirilmez:
        // "address" → "addres", "status" → "statu" gibi düzensiz adlarda tahmin
        // sessizce yanlış sonuç verir. Kozmetik kazanç, sessiz yanlışlığa değmez.
        Assert.Contains("model Users {", schema);
        Assert.Contains("@@map(\"users\")", schema);
        Assert.Contains("createdAt", schema);
        Assert.Contains("@map(\"created_at\")", schema);
    }

    [Fact]
    public void Integer_primary_key_gets_autoincrement_matching_the_ddl_generators()
    {
        // DDL üreticileri tekil tamsayı PK için SERIAL/IDENTITY yazıyor. Prisma tarafı
        // eşleşmezse `prisma db push` sütunu yeniden yazar.
        var schema = Generator.Generate(BlogSchema(), DatabaseType.PostgreSQL).Files["schema.prisma"];
        Assert.Contains("@id @default(autoincrement())", schema);
    }

    [Fact]
    public void Referential_actions_are_always_explicit()
    {
        // Prisma'nın varsayılanı NoAction DEĞİL: zorunlu ilişkide Restrict,
        // opsiyonelde SetNull. Boş bırakmak veritabanındakinden farklı davranış üretir.
        var schema = Generator.Generate(BlogSchema(), DatabaseType.PostgreSQL).Files["schema.prisma"];
        Assert.Contains("onDelete: Cascade", schema);
        Assert.Contains("onUpdate: NoAction", schema);
    }

    [Fact]
    public void Varchar_length_survives_as_a_native_type()
    {
        // Yalnızca `String` yazılsaydı MySQL'de varchar(191)'e düşerdi — sessiz
        // tip değişikliği ve veri kırpma riski.
        var schema = Generator.Generate(BlogSchema(), DatabaseType.MySQL).Files["schema.prisma"];
        Assert.Contains("@db.VarChar(255)", schema);
    }

    [Fact]
    public void Sqlite_gets_no_native_type_attributes()
    {
        // SQLite'ta native niteleyici şemayı GEÇERSİZ kılar.
        var schema = Generator.Generate(BlogSchema(), DatabaseType.SQLite).Files["schema.prisma"];
        Assert.DoesNotContain("@db.", schema);
    }

    // ── Sessiz kayıp koruması ────────────────────────────────────────────────

    [Fact]
    public void Check_constraints_are_reported_because_prisma_cannot_express_them()
    {
        // En kritik uyarı: bu dosyadan `prisma db push` çalıştırılırsa CHECK kısıtı
        // veritabanından DÜŞER. Sessizce atlamak veri bütünlüğünü sessizce kaybetmektir.
        var schema = BlogSchema();
        schema.Tables[1].Checks.Add(new SchemaCheck { Id = "ck-1", Expression = "title <> ''" });

        var result = Generator.Generate(schema, DatabaseType.PostgreSQL);

        Assert.Contains(result.Warnings, w => w.Contains("CHECK") && w.Contains("title"));
        // Uyarı dosyanın BAŞINDA olmalı — sonda olsa görülmeden push edilirdi.
        Assert.Contains("WARNING", result.Files["schema.prisma"][..600]);
    }

    [Fact]
    public void Partial_index_condition_is_reported()
    {
        var schema = BlogSchema();
        schema.Tables[1].Indexes[0].Where = "is_published = true";

        var result = Generator.Generate(schema, DatabaseType.PostgreSQL);
        Assert.Contains(result.Warnings, w => w.Contains("partial index"));
    }

    [Fact]
    public void A_clean_schema_produces_no_warning_banner()
    {
        // Uyarı bandosu her çıktıda görünseydi anlamını yitirirdi.
        var result = Generator.Generate(BlogSchema(), DatabaseType.PostgreSQL);
        Assert.Empty(result.Warnings);
        Assert.DoesNotContain("WARNING", result.Files["schema.prisma"]);
    }

    // ── En kırılgan yer: belirsiz ilişkiler ──────────────────────────────────

    private static DatabaseSchema MessagesSchema()
    {
        // Aynı iki model arasında İKİ ilişki: Prisma adlandırılmış ilişki ZORUNLU kılar.
        var users = new SchemaTable
        {
            Id = "t-users", Name = "users",
            Columns = { Column("u-id", "id", "INT", pk: true) },
        };

        var messages = new SchemaTable
        {
            Id = "t-msg", Name = "messages",
            Columns =
            {
                Column("m-id", "id", "INT", pk: true),
                Column("m-sender", "sender_id", "INT"),
                Column("m-recipient", "recipient_id", "INT"),
            },
        };

        return new DatabaseSchema
        {
            Name = "chat",
            Tables = { users, messages },
            Relations =
            {
                new SchemaRelation
                {
                    Id = "r-s", SourceTableId = "t-msg", SourceColumnId = "m-sender",
                    TargetTableId = "t-users", TargetColumnId = "u-id",
                },
                new SchemaRelation
                {
                    Id = "r-r", SourceTableId = "t-msg", SourceColumnId = "m-recipient",
                    TargetTableId = "t-users", TargetColumnId = "u-id",
                },
            },
        };
    }

    [Fact]
    public void Two_relations_between_the_same_pair_get_distinct_named_relations()
    {
        var schema = Generator.Generate(MessagesSchema(), DatabaseType.PostgreSQL).Files["schema.prisma"];

        // Adlandırılmamış olsaydı Prisma şemayı hiç ayrıştıramazdı.
        Assert.Contains("sender", schema);
        Assert.Contains("recipient", schema);
        Assert.Contains("@relation(\"", schema);
    }

    // ── Gerçek Prisma doğrulaması ────────────────────────────────────────────

    [RequiresPrismaTheory]
    [InlineData(DatabaseType.PostgreSQL)]
    [InlineData(DatabaseType.MySQL)]
    [InlineData(DatabaseType.MSSQL)]
    [InlineData(DatabaseType.SQLite)]
    public void Generated_schema_is_accepted_by_prisma(DatabaseType engine)
    {
        var content = Generator.Generate(BlogSchema(), engine).Files["schema.prisma"];
        var (exitCode, output) = PrismaAvailable.Validate(content);

        // Ham çıktı olduğu gibi aktarılır — özetlemek G5/G12'de bilerek yasaklandı.
        Assert.True(exitCode == 0, $"prisma validate reddetti ({engine}):\n{output}\n\n--- şema ---\n{content}");
    }

    [RequiresPrismaFact]
    public void Ambiguous_relations_are_accepted_by_prisma()
    {
        // Bu senaryo golden-file testinden geçer ama adlandırma yanlışsa gerçek
        // Prisma'da patlar. Testin varlık sebebi tam olarak bu.
        var content = Generator.Generate(MessagesSchema(), DatabaseType.PostgreSQL).Files["schema.prisma"];
        var (exitCode, output) = PrismaAvailable.Validate(content);

        Assert.True(exitCode == 0, $"prisma validate reddetti:\n{output}\n\n--- şema ---\n{content}");
    }

    [RequiresPrismaFact]
    public void Composite_primary_key_is_accepted_by_prisma()
    {
        var schema = new DatabaseSchema
        {
            Name = "join",
            Tables =
            {
                new SchemaTable
                {
                    Id = "t-ur", Name = "user_roles",
                    Columns =
                    {
                        Column("c-u", "user_id", "INT", pk: true),
                        Column("c-r", "role_id", "INT", pk: true),
                    },
                },
            },
        };

        var content = Generator.Generate(schema, DatabaseType.PostgreSQL).Files["schema.prisma"];
        var (exitCode, output) = PrismaAvailable.Validate(content);

        Assert.True(exitCode == 0, $"prisma validate reddetti:\n{output}\n\n--- şema ---\n{content}");
    }
}
