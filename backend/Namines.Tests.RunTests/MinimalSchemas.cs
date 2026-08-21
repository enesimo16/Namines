using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Tests.RunTests;

/// <summary>
/// Namines.Tests/Fixtures/SchemaFixtures.cs'in küçük bir alt kümesi — bu izole projeye
/// bilinçli olarak KOPYALANMADI (import edilse Namines.Tests'e referans gerekirdi, o da
/// Testcontainers'ı beraberinde getirirdi — bkz. .csproj yorumu). Sadece bu testlerin
/// ihtiyaç duyduğu iki senaryo burada minimal biçimde yeniden tanımlanıyor.
/// </summary>
internal static class MinimalSchemas
{
    public static DatabaseSchema Simple() => new()
    {
        Name = "Simple",
        Tables =
        {
            new SchemaTable
            {
                Id = "t_users", Name = "Users", StableUuid = "uuid-t_users",
                Columns =
                {
                    new SchemaColumn { Id = "c_id", Name = "Id", StableUuid = "uuid-c_id", Type = "INT", IsPK = true },
                    new SchemaColumn { Id = "c_email", Name = "Email", StableUuid = "uuid-c_email", Type = "VARCHAR", Length = 255 }
                }
            }
        }
    };

    /// <summary>Users'a iki ayrı yoldan ulaşılıyor (Orders -> Users doğrudan, Orders -> Addresses -> Users
    /// dolaylı) — tüm FK'lar CASCADE olursa SQL Server Msg 1785 ile reddeder (bkz. G3/G5).</summary>
    public static DatabaseSchema MultiCascadePathAllCascade()
    {
        var schema = new DatabaseSchema
        {
            Name = "MultiCascadePath",
            Tables =
            {
                new SchemaTable
                {
                    Id = "t_users", Name = "Users", StableUuid = "uuid-t_users",
                    Columns = { new SchemaColumn { Id = "c_u_id", Name = "Id", StableUuid = "uuid-c_u_id", Type = "INT", IsPK = true } }
                },
                new SchemaTable
                {
                    Id = "t_addr", Name = "Addresses", StableUuid = "uuid-t_addr",
                    Columns =
                    {
                        new SchemaColumn { Id = "c_a_id", Name = "Id", StableUuid = "uuid-c_a_id", Type = "INT", IsPK = true },
                        new SchemaColumn { Id = "c_a_user", Name = "UserId", StableUuid = "uuid-c_a_user", Type = "INT", IsFK = true }
                    }
                },
                new SchemaTable
                {
                    Id = "t_orders", Name = "Orders", StableUuid = "uuid-t_orders",
                    Columns =
                    {
                        new SchemaColumn { Id = "c_o_id", Name = "Id", StableUuid = "uuid-c_o_id", Type = "INT", IsPK = true },
                        new SchemaColumn { Id = "c_o_user", Name = "UserId", StableUuid = "uuid-c_o_user", Type = "INT", IsFK = true },
                        new SchemaColumn { Id = "c_o_addr", Name = "AddressId", StableUuid = "uuid-c_o_addr", Type = "INT", IsFK = true }
                    }
                }
            }
        };

        schema.Relations.Add(new SchemaRelation { Id = "r1", Type = "OneToMany", SourceTableId = "t_addr", SourceColumnId = "c_a_user", TargetTableId = "t_users", TargetColumnId = "c_u_id", OnDelete = ReferentialAction.Cascade });
        schema.Relations.Add(new SchemaRelation { Id = "r2", Type = "OneToMany", SourceTableId = "t_orders", SourceColumnId = "c_o_user", TargetTableId = "t_users", TargetColumnId = "c_u_id", OnDelete = ReferentialAction.Cascade });
        schema.Relations.Add(new SchemaRelation { Id = "r3", Type = "OneToMany", SourceTableId = "t_orders", SourceColumnId = "c_o_addr", TargetTableId = "t_addr", TargetColumnId = "c_a_id", OnDelete = ReferentialAction.Cascade });

        return schema;
    }
}
