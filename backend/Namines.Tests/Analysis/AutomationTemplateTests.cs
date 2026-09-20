using System;
using Namines.Core.Analysis;
using Xunit;

namespace Namines.Tests.Analysis;

public class AutomationTemplateTests
{
    private static readonly DateTime At = new(2026, 9, 20, 8, 30, 0, DateTimeKind.Utc);

    private static string Render(string template, AutomationTriggerContext? ctx = null) =>
        AutomationTemplate.Render(
            template,
            "ColumnDeleted",
            ctx ?? new AutomationTriggerContext("orders", "user_id", "INT"),
            "Shop",
            At);

    [Fact]
    public void Butun_degiskenleri_dolduruyor()
    {
        var text = Render("{{trigger}} {{tableName}} {{columnName}} {{columnType}} {{projectName}}");
        Assert.Equal("ColumnDeleted orders user_id INT Shop", text);
    }

    [Fact]
    public void Ayni_degisken_birden_fazla_kez_gecebiliyor()
    {
        Assert.Equal("orders/orders", Render("{{tableName}}/{{tableName}}"));
    }

    [Fact]
    public void Degeri_olmayan_degisken_BOS_stringe_cevriliyor()
    {
        // Tablo olayında columnName yok. Yer tutucuyu bırakmak, gönderilen
        // webhook gövdesinde ham "{{columnName}}" metni bırakırdı.
        var text = Render("[{{columnName}}]", new AutomationTriggerContext("orders", null, null));
        Assert.Equal("[]", text);
    }

    [Fact]
    public void Bilinmeyen_degisken_OLDUGU_GIBI_kaliyor()
    {
        // Sessizce silinseydi, "{{tablename}}" yazan kullanıcı mesajın
        // ortasındaki boşluğun sebebini anlayamazdı.
        Assert.Equal("x {{nope}} y", Render("x {{nope}} y"));
    }

    [Fact]
    public void Yer_tutucu_yoksa_metin_degismiyor()
    {
        Assert.Equal("duz metin", Render("duz metin"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Bos_sablon_bos_string_donuyor(string? template)
    {
        Assert.Equal(string.Empty, AutomationTemplate.Render(template, "TableAdded", default, "p", At));
    }

    [Fact]
    public void Zaman_damgasi_sirali_bicimde_yaziliyor()
    {
        // Alıcı sistemlerin ayrıştırabilmesi için yerel biçim değil ISO-8601.
        Assert.Contains("2026-09-20T08:30:00", Render("{{timestamp}}"));
    }

    [Fact]
    public void Sablon_icindeki_degerler_YENIDEN_islenmiyor()
    {
        // Tablo adı "{{projectName}}" olsaydı, ikinci bir geçiş onu proje
        // adına çevirirdi — değer enjeksiyonu. Tek geçişli değiştirme bunu
        // yapısal olarak imkânsız kılıyor.
        var text = Render("{{tableName}}", new AutomationTriggerContext("{{projectName}}", null, null));
        Assert.Equal("{{projectName}}", text);
    }
}
