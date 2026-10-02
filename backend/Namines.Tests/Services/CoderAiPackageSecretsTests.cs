using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Namines.Core.Enums;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// CoderAI paketlerinin sırları ve ağ yüzeyi — docs/last-review M-17.
///
/// <b>Bulunan açık:</b> üretilen her pakete AYNI sabit parola
/// (<c>Namines_Secure123!</c>) gömülüyordu ve o parola bu depoda açık yazılı — yani
/// her Namines kullanıcısının ürettiği yığın, herkesin bildiği bir veritabanı
/// parolasını taşıyordu. Üstelik <c>ports: "5432:5432"</c> biçimi portu host'un
/// <b>tüm arayüzlerine</b> (0.0.0.0) açıyor: genel IP'si olan bir makinede bu,
/// veritabanını bilinen bir parolayla internete açmak demekti.
/// </summary>
public sealed class CoderAiPackageSecretsTests : IDisposable
{
    private readonly string _cwd = Directory.GetCurrentDirectory();
    private readonly string _sandbox =
        Path.Combine(Path.GetTempPath(), "namines-m17-" + Guid.NewGuid().ToString("N"));

    public CoderAiPackageSecretsTests()
    {
        // Paketleyici çıktıyı `Directory.GetCurrentDirectory()/Outputs` altına
        // yazıyor; test dizinini kirletmemek için çalışma dizini geçici bir yere
        // alınıyor ve sonunda geri konuyor.
        Directory.CreateDirectory(_sandbox);
        Directory.SetCurrentDirectory(_sandbox);
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_cwd);
        try { Directory.Delete(_sandbox, recursive: true); } catch { /* temizlik testi bozmasin */ }
    }

    private static async Task<string> PackageAsync(DatabaseType dbType)
    {
        var service = new CoderAIPackagerService();
        return await service.PackageAsZipAsync(
            appPyContent: "import os\nprint(os.environ['DATABASE_URL'])\n",
            sqlContent: "CREATE TABLE t (id INT);",
            dbType: dbType,
            projectName: "test");
    }

    private static string ReadEntry(string zipPath, string entryName)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.FirstOrDefault(e =>
            string.Equals(e.Name, entryName, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(entry);
        using var reader = new StreamReader(entry!.Open());
        return reader.ReadToEnd();
    }

    [Theory]
    [InlineData(DatabaseType.PostgreSQL)]
    [InlineData(DatabaseType.MySQL)]
    [InlineData(DatabaseType.MSSQL)]
    public async Task Paket_SABIT_parolayi_ARTIK_tasimiyor(DatabaseType dbType)
    {
        var zipPath = await PackageAsync(dbType);

        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            var content = await reader.ReadToEndAsync();
            Assert.DoesNotContain("Namines_Secure123", content);
        }
    }

    [Theory]
    [InlineData(DatabaseType.PostgreSQL)]
    [InlineData(DatabaseType.MySQL)]
    [InlineData(DatabaseType.MSSQL)]
    public async Task Veritabani_portu_YALNIZCA_loopbacke_baglaniyor(DatabaseType dbType)
    {
        var compose = ReadEntry(await PackageAsync(dbType), "docker-compose.yml");

        // Asıl güvenlik sınırı bu satır: 0.0.0.0'a bağlı bir port, genel IP'si
        // olan makinede veritabanını internete açar.
        Assert.Contains("127.0.0.1:", compose);
        Assert.DoesNotContain("\"5432:5432\"", compose);
        Assert.DoesNotContain("\"3306:3306\"", compose);
        Assert.DoesNotContain("\"1433:1433\"", compose);
        Assert.DoesNotContain("\"8501:8501\"", compose);
    }

    [Fact]
    public async Task Her_paket_FARKLI_parola_aliyor()
    {
        // Sabit parolanın asıl zararı buydu: bir paketi ele geçiren, hepsini
        // ele geçiriyordu.
        var first = ReadEntry(await PackageAsync(DatabaseType.PostgreSQL), ".env");
        var second = ReadEntry(await PackageAsync(DatabaseType.PostgreSQL), ".env");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Parola_compose_yerine_env_dosyasinda()
    {
        var zipPath = await PackageAsync(DatabaseType.PostgreSQL);

        var env = ReadEntry(zipPath, ".env");
        var compose = ReadEntry(zipPath, "docker-compose.yml");

        Assert.Contains("DB_PASSWORD=", env);
        Assert.Contains("DATABASE_URL=", env);
        // Compose yalnızca DEĞİŞKENE atıf yapıyor, değeri taşımıyor.
        Assert.Contains("${DB_PASSWORD}", compose);
    }

    [Fact]
    public async Task Parola_URL_guvenli_karakterlerden_olusuyor()
    {
        // Parola bir SQLAlchemy URL'sinin içine giriyor; `@`, `:`, `/` orada
        // ayırıcı. Üretilen paketin ilk denemede çalışması buna bağlı.
        var env = ReadEntry(await PackageAsync(DatabaseType.PostgreSQL), ".env");

        var line = env.Split('\n').First(l => l.StartsWith("DB_PASSWORD="));
        var password = line["DB_PASSWORD=".Length..].Trim();

        Assert.Equal(32, password.Length);
        Assert.All(password, c => Assert.True(char.IsAsciiLetterOrDigit(c), $"URL-guvenli olmayan karakter: {c}"));
    }

    [Fact]
    public async Task Env_dosyasi_gitignorede()
    {
        // Paketi bir depoya koyan kullanıcı parolayı kazara yayınlamasın.
        var gitignore = ReadEntry(await PackageAsync(DatabaseType.PostgreSQL), ".gitignore");

        Assert.Contains(".env", gitignore);
    }
}
