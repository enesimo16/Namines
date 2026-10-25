using System.Collections.Generic;
using System.Linq;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Core.Nsl;
using Xunit;

namespace Namines.Tests.Nsl;

public class NslNormalizationTests
{
    private static SchemaColumn Col(string id, string name, string type, bool pk = false, int? length = null) =>
        new() { Id = id, Name = name, Type = type, IsPK = pk, Length = length };

    [Fact]
    public void NSL019_flags_repeating_numbered_columns_in_1NF()
    {
        var schema = new DatabaseSchema
        {
            Tables =
            {
                new SchemaTable
                {
                    Id = "t1",
                    Name = "users",
                    Columns =
                    {
                        Col("c1", "id", "INT", pk: true),
                        Col("c2", "phone_1", "VARCHAR", length: 32),
                        Col("c3", "phone_2", "VARCHAR", length: 32),
                        Col("c4", "phone_3", "VARCHAR", length: 32),
                    }
                }
            }
        };

        var findings = NslValidator.Validate(schema);

        var finding = Assert.Single(findings, f => f.Code == "NSL019");
        Assert.Equal("warning", finding.Severity);
        Assert.Equal("users", finding.Table);
        Assert.Contains("phone_1, phone_2, phone_3", finding.Message);
        Assert.Contains("1NF", finding.Message);
    }

    [Fact]
    public void NSL019_does_not_flag_single_numbered_column()
    {
        var schema = new DatabaseSchema
        {
            Tables =
            {
                new SchemaTable
                {
                    Id = "t1",
                    Name = "logs",
                    Columns =
                    {
                        Col("c1", "id", "INT", pk: true),
                        Col("c2", "v1", "VARCHAR", length: 32),
                        Col("c3", "message", "TEXT")
                    }
                }
            }
        };

        var findings = NslValidator.Validate(schema);

        Assert.DoesNotContain(findings, f => f.Code == "NSL019");
    }

    [Fact]
    public void NSL020_flags_transitive_foreign_attribute_duplication_in_3NF()
    {
        var schema = new DatabaseSchema
        {
            Tables =
            {
                new SchemaTable
                {
                    Id = "t_cust",
                    Name = "customers",
                    Columns =
                    {
                        Col("c_cid", "id", "INT", pk: true),
                        Col("c_name", "name", "VARCHAR", length: 100),
                        Col("c_email", "email", "VARCHAR", length: 100),
                    }
                },
                new SchemaTable
                {
                    Id = "t_ord",
                    Name = "orders",
                    Columns =
                    {
                        Col("o_oid", "id", "INT", pk: true),
                        Col("o_cid", "customer_id", "INT"),
                        // These columns transitively duplicate customer attributes!
                        Col("o_cname", "customer_name", "VARCHAR", length: 100),
                        Col("o_cemail", "customer_email", "VARCHAR", length: 100),
                        Col("o_total", "order_total", "DECIMAL"),
                    }
                }
            },
            Relations =
            {
                new SchemaRelation
                {
                    Id = "rel1",
                    SourceTableId = "t_ord",
                    SourceColumnId = "o_cid",
                    TargetTableId = "t_cust",
                    TargetColumnId = "c_cid"
                }
            }
        };

        var findings = NslValidator.Validate(schema);

        var transitiveFindings = findings.Where(f => f.Code == "NSL020").ToList();
        Assert.Equal(2, transitiveFindings.Count);
        Assert.Contains(transitiveFindings, f => f.Column == "customer_name");
        Assert.Contains(transitiveFindings, f => f.Column == "customer_email");
        Assert.All(transitiveFindings, f => Assert.Contains("3NF", f.Message));
    }
}
