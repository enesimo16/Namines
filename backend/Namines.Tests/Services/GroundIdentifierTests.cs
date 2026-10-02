using System.Linq;
using Namines.Ground.Providers;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// <see cref="LocalPostgresProvider.BuildIdentifier"/> — proje kimliğinden
/// PostgreSQL tanımlayıcısı üretimi.
///
/// <b>Neden ayrı test ediliyor:</b> buradaki bir çakışma, iki farklı projenin
/// AYNI veritabanını paylaşması demek — yani kiracı izolasyonunun sessizce
/// yok olması. Docker gerekmediği için her koşuda çalışıyor.
/// </summary>
public class GroundIdentifierTests
{
    private const int PostgresIdentifierLimit = 63;

    private const string Guid1 = "7f3ab21c-9d44-4e0a-8b1e-2c5d6f7a8b90";

    [Fact]
    public void Canonical_guids_keep_their_legacy_name()
    {
        // Mevcut provizyonlanmış veritabanları bu adla duruyor; değişirse
        // silme/temizleme onları bulamaz ve sahipsiz kalırlar.
        Assert.Equal(
            "namines_db_7f3ab21c_9d44_4e0a_8b1e_2c5d6f7a8b90",
            LocalPostgresProvider.BuildIdentifier("namines_db_", Guid1));
    }

    [Theory]
    [InlineData("7f3ab21c_9d44_4e0a_8b1e_2c5d6f7a8b90")]   // tireler alt çizgi
    [InlineData("7F3AB21C-9D44-4E0A-8B1E-2C5D6F7A8B90")]   // büyük harf
    [InlineData("7f3ab21c.9d44.4e0a.8b1e.2c5d6f7a8b90")]   // nokta
    public void Ids_that_sanitize_to_a_guids_name_do_not_collide_with_it(string lookalike)
    {
        // ASIL SALDIRI: kurbanın GUID'inin tireleri alt çizgiyle değiştirilmiş bir kimlikle
        // açılan proje, kurbanın veritabanını ve rolünü devralırdı.
        Assert.NotEqual(
            LocalPostgresProvider.BuildIdentifier("namines_db_", Guid1),
            LocalPostgresProvider.BuildIdentifier("namines_db_", lookalike));
        Assert.NotEqual(
            LocalPostgresProvider.BuildIdentifier("namines_app_", Guid1),
            LocalPostgresProvider.BuildIdentifier("namines_app_", lookalike));
    }

    [Fact]
    public void Lossy_sanitization_no_longer_merges_distinct_ids()
    {
        var a = LocalPostgresProvider.BuildIdentifier("namines_db_", "a-b");
        var b = LocalPostgresProvider.BuildIdentifier("namines_db_", "a_b");
        var c = LocalPostgresProvider.BuildIdentifier("namines_db_", "A-B");

        Assert.Equal(3, new[] { a, b, c }.Distinct().Count());
        Assert.All(new[] { a, b, c }, n => Assert.True(n.Length <= PostgresIdentifierLimit));
    }

    [Fact]
    public void Never_exceeds_the_postgres_limit()
    {
        var identifier = LocalPostgresProvider.BuildIdentifier("namines_db_", new string('x', 200));
        Assert.True(identifier.Length <= PostgresIdentifierLimit, $"uzunluk {identifier.Length}");
    }

    [Fact]
    public void Long_ids_sharing_a_prefix_do_not_collide()
    {
        // ASIL RİSK: yalnızca kırpmak yetseydi, aynı önekle başlayan iki uzun
        // proje kimliği aynı veritabanına düşerdi — iki kiracı tek veritabanını
        // paylaşırdı. Sonekteki özet bunu engelliyor.
        var shared = new string('a', 80);

        var first = LocalPostgresProvider.BuildIdentifier("namines_db_", shared + "-one");
        var second = LocalPostgresProvider.BuildIdentifier("namines_db_", shared + "-two");

        Assert.NotEqual(first, second);
        Assert.True(first.Length <= PostgresIdentifierLimit);
        Assert.True(second.Length <= PostgresIdentifierLimit);
    }

    [Fact]
    public void Same_id_always_yields_the_same_name()
    {
        // İdempotansın dayanağı: ikinci bir provizyon çağrısı aynı veritabanını
        // bulmalı. Ad her seferinde farklı üretilseydi, "zaten var" kontrolü
        // hiçbir zaman tutmazdı.
        var id = new string('b', 120);
        Assert.Equal(
            LocalPostgresProvider.BuildIdentifier("namines_db_", id),
            LocalPostgresProvider.BuildIdentifier("namines_db_", id));
    }

    [Fact]
    public void Role_and_database_names_stay_distinct()
    {
        // Aynı proje için rol ve veritabanı adı çakışmamalı; önek farkı
        // kırpma sonrasında da korunmalı.
        var id = new string('c', 120);

        Assert.NotEqual(
            LocalPostgresProvider.BuildIdentifier("namines_db_", id),
            LocalPostgresProvider.BuildIdentifier("namines_app_", id));
    }

    /// <summary>
    /// Saklanan sağlayıcı adı geçerliyse TÜRETİLMİYOR — kurtarma yolunun dayandığı
    /// davranış (docs/last-review H-7). Türetme kuralı ileride yine değişse bile
    /// mevcut kayıtlar kendi veritabanlarını bulmaya devam etmeli.
    /// </summary>
    [Theory]
    [InlineData("namines_db_7f3ab21c")]
    [InlineData("namines_db_a")]
    [InlineData("namines_app_x_9c1f0e33")]
    public void Saklanan_gecerli_ad_AYNEN_kullaniliyor(string stored)
    {
        Assert.Equal(stored, LocalPostgresProvider.ReuseOrDerive(stored, "namines_db_", "proj-1"));
    }

    /// <summary>
    /// Bozuk ya da boş saklanan ad türetmeye düşüyor — yönetici bağlantısında
    /// <c>CREATE/DROP DATABASE</c> içine giren tek yer burası, ve türetilen ad
    /// her zaman geçerli. Reddetmek yerine düşmek bilinçli: en kötü durumda
    /// bugünkü (türetilmiş) davranışa dönülüyor, uç noktanın patlamasına değil.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Namines_DB_1")]          // buyuk harf: PostgreSQL'de alintilanmadan farkli ad
    [InlineData("namines_db_1; DROP")]    // bosluk ve noktali virgul
    [InlineData("namines-db-1")]          // tire
    [InlineData("1namines")]              // rakamla baslamak
    [InlineData("namines_db_\"x")]
    public void Bozuk_saklanan_ad_TURETMEYE_dusuyor(string? stored)
    {
        var derived = LocalPostgresProvider.ReuseOrDerive(stored, "namines_db_", "proj-1");

        Assert.Equal(LocalPostgresProvider.BuildIdentifier("namines_db_", "proj-1"), derived);
        Assert.NotEqual(stored, derived);
    }
}
