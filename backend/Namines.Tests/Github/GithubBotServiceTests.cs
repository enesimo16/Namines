using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Github;
using Namines.Core.Nsl;
using Namines.Infrastructure.Services;

namespace Namines.Tests.Github;

/// <summary>
/// Bot'un uçtan uca akışı (11 §7): olay → şema farkı → yorum + status check.
///
/// <b>Buradaki testler bot'un KARAR verdiği yeri kilitliyor.</b> Yorum metni ve
/// risk sınıflandırması <see cref="BotTests"/>'te ayrıca test edilmiş durumda;
/// bu dosya "doğru olayda, doğru şeyi, doğru yere yazıyor mu" sorusunu soruyor —
/// yanlış olayda yazmak PR'ı yorum çöplüğüne çevirir, yazmamak ise özelliği
/// görünmez kılar.
/// </summary>
public class GithubBotServiceTests
{
    // ── Sahte GitHub ─────────────────────────────────────────────────────────

    private sealed class FakeGithub : IGithubClient
    {
        public bool IsConfigured { get; set; } = true;
        public List<(int Issue, string Body)> Comments { get; } = new();
        public List<(string Sha, string Conclusion, string Body)> CheckRuns { get; } = new();
        public Dictionary<string, string> Files { get; } = new();

        public Task PostCommentAsync(GithubRepository repository, long installationId, int issueNumber, string body, CancellationToken ct = default)
        {
            Comments.Add((issueNumber, body));
            return Task.CompletedTask;
        }

        public Task CreateCheckRunAsync(GithubRepository repository, long installationId, string headSha,
            string name, string conclusion, string title, string summary, string body, CancellationToken ct = default)
        {
            CheckRuns.Add((headSha, conclusion, body));
            return Task.CompletedTask;
        }

        public Task<string?> GetFileContentAsync(GithubRepository repository, long installationId, string path, string reference, CancellationToken ct = default) =>
            Task.FromResult(Files.TryGetValue(reference, out var content) ? content : null);
    }

    private static (GithubBotService Bot, FakeGithub Github) Bot()
    {
        var github = new FakeGithub();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Github:SchemaPath"] = "schema.nsl" }).Build();

        return (new GithubBotService(github, configuration, NullLogger<GithubBotService>.Instance), github);
    }

    private static string PullRequestEvent(string action = "opened") => JsonSerializer.Serialize(new
    {
        action,
        pull_request = new
        {
            number = 7,
            head = new { sha = "head-sha" },
            @base = new { sha = "base-sha" },
        },
        repository = new { name = "shop", owner = new { login = "acme" } },
        installation = new { id = 99L },
    });

    private const string BaseSchema = "table users {\n  id int pk\n  email varchar(255) not null\n}\n";
    private const string DroppedColumn = "table users {\n  id int pk\n}\n";

    // ── Pull request ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_destructive_change_is_commented_and_fails_the_check()
    {
        // "failure" merge korumalarını tetikler. "neutral" seçmek merge'ü
        // engellemez ve check'i süse çevirirdi — özelliğin tüm amacı bu.
        var (bot, github) = Bot();
        github.Files["base-sha"] = BaseSchema;
        github.Files["head-sha"] = DroppedColumn;

        await bot.HandleAsync("pull_request", PullRequestEvent());

        Assert.Single(github.Comments);
        Assert.Equal(7, github.Comments[0].Issue);
        Assert.Contains("users.email", github.Comments[0].Body);

        Assert.Single(github.CheckRuns);
        Assert.Equal("head-sha", github.CheckRuns[0].Sha);
        Assert.Equal("failure", github.CheckRuns[0].Conclusion);
    }

    [Fact]
    public async Task A_schema_added_for_the_first_time_is_reviewed_not_skipped()
    {
        // Taban ref'te dosya yoksa şema bu PR'da İLK KEZ ekleniyor. Hata saymak
        // ya da atlamak, tam da en çok incelenmesi gereken PR'ı incelemeden
        // geçirmek olurdu.
        var (bot, github) = Bot();
        github.Files["head-sha"] = BaseSchema;

        await bot.HandleAsync("pull_request", PullRequestEvent());

        Assert.Single(github.Comments);
        Assert.Equal("success", github.CheckRuns[0].Conclusion);
    }

