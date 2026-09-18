using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Namines.API.Controllers;
using Namines.Core.Analysis;
using Namines.Core.Github;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.Tests.Controllers;

/// <summary>github/01-DEPO-TARAMA.md — tarama ucunun sözleşmesi.</summary>
public class GithubScanControllerTests
{
    private sealed class StubScanner : IRepositoryScanner
    {
        public GithubRepository? Seen { get; private set; }
        public Exception? Throw { get; init; }

        public Task<RepositoryScanResult> ScanAsync(
            GithubRepository repository, string? branch, long? installationId, CancellationToken ct = default)
        {
            Seen = repository;
            if (Throw is not null) throw Throw;

            return Task.FromResult(new RepositoryScanResult(
                new DatabaseSchema { Tables = new List<SchemaTable>() },
                "prisma", branch ?? "main",
                new[] { "prisma/schema.prisma" },
                new[] { new SkippedItem("node_modules/x.sql", "Build output or dependency directory.") },
                TreeTruncated: false,
                Warnings: Array.Empty<string>()));
        }
    }

    private static ScanRepositoryRequest Request(string url) => new(url, null, null);

    [Fact]
    public async Task A_valid_repository_url_is_scanned()
    {
        var scanner = new StubScanner();

        var result = await new GithubScanController(scanner)
            .Scan(Request("https://github.com/acme/shop"), default) as OkObjectResult;

        Assert.NotNull(result);
        Assert.Equal("acme", scanner.Seen!.Owner);
        Assert.Equal("shop", scanner.Seen.Name);
    }

    [Fact]
    public async Task A_url_that_is_not_a_github_repository_is_rejected_before_any_request()
    {
        // Adres doğrulaması bir SSRF sınırı: reddedilen istek AĞA HİÇ ÇIKMAMALI.
        var scanner = new StubScanner();

        var result = await new GithubScanController(scanner)
            .Scan(Request("https://gitlab.com/acme/shop"), default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null(scanner.Seen);
    }

    [Fact]
    public async Task A_private_repository_is_answered_with_what_to_do_next()
    {
        var scanner = new StubScanner
        {
            Throw = new GithubRepositoryUnavailableException(
                "acme/secret could not be read. It does not exist, or it is private — " +
                "a private repository needs a GitHub App installation."),
        };

        var result = await new GithubScanController(scanner)
            .Scan(Request("https://github.com/acme/secret"), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("private", System.Text.Json.JsonSerializer.Serialize(bad.Value), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rate_limiting_reaches_the_user_as_itself()
    {
        var scanner = new StubScanner
        {
            Throw = new GithubRateLimitedException("GitHub's hourly limit for anonymous requests is used up."),
        };

        var result = await new GithubScanController(scanner)
            .Scan(Request("https://github.com/acme/shop"), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("hourly limit", System.Text.Json.JsonSerializer.Serialize(bad.Value));
    }

    [Fact]
    public async Task An_unrecognised_repository_says_so_instead_of_returning_an_empty_schema()
    {
        var scanner = new StubScanner
        {
            Throw = new CodeSchemaExtractor.UnknownFormatException("Could not recognise the format."),
        };

        var result = await new GithubScanController(scanner)
            .Scan(Request("https://github.com/acme/shop"), default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task What_was_skipped_is_part_of_the_answer()
    {
        // Atlananları yanıttan düşürmek, kullanıcının eksik şemayı tam
        // sanmasının tek sebebi olur (01-DEPO-TARAMA.md).
        var result = await new GithubScanController(new StubScanner())
            .Scan(Request("https://github.com/acme/shop"), default) as OkObjectResult;

        var json = System.Text.Json.JsonSerializer.Serialize(result!.Value);

        Assert.Contains("node_modules/x.sql", json);
        Assert.Contains("Build output or dependency directory.", json);
        Assert.Contains("treeTruncated", json);
    }
}
