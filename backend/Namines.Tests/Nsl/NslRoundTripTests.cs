using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Core.Nsl;

namespace Namines.Tests.Nsl;

/// <summary>
/// NSL metin biçimi (new-phase/04-NSL-SCHEMA-IR.md §2).
///
/// <b>Round-trip bir biçim için en güçlü testtir:</b> yaz → oku → karşılaştır.
/// Tek tek alanları kontrol etmek, yazıcının hiç yazmadığı bir alanı gözden
/// kaçırmaya açıktır; round-trip ise kaybolan HER şeyi yakalar. Bir biçimin
/// sessizce veri düşürmesi, en pahalı arıza türü — kullanıcı şemasını dosyaya
/// yazar, geri okur ve bir kısıtın kaybolduğunu ancak veritabanı reddedince görür.
/// </summary>
public class NslRoundTripTests
{
    private static DatabaseSchema Sample()
    {
        var users = new SchemaTable
        {
            Id = "t1", Name = "users", StableUuid = "uuid-users",
            Columns =
            {
                new SchemaColumn { Id = "c1", Name = "id", Type = "UUID", IsPK = true, StableUuid = "uuid-id", DefaultValue = "gen_random_uuid()" },
                new SchemaColumn { Id = "c2", Name = "email", Type = "VARCHAR", Length = 255, StableUuid = "uuid-email" },
                new SchemaColumn { Id = "c3", Name = "note", Type = "TEXT", IsNullable = true, StableUuid = "uuid-note" },
            },
            Uniques = { new SchemaUnique { Id = "u1", Name = "uq_users_email", ColumnIds = { "c2" } } },
            Indexes =
            {
                new SchemaIndex
                {
                    Id = "i1", Name = "ix_users_note", IsUnique = false,
                    Where = "note is not null",
                    Columns = { new SchemaIndexColumn { ColumnId = "c3", Descending = true } },
                },
            },
            Checks = { new SchemaCheck { Id = "k1", Name = "ck_email_len", Expression = "char_length(email) > 3" } },
        };

        var orders = new SchemaTable
        {
            Id = "t2", Name = "orders", StableUuid = "uuid-orders",
            Columns =
            {
                new SchemaColumn { Id = "c4", Name = "id", Type = "BIGINT", IsPK = true, StableUuid = "uuid-oid" },
                new SchemaColumn { Id = "c5", Name = "user_id", Type = "UUID", StableUuid = "uuid-uid" },
            },
        };

        return new DatabaseSchema
        {
            Name = "shopfront",
            Tables = { users, orders },
            Relations =
            {
                new SchemaRelation
                {
                    Id = "r1", SourceTableId = "t2", SourceColumnId = "c5",
                    TargetTableId = "t1", TargetColumnId = "c1",
                    OnDelete = ReferentialAction.Cascade,
                    OnUpdate = ReferentialAction.NoAction,
                },
            },
        };
    }

    [Fact]
    public void A_schema_survives_a_round_trip()
    {
        var original = Sample();
        var parsed = NslParser.Parse(NslWriter.Write(original));

        Assert.Equal(original.Name, parsed.Name);
        Assert.Equal(original.Tables.Count, parsed.Tables.Count);

        for (var i = 0; i < original.Tables.Count; i++)
        {
            Assert.Equal(original.Tables[i].Name, parsed.Tables[i].Name);
            Assert.Equal(original.Tables[i].Columns.Count, parsed.Tables[i].Columns.Count);
        }
    }

    [Fact]
    public void Column_details_survive()
    {
        var parsed = NslParser.Parse(NslWriter.Write(Sample()));
        var users = parsed.Tables.Single(t => t.Name == "users");

        var id = users.Columns.Single(c => c.Name == "id");
        Assert.True(id.IsPK);
        Assert.False(id.IsNullable);
        Assert.Equal("gen_random_uuid()", id.DefaultValue);

        var email = users.Columns.Single(c => c.Name == "email");
        Assert.Equal(255, email.Length);
        Assert.False(email.IsNullable);

        Assert.True(users.Columns.Single(c => c.Name == "note").IsNullable);
    }

    [Fact]
    public void Stable_identity_survives()
    {
        // Hedef #5. Kimlik kaybolursa dosyadan geri okunan şema her seferinde yeni
        // uuid alır ve "yeniden adlandırma" ile "sil + ekle" ayırt edilemez —
        // SchemaIdentity'de düzeltilen hatanın metin karşılığı.
        var parsed = NslParser.Parse(NslWriter.Write(Sample()));
        var users = parsed.Tables.Single(t => t.Name == "users");

        Assert.Equal("uuid-users", users.StableUuid);
        Assert.Equal("uuid-email", users.Columns.Single(c => c.Name == "email").StableUuid);
    }

    [Fact]
    public void Constraints_survive()
    {
        var parsed = NslParser.Parse(NslWriter.Write(Sample()));
        var users = parsed.Tables.Single(t => t.Name == "users");

        Assert.Equal("uq_users_email", users.Uniques.Single().Name);
        Assert.Equal("ck_email_len", users.Checks.Single().Name);
        Assert.Equal("char_length(email) > 3", users.Checks.Single().Expression);

        var index = users.Indexes.Single();
        Assert.Equal("ix_users_note", index.Name);
        Assert.Equal("note is not null", index.Where);
        Assert.True(index.Columns.Single().Descending);
    }