    [Fact]
    public async Task A_pull_request_without_the_schema_file_is_left_alone()
    {
        // Her PR'a "şema bulunamadı" yazmak bot'u gürültüye çevirir ve insanlar
        // yorumlarını okumayı bırakır.
        var (bot, github) = Bot();

        var result = await bot.HandleAsync("pull_request", PullRequestEvent());

        Assert.Empty(github.Comments);
        Assert.Empty(github.CheckRuns);
        Assert.Contains("not in this pull request", result);
    }

    [Theory]
    [InlineData("labeled")]
    [InlineData("assigned")]
    [InlineData("closed")]
    public async Task Events_that_cannot_change_the_schema_are_ignored(string action)
    {
        // Aksi hâlde aynı yorum, etiket her değiştiğinde tekrar yazılırdı.
        var (bot, github) = Bot();
        github.Files["base-sha"] = BaseSchema;
        github.Files["head-sha"] = DroppedColumn;

        await bot.HandleAsync("pull_request", PullRequestEvent(action));

        Assert.Empty(github.Comments);
    }

    [Fact]
    public async Task A_broken_schema_file_is_not_reported_as_no_risk()
    {
        // Ayrıştırılamayan bir şemayı boş saymak, bozuk bir dosyayı "risk yok"
        // diye raporlamak olurdu — mümkün olan en yanıltıcı çıktı.
        var (bot, github) = Bot();
        github.Files["head-sha"] = "table users {\n  id int pk\n"; // kapanmayan blok

        await Assert.ThrowsAsync<NslParseException>(
            () => bot.HandleAsync("pull_request", PullRequestEvent()));

        Assert.Empty(github.CheckRuns);
    }

    // ── Yorum komutları ──────────────────────────────────────────────────────

    private static string CommentEvent(string body) => JsonSerializer.Serialize(new
    {
        action = "created",
        issue = new { number = 12 },
        comment = new { body },
        repository = new { name = "shop", owner = new { login = "acme" } },
        installation = new { id = 99L },
    });

    [Fact]
    public async Task The_help_command_is_answered_with_the_command_list()
    {
        var (bot, github) = Bot();

        await bot.HandleAsync("issue_comment", CommentEvent("/namines"));

        Assert.Contains("/namines plan", github.Comments[0].Body);
    }

    [Fact]
    public async Task A_recognised_but_unimplemented_command_says_so()
    {
        // Sessiz kalmak, komutu yazan kişiyi cevap beklerken bırakır ve bot'un
        // bozuk olduğunu düşündürür.
        var (bot, github) = Bot();

        await bot.HandleAsync("issue_comment", CommentEvent("/namines preview"));

        Assert.Contains("not implemented yet", github.Comments[0].Body);
    }

    [Fact]
    public async Task An_ordinary_comment_gets_no_reply()
    {
        // Her yoruma cevap veren bir bot tartışmayı okunamaz hâle getirir.
        var (bot, github) = Bot();

        await bot.HandleAsync("issue_comment", CommentEvent("Looks good to me!"));

        Assert.Empty(github.Comments);
    }

    // ── Kimlik bilgisi yokken ────────────────────────────────────────────────

    [Fact]
    public async Task Without_credentials_nothing_is_written_and_it_is_said_out_loud()
    {
        // Sahte bir başarı raporlamak, çalıştığı sanılan ama hiçbir şey yapmayan
        // bir özellik bırakırdı.
        var (bot, github) = Bot();
        github.IsConfigured = false;
        github.Files["head-sha"] = BaseSchema;

        var result = await bot.HandleAsync("pull_request", PullRequestEvent());

        Assert.Empty(github.Comments);
        Assert.Contains("GitHub App credentials", result);
    }
}
