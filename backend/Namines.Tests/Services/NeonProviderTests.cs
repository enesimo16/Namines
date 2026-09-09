using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Ground.Abstractions;
using Namines.Ground.Neon;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// <see cref="NeonProvider"/> — sahte bir <see cref="INeonClient"/> ile.
///
/// <b>Ayrımın tüm amacı bu:</b> HTTP sınırı ayrı bir arayüz olmasaydı, bu
/// mantığı test etmek için gerçek Neon'a çağrı yapmak — yani her koşuda para
/// harcamak ve gerçek kaynak açmak — gerekirdi.
///
/// <b>Bu testler sağlayıcının canlı çalıştığını KANITLAMAZ</b>; yalnızca
/// istemciden gelen cevaba doğru tepki verdiğini gösterir. Ağ davranışı
/// (alan adları, hata gövdeleri) gerçek bir anahtar gelene kadar varsayım.
/// </summary>
public class NeonProviderTests
{
    private static NeonProvider Build(INeonClient client) =>
        new(client, NullLogger<NeonProvider>.Instance);

    private static ProvisionSpec Spec(string id = "proj-1", string name = "Ground Test") =>
        new(id, name);

    [Fact]
    public void Reports_itself_as_live_verified()
    {
        // 2026-09-09'da gerçek Neon'a karşı doğrulandı: proje açıldı, Namines'in
        // KENDİ şifreli bağlantısıyla yazılan bir tablo bağımsız bir psql
        // oturumunda görüldü (ve tersi), idempotans/silme/kalıcı silme (Neon'da
        // proje 404'e düştü) ayrıca kanıtlandı.
        //
        // Bu testin AYNI değeri kontrol etmesi bilinçli: değeri false'tan
        // true'ya çeviren değişiklik, canlı doğrulama olmadan yapıldıysa
        // burada YAKALANMAZ — asıl korunan şey, birinin onu tekrar false'a
        // düşürmesini FARK ETMEK, çünkü o zaman arayüz yanlışlıkla "hazır"
        // gösterirdi.
        Assert.True(Build(new FakeNeonClient()).Capabilities.IsLiveVerified);
    }

    [Fact]
    public async Task Converts_the_uri_neon_returns_into_a_key_value_connection_string()
    {
        // Namines'in geri kalanı (SSRF kontrolü dahil) anahtar=değer bekliyor.
        // URI'yi olduğu gibi saklamak, host'un çıkarılamaması demekti.
        var client = new FakeNeonClient
        {
            Project = new NeonProject("pr-1", "br-1", "aws-eu-central-1",
                "postgresql://kullanici:parola@ep-abc.eu-central-1.aws.neon.tech/veritabani?sslmode=require"),
        };

        var result = await Build(client).CreateAsync(Spec(), CancellationToken.None);

        Assert.Contains("Host=ep-abc.eu-central-1.aws.neon.tech", result.ConnectionString);
        Assert.Contains("Database=veritabani", result.ConnectionString);
        Assert.Contains("Username=kullanici", result.ConnectionString);
        Assert.Contains("Password=parola", result.ConnectionString);
        // TLS düşürülmemeli: bağlantı ağda açık hâle gelirdi.
        Assert.Contains("SSL Mode=Require", result.ConnectionString);
    }

    [Fact]
    public async Task Deletes_the_project_when_the_connection_uri_is_unusable()
    {
        // EN ÖNEMLİ TEST: Neon projeyi AÇTI ama biz onu kullanılamaz bulduk.
        // Temizlemezsek kimsenin bilmediği ama faturalanan bir kaynak kalır.
        var client = new FakeNeonClient
        {
            Project = new NeonProject("pr-1", "br-1", "aws-eu-central-1", ConnectionUri: "  "),
        };

        await Assert.ThrowsAnyAsync<Exception>(
            () => Build(client).CreateAsync(Spec(), CancellationToken.None));

        Assert.Equal("pr-1", client.DeletedProjectId);
    }

    [Fact]
    public async Task Create_failure_leaves_nothing_to_clean_up()
    {
        // Oluşturma hiç başarılı olmadıysa silinecek bir şey de yok; körlemesine
        // silme çağrısı yapmak, var olan başka bir kaynağı riske atardı.
        var client = new FakeNeonClient { FailOnCreate = true };

        await Assert.ThrowsAnyAsync<Exception>(
            () => Build(client).CreateAsync(Spec(), CancellationToken.None));

        Assert.Null(client.DeletedProjectId);
    }

    [Fact]
    public void Project_name_carries_both_a_readable_label_and_the_exact_id()
    {
        // Panele bakan biri kaynağı tanıyabilmeli (ad) ve kesin eşleştirebilmeli
        // (kimlik) — iki Namines projesi aynı ada sahip olabilir.
        var name = NeonProvider.BuildProjectName(new ProvisionSpec("proj-42", "Müşteri Portalı"));

        Assert.StartsWith("namines-", name);
        Assert.EndsWith("proj-42", name);
        // Türkçe karakterler ve boşluk temizlenmeli.
        Assert.DoesNotContain(' ', name);
        Assert.DoesNotContain('ü', name);
    }

    [Fact]
    public void Project_name_survives_an_empty_label()
    {
        var name = NeonProvider.BuildProjectName(new ProvisionSpec("proj-42", "   "));
        Assert.Equal("namines-project-proj-42", name);
    }

    [Fact]
    public async Task Metrics_report_unknown_rather_than_zero()
    {
        // null "bilinmiyor" demek. Sıfır yazmak "ölçüldü ve sıfır çıktı"
        // olurdu — kullanıcı boş bir veritabanı sanırdı.
        var client = new FakeNeonClient { Usage = new NeonUsage(StorageBytes: null) };

        var metrics = await Build(client).GetMetricsAsync(
            new ProvisionedDatabase("pr-1", "br-1", "r", string.Empty), CancellationToken.None);

        Assert.Null(metrics.StorageBytes);
        Assert.Null(metrics.ActiveConnections);
    }

    private sealed class FakeNeonClient : INeonClient
    {
        public NeonProject Project { get; set; } =
            new("pr-1", "br-1", "aws-eu-central-1", "postgresql://u:p@h/d");

        public NeonUsage Usage { get; set; } = new(1024);
        public bool FailOnCreate { get; set; }
        public string? DeletedProjectId { get; private set; }

        public bool IsConfigured => true;

        public Task<NeonProject> CreateProjectAsync(string name, string? regionId, CancellationToken ct) =>
            FailOnCreate
                ? Task.FromException<NeonProject>(new InvalidOperationException("Neon reddetti."))
                : Task.FromResult(Project);

        public Task DeleteProjectAsync(string projectId, CancellationToken ct)
        {
            DeletedProjectId = projectId;
            return Task.CompletedTask;
        }

        public Task<NeonUsage> GetUsageAsync(string projectId, CancellationToken ct) =>
            Task.FromResult(Usage);

        public Task<string?> ProbeAsync(CancellationToken ct) => Task.FromResult<string?>(null);
    }
}
