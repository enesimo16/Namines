using Namines.Tests.Integration;

namespace Namines.Tests.Ddl;

/// <summary>
/// Betik bölücünün kendisi — <b>Docker GEREKTİRMEZ.</b>
///
/// Bu testlerin varlık sebebi: iki entegrasyon testi uzun süredir
/// "PostgreSQL üretilen DDL'i reddetti" diye düşüyordu ve rapor yanlıştı —
/// üretilen DDL geçerliydi, onu parçalara ayıran test yardımcısı bozuktu.
/// Bölücü Docker'sız test edilebilir olsaydı bu, saatler süren bir
/// entegrasyon koşusu yerine milisaniyelerde görülürdü.
/// </summary>
public class SqlStatementSplitterTests
{
    [Fact]
    public void Plain_statements_are_split_on_semicolons()
    {
        var statements = SqlStatementSplitter.Split("CREATE TABLE a (x int); CREATE TABLE b (y int);");

        Assert.Equal(2, statements.Count);
        Assert.StartsWith("CREATE TABLE a", statements[0]);
        Assert.StartsWith("CREATE TABLE b", statements[1]);
    }

    [Fact]
    public void A_dollar_quoted_function_body_stays_in_one_piece()
    {
        // ASIL HATA BUYDU: gövdedeki noktalı virgüller `RETURN NEW;` satırını
        // ayrı bir ifade sanıyordu ve Postgres haklı olarak sözdizimi hatası
        // veriyordu.
        const string ddl = """
            CREATE OR REPLACE FUNCTION audit_order() RETURNS TRIGGER AS $$
            BEGIN
              RAISE NOTICE 'order %', NEW."Id";
              RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER t AFTER INSERT ON "Orders" FOR EACH ROW EXECUTE FUNCTION audit_order();
            """;

        var statements = SqlStatementSplitter.Split(ddl);

        Assert.Equal(2, statements.Count);
        Assert.Contains("RETURN NEW;", statements[0]);
        Assert.Contains("LANGUAGE plpgsql", statements[0]);
        Assert.StartsWith("CREATE TRIGGER", statements[1]);
    }

    [Fact]
    public void A_named_dollar_tag_is_honoured()
    {
        var statements = SqlStatementSplitter.Split(
            "CREATE FUNCTION f() RETURNS int AS $body$ BEGIN; RETURN 1; END; $body$ LANGUAGE plpgsql;");

        Assert.Single(statements);
    }

    [Fact]
    public void A_semicolon_inside_a_string_literal_does_not_split()
    {
        var statements = SqlStatementSplitter.Split("CREATE TABLE a (x text DEFAULT 'a;b');");

        Assert.Single(statements);
        Assert.Contains("'a;b'", statements[0]);
    }

    [Fact]
    public void An_escaped_quote_does_not_end_the_literal()
    {
        var statements = SqlStatementSplitter.Split("INSERT INTO a VALUES ('it''s; fine');");

        Assert.Single(statements);
    }

    [Fact]
    public void A_semicolon_in_a_line_comment_does_not_split()
    {
        var statements = SqlStatementSplitter.Split("CREATE TABLE a (\n  x int -- note; here\n);");

        Assert.Single(statements);
    }

    [Fact]
    public void Comment_only_fragments_are_dropped()
    {
        var statements = SqlStatementSplitter.Split("-- Trigger: trg (After Insert)\nCREATE TABLE a (x int);");

        Assert.Single(statements);
        Assert.Contains("CREATE TABLE a", statements[0]);
    }
}
