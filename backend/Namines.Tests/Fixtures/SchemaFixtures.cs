using Namines.Core.Models;

namespace Namines.Tests.Fixtures;

/// <summary>
/// Golden-file testlerinin girdisi olan sabit şemalar.
///
/// KURAL: Bu şemalar DEĞİŞMEZ. Bir fixture'ı değiştirmek, ona bağlı tüm golden
/// dosyaların anlamını bozar. Yeni bir senaryo gerekiyorsa YENİ fixture ekleyin.
///
/// Şemalar deterministik olmalıdır — Guid.NewGuid() gibi rastgelelik kullanılmaz,
/// aksi halde her çalıştırmada farklı çıktı üretilir ve snapshot testleri anlamsızlaşır.
/// </summary>
public static class SchemaFixtures
{
    /// <summary>Tüm fixture'lar — testlerin [MemberData] kaynağı.</summary>
    public static IEnumerable<(string Name, DatabaseSchema Schema)> All()
    {
        yield return ("01-minimal", Minimal());
        yield return ("02-ecommerce", ECommerce());
        yield return ("03-composite-key", CompositeKey());
        yield return ("04-self-referencing", SelfReferencing());
        yield return ("05-multi-cascade-path", MultiCascadePath());
        yield return ("06-indexes-constraints", IndexesAndConstraints());
    }

    public static DatabaseSchema ByName(string name) =>
        All().First(f => f.Name == name).Schema;

    // ── 01 — En küçük geçerli şema ────────────────────────────────────────────
    // Amaç: temel CREATE TABLE + PK + identity davranışını sabitlemek.
    public static DatabaseSchema Minimal() => new()
    {
        SchemaId = "fixture-01",
        Name = "Minimal",
        Tables =
        {
            Table("t_user", "users",
                Col("c_user_id", "Id", "INT", isPk: true),
                Col("c_user_name", "Name", "NVARCHAR", length: 100))
        }
    };

    // ── 02 — Gerçekçi e-ticaret şeması ────────────────────────────────────────
    // Amaç: çok tablolu, çok FK'lı tipik kullanım. Tip eşleme ve FK üretiminin
    // ana regresyon koruması burasıdır.
    public static DatabaseSchema ECommerce()
    {
        var schema = new DatabaseSchema
        {
            SchemaId = "fixture-02",
            Name = "ECommerce",
            Tables =
            {
                Table("t_users", "Users",
                    Col("c_u_id", "Id", "INT", isPk: true),
                    Col("c_u_email", "Email", "NVARCHAR", length: 255),
                    Col("c_u_created", "CreatedAt", "DATETIME2", defaultValue: "GETUTCDATE()")),

                Table("t_products", "Products",
                    Col("c_p_id", "Id", "INT", isPk: true),
                    Col("c_p_name", "Name", "NVARCHAR", length: 200),
                    Col("c_p_price", "Price", "DECIMAL"),
                    Col("c_p_stock", "Stock", "INT", defaultValue: "0")),

                Table("t_orders", "Orders",
                    Col("c_o_id", "Id", "INT", isPk: true),
                    Col("c_o_user", "UserId", "INT", isFk: true),
                    Col("c_o_total", "Total", "DECIMAL"),
                    Col("c_o_placed", "PlacedAt", "DATETIME2")),

                Table("t_items", "OrderItems",
                    Col("c_i_id", "Id", "INT", isPk: true),
                    Col("c_i_order", "OrderId", "INT", isFk: true),
                    Col("c_i_product", "ProductId", "INT", isFk: true),
                    Col("c_i_qty", "Quantity", "INT", defaultValue: "1"))
            }
        };

        schema.Relations.Add(Fk("r1", "t_orders", "c_o_user", "t_users", "c_u_id"));
        schema.Relations.Add(Fk("r2", "t_items", "c_i_order", "t_orders", "c_o_id"));
        schema.Relations.Add(Fk("r3", "t_items", "c_i_product", "t_products", "c_p_id"));

        return schema;
    }