    [Fact]
    public void Relations_and_their_actions_survive()
    {
        // Silme davranışı kaybolursa varsayılan NO ACTION'a düşer — güvenli
        // yönde ama SESSİZCE davranış değiştirir; kullanıcı cascade beklerken
        // silme reddedilir.
        var parsed = NslParser.Parse(NslWriter.Write(Sample()));
        var relation = parsed.Relations.Single();

        var orders = parsed.Tables.Single(t => t.Name == "orders");
        var users = parsed.Tables.Single(t => t.Name == "users");

        Assert.Equal(orders.Id, relation.SourceTableId);
        Assert.Equal(users.Id, relation.TargetTableId);
        Assert.Equal(ReferentialAction.Cascade, relation.OnDelete);
        Assert.Equal(ReferentialAction.NoAction, relation.OnUpdate);
    }

    [Fact]
    public void Writing_is_deterministic()
    {
        // Hedef #4. Aynı şema farklı metin üretirse her açılış gereksiz bir git
        // diff'i doğurur ve gerçek değişiklikler gürültüde kaybolur.
        Assert.Equal(NslWriter.Write(Sample()), NslWriter.Write(Sample()));
    }

    [Fact]
    public void A_second_round_trip_changes_nothing()
    {
        // Metin sabit noktaya ulaşmalı: yaz → oku → yaz aynı metni vermeli.
        var once = NslWriter.Write(Sample());
        var twice = NslWriter.Write(NslParser.Parse(once));

        Assert.Equal(once, twice);
    }

    // ── Ayrıştırıcı davranışı ────────────────────────────────────────────────

    [Fact]
    public void An_unknown_statement_is_rejected_not_skipped()
    {
        // Sessizce atlamak, yazım hatası içeren bir kısıtın kaybolması ve
        // kullanıcının şemasını EKSİK geri alması demek.
        var text = "nsl 1.0\nproject \"x\" {\n}\ntable t {\n  id int pk\n  uniqe (id)\n}\n";

        Assert.Throws<NslParseException>(() => NslParser.Parse(text));
    }

    [Fact]
    public void A_comment_inside_a_string_is_not_treated_as_a_comment()
    {
        // Bir CHECK ifadesi ya da URL varsayılanı '//' içerebilir; onu yorum
        // sanmak ifadeyi ortadan keser ve kısıt anlamını değiştirir.
        var text = "nsl 1.0\nproject \"x\" {\n}\ntable t {\n  id int pk\n  check \"url like 'https://%'\"\n}\n";

        var parsed = NslParser.Parse(text);

        Assert.Equal("url like 'https://%'", parsed.Tables.Single().Checks.Single().Expression);
    }

    [Fact]
    public void A_comment_after_a_statement_is_stripped()
    {
        var text = "nsl 1.0\nproject \"x\" {\n}\ntable t {\n  id int pk // the key\n}\n";

        var parsed = NslParser.Parse(text);

        Assert.Equal("id", parsed.Tables.Single().Columns.Single().Name);
    }

    [Fact]
    public void A_forward_reference_resolves()
    {
        // İlişkiler sonda çözülüyor: bir tablo kendisinden SONRA tanımlanan bir
        // tabloya referans verebilmeli, aksi hâlde kullanıcı dosyayı elle
        // sıralamak zorunda kalırdı.
        var text = """
            nsl 1.0
            project "x" {
            }
            table orders {
              id int pk
              user_id int not null
              fk (user_id) -> users(id) on delete cascade on update no action
            }
            table users {
              id int pk
            }
            """;

        var parsed = NslParser.Parse(text);

        Assert.Single(parsed.Relations);
        Assert.Equal(ReferentialAction.Cascade, parsed.Relations[0].OnDelete);
    }

    [Fact]
    public void A_foreign_key_to_a_missing_table_is_rejected()
    {
        var text = """
            nsl 1.0
            project "x" {
            }
            table orders {
              id int pk
              user_id int not null
              fk (user_id) -> nowhere(id)
            }
            """;

        var ex = Assert.Throws<NslParseException>(() => NslParser.Parse(text));
        Assert.Contains("nowhere", ex.Message);
    }

    [Fact]
    public void A_composite_primary_key_survives()
    {
        var schema = new DatabaseSchema
        {
            Name = "join",
            Tables =
            {
                new SchemaTable
                {
                    Id = "t", Name = "user_roles",
                    Columns =
                    {
                        new SchemaColumn { Id = "a", Name = "user_id", Type = "INT", IsPK = true },
                        new SchemaColumn { Id = "b", Name = "role_id", Type = "INT", IsPK = true },
                    },
                },
            },
        };

        var parsed = NslParser.Parse(NslWriter.Write(schema));

        Assert.Equal(2, parsed.Tables.Single().Columns.Count(c => c.IsPK));
    }

    [Fact]
    public void A_name_needing_quotes_survives()
    {
        // Boşluklu ya da özel karakterli ad tırnaklanmazsa üretilen dosya kendi
        // ayrıştırıcımız tarafından okunamaz hâle gelir.
        var schema = new DatabaseSchema
        {
            Name = "x",
            Tables =
            {
                new SchemaTable
                {
                    Id = "t", Name = "order items",
                    Columns = { new SchemaColumn { Id = "a", Name = "line-no", Type = "INT", IsPK = true } },
                },
            },
        };

        var parsed = NslParser.Parse(NslWriter.Write(schema));

        Assert.Equal("order items", parsed.Tables.Single().Name);
        Assert.Equal("line-no", parsed.Tables.Single().Columns.Single().Name);
    }
}
