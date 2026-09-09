using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Namines.Vault.Providers;

/// <summary>
/// MySQL ve MariaDB yedek/geri yükleme — <c>mysqldump</c> / <c>mysql</c>.
///
/// <b>Tek sınıf, iki motor:</b> ikisi de aynı istemci araçlarını taşıyor ve
/// aynı protokolü konuşuyor; aralarındaki tek fark imaj ve motorun adı. İki
/// ayrı sınıf yazmak, aynı komut dizisini iki yerde bakmak olurdu. Yine de
/// <b>imajlar ayrı</b>: MySQL 8 istemcisiyle MariaDB'ye (ya da tersine) dump
/// almak, kimlik doğrulama eklentisi ve sürüm-özel <c>SET</c> deyimleri
/// yüzünden sessizce eksik/bozuk çıktı verebiliyor.
///
/// <b><c>--protocol=TCP</c> her komutta var</b> ve bu bilinçli: bu depoda
/// daha önce ölçüldü — MySQL imajı ilk açılışta yalnızca unix soketini
/// dinleyen GEÇİCİ bir sunucu başlatıyor, soket üzerinden yapılan hazırlık
/// kontrolü onu "hazır" görüyor ve hemen ardından asıl sunucu yeniden
/// başlarken soket kayboluyor. TCP'yi yalnızca asıl sunucu dinliyor.
/// </summary>
public sealed class MySqlFamilyBackupProvider : ContainerBackupProvider
{
    private readonly string _engine;
    private readonly string _image;
    private readonly bool _hasColumnStatisticsFlag;

    /// <param name="engine">
    /// Kayıtta saklanan motor adı ("MySQL" / "MariaDB"). Geri yükleme sırasında
    /// doğru sağlayıcının seçilmesi buna bağlı.
    /// </param>
    /// <param name="hasColumnStatisticsFlag">
    /// İstemcinin <c>--column-statistics</c> bayrağını TANIYIP tanımadığı.
    /// MySQL 8 istemcisinde var, MariaDB istemcisinde YOK — MariaDB'ye
    /// verildiğinde dump daha ilk adımda
    /// "unknown variable 'column-statistics=0'" ile düşüyor (bu depoda ölçüldü).
    /// </param>
    public MySqlFamilyBackupProvider(
        string engine, string image, bool hasColumnStatisticsFlag, ILogger logger)
        : base(logger)
    {
        _engine = engine;
        _image = image;
        _hasColumnStatisticsFlag = hasColumnStatisticsFlag;
    }

    public override string Engine => _engine;

    protected override string Image => _image;

    protected override int DefaultPort => 3306;

    protected override string ToolNames => "mysqldump/mysql";

    /// <summary>
    /// Parola ortam değişkeniyle geçiyor.
    ///
    /// <c>-p&lt;parola&gt;</c> biçimi bilerek KULLANILMIYOR: komut satırı
    /// konteyner meta verisinde kalıcı olarak durur ve <c>docker inspect</c>
    /// ile okunabilir. <c>MYSQL_PWD</c> ikisinde de destekleniyor.
    /// </summary>
    internal override IList<string> BuildEnvironment(DbConnectionParts conn) =>
        new List<string> { $"MYSQL_PWD={conn.Password}" };

    internal override IList<string> BuildDumpCommand(DbConnectionParts conn)
    {
        var command = new List<string>
        {
            "mysqldump", "--protocol=TCP",
            "-h", conn.ContainerVisibleHost, "-P", conn.Port.ToString(), "-u", conn.Username,
            // --single-transaction: InnoDB'de tabloları KİLİTLEMEDEN tutarlı bir
            // anlık görüntü alır. Olmadan mysqldump tabloları kilitler ve yedek
            // süresince kullanıcının uygulaması yazma yapamaz.
            "--single-transaction",
            // --routines/--triggers/--events: varsayılan olarak DIŞARIDA kalıyorlar.
            // Alınmazsa geri yükleme "başarılı" görünür ama saklı yordamlar ve
            // tetikleyiciler sessizce kaybolur — en kötü türden veri kaybı.
            "--routines", "--triggers", "--events",
            // --add-drop-table: geri yükleme "üzerine yaz" olabilsin diye.
            "--add-drop-table",
        };

        // MySQL 8 istemcisi varsayılan olarak information_schema.COLUMN_STATISTICS
        // sorguluyor; eski MySQL sunucularında o tablo YOK ve dump en başta hata
        // veriyor. Bayrak yalnızca onu TANIYAN istemciye veriliyor — MariaDB
        // istemcisi bayrağın kendisini bilmiyor ve reddediyor.
        if (_hasColumnStatisticsFlag) command.Add("--column-statistics=0");

        command.Add(conn.Database);
        return command;
    }

    internal override IList<string> BuildRestoreCommand(DbConnectionParts conn) => new List<string>
    {
        // Kabuk şart: mysql istemcisinin dosyadan okuması yalnızca yönlendirmeyle
        // (`<`) mümkün, dump yolunu argüman olarak almıyor.
        //
        // Kabuk devreye girdiği an, bağlantı dizesinden gelen değerler artık ham
        // metin değil KOMUT parçası; tırnaklanmazsa kullanıcı adındaki bir
        // `;` ya da `$(...)` konteynerin içinde komut çalıştırırdı.
        "sh", "-c",
        $"mysql --protocol=TCP -h {Quote(conn.ContainerVisibleHost)} -P {conn.Port} " +
        $"-u {Quote(conn.Username)} {Quote(conn.Database)} < {ContainerDumpPath}",
    };

    /// <summary>
    /// Bir değeri kabuk için güvenli hâle getirir: tek tırnak içinde hiçbir şey
    /// yorumlanmaz; tek tırnağın kendisi dizeyi kapatıp yeniden açarak kaçırılır.
    /// </summary>
    private static string Quote(string value) => $"'{value.Replace("'", @"'\''")}'";

    /// <summary>
    /// MariaDB imajı <c>MARIADB_*</c> adlarını tercih ediyor ama <c>MYSQL_*</c>
    /// karşılıklarını da kabul ediyor; ikisini birden vermek yerine ortak olanı
    /// kullanmak tek bir kod yolu bırakıyor.
    /// </summary>
    protected override IList<string> BuildVerifyServerEnvironment() => new List<string>
    {
        $"MYSQL_ROOT_PASSWORD={VerifyPassword}",
        $"MYSQL_DATABASE={VerifyDatabase}",
    };

    protected override IList<string> BuildVerifyReadinessCommand() => new List<string>
    {
        // Ping değil GERÇEK bir sorgu: mysqladmin ping, kimlik doğrulaması
        // hazır olmadan da 0 dönebiliyor. Kimlik doğrulamalı bir SELECT,
        // sunucunun gerçekten kullanılabilir olduğunu kanıtlar.
        "sh", "-c",
        $"MYSQL_PWD={VerifyPassword} mysql --protocol=TCP -h {VerifyHost} -u root -e 'SELECT 1'",
    };

    protected override IList<string> BuildVerifyRestoreCommand() => new List<string>
    {
        // Bayrak yok: mysql istemcisi ilk hatada zaten durur ve sıfırdan farklı
        // çıkış kodu döner — doğrulamanın dayandığı davranış bu.
        "sh", "-c",
        $"MYSQL_PWD={VerifyPassword} mysql --protocol=TCP -h {VerifyHost} -u root " +
        $"{VerifyDatabase} < {ContainerDumpPath}",
    };
}
