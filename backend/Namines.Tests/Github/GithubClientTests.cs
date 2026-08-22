using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Github;
using Namines.Infrastructure.Services;

namespace Namines.Tests.Github;

/// <summary>
/// Bot'un GitHub'a YAZAN yüzü (11 §7).
///
/// <b>Gerçek bir GitHub App olmadan da doğrulanabilir</b> — ve bu, özelliğin
/// hesap beklerken yazılabilmesinin tek sebebi. Sahte bir <c>HttpMessageHandler</c>
/// isteği yakalıyor; testler URL'i, metodu, başlıkları ve gövdeyi kontrol ediyor.
/// Bunlar "iyimser" testler değil: yanlış bir URL ya da eksik bir başlık,
/// üretimde sebebi anlaşılmayan bir 403/404 olarak görünürdü.
/// </summary>
public class GithubClientTests
{
    // ── Sahte HTTP ───────────────────────────────────────────────────────────

    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage>? Respond { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            if (Respond is not null) return Respond(request);

            // Kurulum token'ı isteği ile diğerlerini ayırmak zorundayız: istemci
            // her yazma öncesi token alır.
            if (request.RequestUri!.AbsolutePath.EndsWith("/access_tokens", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.Created, new
                {
                    token = "ghs_installation_token",
                    expires_at = DateTimeOffset.UtcNow.AddHours(1).ToString("o"),
                });
            }

            return Json(HttpStatusCode.Created, new { id = 1 });
        }

