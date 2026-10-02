using System;
using System.IO;
using System.Threading.Tasks;
using Namines.Core.Enums;
using Namines.Core.Security;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// <see cref="DatabaseExecutorService"/> — SQLite yoluyla sunucuda dosya oluşturma.
///
/// <b>Bulunan açık (docs/last-review H-6):</b> SQLite'ın "bağlantı dizesi" bir ağ
/// hedefi değil, <b>sunucudaki bir dosya yolu</b>. <c>ExtractHosts</c> SQLite için
/// boş dönüyordu ve boş liste üzerindeki <c>foreach</c> hiç çalışmadığı için
/// <c>ValidateConnectionTarget</c> <b>fail-open</b>'dı — ortak
/// <c>DbIntrospectionService.FindDisallowedHost</c> ise aynı durumda fail-closed.
/// Ham dize doğrudan <c>new SqliteConnection(...)</c>'a gidiyor ve <c>Mode</c>
/// varsayılanı <c>ReadWriteCreate</c>: kimliği doğrulanmış herhangi bir kullanıcı
/// sunucuda istediği yolda dosya oluşturup içine kendi betiğiyle içerik
/// yazabiliyordu.
///
/// Bu testler gerçek bir veritabanı gerektirmiyor: iddia, bağlantının hiç
/// AÇILMAMASI ve dosyanın hiç OLUŞMAMASI.
/// </summary>
public sealed class ExecutorSqliteFileCreationTests : IDisposable
{
    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), "namines-h6-" + Guid.NewGuid().ToString("N"));

    public ExecutorSqliteFileCreationTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch { /* temizlik testi bozmasın */ }
    }

    /// <summary>
    /// Her hostu ONAYLAYAN politika — testin SQLite kapısını ölçtüğünden emin
    /// olmak için. Kapı çalışmıyorsa bu politika hiçbir şeyi engellemez.
    /// </summary>
    private sealed class AllowEverything : IDbHostAccessPolicy
    {
        public bool IsHostAllowed(string? host, out string denyReason)
        {
            denyReason = string.Empty;
            return true;
        }
    }

    private DatabaseExecutorService NewExecutor() => new(new AllowEverything());

    [Fact]
    public async Task SQLite_betigi_sunucuda_dosya_OLUSTURMUYOR()
    {
        var target = Path.Combine(_scratch, "planted.db");
        var executor = NewExecutor();

        var result = await executor.ExecuteScriptAsync(
            $"Data Source={target}", "CREATE TABLE evil (x INTEGER);", DatabaseType.SQLite);

        Assert.False(result.Success);
        Assert.False(File.Exists(target), "Sunucuda dosya olusturuldu — H-6 hala acik.");
        Assert.Equal(0, result.StatementsExecuted);
    }

    [Fact]
    public async Task SQLite_baglanti_testi_de_dosya_OLUSTURMUYOR()
    {
        // TestConnection'ın kendisi de bir yazma yoluydu: SqliteConnection'ı
        // açmak, Mode=ReadWriteCreate ile dosyayı YARATIR.
        var target = Path.Combine(_scratch, "probed.db");
        var executor = NewExecutor();

        var ok = await executor.TestConnectionAsync($"Data Source={target}", DatabaseType.SQLite);

        Assert.False(ok);
        Assert.False(File.Exists(target), "Sunucuda dosya olusturuldu — H-6 hala acik.");
    }

    [Theory]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=../../../etc/namines.db")]
    [InlineData("Data Source=C:\\\\Windows\\\\Temp\\\\x.db;Mode=ReadWriteCreate")]
    public async Task Her_SQLite_dizesi_REDDEDILIYOR(string connectionString)
    {
        // Yolu allowlist'lemek yerine motorun tamamı reddediliyor: bu servisin işi
        // KULLANICININ KENDİ veritabanına bağlanmak ve Namines'in sunucusunda
        // kullanıcıya ait bir dosya sistemi yok.
        var executor = NewExecutor();

        Assert.False(await executor.TestConnectionAsync(connectionString, DatabaseType.SQLite));
    }

    /// <summary>
    /// Host ayıklanamayan bir dizede de fail-closed: "ayıklayamadım" ile
    /// "güvenli" aynı şey değil. Bu, boş aday listesini geçerli sayan aynı
    /// hatanın diğer motorlardaki hâli.
    /// </summary>
    [Theory]
    [InlineData(DatabaseType.PostgreSQL, "Database=x;Username=u;Password=p")]
    [InlineData(DatabaseType.MySQL, "Database=x;Uid=u;Pwd=p")]
    public async Task Host_ayiklanamayan_dize_REDDEDILIYOR(DatabaseType dbType, string connectionString)
    {
        var executor = NewExecutor();

        var result = await executor.ExecuteScriptAsync(connectionString, "SELECT 1;", dbType);

        Assert.False(result.Success);
        Assert.Equal(0, result.StatementsExecuted);
    }
}