    // ── 03 — Bileşik birincil anahtar ─────────────────────────────────────────
    // Amaç: iki kolonun birlikte PK olması. Mevcut modelde bu "iki kolonda da
    // IsPK=true" demektir; üreticilerin bunu TEK bir bileşik PK olarak yazması
    // beklenir (her kolona ayrı PK değil).
    public static DatabaseSchema CompositeKey()
    {
        var schema = new DatabaseSchema
        {
            SchemaId = "fixture-03",
            Name = "CompositeKey",
            Tables =
            {
                Table("t_o", "Orders",
                    Col("c_o_id", "Id", "INT", isPk: true)),

                Table("t_p", "Products",
                    Col("c_p_id", "Id", "INT", isPk: true)),

                Table("t_op", "OrderProducts",
                    Col("c_op_order", "OrderId", "INT", isPk: true, isFk: true),
                    Col("c_op_product", "ProductId", "INT", isPk: true, isFk: true),
                    Col("c_op_qty", "Quantity", "INT"))
            }
        };

        schema.Relations.Add(Fk("r1", "t_op", "c_op_order", "t_o", "c_o_id"));
        schema.Relations.Add(Fk("r2", "t_op", "c_op_product", "t_p", "c_p_id"));

        return schema;
    }

    // ── 04 — Kendine referans veren tablo ─────────────────────────────────────
    // Amaç: parent_id deseni. Bu, ON DELETE CASCADE ile birlikte SQL Server'da
    // "cycles or multiple cascade paths" hatasının en basit tetikleyicisidir.
    public static DatabaseSchema SelfReferencing()
    {
        var schema = new DatabaseSchema
        {
            SchemaId = "fixture-04",
            Name = "SelfReferencing",
            Tables =
            {
                Table("t_cat", "Categories",
                    Col("c_cat_id", "Id", "INT", isPk: true),
                    Col("c_cat_name", "Name", "NVARCHAR", length: 120),
                    Col("c_cat_parent", "ParentId", "INT", isFk: true, isNullable: true))
            }
        };

        schema.Relations.Add(Fk("r1", "t_cat", "c_cat_parent", "t_cat", "c_cat_id"));

        return schema;
    }

    // ── 05 — Çoklu cascade yolu (BİLİNEN HATA SENARYOSU) ──────────────────────
    // Users'a İKİ ayrı yoldan ulaşılıyor:
    //     Orders -> Users            (doğrudan)
    //     Orders -> Addresses -> Users (dolaylı)
    //
    // Üretilen DDL her FK'ya ON DELETE CASCADE yazarsa, SQL Server bu şemayı
    // "Introducing FOREIGN KEY constraint ... may cause cycles or multiple
    // cascade paths" (Msg 1785) hatasıyla REDDEDER.
    //
    // Bu, e-ticarette son derece yaygın bir modeldir — yani üretilen DDL gerçek
    // hayatta çalışmıyor demektir. Düzeltme G3'te yapılacak.
    public static DatabaseSchema MultiCascadePath()
    {
        var schema = new DatabaseSchema
        {
            SchemaId = "fixture-05",
            Name = "MultiCascadePath",
            Tables =
            {
                Table("t_users", "Users",
                    Col("c_u_id", "Id", "INT", isPk: true),
                    Col("c_u_email", "Email", "NVARCHAR", length: 255)),

                Table("t_addr", "Addresses",
                    Col("c_a_id", "Id", "INT", isPk: true),
                    Col("c_a_user", "UserId", "INT", isFk: true),
                    Col("c_a_line", "Line1", "NVARCHAR", length: 200)),

                Table("t_orders", "Orders",
                    Col("c_o_id", "Id", "INT", isPk: true),
                    Col("c_o_user", "UserId", "INT", isFk: true),
                    Col("c_o_addr", "AddressId", "INT", isFk: true))
            }
        };

        schema.Relations.Add(Fk("r1", "t_addr", "c_a_user", "t_users", "c_u_id"));
        schema.Relations.Add(Fk("r2", "t_orders", "c_o_user", "t_users", "c_u_id"));
        schema.Relations.Add(Fk("r3", "t_orders", "c_o_addr", "t_addr", "c_a_id"));

        return schema;
    }

