using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Ground.Abstractions;
using Namines.Ground.Supabase;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// <see cref="SupabaseProvider"/> — sahte bir <see cref="ISupabaseClient"/> ile
/// (F-10 / B-45).
///
/// <b>Bu testler sağlayıcının canlı çalıştığını KANITLAMAZ</b> — ve sağlayıcı
/// da bunu kendisi söylüyor (<c>IsLiveVerified: false</c>). Bu oturumda
/// Supabase erişim jetonu yoktu; uçlar belgeden alındı, gerçek bir kaynağa
/// karşı denenmedi. Test edilen şey, istemciden gelen cevaba doğru tepki
/// verilmesi: adlandırma, yarım kalan kaynağın temizlenmesi, ölçümlerin
/// uydurulmaması.
/// </summary>
public class SupabaseProviderTests
{
    private static SupabaseProvider Build(ISupabaseClient client) =>
        new(client, NullLogger<SupabaseProvider>.Instance);

    private static ProvisionSpec Spec(string id = "proj-1", string name = "Ground Test") =>
        new(id, name);

    /// <summary>
    /// <b>Bayrak false KALMALI.</b> Bu testin amacı, birinin canlı doğrulama
    /// YAPMADAN bayrağı true'ya çevirmesini fark etmek: o durumda arayüz
    /// kullanıcıya "hazır" der ve kullanıcı verisini kanıtlanmamış bir yola
    /// koyar. Doğrulama gerçekten yapıldığında bu test de bilinçli olarak
    /// güncellenmeli.
    /// </summary>
    [Fact]
    public void Reports_itself_as_NOT_live_verified()
    {
        Assert.False(Build(new FakeSupabaseClient()).Capabilities.IsLiveVerified);
    }

    /// <summary>
    /// Sorumluluk notu, kaynağı KİMİN işlettiğini söylemek zorunda. Ground'un
    /// kendi barındırdığı veritabanıyla Supabase'de açılan bir veritabanı
    /// arasındaki fark, arıza anında kime başvurulacağını belirliyor.
    /// </summary>
    [Fact]
    public void Responsibility_note_names_the_operator_and_the_missing_verification()
    {
        var note = Build(new FakeSupabaseClient()).Capabilities.ResponsibilityNote;

        Assert.Contains("Supabase", note);
        Assert.Contains("not been verified", note);
    }

    [Fact]
    public async Task Passes_the_connection_string_through_from_the_client()
    {
        var client = new FakeSupabaseClient
        {
            Project = new SupabaseProjectInfo("abcdefgh", "eu-central-1", "Host=db.abcdefgh.supabase.co;Port=5432"),
        };

        var database = await Build(client).CreateAsync(Spec(), CancellationToken.None);

        Assert.Equal("abcdefgh", database.ProviderProjectId);
        Assert.Equal("eu-central-1", database.Region);
        Assert.Contains("db.abcdefgh.supabase.co", database.ConnectionString);
    }

    /// <summary>
    /// Supabase'de dal kullanılmıyor, bu yüzden alan null. "Var" demek,
    /// olmayan bir düğme vaat etmek olurdu.
    /// </summary>
    [Fact]
    public async Task Does_not_invent_a_branch_id()
    {
        var database = await Build(new FakeSupabaseClient()).CreateAsync(Spec(), CancellationToken.None);

        Assert.Null(database.ProviderBranchId);
    }

    [Fact]
    public void Project_name_carries_both_the_label_and_the_id()
    {
        var name = SupabaseProvider.BuildProjectName(Spec("proj-42", "Benim Projem"));

        // Ad okunabilirlik, kimlik kesinlik: iki Namines projesi aynı ada
        // sahip olabilir, panelde hangisinin hangisi olduğu ayırt edilmeli.
        Assert.StartsWith("namines-", name);
        Assert.Contains("benim-projem", name);
        Assert.EndsWith("proj-42", name);
    }

    [Fact]
    public void Project_name_survives_an_empty_label()
    {
        var name = SupabaseProvider.BuildProjectName(Spec("proj-7", "   "));

        Assert.Equal("namines-project-proj-7", name);
    }

