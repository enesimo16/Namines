using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Services;
using Xunit;

namespace Namines.Tests.Security;

public class SystemAuditProbeTests
{
    private static IConfiguration Config(params (string Key, string Value)[] items)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (k, v) in items) dict[k] = v;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public async Task Redis_ping_hata_firlatirsa_probe_fail_doner_ve_mesaj_sizdirmaz()
    {
        var result = await AdminTelemetryService.RunProbeAsync("Redis", NullLogger.Instance, ct =>
            AdminTelemetryService.ProbeRedisBackplaneAsync(
                configured: true,
                ping: _ => throw new InvalidOperationException("redis://user:secret@host"),
                ct));

        Assert.Equal("fail", result.Status);
        Assert.DoesNotContain("secret", result.Message);
    }

    [Fact]
    public async Task Redis_tanimli_degilse_probe_firlatmadan_pass_doner()
    {
        var (status, message) = await AdminTelemetryService.ProbeRedisBackplaneAsync(false, null, CancellationToken.None);

        Assert.Equal("pass", status);
        Assert.Contains("tek instance", message);
    }

    [Fact]
    public async Task Redis_tanimli_ama_baglanti_kayitli_degilse_fail()
    {
        var (status, _) = await AdminTelemetryService.ProbeRedisBackplaneAsync(true, null, CancellationToken.None);
        Assert.Equal("fail", status);
    }

    [Fact]
    public async Task Redis_ping_basariliysa_pass()
    {
        var (status, _) = await AdminTelemetryService.ProbeRedisBackplaneAsync(
            true, _ => Task.FromResult(TimeSpan.FromMilliseconds(3)), CancellationToken.None);
        Assert.Equal("pass", status);
    }

    [Fact]
    public async Task Probe_zaman_asiminda_fail_doner()
    {
        var result = await AdminTelemetryService.RunProbeAsync("Yavas", NullLogger.Instance, async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            return ("pass", "asla");
        });

        Assert.Equal("fail", result.Status);
        Assert.True(result.LatencyMs < 25_000);
    }

    [Fact]
    public void AiGateway_anahtar_yoksa_fail_varsa_pass()
    {
        Assert.Equal("fail", AdminTelemetryService.ProbeAiGateway(Config()).Status);
        Assert.Equal("pass", AdminTelemetryService.ProbeAiGateway(Config(("Groq:ApiKey", "k"))).Status);
    }

    [Fact]
    public void Skor_skipped_probe_leri_disarida_birakir()
    {
        var report = new SystemBotAuditReport
        {
            Probes = new List<SystemBotProbeResult>
            {
                new() { Status = "pass" },
                new() { Status = "pass" },
                new() { Status = "skipped" },
                new() { Status = "fail" },
            }
        };

        AdminTelemetryService.ApplyScore(report);

        Assert.Equal(67, report.HealthScore); // 2/3, skipped paydada yok
        Assert.Equal("Degraded", report.OverallStatus);
        Assert.Contains("1 atlandı", report.SummaryText);
    }

    [Fact]
    public void Skor_hepsi_pass_ve_biri_skipped_ise_100()
    {
        var report = new SystemBotAuditReport
        {
            Probes = new List<SystemBotProbeResult>
            {
                new() { Status = "pass" },
                new() { Status = "skipped" },
            }
        };

        AdminTelemetryService.ApplyScore(report);

        Assert.Equal(100, report.HealthScore);
        Assert.Equal("Healthy", report.OverallStatus);
    }
}
