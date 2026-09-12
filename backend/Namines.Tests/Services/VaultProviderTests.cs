using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Vault.Abstractions;
using Namines.Vault.DependencyInjection;
using Namines.Vault.Providers;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// Vault'un motor sağlayıcıları: hangi motorların kayıtlı olduğu ve her birinin
/// KENDİ istemcisinin kabul ettiği komutu ürettiği.
///
/// <b>Neden test ediliyor:</b> komut dizileri yalnızca canlı bir sunucuya karşı
/// çalıştırıldığında patlıyor — yani en erken, kullanıcının ilk yedek
/// denemesinde. Bu depoda tam olarak bu oldu: MariaDB istemcisi
/// <c>--column-statistics</c> bayrağını tanımıyor ve dump daha ilk adımda
/// "unknown variable" ile düştü. Testler o hatayı derleme zamanına yakın bir
/// yere çekiyor.
/// </summary>
public class VaultProviderTests
{
    private static IEnumerable<IBackupProvider> ResolveProviders()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Cipher olmadan Vault AÇIKÇA duruyor; testin ölçtüğü şey o değil.
                ["Vault:BackupEncryptionKey"] = new string('k', 40),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddNaminesVault();

        return services.BuildServiceProvider().GetRequiredService<IEnumerable<IBackupProvider>>();
    }

    /// <summary>
    /// Desteklenen motorlar listesi kayıtlardan TÜRETİLİYOR ve arayüzde bire bir
    /// gösteriliyor. Bir kayıt sessizce düşerse kullanıcı, motoru destekleniyor
    /// sanarak ilk yedek denemesinde öğrenirdi.
    /// </summary>
    [Fact]
    public void Kayitli_motorlar_beklenen_uclu()
    {
        var engines = ResolveProviders().Select(p => p.Engine).OrderBy(e => e).ToArray();

        Assert.Equal(new[] { "MariaDB", "MySQL", "PostgreSQL" }, engines);
    }

    /// <summary>
    /// MariaDB istemcisi <c>--column-statistics</c> bayrağını TANIMIYOR; verilirse
    /// dump hiç başlamadan düşüyor (bu depoda canlı ölçüldü). MySQL 8 istemcisinde
    /// ise bayrak gerekli: yoksa istemci eski sunucularda var olmayan
    /// <c>information_schema.COLUMN_STATISTICS</c>'i sorguluyor.
    /// </summary>
    [Fact]
    public void MariaDb_dump_komutunda_column_statistics_bayragi_olmaz()
    {
        var conn = DbConnectionParts.Parse(
            "Server=db.example.com;Port=3306;Database=uygulama;User ID=kok;Password=gizli;", 3306);

        var mariaDb = new MySqlFamilyBackupProvider(
            "MariaDB", "mariadb:10.6", hasColumnStatisticsFlag: false, NullLogger.Instance);
        var mySql = new MySqlFamilyBackupProvider(
            "MySQL", "mysql:8.0", hasColumnStatisticsFlag: true, NullLogger.Instance);

        Assert.DoesNotContain("--column-statistics=0", mariaDb.BuildDumpCommand(conn));
        Assert.Contains("--column-statistics=0", mySql.BuildDumpCommand(conn));
    }

    /// <summary>
    /// Parola KOMUT SATIRINDA geçmemeli: komut satırı konteyner meta verisinde
    /// kalıcı olarak duruyor ve <c>docker inspect</c> ile okunabiliyor.
    /// </summary>
    [Fact]
    public void Parola_komut_satirina_degil_ortam_degiskenine_yazilir()
    {
        var conn = DbConnectionParts.Parse(
            "Server=db.example.com;Database=uygulama;User ID=kok;Password=cok-gizli;", 3306);

        var provider = new MySqlFamilyBackupProvider(
            "MySQL", "mysql:8.0", hasColumnStatisticsFlag: true, NullLogger.Instance);

        Assert.Contains("MYSQL_PWD=cok-gizli", provider.BuildEnvironment(conn));
        Assert.DoesNotContain(provider.BuildDumpCommand(conn), arg => arg.Contains("cok-gizli"));
    }

    /// <summary>
    /// Geri yükleme komutu bir KABUK üzerinden çalışıyor (mysql istemcisi dump'ı
    /// yalnızca yönlendirmeyle okuyabiliyor). Kabuk devreye girdiği an bağlantı
    /// dizesinden gelen değerler komut parçasına dönüşüyor; tırnaklanmazsa
    /// veritabanı adındaki bir <c>$(...)</c> konteynerin içinde komut çalıştırırdı.
    ///
    /// <b>Noktalı virgül denenmiyor</b> çünkü bağlantı dizesini ayıran karakter
    /// zaten o; ayrıştırmadan sağ çıkan metakarakterler test ediliyor.
    /// </summary>
    [Fact]
    public void Geri_yukleme_komutunda_degerler_kabuk_icin_tirnaklanir()
    {
        var conn = DbConnectionParts.Parse(
            "Server=db;Port=3306;Database=uygulama$(whoami);User ID=kok&&id;Password=gizli;", 3306);

        var provider = new MySqlFamilyBackupProvider(
            "MySQL", "mysql:8.0", hasColumnStatisticsFlag: true, NullLogger.Instance);

        var script = provider.BuildRestoreCommand(conn, tables: null).Last();

        // Tek tırnağın içinde kabuk hiçbir şey yorumlamaz.
        Assert.Contains("'uygulama$(whoami)'", script);
        Assert.Contains("'kok&&id'", script);
    }

    /// <summary>
    /// Değerin KENDİSİ tek tırnak içeriyorsa naif bir tırnaklama, tırnağı erken
    /// kapatıp geri kalanı komut hâline getirirdi — tam olarak kaçınılmak istenen
    /// şey. Tek tırnak, dizeyi kapatıp kaçırıp yeniden açarak geçiriliyor.
    /// </summary>
    [Fact]
    public void Degerin_icindeki_tek_tirnak_kacirilir()
    {
        var conn = DbConnectionParts.Parse(
            "Server=db;Database=o'brien;User ID=kok;Password=gizli;", 3306);

        var provider = new MySqlFamilyBackupProvider(
            "MySQL", "mysql:8.0", hasColumnStatisticsFlag: true, NullLogger.Instance);

        var script = provider.BuildRestoreCommand(conn, tables: null).Last();

        Assert.Contains(@"'o'\''brien'", script);
    }

    /// <summary>
    /// Yedek, API sunucusunun DEĞİL kısa ömürlü bir konteynerin içinden bağlanıyor;
    /// oradaki "localhost" konteynerin kendisi demek. Eşleme yapılmazsa yedek,
    /// kullanıcının veritabanına hiç ulaşamadan boş bir hatayla düşerdi.
    /// </summary>
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void Yerel_adres_konteynerden_gorunur_ada_cevrilir(string host)
    {
        var conn = DbConnectionParts.Parse(
            $"Host={host};Database=uygulama;Username=kok;Password=gizli;", 5432);

        Assert.Equal(DbConnectionParts.HostGatewayName, conn.ContainerVisibleHost);
    }

    /// <summary>
    /// Uzak bir adres OLDUĞU GİBİ kalmalı — eşlemenin her hostu yutması, yedeğin
    /// yanlış sunucudan alınması demek olurdu.
    /// </summary>
    [Fact]
    public void Uzak_adres_oldugu_gibi_kalir()
    {
        var conn = DbConnectionParts.Parse(
            "Host=db.example.com;Database=uygulama;Username=kok;Password=gizli;", 5432);

        Assert.Equal("db.example.com", conn.ContainerVisibleHost);
    }

    /// <summary>
    /// Bağlantı dizesi anahtarları sürücüden sürücüye değişiyor (Npgsql "Username",
    /// MySqlConnector "User ID", SqlClient "Data Source"). Eş anlamlıları tanımayan
    /// bir ayrıştırıcı, geçerli bir bağlantıyı kullanıcı adı boş sanarak reddederdi.
    /// </summary>
    [Theory]
    [InlineData("Server=db;Port=3306;Database=uygulama;User ID=kok;Password=gizli;")]
    [InlineData("Data Source=db;Port=3306;Initial Catalog=uygulama;Uid=kok;Pwd=gizli;")]
    [InlineData("Host=db;Port=3306;Database=uygulama;Username=kok;Password=gizli;")]
    public void Es_anlamli_anahtarlar_ayni_sonucu_verir(string connectionString)
    {
        var conn = DbConnectionParts.Parse(connectionString, 3306);

        Assert.Equal("db", conn.Host);
        Assert.Equal(3306, conn.Port);
        Assert.Equal("uygulama", conn.Database);
        Assert.Equal("kok", conn.Username);
        Assert.Equal("gizli", conn.Password);
    }

    /// <summary>
    /// Port verilmemişse motorun VARSAYILANI kullanılmalı; sıfır port'la kurulan
    /// bağlantı anlamsız bir hata verirdi.
    /// </summary>
    [Fact]
    public void Port_yoksa_motorun_varsayilani_kullanilir()
    {
        var conn = DbConnectionParts.Parse(
            "Host=db;Database=uygulama;Username=kok;Password=gizli;", 5432);

        Assert.Equal(5432, conn.Port);
    }

    /// <summary>
    /// B-43: PostgreSQL <c>-t</c> ile KISMİ geri yükleme.
    ///
    /// Docker gerektirmeyen tek gerçek kanıt bu: komutun bayrakları doğru
    /// üretip üretmediği. Gerçek `pg_restore` çalıştırması (dump'ın verdiği
    /// sonucu Docker/disk kısıtı yüzünden bu depoda canlı doğrulanamadı;
    /// bkz. namines-vault dokümanı) ayrı bir kanıt gerektirir.
    /// </summary>
    [Fact]
    public void Postgres_kismi_geri_yuklemede_t_bayragi_uretilir()
    {
        var conn = DbConnectionParts.Parse(
            "Host=db;Database=uygulama;Username=kok;Password=gizli;", 5432);
        var provider = new PostgresBackupProvider(
            new ConfigurationBuilder().Build(), NullLogger<PostgresBackupProvider>.Instance);

        var full = provider.BuildRestoreCommand(conn, tables: null);
        var partial = provider.BuildRestoreCommand(conn, tables: new[] { "orders", "order_items" });

        Assert.DoesNotContain("-t", full);
        Assert.Contains("-t", partial);
        Assert.Contains("orders", partial);
        Assert.Contains("order_items", partial);
        // --clean KISMİ geri yüklemede de kalmalı: pg_restore -t + --clean
        // yalnızca SEÇİLEN tabloları düşürüp geri yükler (tüm veritabanını
        // değil) — bu davranış olmadan kısmi geri yükleme "ekle" anlamına
        // gelirdi, "üzerine yaz" değil.
        Assert.Contains("--clean", partial);
    }

    /// <summary>
    /// B-43: MySQL/MariaDB kısmi geri yüklemeyi AÇIKÇA reddetmeli.
    ///
    /// Dump düz SQL; seçici uygulamanın tek yolu metni ayrıştırmak, ki bu
    /// yapılmadı. Sessizce TAM geri yükleme yapmak "kullanıcının seçtiği
    /// tabloyu görmezden gel" demek olurdu — bu yüzden reddetmek DOĞRU
    /// davranış ve test onu koruyor.
    /// </summary>
    [Fact]
    public void MySql_kismi_geri_yuklemeyi_acikca_reddeder()
    {
        var conn = DbConnectionParts.Parse(
            "Server=db;Database=uygulama;User ID=kok;Password=gizli;", 3306);
        var provider = new MySqlFamilyBackupProvider(
            "MySQL", "mysql:8.0", hasColumnStatisticsFlag: true, NullLogger.Instance);

        Assert.Throws<NotSupportedException>(
            () => provider.BuildRestoreCommand(conn, tables: new[] { "orders" }));

        // Tablo listesi BOŞSA (tam geri yükleme) hiç sorun olmamalı.
        var full = provider.BuildRestoreCommand(conn, tables: null);
        Assert.NotEmpty(full);
    }
}
