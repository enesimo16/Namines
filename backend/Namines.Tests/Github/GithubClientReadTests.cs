using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Github;
using Namines.Infrastructure.Services;

namespace Namines.Tests.Github;

/// <summary>
/// github/01-DEPO-TARAMA.md — public depo okuma yolu.
///
/// <b>App kimliği olmadan çalıştığı burada kanıtlanıyor.</b> F1'in GitHub
/// App'i beklememesinin tek sebebi bu yol; yapılandırma boş bırakılarak test
/// ediliyor ki "aslında kimlik gerekiyormuş" sürprizi üretimde çıkmasın.
/// </summary>
public class GithubClientReadTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage>? Respond { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(Respond!(request));
        }

        public static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
    }

    private static GithubClient Client(FakeHandler handler) => new(
        new HttpClient(handler),
        new ConfigurationBuilder().AddInMemoryCollection().Build(),   // App kimliği YOK
        NullLogger<GithubClient>.Instance,
        new GithubInstallationTokenCache());

    [Fact]
    public async Task A_public_repository_is_read_without_any_credentials()
    {
        var handler = new FakeHandler
        {
            Respond = _ => FakeHandler.Json(HttpStatusCode.OK, new
            {
                tree = new object[]
                {
                    new { path = "prisma/schema.prisma", type = "blob" },
                    new { path = "src", type = "tree" },
                },
                truncated = false,
            }),
        };

        var tree = await Client(handler).GetRepositoryTreeAsync(
            new GithubRepository("acme", "shop"), "main", installationId: null);

        // Yalnızca blob'lar: "tree" girdileri dizindir, içeriği okunamaz.
        Assert.Equal(new[] { "prisma/schema.prisma" }, tree.Paths);
        Assert.False(tree.Truncated);
        Assert.Null(handler.Requests[0].Headers.Authorization);
        Assert.Single(handler.Requests);   // kurulum token'ı hiç istenmedi
    }

    [Fact]
    public async Task A_truncated_tree_is_reported_not_hidden()
    {
        // Dev depolarda GitHub ağacı kesiyor. Bunu yutmak, "şemanı çıkardım"
        // derken deponun hiç görülmemiş bir kısmını gizlemek olurdu.
        var handler = new FakeHandler
        {
            Respond = _ => FakeHandler.Json(HttpStatusCode.OK, new
            {
                tree = new object[] { new { path = "a.sql", type = "blob" } },
                truncated = true,
            }),
        };

        var tree = await Client(handler).GetRepositoryTreeAsync(
            new GithubRepository("acme", "shop"), "main", installationId: null);

        Assert.True(tree.Truncated);
    }

    [Fact]
    public async Task The_default_branch_is_read_from_the_repository()
    {
        var handler = new FakeHandler
        {
            Respond = _ => FakeHandler.Json(HttpStatusCode.OK, new { default_branch = "develop" }),
        };

        var branch = await Client(handler).GetDefaultBranchAsync(
            new GithubRepository("acme", "shop"), installationId: null);

        Assert.Equal("develop", branch);
    }

    [Fact]
    public async Task A_private_repository_without_credentials_is_refused_clearly()
    {
        // GitHub, göremediğimiz depoya 404 döner (varlığını sızdırmamak için).
        // Kullanıcıya düz "bulunamadı" demek yanıltıcı olurdu: depo var
        // olabilir, biz göremiyoruz. İkisini ayırt ettiremediğimiz için de
        // uydurmuyoruz — mesaj iki ihtimali birlikte söylüyor.
        var handler = new FakeHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound) };

        await Assert.ThrowsAsync<GithubRepositoryUnavailableException>(() =>
            Client(handler).GetRepositoryTreeAsync(
                new GithubRepository("acme", "secret"), "main", installationId: null));
    }

    [Fact]
    public async Task Reading_a_file_uses_the_read_classifier_too()
    {
        // CANLI DOĞRULAMADA BULUNDU: ağaç okuması geçip dosya okuması kotaya
        // takıldığında, bu metot yazma yolunun genel hatasını kullandığı için
        // kullanıcı anlaşılmaz bir 500 görüyordu. Birim testler görmemişti
        // çünkü hiçbiri dosya okumasını hata durumunda denememişti.
        var handler = new FakeHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"message\":\"API rate limit exceeded for 1.2.3.4.\"}"),
            },
        };

        await Assert.ThrowsAsync<GithubRateLimitedException>(() =>
            Client(handler).GetFileContentAsync(
                new GithubRepository("acme", "shop"), installationId: null,
                path: "prisma/schema.prisma", reference: "main"));
    }

    [Fact]
    public async Task The_rate_limit_is_recognised_from_the_body_when_the_header_is_missing()
    {
        // GitHub kota reddini gövdede açıklıyor; tek sinyale (başlığa) güvenmek
        // bu durumu "bilinmeyen 403"e düşürüyordu.
        var handler = new FakeHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"message\":\"API rate limit exceeded for 1.2.3.4.\"}"),
            },
        };

        await Assert.ThrowsAsync<GithubRateLimitedException>(() =>
            Client(handler).GetRepositoryTreeAsync(
                new GithubRepository("acme", "shop"), "main", installationId: null));
    }

    [Fact]
    public async Task A_forbidden_that_is_not_about_rate_limiting_stays_a_plain_failure()
    {
        // Her 403'ü "kota doldu" diye çevirmek, izin hatasını kullanıcıya
        // yanlış teşhisle sunmak olurdu.
        var handler = new FakeHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"message\":\"Resource not accessible by integration\"}"),
            },
        };

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Client(handler).GetRepositoryTreeAsync(
                new GithubRepository("acme", "shop"), "main", installationId: null));
    }

    [Fact]
    public async Task Rate_limiting_is_reported_as_itself()
    {
        var handler = new FakeHandler
        {
            Respond = _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
                response.Headers.Add("x-ratelimit-remaining", "0");
                return response;
            },
        };

        await Assert.ThrowsAsync<GithubRateLimitedException>(() =>
            Client(handler).GetRepositoryTreeAsync(
                new GithubRepository("acme", "shop"), "main", installationId: null));
    }
}
