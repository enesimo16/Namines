using System.Globalization;
using Namines.Core.Analysis;

namespace Namines.Tests.Services;

/// <summary>
/// CSV üretimi (08 §2 <c>/export</c>).
///
/// Bu testlerin tamamı tek bir arıza sınıfını kovalıyor: <b>sessizce bozulan
/// dosya.</b> Kaçırılmayan bir virgül, sütun sayısını değiştirir; kültüre bağlı
/// bir ondalık ayırıcı yeni bir sütun açar. İkisi de hata vermez — dosyayı açan
/// kişi veriler kaymış hâlde bulur ve nedenini anlamaz.
/// </summary>
public class CsvWriterTests
{
    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows(
        params Dictionary<string, object?>[] rows) => rows;

    [Fact]
    public void An_empty_result_produces_an_empty_file()
    {
        // Yalnızca başlık satırı yazmak, "0 satır" ile "1 boş satır"ı karıştırırdı.
        Assert.Equal(string.Empty, CsvWriter.Write(Array.Empty<IReadOnlyDictionary<string, object?>>()));
    }

    [Fact]
    public void Headers_come_first_and_values_follow_in_the_same_order()
    {
        var csv = CsvWriter.Write(Rows(
            new() { ["id"] = 1, ["name"] = "ali" },
            new() { ["id"] = 2, ["name"] = "veli" }));

        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("id,name", lines[0]);
        Assert.Equal("1,ali", lines[1]);
        Assert.Equal("2,veli", lines[2]);
    }

    [Fact]
    public void A_row_missing_a_column_still_lines_up()
    {
        // Satır başına anahtar sırasına güvenilseydi, eksik kolonlu bir satırda
        // değerler bir sütun kayardı.
        var csv = CsvWriter.Write(Rows(
            new() { ["id"] = 1, ["name"] = "ali" },
            new() { ["id"] = 2 }));

        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("2,", lines[2]);
    }

    [Theory]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    [InlineData("a\nb", "\"a\nb\"")]
    [InlineData(" a", "\" a\"")]
    [InlineData("a ", "\"a \"")]
    [InlineData("plain", "plain")]
    public void Fields_are_escaped_when_they_would_break_the_shape(string input, string expected)
    {
        Assert.Equal(expected, CsvWriter.Escape(input));
    }

    [Fact]
    public void Numbers_use_the_invariant_culture()
    {
        // Türkçe kültürde ondalık ayırıcı virgüldür: 12,5 yazmak CSV'de YENİ BİR
        // SÜTUN açar ve dosya sessizce bozulur.
        var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
        System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("tr-TR");
        try
        {
            Assert.Equal("12.5", CsvWriter.Format(12.5m));
            Assert.Equal("12.5", CsvWriter.Format(12.5d));
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Dates_are_iso8601()
    {
        // Yerel biçim, dosyayı başka bir makinede açan için belirsizdir (03/04 hangisi ay?).
        var value = CsvWriter.Format(new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc));
        Assert.StartsWith("2026-03-04T05:06:07", value);
    }

    [Fact]
    public void Nulls_become_empty_and_booleans_are_lowercase()
    {
        Assert.Equal(string.Empty, CsvWriter.Format(null));
        Assert.Equal(string.Empty, CsvWriter.Format(DBNull.Value));
        Assert.Equal("true", CsvWriter.Format(true));
        Assert.Equal("false", CsvWriter.Format(false));
    }

    [Fact]
    public void Binary_values_are_base64_not_a_type_name()
    {
        // ToString() bir byte[] için "System.Byte[]" verir; bu, veriyi sessizce yok eder.
        Assert.Equal("AQID", CsvWriter.Format(new byte[] { 1, 2, 3 }));
    }
}
