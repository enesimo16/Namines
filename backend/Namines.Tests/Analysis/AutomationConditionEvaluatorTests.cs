using Namines.Core.Analysis;
using Xunit;

namespace Namines.Tests.Analysis;

public class AutomationConditionEvaluatorTests
{
    private static AutomationTriggerContext Ctx(string? table = "orders", string? column = "user_id", string? type = "INT")
        => new(table, column, type);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]
    public void Bos_kosul_listesi_her_zaman_eslesiyor(string? json)
    {
        Assert.True(AutomationConditionEvaluator.Matches(json, Ctx()));
    }

    [Fact]
    public void Bozuk_json_kosulsuz_sayiliyor()
    {
        // Sessizce "hiç eşleşme" demek kuralı ölü hâle getirirdi ve bunu fark
        // etmek fazladan çalışmasını fark etmekten çok daha zor olurdu.
        Assert.True(AutomationConditionEvaluator.Matches("{bozuk", Ctx()));
    }

    [Theory]
    [InlineData("equals", "user_id", true)]
    [InlineData("equals", "USER_ID", true)]       // büyük/küçük harf duyarsız
    [InlineData("equals", "other", false)]
    [InlineData("notEquals", "other", true)]
    [InlineData("notEquals", "user_id", false)]
    [InlineData("contains", "ser", true)]
    [InlineData("contains", "zzz", false)]
    [InlineData("startsWith", "user", true)]
    [InlineData("startsWith", "id", false)]
    [InlineData("endsWith", "_id", true)]
    [InlineData("endsWith", "user", false)]
    public void Operatorler(string op, string value, bool expected)
    {
        var json = $$"""[{"field":"columnName","op":"{{op}}","value":"{{value}}"}]""";
        Assert.Equal(expected, AutomationConditionEvaluator.Matches(json, Ctx()));
    }

    [Fact]
    public void Kosullar_arasinda_VE_mantigi_var()
    {
        var json = """
            [{"field":"tableName","op":"equals","value":"orders"},
             {"field":"columnName","op":"endsWith","value":"_id"}]
            """;
        Assert.True(AutomationConditionEvaluator.Matches(json, Ctx()));

        var failing = """
            [{"field":"tableName","op":"equals","value":"orders"},
             {"field":"columnName","op":"endsWith","value":"_at"}]
            """;
        Assert.False(AutomationConditionEvaluator.Matches(failing, Ctx()));
    }

    [Fact]
    public void Kolon_tipi_uzerinden_kosul_kurulabiliyor()
    {
        var json = """[{"field":"columnType","op":"equals","value":"int"}]""";
        Assert.True(AutomationConditionEvaluator.Matches(json, Ctx(type: "INT")));
    }

    [Fact]
    public void Bu_tetikleyicide_karsiligi_olmayan_alan_kosulu_dusuruyor()
    {
        // Tablo olayında columnName yok. Yok saymak, kuralı kullanıcının
        // sandığından daha geniş tetiklerdi.
        var json = """[{"field":"columnName","op":"equals","value":"x"}]""";
        Assert.False(AutomationConditionEvaluator.Matches(json, Ctx(column: null)));
    }

    [Fact]
    public void Taninmayan_alan_ve_operator_kosulu_dusuruyor()
    {
        Assert.False(AutomationConditionEvaluator.Matches("""[{"field":"nope","op":"equals","value":"x"}]""", Ctx()));
        Assert.False(AutomationConditionEvaluator.Matches("""[{"field":"columnName","op":"regex","value":".*"}]""", Ctx()));
    }
}
