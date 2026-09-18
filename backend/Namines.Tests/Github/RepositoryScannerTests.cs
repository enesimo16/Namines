using Namines.Core.Analysis;
using Namines.Core.Github;
using Namines.Infrastructure.Services;

namespace Namines.Tests.Github;

/// <summary>
/// github/01-DEPO-TARAMA.md — depo → şema zinciri.
///
/// <b>Ağ sahte, karar gerçek:</b> tarayıcının işi dosya TOPLAMAK ve var olan
/// ayrıştırıcıya vermek. Bu testler hangi dosyanın çekildiğini ve neyin
/// atlandığının bildirildiğini kilitliyor.
/// </summary>
public class RepositoryScannerTests
{
    private sealed class FakeGithub : IGithubClient
    {
        public Dictionary<string, string> Files { get; init; } = new();
        public List<string> Paths { get; init; } = new();
        public bool Truncated { get; init; }
        public string DefaultBranch { get; init; } = "main";

        /// <summary>Hangi dosyaların GERÇEKTEN indirildiği — bütçenin kanıtı.</summary>
        public List<string> Fetched { get; } = new();
        public List<string> References { get; } = new();

        public bool IsConfigured => false;

        public Task<string?> GetDefaultBranchAsync(GithubRepository r, long? i, CancellationToken ct = default) =>
            Task.FromResult<string?>(DefaultBranch);

        public Task<RepositoryTree> GetRepositoryTreeAsync(GithubRepository r, string reference, long? i, CancellationToken ct = default)
        {
            References.Add(reference);
            return Task.FromResult(new RepositoryTree(Paths, Truncated));
        }

        public Task<string?> GetFileContentAsync(GithubRepository r, long? i, string path, string reference, CancellationToken ct = default)
        {
            Fetched.Add(path);
            return Task.FromResult(Files.TryGetValue(path, out var content) ? content : null);
        }

        public Task PostCommentAsync(GithubRepository r, long i, int n, string b, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task CreateCheckRunAsync(GithubRepository r, long i, string sha, string n, string c, string t, string s, string b, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private const string PrismaSchema = "model User {\n  id Int @id\n}";

    [Fact]
    public async Task A_prisma_repository_becomes_a_schema()
    {
        var github = new FakeGithub
        {
            Paths = { "README.md", "prisma/schema.prisma" },
            Files = { ["prisma/schema.prisma"] = PrismaSchema },
        };

        var result = await new RepositoryScanner(github).ScanAsync(
            new GithubRepository("acme", "shop"), branch: null, installationId: null);

        Assert.Equal("prisma", result.Format);
        Assert.Equal("main", result.Branch);
        Assert.Contains(result.Schema.Tables, t => t.Name == "User");

        // README hiç indirilmedi: bütçe ağaçta uygulanıyor, indirdikten sonra
        // değil — sınırın amacı ayrıştırma maliyetinden önce AĞ maliyeti.
        Assert.Equal(new[] { "prisma/schema.prisma" }, github.Fetched);
    }

    [Fact]
    public async Task The_requested_branch_wins_over_the_default()
    {
        var github = new FakeGithub
        {
            Paths = { "prisma/schema.prisma" },
            Files = { ["prisma/schema.prisma"] = PrismaSchema },
        };

        var result = await new RepositoryScanner(github).ScanAsync(
            new GithubRepository("acme", "shop"), branch: "feature/x", installationId: null);

        Assert.Equal("feature/x", result.Branch);
        Assert.Equal(new[] { "feature/x" }, github.References);
    }

    [Fact]
    public async Task A_truncated_tree_travels_all_the_way_to_the_caller()
    {
        var github = new FakeGithub
        {
            Truncated = true,
            Paths = { "prisma/schema.prisma" },
            Files = { ["prisma/schema.prisma"] = PrismaSchema },
        };

        var result = await new RepositoryScanner(github).ScanAsync(
            new GithubRepository("acme", "shop"), branch: null, installationId: null);

        Assert.True(result.TreeTruncated);
    }

    [Fact]
    public async Task A_repository_with_nothing_parseable_says_so_instead_of_guessing()
    {
        var github = new FakeGithub { Paths = { "README.md", "src/app.ts" } };

        await Assert.ThrowsAsync<CodeSchemaExtractor.UnknownFormatException>(() =>
            new RepositoryScanner(github).ScanAsync(
                new GithubRepository("acme", "shop"), branch: null, installationId: null));
    }

    [Fact]
    public async Task A_file_that_vanished_between_listing_and_reading_is_reported()
    {
        // Ağaç listelemesi ile içerik okuması arasında dal ilerleyebilir.
        // Sessizce atlamak, eksik şemayı tam gibi göstermek olurdu.
        var github = new FakeGithub
        {
            Paths = { "prisma/schema.prisma", "db/migrations/001.sql" },
            Files = { ["prisma/schema.prisma"] = PrismaSchema },
        };

        var result = await new RepositoryScanner(github).ScanAsync(
            new GithubRepository("acme", "shop"), branch: null, installationId: null);

        Assert.Contains(result.Skipped, s => s.Name == "db/migrations/001.sql");
    }

    [Fact]
    public async Task An_anonymous_scan_stays_inside_githubs_hourly_budget()
    {
        // CANLI DOĞRULAMADA BULUNDU: anonim sınır saatte 60 istek ve IP
        // başına — yani sunucunun tamamı için. 200 dosyalık bütçeyle tarama
        // yapmak, tek bir kullanıcının tek bir deposunun herkesin kotasını
        // bitirmesi demekti. Aşanlar atılmıyor, bildiriliyor.
        var paths = Enumerable.Range(0, 60).Select(i => $"db/migrations/{i:D3}_init.sql").ToList();
        var files = paths.ToDictionary(p => p, _ => "CREATE TABLE users (id int primary key);");

        var github = new FakeGithub { Paths = paths, Files = files };

        var result = await new RepositoryScanner(github).ScanAsync(
            new GithubRepository("acme", "shop"), branch: null, installationId: null);

        Assert.Equal(RepositoryScanner.AnonymousMaxFiles, github.Fetched.Count);
        Assert.Contains(result.Skipped, s => s.Reason.Contains("file budget"));
    }

    [Fact]
    public async Task What_the_selector_skipped_reaches_the_caller_too()
    {
        var github = new FakeGithub
        {
            Paths = { "node_modules/pkg/schema.prisma", "prisma/schema.prisma" },
            Files = { ["prisma/schema.prisma"] = PrismaSchema },
        };

        var result = await new RepositoryScanner(github).ScanAsync(
            new GithubRepository("acme", "shop"), branch: null, installationId: null);

        Assert.Contains(result.Skipped, s => s.Name == "node_modules/pkg/schema.prisma");
    }
}
