using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Security;
using Namines.Infrastructure.Security;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// <see cref="DbIntrospectionService.ExtractHosts"/> / <see cref="DbIntrospectionService.FindDisallowedHost"/>.
///
/// <b>Bulunan açık:</b> bir bağlantı dizesi birden fazla host adayı taşıyabiliyor
/// (yinelenen anahtar: <c>Host=a;Host=b</c>, ya da Npgsql'in çoklu-host söz
/// dizimi: <c>Host=a,b</c>). Eski <c>ExtractHost</c> yalnızca İLK eşleşen değeri
/// döndürüyordu ve SSRF guard'ı yalnızca onu doğruluyordu. Ama sürücüler
/// (Npgsql, MySqlConnector, <c>DbConnectionParts.Parse</c>) yinelenen anahtarda
/// SON değeri kullanıyor — yani "izin verilen" bir host'la guard'ı geçip
/// gerçekte "izin verilmeyen" bir host'a bağlanmak mümkündü, hiç DNS'e
/// dokunmadan.
///
/// Bu testler gerçek <see cref="DbHostAccessPolicy"/>'yi (sahte değil)
/// kullanıyor — allowlist mantığının kendisi de teste dahil olsun diye.
/// </summary>
public class DbIntrospectionHostExtractionTests
{
    private sealed class FakeEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    // IP DEĞİŞMEZLERİ kasıtlı: `IsHostSafe`/`IsHostPrivate` bir ana bilgisayar
    // adı için GERÇEK DNS çözümlemesi yapıyor (SsrfGuard.cs). Bir test etki
    // alanı adı kullansaydı, testin sonucu o anki ağ/DNS durumuna (ya da CI'da
    // hiç ağ olmamasına) bağlı kalırdı. IP değişmezleri `IPAddress.TryParse`
    // ile anında çözülüyor, ağa hiç çıkmıyor.
    private const string AllowedIp = "93.184.216.34";   // IANA örnek ayırma — herkese açık aralık.
    private const string BlockedIp = "169.254.169.254"; // Link-local — bulut metadata SSRF hedefi klasik örneği.

    /// <summary>Yalnızca <see cref="AllowedIp"/>'ye izin veren bir allowlist politikası.</summary>
    private static IDbHostAccessPolicy AllowlistPolicy()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:DbEgress:AllowedHosts"] = AllowedIp,
            })
            .Build();
        return new DbHostAccessPolicy(new FakeEnv(), config, NullLogger<DbHostAccessPolicy>.Instance);
    }

    [Fact]
    public void Tek_host_normal_calisiyor()
    {
        var hosts = DbIntrospectionService.ExtractHosts("Host=allowed.example.com;Database=x", "PostgreSQL");
        Assert.Equal(new[] { "allowed.example.com" }, hosts);
    }

    [Fact]
    public void Yinelenen_Host_anahtarindaki_TUM_adaylar_bulunuyor()
    {
        var hosts = DbIntrospectionService.ExtractHosts(
            "Host=allowed.example.com;Database=x;Host=169.254.169.254", "PostgreSQL");

        Assert.Equal(new[] { "allowed.example.com", "169.254.169.254" }, hosts);
    }

    [Fact]
    public void Virgulle_ayrilmis_coklu_host_TUMU_bulunuyor()
    {
        // Npgsql çoklu-host/failover söz dizimi: "Host=a,b".
        var hosts = DbIntrospectionService.ExtractHosts(
            "Host=allowed.example.com,169.254.169.254;Database=x", "PostgreSQL");

        Assert.Equal(new[] { "allowed.example.com", "169.254.169.254" }, hosts);
    }

    [Fact]
    public void Port_ve_yol_kirpiliyor_MSSQL_ve_Oracle_bicimleri()
    {
        Assert.Equal(new[] { "db.example.com" },
            DbIntrospectionService.ExtractHosts("Server=db.example.com,1433;Database=x", "MSSQL"));
        Assert.Equal(new[] { "db.example.com" },
            DbIntrospectionService.ExtractHosts("Data Source=db.example.com:1521/ORCL", "Oracle"));
    }

    [Fact]
    public void ONCEKI_ACIK_ilk_host_izinli_ikinci_degilken_ARTIK_REDDEDILIYOR()
    {
        // Tam olarak raporlanan saldırı: guard'ı geçecek "masum" bir ilk host,
        // ardından sürücünün gerçekte kullanacağı (yinelenen anahtarda SON
        // gelen) özel/ayrılmış host.
        var policy = AllowlistPolicy();
        var cs = $"Host={AllowedIp};Database=x;Host={BlockedIp};Username=u;Password=p";

        var denyReason = DbIntrospectionService.FindDisallowedHost(policy, cs, "PostgreSQL");

        Assert.NotNull(denyReason);
        Assert.Contains(BlockedIp, denyReason);
    }

    [Fact]
    public void Virgulle_ayrilmis_formdaki_ayni_saldiri_da_reddediliyor()
    {
        var policy = AllowlistPolicy();
        var cs = $"Host={AllowedIp},{BlockedIp};Database=x;Username=u;Password=p";

        var denyReason = DbIntrospectionService.FindDisallowedHost(policy, cs, "PostgreSQL");

        Assert.NotNull(denyReason);
    }

    [Fact]
    public void Butun_adaylar_izinliyse_kabul_ediliyor()
    {
        var policy = AllowlistPolicy();
        var cs = $"Host={AllowedIp};Database=x;Username=u;Password=p";

        Assert.Null(DbIntrospectionService.FindDisallowedHost(policy, cs, "PostgreSQL"));
    }

    [Fact]
    public void Host_hic_bulunamazsa_GUVENLI_VARSAYILAN_reddediliyor()
    {
        // Boş liste "hepsi geçti" demek DEĞİL — foreach'in hiç dönmemesi
        // yanlışlıkla "izinli" sonucuna düşmemeli.
        var policy = AllowlistPolicy();

        var denyReason = DbIntrospectionService.FindDisallowedHost(policy, "Database=x;Username=u", "PostgreSQL");

        Assert.NotNull(denyReason);
    }

    [Fact]
    public void ExtractHost_tekil_hala_ILK_adayi_donduruyor_SSRF_disi_kullanim_icin()
    {
        // Geriye uyumluluk: TLS kararı gibi SSRF-dışı kullanım için tek host
        // yeterli. Bu metot SSRF izin kontrolü için KULLANILMAMALI.
        var host = DbIntrospectionService.ExtractHost("Host=a.example.com;Host=b.example.com", "PostgreSQL");
        Assert.Equal("a.example.com", host);
    }
}
