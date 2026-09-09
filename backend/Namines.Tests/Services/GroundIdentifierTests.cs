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

    [Fact]
    public void Keeps_short_ids_readable()
    {
        // Ada bakan birinin projeyi tanıyabilmesi gerekir; kısa kimlikler
        // olduğu gibi korunuyor.
        Assert.Equal("namines_db_ground_test", LocalPostgresProvider.BuildIdentifier("namines_db_", "ground-test"));
    }

    [Fact]
    public void Replaces_characters_postgres_cannot_take()
    {
        // Tire, nokta ve büyük harf: GUID'lerde ve kullanıcı kimliklerinde
        // sık; tırnaksız bir tanımlayıcıda geçersiz ya da sürprizli.
        Assert.Equal(
            "namines_db_a1b2_c3d4_e5",
            LocalPostgresProvider.BuildIdentifier("namines_db_", "A1B2-c3d4.e5"));
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
}
