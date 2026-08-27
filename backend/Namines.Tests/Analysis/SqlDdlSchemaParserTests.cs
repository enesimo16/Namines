using System.Linq;
using Namines.Core.Analysis;

namespace Namines.Tests.Analysis;

/// <summary>
/// second-phase/11-KODDAN-SEMA.md (ham CREATE TABLE) ve
/// second-phase/12-ENTEGRASYONLAR.md adım 2 (Supabase migration klasörü).
/// </summary>
public class SqlDdlSchemaParserTests
{
    private const string SupabaseMigration = """
        -- 20240101000000_init.sql
        CREATE TABLE public.profiles (
          id uuid PRIMARY KEY,
          username varchar(64) NOT NULL,
          bio text,
          created_at timestamptz NOT NULL DEFAULT now()
        );

        CREATE TABLE public.posts (
          id bigserial PRIMARY KEY,
          author_id uuid NOT NULL REFERENCES public.profiles(id),
          title varchar(200) NOT NULL,
          score numeric(10,2)
        );
        """;

    [Fact]
    public void Create_table_statements_become_tables_with_columns()
    {
        var result = SqlDdlSchemaParser.Parse(SupabaseMigration);

        Assert.Equal("sql", result.Format);
        Assert.Equal(2, result.Schema.Tables.Count);
        Assert.Equal(4, result.Schema.Tables.Single(t => t.Name == "profiles").Columns.Count);
    }

    [Fact]
    public void The_schema_prefix_is_stripped_from_the_table_name()
    {
        var result = SqlDdlSchemaParser.Parse(SupabaseMigration);

        Assert.Contains(result.Schema.Tables, t => t.Name == "profiles");
        Assert.DoesNotContain(result.Schema.Tables, t => t.Name.Contains("."));
    }

    [Fact]
    public void Supabase_internal_schemas_are_excluded_and_reported()
    {
        // auth.users kullanıcının tablosu DEĞİL — Supabase'in kendi tablosu.
        // Şemaya koymak, kullanıcıya sahip olmadığı bir tabloyu göstermek olurdu.
        var sql = """
            CREATE TABLE auth.users (id uuid PRIMARY KEY);
            CREATE TABLE public.notes (id int PRIMARY KEY);
            """;

        var result = SqlDdlSchemaParser.Parse(sql);

        Assert.Single(result.Schema.Tables);
        Assert.Equal("notes", result.Schema.Tables[0].Name);
        Assert.Contains(result.Skipped, s => s.Reason.Contains("internal schema"));
    }

    [Fact]
    public void Not_null_and_nullable_columns_are_distinguished()
    {
        var result = SqlDdlSchemaParser.Parse(SupabaseMigration);
        var profiles = result.Schema.Tables.Single(t => t.Name == "profiles");

        Assert.False(profiles.Columns.Single(c => c.Name == "username").IsNullable);
        Assert.True(profiles.Columns.Single(c => c.Name == "bio").IsNullable);
    }

    [Fact]
    public void Varchar_length_is_captured_and_numeric_precision_does_not_break_the_split()
    {
        // numeric(10,2) içindeki virgül bir KOLON ayırıcısı değil — parantez
        // derinliği sayılmazsa tablo yanlış bölünür.
        var result = SqlDdlSchemaParser.Parse(SupabaseMigration);
        var posts = result.Schema.Tables.Single(t => t.Name == "posts");

        Assert.Equal(200, posts.Columns.Single(c => c.Name == "title").Length);
        Assert.Equal(4, posts.Columns.Count);
        Assert.Contains(posts.Columns, c => c.Name == "score");
    }

    [Fact]
    public void Serial_types_are_normalised_and_marked_as_database_generated()
    {
        var result = SqlDdlSchemaParser.Parse(SupabaseMigration);

        var id = result.Schema.Tables.Single(t => t.Name == "posts").Columns.Single(c => c.Name == "id");
        Assert.Equal("BIGINT", id.Type);
        Assert.True(id.Identity);
    }