    /// <summary>
    /// Ölçümler UYDURULMAZ. Supabase bu yoldan depolama/bağlantı sayısı
    /// vermiyor; sıfır yazmak "ölçüldü ve sıfır çıktı" demek olurdu ve
    /// Ground'un boyut uyarısı o sayının üstüne kuruluyor.
    /// </summary>
    [Fact]
    public async Task Metrics_are_null_because_supabase_does_not_report_them()
    {
        var metrics = await Build(new FakeSupabaseClient()).GetMetricsAsync(
            new ProvisionedDatabase("ref", null, "eu-central-1", "Host=x"), CancellationToken.None);

        Assert.Null(metrics.StorageBytes);
        Assert.Null(metrics.ActiveConnections);
    }

    [Fact]
    public async Task Delete_is_skipped_when_there_is_no_provider_id()
    {
        var client = new FakeSupabaseClient();

        await Build(client).DeleteAsync(
            new ProvisionedDatabase(string.Empty, null, "eu-central-1", "Host=x"), CancellationToken.None);

        Assert.Empty(client.Deleted);
    }

    [Fact]
    public async Task Delete_forwards_the_project_reference()
    {
        var client = new FakeSupabaseClient();

        await Build(client).DeleteAsync(
            new ProvisionedDatabase("abcdefgh", null, "eu-central-1", "Host=x"), CancellationToken.None);

        Assert.Equal(new[] { "abcdefgh" }, client.Deleted);
    }

    [Fact]
    public async Task Probe_is_forwarded_to_the_client()
    {
        var client = new FakeSupabaseClient { ProbeResult = "token missing" };

        Assert.Equal("token missing", await Build(client).ProbeAsync(CancellationToken.None));
    }

    // ── Bağlantı dizesi ve parola (istemci yardımcıları) ────────────────────

    /// <summary>
    /// Anahtar=değer biçimi ZORUNLU: Namines'in geri kalanı (Desk, Vault,
    /// Gateway ve SSRF kontrolü) bağlantıyı böyle ayrıştırıyor. URI olarak
    /// saklamak, o yolların host'u çıkaramaması — yani SSRF kontrolünün
    /// sessizce atlanması — demekti.
    /// </summary>
    [Fact]
    public void Connection_string_is_key_value_and_requires_tls()
    {
        var connection = SupabaseClient.BuildConnectionString("abcdefgh", "s3cret");

        Assert.Contains("Host=db.abcdefgh.supabase.co", connection);
        Assert.Contains("Port=5432", connection);
        Assert.Contains("Database=postgres", connection);
        Assert.Contains("SSL Mode=Require", connection);
        // URI biçimi DEĞİL.
        Assert.DoesNotContain("://", connection);
    }

    [Fact]
    public void Generated_passwords_are_long_and_not_repeated()
    {
        var first = SupabaseClient.GeneratePassword();
        var second = SupabaseClient.GeneratePassword();

        Assert.Equal(32, first.Length);
        // Aynı parolanın iki kez üretilmesi, tohumun zamana bağlı olduğunu
        // (yani `Random`'a dönüldüğünü) gösterirdi.
        Assert.NotEqual(first, second);
    }

    private sealed class FakeSupabaseClient : ISupabaseClient
    {
        public SupabaseProjectInfo Project { get; set; } =
            new("abcdefgh", "eu-central-1", "Host=db.abcdefgh.supabase.co;Port=5432");

        public string? ProbeResult { get; set; }

        public List<string> Deleted { get; } = new();

        public bool IsConfigured => true;

        public Task<SupabaseProjectInfo> CreateProjectAsync(string name, string? region, CancellationToken ct) =>
            Task.FromResult(Project);

        public Task DeleteProjectAsync(string projectRef, CancellationToken ct)
        {
            Deleted.Add(projectRef);
            return Task.CompletedTask;
        }

        public Task<string?> ProbeAsync(CancellationToken ct) => Task.FromResult(ProbeResult);
    }
}
