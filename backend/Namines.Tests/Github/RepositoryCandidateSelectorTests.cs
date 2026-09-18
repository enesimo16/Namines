using System.Linq;
using Namines.Core.Github;

namespace Namines.Tests.Github;

/// <summary>github/01-DEPO-TARAMA.md — aday seçimi ve sınırlar.</summary>
public class RepositoryCandidateSelectorTests
{
    [Theory]
    [InlineData("https://github.com/acme/shop", "acme", "shop")]
    [InlineData("https://github.com/acme/shop.git", "acme", "shop")]
    [InlineData("https://github.com/acme/shop/tree/main/src", "acme", "shop")]
    [InlineData("github.com/acme/shop", "acme", "shop")]
    public void Repository_urls_in_the_shapes_people_paste_are_understood(string url, string owner, string name)
    {
        Assert.True(GithubRepositoryUrl.TryParse(url, out var repo));
        Assert.Equal(owner, repo!.Owner);
        Assert.Equal(name, repo.Name);
    }

    [Theory]
    [InlineData("https://gitlab.com/acme/shop")]
    [InlineData("https://github.com.evil.example/acme/shop")]
    [InlineData("http://localhost/acme/shop")]
    [InlineData("https://github.com/acme")]
    [InlineData("not a url")]
    public void Anything_that_is_not_a_github_repository_is_refused(string url)
    {
        // Host beyaz listesi bir SSRF sınırı: sunucu yalnızca github.com'a
        // çıkar. "github.com.evil.example" tam da naif bir `Contains`
        // kontrolünün kaçıracağı biçim.
        Assert.False(GithubRepositoryUrl.TryParse(url, out _));
    }

    [Fact]
    public void Schema_files_are_preferred_over_everything_else()
    {
        var paths = new[] { "README.md", "src/app.ts", "prisma/schema.prisma" };

        var selection = RepositoryCandidateSelector.Select(paths, maxFiles: 200);

        Assert.Equal("prisma/schema.prisma", selection.Paths[0]);
    }

    [Fact]
    public void Namines_own_schema_file_wins_when_present()
    {
        var paths = new[] { "prisma/schema.prisma", ".namines/schema.nsl" };

        var selection = RepositoryCandidateSelector.Select(paths, maxFiles: 200);

        Assert.Equal(".namines/schema.nsl", selection.Paths[0]);
    }

    [Fact]
    public void Build_output_and_dependencies_never_become_candidates()
    {
        // Bu filtre olmadan bir Next.js deposunda 200 dosyalık bütçe ilk 200
        // node_modules dosyasıyla dolar ve gerçek şema dosyasına hiç sıra
        // gelmez.
        var paths = new[]
        {
            "node_modules/pkg/schema.prisma",
            ".next/cache/x.sql",
            "backend/bin/Debug/Model.cs",
            "vendor/lib/schema.sql",
            "prisma/schema.prisma",
        };

        var selection = RepositoryCandidateSelector.Select(paths, maxFiles: 200);

        Assert.Equal(new[] { "prisma/schema.prisma" }, selection.Paths);
        Assert.Equal(4, selection.Skipped.Count);
        Assert.All(selection.Skipped, s => Assert.Contains("Build output", s.Reason));
    }

    [Fact]
    public void Files_that_no_parser_understands_are_not_candidates()
    {
        var selection = RepositoryCandidateSelector.Select(
            new[] { "README.md", "infra/main.tf", "src/app.ts" }, maxFiles: 200);

        Assert.Empty(selection.Paths);
    }

    [Fact]
    public void Over_the_limit_files_are_reported_not_dropped_silently()
    {
        var paths = Enumerable.Range(0, 5).Select(i => $"db/migrations/{i:D3}_init.sql").ToArray();

        var selection = RepositoryCandidateSelector.Select(paths, maxFiles: 2);

        Assert.Equal(2, selection.Paths.Count);
        Assert.Equal(3, selection.Skipped.Count);
        Assert.All(selection.Skipped, s => Assert.Contains("file budget", s.Reason));
    }
}