    [Fact]
    public void Postgres_specific_types_map_onto_the_canonical_names()
    {
        var result = SqlDdlSchemaParser.Parse(SupabaseMigration);

        var createdAt = result.Schema.Tables.Single(t => t.Name == "profiles").Columns.Single(c => c.Name == "created_at");
        Assert.Equal("TIMESTAMP", createdAt.Type);
    }

    [Fact]
    public void Inline_references_clause_produces_a_foreign_key()
    {
        var result = SqlDdlSchemaParser.Parse(SupabaseMigration);

        var relation = Assert.Single(result.Schema.Relations);
        Assert.Equal("posts", relation.SourceTableId);
        Assert.Equal("profiles", relation.TargetTableId);
        Assert.True(result.Schema.Tables.Single(t => t.Name == "posts").Columns.Single(c => c.Name == "author_id").IsFK);
    }

    [Fact]
    public void Alter_table_add_foreign_key_is_also_picked_up()
    {
        var sql = """
            CREATE TABLE orders (id int PRIMARY KEY, customer_id int NOT NULL);
            CREATE TABLE customers (id int PRIMARY KEY);
            ALTER TABLE orders ADD CONSTRAINT fk_orders_customer FOREIGN KEY (customer_id) REFERENCES customers(id);
            """;

        var result = SqlDdlSchemaParser.Parse(sql);

        var relation = Assert.Single(result.Schema.Relations);
        Assert.Equal("orders", relation.SourceTableId);
        Assert.Equal("customers", relation.TargetTableId);
    }

    [Fact]
    public void Table_level_primary_key_marks_every_named_column()
    {
        var sql = """
            CREATE TABLE memberships (
              user_id int NOT NULL,
              org_id int NOT NULL,
              PRIMARY KEY (user_id, org_id)
            );
            """;

        var result = SqlDdlSchemaParser.Parse(sql);

        Assert.Equal(2, result.Schema.Tables.Single().Columns.Count(c => c.IsPK));
    }

    [Fact]
    public void Default_value_is_captured_without_swallowing_the_not_null()
    {
        var result = SqlDdlSchemaParser.Parse(SupabaseMigration);

        var createdAt = result.Schema.Tables.Single(t => t.Name == "profiles").Columns.Single(c => c.Name == "created_at");
        Assert.Equal("now()", createdAt.DefaultValue);
        Assert.False(createdAt.IsNullable);
    }

    [Fact]
    public void Comments_are_stripped_and_never_parsed_as_columns()
    {
        var result = SqlDdlSchemaParser.Parse(SupabaseMigration);

        Assert.DoesNotContain(result.Skipped, s => s.Name.Contains("20240101"));
    }

    [Fact]
    public void An_unimported_table_level_constraint_is_reported_not_hidden()
    {
        var sql = """
            CREATE TABLE items (
              id int PRIMARY KEY,
              qty int NOT NULL,
              CHECK (qty > 0)
            );
            """;

        var result = SqlDdlSchemaParser.Parse(sql);

        Assert.Contains(result.Skipped, s => s.Reason.Contains("CHECK"));
        // Ama tablo yine de üretiliyor — hepsi ya da hiçbiri değil.
        Assert.Single(result.Schema.Tables);
    }

    [Fact]
    public void A_file_with_no_create_table_says_so_instead_of_returning_an_empty_schema_silently()
    {
        var result = SqlDdlSchemaParser.Parse("CREATE POLICY p ON t FOR SELECT USING (true);");

        Assert.Empty(result.Schema.Tables);
        Assert.Contains(result.Skipped, s => s.Reason.Contains("no CREATE TABLE"));
    }

    [Fact]
    public void Quoted_identifiers_are_unquoted()
    {
        var sql = """CREATE TABLE "Order Items" ("Item Id" int PRIMARY KEY);""";

        var result = SqlDdlSchemaParser.Parse(sql);

        Assert.Equal("Order Items", result.Schema.Tables.Single().Name);
    }
}
