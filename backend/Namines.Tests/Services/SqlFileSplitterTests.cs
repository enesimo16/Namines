using System.Text;
using Namines.Infrastructure.Services;

namespace Namines.Tests.Services;

/// <summary>second-phase/13-DAGITIM-HEDEFLERI.md — dosya boyutu bölme.</summary>
public class SqlFileSplitterTests
{
    [Fact]
    public void A_file_under_the_limit_is_not_split()
    {
        var sql = "CREATE TABLE a (id INT);\nCREATE TABLE b (id INT);\n";

        var parts = SqlFileSplitter.Split(sql, maxBytes: 10_000);

        Assert.Single(parts);
        Assert.Equal(sql, parts[0]);
    }

    [Fact]
    public void A_large_file_is_split_into_multiple_parts()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 50; i++)
            sb.Append($"CREATE TABLE t{i} (id INT, name VARCHAR(255));\n");

        var parts = SqlFileSplitter.Split(sb.ToString(), maxBytes: 500);

        Assert.True(parts.Count > 1);
    }

    [Fact]
    public void No_statement_is_split_in_the_middle()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 50; i++)
            sb.Append($"CREATE TABLE t{i} (id INT, name VARCHAR(255));\n");

        var parts = SqlFileSplitter.Split(sb.ToString(), maxBytes: 500);

        // Her parça, tam ifadelerle biter — yarım bir ")" ya da "VARCHAR(" ile değil.
        foreach (var part in parts)
            Assert.EndsWith(");", part.TrimEnd());
    }

    [Fact]
    public void Every_original_statement_survives_the_split()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 50; i++)
            sb.Append($"CREATE TABLE t{i} (id INT);\n");

        var parts = SqlFileSplitter.Split(sb.ToString(), maxBytes: 300);
        var rejoined = string.Concat(parts);

        for (var i = 0; i < 50; i++)
            Assert.Contains($"CREATE TABLE t{i} (id INT);", rejoined);
    }

    [Fact]
    public void A_single_statement_larger_than_the_limit_still_gets_its_own_part_whole()
    {
        var hugeStatement = "CREATE TABLE huge (" + string.Join(", ", System.Linq.Enumerable.Range(0, 200).Select(i => $"col{i} INT")) + ");\n";

        var parts = SqlFileSplitter.Split(hugeStatement, maxBytes: 50);

        Assert.Single(parts);
        Assert.Equal(hugeStatement, parts[0]);
    }

    [Fact]
    public void Empty_lines_between_statements_do_not_produce_empty_parts()
    {
        var sql = "CREATE TABLE a (id INT);\n\nCREATE TABLE b (id INT);\n";

        var parts = SqlFileSplitter.Split(sql, maxBytes: 20);

        Assert.All(parts, p => Assert.False(string.IsNullOrWhiteSpace(p)));
    }
}
