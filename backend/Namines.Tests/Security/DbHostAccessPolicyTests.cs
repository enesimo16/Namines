using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Infrastructure.Security;

namespace Namines.Tests.Security;

/// <summary>
/// SSRF gevşetmesinin ÜRETİME SIZAMAYACAĞININ kanıtı. Asıl risk bu — gevşetmenin
/// kendisi değil, yanlış ortamda etkin olması.
/// </summary>
public class DbHostAccessPolicyTests
{
    private sealed class FakeEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static DbHostAccessPolicy Build(string environment, bool? flag, string? allowedHosts = null)
    {
        var settings = new Dictionary<string, string?>();
        if (flag is not null) settings["Security:AllowPrivateDbHosts"] = flag.Value ? "true" : "false";
        if (allowedHosts is not null) settings["Security:DbEgress:AllowedHosts"] = allowedHosts;

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new DbHostAccessPolicy(
            new FakeEnv { EnvironmentName = environment },
            config,
            NullLogger<DbHostAccessPolicy>.Instance);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Private_hosts_stay_blocked_in_non_development_even_with_the_flag_on(string environment)
    {
        var policy = Build(environment, flag: true);

        Assert.False(policy.IsHostAllowed("localhost", out var reason));
        Assert.Contains("not allowed", reason);
        Assert.False(policy.IsHostAllowed("127.0.0.1", out _));
        Assert.False(policy.IsHostAllowed("10.0.0.5", out _));
        Assert.False(policy.IsHostAllowed("169.254.169.254", out _)); // cloud metadata
    }

    [Fact]
    public void Private_hosts_stay_blocked_in_development_when_the_flag_is_absent()
    {
        var policy = Build("Development", flag: null);
        Assert.False(policy.IsHostAllowed("localhost", out _));
    }

    [Fact]
    public void Private_hosts_stay_blocked_in_development_when_the_flag_is_false()
    {
        var policy = Build("Development", flag: false);
        Assert.False(policy.IsHostAllowed("localhost", out _));
    }

    [Fact]
    public void Private_hosts_are_allowed_only_with_both_gates_open()
    {
        var policy = Build("Development", flag: true);

        Assert.True(policy.IsHostAllowed("localhost", out _));
        Assert.True(policy.IsHostAllowed("127.0.0.1", out _));
        Assert.True(policy.IsHostAllowed("192.168.1.10", out _));
    }

    [Fact]
    public void Empty_host_is_always_rejected()
    {
        // Gevşetme açıkken bile: host çıkarılamadıysa bağlanma (fail-closed).
        var policy = Build("Development", flag: true);

        Assert.False(policy.IsHostAllowed(null, out var reason));
        Assert.False(policy.IsHostAllowed("   ", out _));
        Assert.Contains("could not be determined", reason);
    }

    [Fact]
    public void Public_hosts_are_allowed_regardless_of_environment_or_flag()
    {
        // IP literal kullanılıyor: SsrfGuard host ADI verilirse DNS çözümlemesi yapar ve
        // çözülemeyen adı güvenli saymaz (fail-closed, doğru davranış) — testi ağ/DNS
        // durumuna bağımlı hale getirmemek için literal public adres veriyoruz.
        foreach (var env in new[] { "Development", "Production" })
        foreach (var flag in new bool?[] { null, false, true })
        {
            var policy = Build(env, flag);
            Assert.True(policy.IsHostAllowed("8.8.8.8", out _), $"env={env} flag={flag}");
        }
    }

    [Fact]
    public void Unresolvable_hostname_is_rejected_when_the_relaxation_is_off()
    {
        // Fail-closed: DNS çözülemiyorsa bağlanma. (Gevşetme AÇIKKEN geliştiricinin
        // kendi makinesindeki adlara izin verilmesi bilinçli — bkz. diğer testler.)
        var policy = Build("Production", flag: false);
        Assert.False(policy.IsHostAllowed("bu-alan-adi-yok.invalid", out _));
    }

    // -- Egress allowlist (B-27) ----------------------------------------------

    /// <summary>
    /// Allowlist tanimli DEGILSE davranis degismiyor: public adreslere izin var,
    /// ozel adresler reddediliyor. Geriye uyumluluk sart -- aksi halde bu
    /// ozellik mevcut her kurulumu kirardi.
    /// </summary>
    [Fact]
    public void Allowlist_tanimsizsa_public_hedeflere_izin_veriliyor()
    {
        var policy = Build("Production", flag: false, allowedHosts: null);

        Assert.True(policy.IsHostAllowed("example.com", out _));
        Assert.False(policy.IsHostAllowed("10.0.0.5", out _));
    }

