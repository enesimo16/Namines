using System.Linq;
using Namines.Core.Analysis;

namespace Namines.Tests.Analysis;

/// <summary>
/// second-phase/11-KODDAN-SEMA.md kademe 1. Girdiler GERÇEK Prisma sözdizimi —
/// üreticimizin ürettiği "temiz" çıktı değil, insan eliyle yazılmış dosyalarda
/// görülen biçimler (özel adlar, @@map, opsiyonel alanlar, ters ilişkiler).
/// </summary>
public class PrismaSchemaParserTests
{
    private const string BlogSchema = """
        datasource db {
          provider = "postgresql"
          url      = env("DATABASE_URL")
        }

        generator client {
          provider = "prisma-client-js"
        }

        model User {
          id        Int      @id @default(autoincrement())
          email     String   @unique @db.VarChar(320)
          name      String?
          posts     Post[]
          createdAt DateTime @default(now())

          @@map("users")
        }

        model Post {
          id       Int    @id @default(autoincrement())
          title    String @db.VarChar(200)
          body     String?
          authorId Int
          author   User   @relation(fields: [authorId], references: [id])
        }
        """;

    [Fact]
    public void Models_become_tables_and_scalar_fields_become_columns()
    {
        var result = PrismaSchemaParser.Parse(BlogSchema);

        Assert.Equal("prisma", result.Format);
        Assert.Equal(2, result.Schema.Tables.Count);

        var post = result.Schema.Tables.Single(t => t.Id == "Post");
        Assert.Equal(4, post.Columns.Count); // id, title, body, authorId — "author" navigasyon, kolon değil
    }

    [Fact]
    public void Block_map_renames_the_table_but_not_the_model_id()
    {
        var result = PrismaSchemaParser.Parse(BlogSchema);

        var users = result.Schema.Tables.Single(t => t.Id == "User");
        // @@map("users") gerçek tablo adıdır; model adı (User) iç kimlik olarak kalır.
        Assert.Equal("users", users.Name);
    }

    [Fact]
    public void Native_db_attribute_carries_the_length_through()
    {
        var result = PrismaSchemaParser.Parse(BlogSchema);

        var email = result.Schema.Tables.Single(t => t.Id == "User").Columns.Single(c => c.Name == "email");
        Assert.Equal(320, email.Length);
        Assert.Equal("VARCHAR", email.Type);
    }

    [Fact]
    public void Optional_marker_makes_the_column_nullable_and_id_never_is()
    {
        var result = PrismaSchemaParser.Parse(BlogSchema);
        var user = result.Schema.Tables.Single(t => t.Id == "User");

        Assert.True(user.Columns.Single(c => c.Name == "name").IsNullable);
        Assert.False(user.Columns.Single(c => c.Name == "id").IsNullable);
    }

    [Fact]
    public void Autoincrement_default_becomes_identity_not_a_literal_default()
    {
        var result = PrismaSchemaParser.Parse(BlogSchema);

        var id = result.Schema.Tables.Single(t => t.Id == "User").Columns.Single(c => c.Name == "id");
        Assert.True(id.Identity);
        // "autoincrement()" bir SQL varsayılanı DEĞİL — oraya yazmak geçersiz DDL üretirdi.
        Assert.Null(id.DefaultValue);
    }

    [Fact]
    public void Relation_attribute_produces_a_foreign_key_between_the_right_columns()
    {
        var result = PrismaSchemaParser.Parse(BlogSchema);

        var relation = Assert.Single(result.Schema.Relations);
        Assert.Equal("Post", relation.SourceTableId);
        Assert.Equal("Post.authorId", relation.SourceColumnId);
        Assert.Equal("User", relation.TargetTableId);
        Assert.Equal("User.id", relation.TargetColumnId);

        var authorId = result.Schema.Tables.Single(t => t.Id == "Post").Columns.Single(c => c.Name == "authorId");
        Assert.True(authorId.IsFK);
    }

    [Fact]
    public void Back_reference_list_field_is_not_reported_as_skipped()
    {
        // `posts Post[]` bir hata değil — FK'yı karşı taraf taşıyor. Bunu
        // "atlandı" diye bildirmek, dürüst raporu gürültüyle doldururdu.
        var result = PrismaSchemaParser.Parse(BlogSchema);

        Assert.DoesNotContain(result.Skipped, s => s.Name.Contains("posts"));
    }

    [Fact]
    public void Enums_are_extracted_with_their_values_in_order()
    {
        var text = """
            enum Role {
              USER
              ADMIN
              OWNER
            }

            model Account {
              id   Int  @id
              role Role
            }
            """;

        var result = PrismaSchemaParser.Parse(text);

        var role = Assert.Single(result.Schema.Enums);
        Assert.Equal(new[] { "USER", "ADMIN", "OWNER" }, role.Values);
    }

    [Fact]
    public void Composite_foreign_keys_are_reported_as_skipped_instead_of_silently_halved()
    {
        var text = """
            model Parent {
              a Int
              b Int
              @@id([a, b])
            }

            model Child {
              id Int @id
              pa Int
              pb Int
              parent Parent @relation(fields: [pa, pb], references: [a, b])
            }
            """;

        var result = PrismaSchemaParser.Parse(text);

        Assert.Empty(result.Schema.Relations);
        Assert.Contains(result.Skipped, s => s.Reason.Contains("composite foreign key"));
    }

    [Fact]
    public void Composite_block_id_marks_every_named_column_as_primary_key()
    {
        var text = """
            model Membership {
              userId Int
              orgId  Int
              @@id([userId, orgId])
            }
            """;

        var result = PrismaSchemaParser.Parse(text);

        var table = Assert.Single(result.Schema.Tables);
        Assert.Equal(2, table.Columns.Count(c => c.IsPK));
    }

    [Fact]
    public void A_relation_to_an_unparsed_model_is_reported_not_dropped_silently()
    {
        var text = """
            model Order {
              id     Int  Int
              userId Int
              user   User @relation(fields: [userId], references: [id])
            }
            """;

        var result = PrismaSchemaParser.Parse(text);

        Assert.Contains(result.Skipped, s => s.Reason.Contains("model that was not parsed"));
    }

    [Fact]
    public void Comments_and_datasource_blocks_never_become_tables()
    {
        var result = PrismaSchemaParser.Parse(BlogSchema);

        Assert.DoesNotContain(result.Schema.Tables, t => t.Name is "db" or "client");
    }

    [Fact]
    public void Parsed_model_list_matches_the_tables_actually_produced()
    {
        var result = PrismaSchemaParser.Parse(BlogSchema);

        // Dürüst rapor: "okundu" dediğimiz her model gerçekten şemada olmalı.
        Assert.Equal(result.Schema.Tables.Count, result.ParsedModels.Count(m => !m.StartsWith("enum ")));
    }
}
