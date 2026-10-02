using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Interfaces;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// <see cref="SandboxOutputRetentionService"/> — <c>Outputs/</c> temizliği.
///
/// <b>Bulunan açık (docs/last-review M-6):</b> <c>DockerJobManager.EvictExpiredJobs</c>
/// vardı ama <b>hiçbir yerden çağrılmıyordu</b>. İş kayıtları bellekte sınırsız
/// birikiyordu ve <c>Outputs/</c> altındaki zip'ler <b>asla silinmiyordu</b> — oysa
/// indirme ucu kullanıcıya "saklama penceresi (2 saat) geçmiş olabilir" diyordu.
/// Ürün tutmadığı bir söz veriyordu ve o dosyalar kullanıcının tam şemasını taşıyor.
///
/// <c>Sweep</c> <c>private</c> olduğu için servis <c>StartAsync</c> ile çalıştırılıp
/// ilk tur bekleniyor: iddia dosyaların GERÇEKTEN silinmesi, bir metodun çağrılması
/// değil.
/// </summary>
public sealed class SandboxOutputRetentionTests : IDisposable
{
    private readonly string _outputs =
        Path.Combine(Path.GetTempPath(), "namines-m6-" + Guid.NewGuid().ToString("N"));

    public SandboxOutputRetentionTests() => Directory.CreateDirectory(_outputs);

    public void Dispose()
    {
        try { Directory.Delete(_outputs, recursive: true); } catch { /* temizlik testi bozmasin */ }
    }

    /// <summary>Hangi işlerin aktif, hangilerinin süresinin dolduğunu testin söylediği kayıt.</summary>
    private sealed class FakeRegistry : ISandboxJobRegistry
    {
        private readonly string[] _active;
        private readonly string[] _expired;
        public int EvictCallCount { get; private set; }

        public FakeRegistry(string[]? active = null, string[]? expired = null)
        {
            _active = active ?? Array.Empty<string>();
            _expired = expired ?? Array.Empty<string>();
        }

        public IReadOnlyCollection<string> GetActiveJobIds() => _active;

        public IReadOnlyCollection<string> EvictExpiredJobs()
        {
            EvictCallCount++;
            return _expired;
        }
    }

    private string WriteOutput(string fileName, TimeSpan age)
    {
        var path = Path.Combine(_outputs, fileName);
        File.WriteAllText(path, "zip-icerigi");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
        return path;
    }

    private async Task RunSweepAsync(FakeRegistry registry)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sandbox:OutputsDirectory"] = _outputs,
            })
            .Build();

        var service = new SandboxOutputRetentionService(
            NullLogger<SandboxOutputRetentionService>.Instance, registry, config);

        // BackgroundService.StartAsync, ExecuteAsync'i havuz is parcaciginda
        // baslatip HEMEN donuyor — yani ilk tur henuz calismamis olabilir.
        // Sabit bir gecikme kirilgan olurdu; tahliyenin GERCEKTEN olmasi beklenir.
        await service.StartAsync(CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (registry.EvictCallCount == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        await service.StopAsync(CancellationToken.None);
        Assert.True(registry.EvictCallCount > 0, "Ilk temizlik turu hic calismadi.");
    }

    [Fact]
    public async Task Suresi_dolmus_isin_dosyasi_SILINIYOR()
    {
        var path = WriteOutput("backup_job-1.zip", TimeSpan.FromMinutes(5));
        var registry = new FakeRegistry(expired: new[] { "job-1" });

        await RunSweepAsync(registry);

        Assert.False(File.Exists(path), "Dosya silinmedi — M-6 hala acik.");
        Assert.Equal(1, registry.EvictCallCount);
    }

    [Fact]
    public async Task CoderAI_paketi_de_SILINIYOR()
    {
        // Iki ayri onek var (`backup_`, `coderai_`); kimligi ICEREN dosya
        // aranmasinin sebebi tam olarak bu.
        var path = WriteOutput("coderai_job-2.zip", TimeSpan.FromMinutes(5));

        await RunSweepAsync(new FakeRegistry(expired: new[] { "job-2" }));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task SAHIPSIZ_eski_dosya_da_SILINIYOR()
    {
        // Asil temizligi yapan olcut bu: uygulama yeniden baslatildiginda is
        // sozlugu bosaliyor, yani hicbir eski dosya kimlikle eslesemez ve
        // yalnizca kimlige bakan bir temizlik onlari SONSUZA KADAR birakirdi.
        var path = WriteOutput("backup_forgotten-job.zip", TimeSpan.FromHours(5));

        await RunSweepAsync(new FakeRegistry());

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task AKTIF_isin_dosyasina_DOKUNULMUYOR()
    {
        // Uzun suren ama canli bir isin ciktisini altindan cekmek, ozelligi
        // bozmak olurdu.
        var path = WriteOutput("backup_running-job.zip", TimeSpan.FromHours(9));

        await RunSweepAsync(new FakeRegistry(active: new[] { "running-job" }));

        Assert.True(File.Exists(path), "Aktif isin dosyasi silindi.");
    }

    [Fact]
    public async Task YENI_ve_sahipsiz_dosya_KORUNUYOR()
    {
        // Yas olcutu retention + pay esiginde uygulaniyor: henuz indirilebilir
        // olan bir dosyayi silmek, kullaniciya verilen 2 saatlik sozu bu kez
        // ters yonden bozardi.
        var path = WriteOutput("backup_fresh-job.zip", TimeSpan.FromMinutes(10));

        await RunSweepAsync(new FakeRegistry());

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Dizin_YOKSA_patlamiyor()
    {
        Directory.Delete(_outputs, recursive: true);

        var registry = new FakeRegistry(expired: new[] { "job-3" });
        await RunSweepAsync(registry);

        // Bellek tahliyesi diskten BAGIMSIZ yapilmali: is kayitlari diskten
        // bagimsiz birikiyor.
        Assert.Equal(1, registry.EvictCallCount);
    }

    [Fact]
    public async Task Alt_dizinlere_INILMIYOR()
    {
        // Servisin sahibi olmadigi bir seyi silmesi riskini acmamak icin
        // TopDirectoryOnly kullaniliyor.
        var nested = Path.Combine(_outputs, "keep");
        Directory.CreateDirectory(nested);
        var path = Path.Combine(nested, "backup_old-job.zip");
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - TimeSpan.FromDays(30));

        await RunSweepAsync(new FakeRegistry(expired: new[] { "old-job" }));

        Assert.True(File.Exists(path));
    }
}
