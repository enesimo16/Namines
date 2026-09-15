using Namines.Core.Analysis;
using Xunit;

namespace Namines.Tests.Analysis;

public class SchemaScopePlanTests
{
    private const string ValidJson = """
    {
      "schemaName": "Shop",
      "domains": [
        { "name": "Identity", "tables": ["users", "roles"] },
        { "name": "Catalog",  "tables": ["products", "categories", "variants"] }
      ]
    }
    """;

    [Fact]
    public void Gecerli_plan_ayristiriliyor()
    {
        var plan = SchemaScopePlan.TryParse(ValidJson);

        Assert.NotNull(plan);
        Assert.Equal("Shop", plan!.SchemaName);
        Assert.Equal(2, plan.Domains.Count);
        Assert.Equal(5, plan.TableCount);
        Assert.Equal(new[] { "users", "roles", "products", "categories", "variants" }, plan.AllTableNames);
    }

    [Fact]
    public void Kod_blogu_icine_sarilmis_JSON_de_ayristiriliyor()
    {
        // Model talimata ragmen ```json ... ``` sarabiliyor; plan turunu bu
        // yuzden kaybetmek, opsiyonel bir adimi kirilgan yapardi.
        var plan = SchemaScopePlan.TryParse("```json\n" + ValidJson + "\n```");

        Assert.NotNull(plan);
        Assert.Equal(5, plan!.TableCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bu JSON degil")]
    [InlineData("{}")]
    [InlineData("""{"schemaName":"X","domains":[]}""")]
    [InlineData("""{"schemaName":"X","domains":[{"name":"D","tables":[]}]}""")]
    public void Bozuk_veya_bos_plan_null_donuyor(string? json)
    {
        // null = "plan yok" demek; hat bugunku plansiz yoluna duser.
        Assert.Null(SchemaScopePlan.TryParse(json));
    }

    [Fact]
    public void Ayni_tablo_iki_alanda_gecerse_tekillestiriliyor()
    {
        var plan = SchemaScopePlan.TryParse("""
        {"schemaName":"X","domains":[
          {"name":"A","tables":["users","orders"]},
          {"name":"B","tables":["Users","payments"]}]}
        """);

        Assert.NotNull(plan);
        Assert.Equal(3, plan!.TableCount);
    }

    [Fact]
    public void Duz_metne_render_ediliyor()
    {
        var text = SchemaScopePlan.TryParse(ValidJson)!.RenderAsText();

        Assert.Contains("Identity", text);
        Assert.Contains("users", text);
        Assert.Contains("variants", text);
    }

    [Theory]
    [InlineData("users", "t_users")]
    [InlineData("User Roles", "t_user_roles")]
    [InlineData("OrderItems", "t_order_items")]
    public void Tablo_id_si_isimden_deterministik_turetiliyor(string name, string expected)
    {
        Assert.Equal(expected, SchemaIdConvention.TableId(name));
    }

    [Fact]
    public void Kolon_id_si_tablo_ve_kolon_adindan_turetiliyor()
    {
        Assert.Equal("c_order_items_unit_price", SchemaIdConvention.ColumnId("OrderItems", "UnitPrice"));
    }
}
