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

    private static DbHostAccessPolicy Build(string environment, bool? flag)
    {
        var settings = new Dictionary<string, string?>();
        if (flag is not null) settings["Security:AllowPrivateDbHosts"] = flag.Value ? "true" : "false";

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
}