        public static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
    }

    private static string PrivateKey()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportRSAPrivateKeyPem();
    }

    private static (GithubClient Client, FakeHandler Handler) Client(bool configured = true)
    {
        var handler = new FakeHandler();
        var http = new HttpClient(handler);

        var settings = new Dictionary<string, string?>();
        if (configured)
        {
            settings["Github:AppId"] = "12345";
            settings["Github:PrivateKey"] = PrivateKey();
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return (new GithubClient(http, configuration, NullLogger<GithubClient>.Instance), handler);
    }

    private static GithubRepository Repo => new("acme", "shop");

    // ── Kimlik ───────────────────────────────────────────────────────────────

    [Fact]
    public void Without_credentials_the_client_reports_itself_unconfigured()
    {
        // Çağıran buna bakıp yazmayı hiç denemiyor; sahte bir başarı raporlamak
        // çalıştığı sanılan ama hiçbir şey yapmayan bir özellik bırakırdı.
        Assert.False(Client(configured: false).Client.IsConfigured);
    }

    [Fact]
    public async Task Writing_without_credentials_fails_loudly()
    {
        var (client, _) = Client(configured: false);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.PostCommentAsync(Repo, 1, 7, "hello"));

        Assert.Contains("Github:AppId", error.Message);
    }

    [Fact]
    public async Task The_repository_is_touched_with_an_installation_token_not_the_app_key()
    {
        // Özel anahtarla doğrudan API çağırmak mümkün değil; ama daha önemlisi,
        // tek bir sızıntı bütün kurulumları açardı. Kurulum token'ı bir saatte
        // ölür ve yalnızca o kuruluma bakar.
        var (client, handler) = Client();

        await client.PostCommentAsync(Repo, 42, 7, "hello");

        var tokenRequest = handler.Requests[0];
        var commentRequest = handler.Requests[1];

        Assert.EndsWith("/app/installations/42/access_tokens", tokenRequest.RequestUri!.AbsolutePath);
        Assert.Equal("ghs_installation_token", commentRequest.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task An_installation_token_is_reused_rather_than_requested_every_time()
    {
        // Her istekte yeni token almak, gereksiz gecikme ve GitHub'ın kendi hız
        // sınırını boşa harcamak demek.
        var (client, handler) = Client();

        await client.PostCommentAsync(Repo, 42, 7, "one");
        await client.PostCommentAsync(Repo, 42, 7, "two");

        Assert.Single(handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/access_tokens"));
    }

    // ── İstek şekli ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_comment_goes_to_the_issues_endpoint_with_the_body()
    {
        var (client, handler) = Client();

        await client.PostCommentAsync(Repo, 1, 7, "**[SAFE]** looks fine");

        var request = handler.Requests[^1];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/repos/acme/shop/issues/7/comments", request.RequestUri!.AbsolutePath);
        Assert.Contains("looks fine", handler.Bodies[^1]);
    }

    [Fact]
    public async Task A_check_run_carries_the_conclusion_that_blocks_merging()
    {
        // "failure" merge korumalarını tetikler; özelliğin tüm amacı bu.
        var (client, handler) = Client();

        await client.CreateCheckRunAsync(Repo, 1, "abc123", "Namines", "failure", "t", "s", "b");

        var body = JsonDocument.Parse(handler.Bodies[^1]).RootElement;
        Assert.Equal("/repos/acme/shop/check-runs", handler.Requests[^1].RequestUri!.AbsolutePath);
        Assert.Equal("abc123", body.GetProperty("head_sha").GetString());
        Assert.Equal("failure", body.GetProperty("conclusion").GetString());
        Assert.Equal("completed", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Every_request_carries_a_user_agent()
    {
        // GitHub, User-Agent taşımayan isteği 403 ile reddeder ve sebebi
        // mesajdan anlaşılmaz.
        var (client, handler) = Client();

        await client.PostCommentAsync(Repo, 1, 7, "hi");

        Assert.All(handler.Requests, r =>
            Assert.Contains("namines-bot", r.Headers.UserAgent.ToString()));
    }

    // ── Dosya okuma ──────────────────────────────────────────────────────────

    [Fact]
    public async Task File_content_is_decoded_from_base64()
    {
        var (client, handler) = Client();
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("table users {\n  id int pk\n}\n"));

        handler.Respond = request =>
            request.RequestUri!.AbsolutePath.EndsWith("/access_tokens", StringComparison.Ordinal)
                ? FakeHandler.Json(HttpStatusCode.Created, new { token = "t", expires_at = DateTimeOffset.UtcNow.AddHours(1).ToString("o") })
                // GitHub içeriği satır sonlarıyla bölünmüş base64 olarak veriyor;
                // temizlenmezse çözme patlar.
                : FakeHandler.Json(HttpStatusCode.OK, new { content = InsertLineBreaks(encoded) });

        var text = await client.GetFileContentAsync(Repo, 1, "schema.nsl", "main");

        Assert.Contains("table users", text);
    }

    [Fact]
    public async Task A_missing_file_is_null_not_an_error()
    {
        // Şemayı İLK KEZ ekleyen bir PR'da taban ref'te dosya bulunmaz. Hata
        // saymak, o PR'ı hiç inceleyememek demek olurdu.
        var (client, handler) = Client();

        handler.Respond = request =>
            request.RequestUri!.AbsolutePath.EndsWith("/access_tokens", StringComparison.Ordinal)
                ? FakeHandler.Json(HttpStatusCode.Created, new { token = "t", expires_at = DateTimeOffset.UtcNow.AddHours(1).ToString("o") })
                : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };

        Assert.Null(await client.GetFileContentAsync(Repo, 1, "schema.nsl", "main"));
    }

    private static string InsertLineBreaks(string value)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < value.Length; i += 60)
            sb.Append(value.AsSpan(i, Math.Min(60, value.Length - i))).Append('\n');
        return sb.ToString();
    }

    // ── App JWT ──────────────────────────────────────────────────────────────

    [Fact]
    public void The_app_jwt_is_signed_and_readable_by_github()
    {
        var pem = PrivateKey();
        var jwt = GithubAppJwt.Create("12345", pem);

        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);

        var payload = JsonDocument.Parse(Decode(parts[1])).RootElement;
        Assert.Equal("12345", payload.GetProperty("iss").GetString());

        // 'iat' geriye alınıyor: sunucu saati GitHub'ınkinden birkaç saniye
        // ileriyse token "gelecekte üretilmiş" sayılır ve reddedilir.
        Assert.True(payload.GetProperty("iat").GetInt64() < DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        Assert.True(rsa.VerifyData(
            Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]),
            DecodeBytes(parts[2]),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public void An_over_long_app_jwt_is_refused_before_github_refuses_it()
    {
        // GitHub 10 dakikadan uzun yaşayan bir token'ı reddeder; burada yakalamak,
        // sebebi anlaşılmayan bir 401 yerine net bir hata verir.
        Assert.Throws<ArgumentException>(
            () => GithubAppJwt.Create("1", PrivateKey(), TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void The_app_jwt_uses_base64url_not_plain_base64()
    {
        // Düz base64 göndermek token'ı geçersiz kılar; '+' ve '/' JWT'de yasak.
        for (var i = 0; i < 20; i++)
        {
            var jwt = GithubAppJwt.Create("12345", PrivateKey());
            Assert.DoesNotContain('+', jwt);
            Assert.DoesNotContain('/', jwt);
            Assert.DoesNotContain('=', jwt);
        }
    }

    private static string Decode(string part) => Encoding.UTF8.GetString(DecodeBytes(part));

    private static byte[] DecodeBytes(string part)
    {
        var padded = part.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
