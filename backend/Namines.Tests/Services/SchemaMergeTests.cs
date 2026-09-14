using Namines.Core.Enums;
using Namines.Core.Models;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// Onarım turu, bulguyla ilgisi olmayan şema parçalarını KORUMALI.
///
/// Revizyon promptu modelden yalnızca tables+relations istiyor; dönen JSON'dan
/// kurulan şemada trigger/saklı yordam/enum BOŞ olur. Bu nesneyi doğrudan
/// kullanmak, her düzeltme turunda kullanıcının trigger'larını sessizce silmek
/// demekti — ve bunu ancak DDL'e bakınca fark ederdi.
/// </summary>
public class SchemaMergeTests
{
    [Fact]
    public void Triggers_the_partial_omitted_are_preserved()
    {
        var original = new DatabaseSchema { SchemaId = "s", Name = "S" };
        original.Triggers.Add(new SchemaTrigger
        {
            Id = "trg1",
            TableId = "t1",
            Timing = "After",
            Event = "Insert",
            TargetEngine = DatabaseType.PostgreSQL,
            Body = "CREATE TRIGGER x ...;"
        });

        var partial = new DatabaseSchema { SchemaId = "s", Name = "S" };

        var merged = SchemaMerge.PreserveUnrevised(original, partial);

        Assert.Single(merged.Triggers);
        Assert.Equal("trg1", merged.Triggers[0].Id);
    }

    [Fact]
    public void Enums_and_procedures_the_partial_omitted_are_preserved()
    {
        var original = new DatabaseSchema { SchemaId = "s", Name = "S" };
        original.Enums.Add(new SchemaEnum { Id = "e1", Name = "Status", Values = { "A", "B" } });
        original.StoredProcedures.Add(new SchemaStoredProcedure
        {
            Id = "sp1",
            Name = "P",
            TargetEngine = DatabaseType.MySQL,
            Body = "BEGIN END;"
        });

        var partial = new DatabaseSchema { SchemaId = "s", Name = "S" };

        var merged = SchemaMerge.PreserveUnrevised(original, partial);

        Assert.Single(merged.Enums);
        Assert.Single(merged.StoredProcedures);
    }

    [Fact]
    public void The_partial_wins_when_it_does_return_them()
    {
        var original = new DatabaseSchema { SchemaId = "s", Name = "S" };
        original.Triggers.Add(new SchemaTrigger { Id = "old", TargetEngine = DatabaseType.MSSQL, Body = "x" });

        var partial = new DatabaseSchema { SchemaId = "s", Name = "S" };
        partial.Triggers.Add(new SchemaTrigger { Id = "new", TargetEngine = DatabaseType.PostgreSQL, Body = "y" });

        var merged = SchemaMerge.PreserveUnrevised(original, partial);

        Assert.Single(merged.Triggers);
        Assert.Equal("new", merged.Triggers[0].Id);
    }

    [Fact]
    public void Identity_fields_fall_back_to_the_original_when_the_partial_leaves_them_blank()
    {
        var original = new DatabaseSchema { SchemaId = "schema-1", Name = "Shop" };
        var partial = new DatabaseSchema { SchemaId = "", Name = "" };

        var merged = SchemaMerge.PreserveUnrevised(original, partial);

        Assert.Equal("schema-1", merged.SchemaId);
        Assert.Equal("Shop", merged.Name);
    }
}
