using Namines.Infrastructure.Services;

namespace Namines.Tests.Services;

/// <summary>
/// Canlı veritabanından okunan ham varsayılan değerin, yeniden derlenebilir bir
/// ifadeye çevrilmesi.
///
/// BULUNMA YERİ: gerçek bir PostgreSQL içe aktarılıp geri derlendiğinde
/// <c>created_at DEFAULT now()</c> ve <c>status DEFAULT 'pending'</c> tamamen
/// kayboluyordu. Kolon <c>NOT NULL</c> kalıp varsayılansız üretildiği için
/// oluşan DDL çalışıyor ama her <c>INSERT</c> patlıyordu.
/// </summary>
public class IntrospectionDefaultTests
{
    [Theory]
    // PostgreSQL tip niteleyicisini atar — 'pending'::character varying başka motorda geçmez.
    [InlineData("'pending'::character varying", "'pending'")]
    [InlineData("now()", "now()")]
    [InlineData("0", "0")]
    // SQL Server varsayılanı parantezle sarar.
    [InlineData("((0))", "0")]
    [InlineData("(getdate())", "getdate()")]
    [InlineData("('TR')", "'TR'")]
    public void Normalizes_engine_specific_wrapping(string raw, string expected)
    {
        Assert.Equal(expected, DbIntrospectionService.NormalizeDefault(raw));
    }

    /// <summary>
    /// Otomatik artan bir kolonun "varsayılanı" gerçek bir varsayılan değil, dizinin
    /// kendisidir. Taşımak <c>SERIAL ... DEFAULT nextval('eski_seq')</c> gibi var olmayan
    /// bir diziye işaret eden DDL üretirdi.
    /// </summary>
    [Theory]
    [InlineData("nextval('customers_id_seq'::regclass)")]
    [InlineData("AUTO_INCREMENT")]
    [InlineData("IDENTITY(1,1)")]
    public void Drops_auto_increment_pseudo_defaults(string raw)
    {
        Assert.Null(DbIntrospectionService.NormalizeDefault(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Treats_missing_default_as_none(string? raw)
    {
        Assert.Null(DbIntrospectionService.NormalizeDefault(raw));
    }

    /// <summary>
    /// Sarmalayıcı olmayan parantezler soyulmamalı: <c>(a)+(b)</c> ilk '(' ile son ')'
    /// arasında görünse de tek bir sarmalayıcı değil, soyulursa ifade bozulur.
    /// </summary>
    [Fact]
    public void Keeps_non_wrapping_parentheses()
    {
        Assert.Equal("(a)+(b)", DbIntrospectionService.NormalizeDefault("(a)+(b)"));
    }
}