    /// <summary>
    /// <b>Bu test B-27'nin ozudur.</b> Allowlist doluysa, PUBLIC bir adres bile
    /// listede degilse reddedilir.
    ///
    /// DNS rebinding'e karsi calisma sekli bu: saldirganin alan adi operatorun
    /// listesinde olmadigi icin, o adin hangi IP'ye cozuldugu -- ve iki cozum
    /// arasinda degisip degismedigi -- artik fark etmiyor.
    /// </summary>
    [Fact]
    public void Allowlist_doluysa_listede_olmayan_public_hedef_reddediliyor()
    {
        var policy = Build("Production", flag: false, allowedHosts: "db.izinli.com");

        Assert.True(policy.IsHostAllowed("db.izinli.com", out _));

        Assert.False(policy.IsHostAllowed("example.com", out var reason));
        Assert.Contains("egress allowlist", reason);
    }

    /// <summary>
    /// Joker eslesmesi -- DNS'ten BAGIMSIZ olarak test ediliyor.
    ///
    /// <b>Ilk hali DNS'e bagliydi ve YANLISTI:</b> `ornek.com` cozulemeyen bir
    /// ornek alan adi oldugu icin testler kirildi. Kod dogruydu, test yanlisti
    /// -- allowlist adres kontrolunu EZMIYOR (bir alt testte bilerek boyle
    /// dogrulaniyor), dolayisiyla cozulemeyen bir ad allowlist'te olsa da
    /// reddediliyor.
    ///
    /// Dogru cozum, esleyiciyi ayri test etmek: aradigimiz sey "bu desen bu
    /// host'u kapsiyor mu", "bu host cozuluyor mu" degil.
    ///
    /// Kok DAHIL: aksi halde operator ayni alan adi icin iki satir yazmak
    /// zorunda kalir ve birini unutmak sessiz bir kesinti uretir.
    /// </summary>
    [Theory]
    [InlineData("*.ornek.com", "db.ornek.com", true)]
    [InlineData("*.ornek.com", "replica.db.ornek.com", true)]
    [InlineData("*.ornek.com", "ornek.com", true)]
    [InlineData("*.ornek.com", "ORNEK.COM", true)]
    // Kritik: son ek benzerligi yeterli DEGIL. "ornek.com.saldirgan.net"
    // ".ornek.com" ile BITMIYOR, dolayisiyla eslesmemeli.
    [InlineData("*.ornek.com", "ornek.com.saldirgan.net", false)]
    [InlineData("*.ornek.com", "baskaornek.com", false)]
    [InlineData("db.ornek.com", "db.ornek.com", true)]
    [InlineData("db.ornek.com", "DB.ORNEK.COM", true)]
    [InlineData("db.ornek.com", "other.ornek.com", false)]
    [InlineData("203.0.113.5", "203.0.113.5", true)]
    [InlineData("203.0.113.5", "203.0.113.6", false)]
    public void Joker_ve_tam_eslesme_dogru_calisiyor(string entry, string host, bool expected)
    {
        Assert.Equal(expected, DbHostAccessPolicy.Matches(entry, host));
    }

    /// <summary>
    /// Allowlist ozel adres kontrolunu EZMIYOR: iki kapi birlikte calisiyor.
    /// Listeye localhost yazmak, uretimde ozel adres yasagini kaldirmiyor.
    /// </summary>
    [Fact]
    public void Allowlist_ozel_adres_yasagini_ezmiyor()
    {
        var policy = Build("Production", flag: false, allowedHosts: "localhost,127.0.0.1");

        Assert.False(policy.IsHostAllowed("localhost", out var reason));
        Assert.Contains("private/reserved", reason);
    }

    [Theory]
    [InlineData("a.com,b.com", 2)]
    [InlineData("a.com; b.com ;c.com", 3)]
    [InlineData("  ", 0)]
    [InlineData(null, 0)]
    public void Allowlist_ayirici_ve_bosluklara_dayanikli(string? raw, int expected)
    {
        Assert.Equal(expected, DbHostAccessPolicy.ParseAllowedHosts(raw).Length);
    }
}