    // ── 06 — Index, UNIQUE ve CHECK kısıtları ─────────────────────────────────
    // Amaç: Faz 1'de modelde HİÇ OLMAYAN kavramları kapsamak.
    // Kısmi index ve INCLUDE her motorda yok — üreticilerin bunları sessizce
    // düşürmeyip açıklama satırı yazdığını da bu fixture doğrular.
    public static DatabaseSchema IndexesAndConstraints()
    {
        var users = Table("t_u", "Users",
            Col("c_id", "Id", "INT", isPk: true),
            Col("c_email", "Email", "NVARCHAR", length: 255),
            Col("c_country", "CountryCode", "CHAR", length: 2, defaultValue: "'TR'"),
            Col("c_age", "Age", "INT", isNullable: true),
            Col("c_created", "CreatedAt", "DATETIME2"),
            Col("c_deleted", "DeletedAt", "DATETIME2", isNullable: true));

        users.Uniques.Add(new SchemaUnique
        {
            Id = "uq1",
            StableUuid = "uuid-uq1",
            Name = "UQ_Users_Email",
            ColumnIds = { "c_email" }
        });

        users.Checks.Add(new SchemaCheck
        {
            Id = "ck1",
            StableUuid = "uuid-ck1",
            Name = "CK_Users_Age",
            // Kolon adları PascalCase ve tüm üreticiler tarafından tırnaklanarak
            // (case-preserving) yaratılıyor. CHECK ifadesi HAM SQL olarak aktarılır
            // (bilinçli tasarım — bkz. ConstraintSql.cs), bu yüzden burada da tırnaklı
            // referans vermek gerekiyor: PostgreSQL tırnaksız tanımlayıcıyı küçük harfe
            // çevirir ("age" ≠ "Age") — gerçek bir Postgres container'ına karşı
            // çalıştırılan entegrasyon testi bunu kanıtladı.
            Expression = "\"Age\" IS NULL OR \"Age\" >= 0"
        });

        // Bileşik index, ikinci kolon azalan sıralı
        users.Indexes.Add(new SchemaIndex
        {
            Id = "ix1",
            StableUuid = "uuid-ix1",
            Columns =
            {
                new SchemaIndexColumn { ColumnId = "c_country" },
                new SchemaIndexColumn { ColumnId = "c_created", Descending = true }
            }
        });

        // Kısmi (filtreli) benzersiz index — MySQL/MariaDB/Oracle desteklemez
        users.Indexes.Add(new SchemaIndex
        {
            Id = "ix2",
            StableUuid = "uuid-ix2",
            Name = "UX_Users_Email_Active",
            IsUnique = true,
            Columns = { new SchemaIndexColumn { ColumnId = "c_email" } },
            // Ham SQL — aynı case-folding gerekçesiyle tırnaklı (bkz. CK_Users_Age yorumu).
            Where = "\"DeletedAt\" IS NULL"
        });

        // Kapsayan index — yalnızca MSSQL ve PostgreSQL destekler
        users.Indexes.Add(new SchemaIndex
        {
            Id = "ix3",
            StableUuid = "uuid-ix3",
            Columns = { new SchemaIndexColumn { ColumnId = "c_created" } },
            IncludeColumnIds = { "c_email" }
        });

        var orders = Table("t_o", "Orders",
            Col("c_o_id", "Id", "INT", isPk: true),
            Col("c_o_user", "UserId", "INT", isFk: true),
            Col("c_o_total", "Total", "DECIMAL"));

        orders.Checks.Add(new SchemaCheck
        {
            Id = "ck2",
            StableUuid = "uuid-ck2",
            Expression = "\"Total\" >= 0"
        });

        // FK kolonunda index — en yaygın performans hatasının düzeltmesi
        orders.Indexes.Add(new SchemaIndex
        {
            Id = "ix4",
            StableUuid = "uuid-ix4",
            Columns = { new SchemaIndexColumn { ColumnId = "c_o_user" } }
        });

        var schema = new DatabaseSchema
        {
            SchemaId = "fixture-06",
            Name = "IndexesAndConstraints",
            Tables = { users, orders }
        };

        schema.Relations.Add(Fk("r1", "t_o", "c_o_user", "t_u", "c_id"));

        return schema;
    }

    // ── Yardımcılar ───────────────────────────────────────────────────────────
    // StableUuid açıkça veriliyor: modelin varsayılanı Guid.NewGuid() olduğu için
    // aksi halde her çalıştırmada değişir ve snapshot testleri anlamsızlaşır.

    private static SchemaTable Table(string id, string name, params SchemaColumn[] columns) => new()
    {
        Id = id,
        Name = name,
        StableUuid = $"uuid-{id}",
        Columns = columns.ToList()
    };

    private static SchemaColumn Col(
        string id,
        string name,
        string type,
        int? length = null,
        bool isPk = false,
        bool isFk = false,
        bool isNullable = false,
        string? defaultValue = null) => new()
    {
        Id = id,
        Name = name,
        StableUuid = $"uuid-{id}",
        Type = type,
        Length = length,
        IsPK = isPk,
        IsFK = isFk,
        IsNullable = isNullable,
        DefaultValue = defaultValue
    };

    private static SchemaRelation Fk(
        string id,
        string sourceTable,
        string sourceColumn,
        string targetTable,
        string targetColumn) => new()
    {
        Id = id,
        Type = "OneToMany",
        SourceTableId = sourceTable,
        SourceColumnId = sourceColumn,
        TargetTableId = targetTable,
        TargetColumnId = targetColumn
    };
}
