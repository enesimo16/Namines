using System.Collections.Generic;
using System.Linq;
using Namines.Core.Analysis;

namespace Namines.Tests.Analysis;

/// <summary>second-phase/11-KODDAN-SEMA.md kademe 2 — C# entity sınıfları.</summary>
public class EfCoreEntityParserTests
{
    private const string DbContextFile = """
        using Microsoft.EntityFrameworkCore;

        public class ShopContext : DbContext
        {
            public DbSet<Customer> Customers { get; set; } = null!;
            public DbSet<Order> Orders { get; set; } = null!;
        }
        """;

    private const string CustomerFile = """
        using System;
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        [Table("customers")]
        public class Customer
        {
            public int Id { get; set; }
            [MaxLength(320)]
            public string Email { get; set; } = null!;
            public string? DisplayName { get; set; }
            public DateTime CreatedAt { get; set; }
            public List<Order> Orders { get; set; } = new();
        }
        """;

    private const string OrderFile = """
        public class Order
        {
            public int Id { get; set; }
            public decimal Total { get; set; }
            public int CustomerId { get; set; }
            public Customer Customer { get; set; } = null!;
        }
        """;

    private static Dictionary<string, string> ShopFiles() => new()
    {
        ["ShopContext.cs"] = DbContextFile,
        ["Customer.cs"] = CustomerFile,
        ["Order.cs"] = OrderFile,
    };

    [Fact]
    public void Entity_classes_become_tables_and_properties_become_columns()
    {
        var result = EfCoreEntityParser.Parse(ShopFiles());

        Assert.Equal("efcore", result.Format);
        Assert.Equal(2, result.Schema.Tables.Count);

        var order = result.Schema.Tables.Single(t => t.Id == "Order");
        // Id, Total, CustomerId — "Customer" navigasyon özelliği kolon DEĞİL.
        Assert.Equal(3, order.Columns.Count);
    }

    [Fact]
    public void Table_attribute_overrides_the_table_name()
    {
        var result = EfCoreEntityParser.Parse(ShopFiles());

        Assert.Equal("customers", result.Schema.Tables.Single(t => t.Id == "Customer").Name);
    }

    [Fact]
    public void Nullable_reference_marker_makes_the_column_nullable()
    {
        var result = EfCoreEntityParser.Parse(ShopFiles());
        var customer = result.Schema.Tables.Single(t => t.Id == "Customer");

        Assert.True(customer.Columns.Single(c => c.Name == "DisplayName").IsNullable);
        Assert.False(customer.Columns.Single(c => c.Name == "Email").IsNullable);
    }

    [Fact]
    public void MaxLength_attribute_sets_the_column_length()
    {
        var result = EfCoreEntityParser.Parse(ShopFiles());

        var email = result.Schema.Tables.Single(t => t.Id == "Customer").Columns.Single(c => c.Name == "Email");
        Assert.Equal(320, email.Length);
    }

    [Fact]
    public void Id_property_is_recognised_as_the_primary_key_by_convention()
    {
        var result = EfCoreEntityParser.Parse(ShopFiles());

        var id = result.Schema.Tables.Single(t => t.Id == "Order").Columns.Single(c => c.Name == "Id");
        Assert.True(id.IsPK);
    }

    [Fact]
    public void FooId_plus_Foo_navigation_produces_a_foreign_key()
    {
        var result = EfCoreEntityParser.Parse(ShopFiles());

        var relation = Assert.Single(result.Schema.Relations);
        Assert.Equal("Order", relation.SourceTableId);
        Assert.Equal("Order.CustomerId", relation.SourceColumnId);
        Assert.Equal("Customer", relation.TargetTableId);
        Assert.Equal("Customer.Id", relation.TargetColumnId);
    }

    [Fact]
    public void A_scalar_ending_in_Id_without_a_matching_navigation_is_not_a_relation()
    {
        // "ExternalRefId" gibi bir alan yalnızca ada bakarak FK sayılırsa,
        // olmayan bir ilişki uydurmuş oluruz.
        var files = new Dictionary<string, string>
        {
            ["Ctx.cs"] = "public class C : DbContext { public DbSet<Thing> Things { get; set; } }",
            ["Thing.cs"] = """
                public class Thing
                {
                    public int Id { get; set; }
                    public int ExternalRefId { get; set; }
                }
                """,
        };

        var result = EfCoreEntityParser.Parse(files);

        Assert.Empty(result.Schema.Relations);
        Assert.False(result.Schema.Tables.Single().Columns.Single(c => c.Name == "ExternalRefId").IsFK);
    }

    [Fact]
    public void Only_DbSet_types_become_tables_when_a_DbContext_is_present()
    {
        // Bir depodaki her POCO tablo değildir — DTO'lar, view model'ler.
        var files = ShopFiles();
        files["CustomerDto.cs"] = "public class CustomerDto { public int Id { get; set; } public string Name { get; set; } }";

        var result = EfCoreEntityParser.Parse(files);

        Assert.DoesNotContain(result.Schema.Tables, t => t.Id == "CustomerDto");
        Assert.Equal(2, result.Schema.Tables.Count);
    }

    [Fact]
    public void A_DbSet_whose_class_is_missing_is_reported_as_skipped()
    {
        var files = new Dictionary<string, string>
        {
            ["Ctx.cs"] = "public class C : DbContext { public DbSet<Missing> Missings { get; set; } public DbSet<Here> Heres { get; set; } }",
            ["Here.cs"] = "public class Here { public int Id { get; set; } }",
        };

        var result = EfCoreEntityParser.Parse(files);

        Assert.Contains(result.Skipped, s => s.Name == "Missing" && s.Reason.Contains("not among the given files"));
    }

    [Fact]
    public void An_unmapped_property_type_is_reported_rather_than_guessed()
    {
        var files = new Dictionary<string, string>
        {
            ["Ctx.cs"] = "public class C : DbContext { public DbSet<Doc> Docs { get; set; } }",
            ["Doc.cs"] = """
                public class Doc
                {
                    public int Id { get; set; }
                    public SomeCustomType Payload { get; set; }
                }
                """,
        };

        var result = EfCoreEntityParser.Parse(files);

        Assert.Contains(result.Skipped, s => s.Name == "Doc.Payload" && s.Reason.Contains("unmapped type"));
        // Ama okunabilen kısım yine de üretiliyor — hepsi ya da hiçbiri değil.
        Assert.Single(result.Schema.Tables.Single().Columns);
    }

    [Fact]
    public void Collection_navigation_properties_are_not_columns()
    {
        var result = EfCoreEntityParser.Parse(ShopFiles());

        var customer = result.Schema.Tables.Single(t => t.Id == "Customer");
        Assert.DoesNotContain(customer.Columns, c => c.Name == "Orders");
    }

    [Fact]
    public void Without_a_DbContext_every_class_with_properties_is_treated_as_a_candidate()
    {
        var files = new Dictionary<string, string>
        {
            ["Customer.cs"] = CustomerFile,
            ["Order.cs"] = OrderFile,
        };

        var result = EfCoreEntityParser.Parse(files);

        Assert.Equal(2, result.Schema.Tables.Count);
    }
}
