using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Github;
using Namines.Core.Models;
using Namines.Core.Nsl;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Namines Bot'un olay işleyicisi (11 §7).
///
/// <b>Bot yeni bulgu üretmiyor.</b> Yaptığı iş, PR'daki <c>.nsl</c> dosyasının
/// taban ve baş hâlini okumak, aradaki farkı <see cref="SchemaImpactAnalyzer"/>'a
/// vermek ve çıkan raporu <see cref="PullRequestReviewComposer"/> ile insan
/// diline çevirip yayımlamak. Karar veren kural motoru, dil modeli değil — PR'da
/// gördüğü tabloya bakıp merge kararı veren insan için bu fark belirleyici.
/// </summary>
public interface IGithubBotService
{
    /// <param name="eventName">GitHub'ın <c>X-GitHub-Event</c> başlığı.</param>
    /// <returns>Yapılan işin insan tarafından okunabilir özeti.</returns>
    Task<string> HandleAsync(string eventName, string payload, CancellationToken cancellationToken = default);
}

public sealed class GithubBotService : IGithubBotService
{
    private const string CheckName = "Namines schema review";

    private readonly IGithubClient _github;
    private readonly ILogger<GithubBotService> _logger;
    private readonly string _schemaPath;

    public GithubBotService(IGithubClient github, IConfiguration configuration, ILogger<GithubBotService> logger)
    {
        _github = github;
        _logger = logger;

        // Yol yapılandırılabilir: her deponun şemayı aynı yere koyduğunu varsaymak,
        // özelliği yalnızca bizim örneğimize benzeyen depolarda çalıştırırdı.
        _schemaPath = configuration["Github:SchemaPath"] ?? "schema.nsl";
    }

    public async Task<string> HandleAsync(string eventName, string payload, CancellationToken cancellationToken = default)
    {
        if (!_github.IsConfigured)
        {
            // Sahte bir başarı raporlamak, çalıştığı sanılan ama hiçbir şey yapmayan
            // bir özellik bırakırdı. Olay kabul ediliyor, yazılmadığı SÖYLENİYOR.
            _logger.LogInformation("GitHub event {Event} accepted but not answered: the app is not configured.", eventName);
            return "Accepted. Posting back to GitHub needs the Namines GitHub App credentials.";
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        return eventName?.ToLowerInvariant() switch
        {
            "pull_request" => await HandlePullRequestAsync(root, cancellationToken),
            "issue_comment" => await HandleCommentAsync(root, cancellationToken),
            _ => $"Ignored: {eventName} is not an event the bot acts on.",
        };
    }

    private async Task<string> HandlePullRequestAsync(JsonElement root, CancellationToken ct)
    {
        var action = Text(root, "action");

        // Yalnızca şemanın değişmiş OLABİLECEĞİ eylemler. "labeled" ya da
        // "assigned" gibi olaylarda yeniden inceleme yapmak, aynı yorumu
        // tekrar tekrar yazmak olurdu.
        if (action is not ("opened" or "reopened" or "synchronize"))
            return $"Ignored: pull_request/{action} does not change the schema.";

        var context = ReadContext(root);
        if (context is null) return "Ignored: the payload did not identify a repository or installation.";

        var pullRequest = root.GetProperty("pull_request");
        var number = pullRequest.GetProperty("number").GetInt32();
        var headSha = Text(pullRequest.GetProperty("head"), "sha") ?? string.Empty;
        var headRef = Text(pullRequest.GetProperty("head"), "sha");
        var baseRef = Text(pullRequest.GetProperty("base"), "sha");

        var head = await ReadSchemaAsync(context.Value, headRef, ct);

        // Baş ref'te şema yoksa bu PR şemaya dokunmuyor. Her PR'a "şema bulunamadı"
        // yazmak, bot'u gürültüye çevirir ve insanlar yorumlarını okumayı bırakır.
        if (head is null)
            return $"Ignored: {_schemaPath} is not in this pull request.";

        // Taban ref'te yoksa şema bu PR'da İLK KEZ ekleniyor; boş şemaya karşı
        // karşılaştırmak doğru cevabı verir ("her şey yeni"), hata değil.
        var baseline = await ReadSchemaAsync(context.Value, baseRef, ct) ?? new DatabaseSchema();

        var report = SchemaImpactAnalyzer.Analyze(baseline, head, DatabaseType.PostgreSQL);
        var review = PullRequestReviewComposer.Compose(report);

        await _github.PostCommentAsync(context.Value.Repository, context.Value.InstallationId, number, review.Body, ct);

        await _github.CreateCheckRunAsync(
            context.Value.Repository, context.Value.InstallationId, headSha,
            CheckName, review.Conclusion, review.Title, review.Summary, review.Body, ct);

        return $"Reviewed pull request #{number}: {review.Conclusion}.";
    }

    private async Task<string> HandleCommentAsync(JsonElement root, CancellationToken ct)
    {
        if (Text(root, "action") != "created")
            return "Ignored: only new comments are read.";

        var context = ReadContext(root);
        if (context is null) return "Ignored: the payload did not identify a repository or installation.";

        var comment = root.TryGetProperty("comment", out var c) ? Text(c, "body") : null;
        var command = BotCommandParser.Parse(comment);

        // Komut içermeyen yorum sessizce geçiliyor: her yoruma cevap veren bir bot,
        // tartışmayı okunamaz hâle getirir.
        if (command is null) return "Ignored: no /namines command in the comment.";

        var number = root.GetProperty("issue").GetProperty("number").GetInt32();

        if (command.Name == "help")
        {
            await _github.PostCommentAsync(context.Value.Repository, context.Value.InstallationId,
                number, BotCommandParser.HelpText(), ct);
            return "Answered with the command list.";
        }

        // Uygulanmamış komutlar için AÇIKÇA "henüz yok" deniyor. Sessiz kalmak,
        // komutu yazan kişiyi cevap beklerken bırakır ve bot'un bozuk olduğunu
        // düşündürür.
        await _github.PostCommentAsync(context.Value.Repository, context.Value.InstallationId, number,
            $"`/namines {command.Name}` is recognised but not implemented yet. " +
            "The schema review runs automatically on every push to this pull request.", ct);

        return $"Answered /namines {command.Name} with a not-implemented note.";
    }

    private async Task<DatabaseSchema?> ReadSchemaAsync(BotContext context, string? reference, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;

        var text = await _github.GetFileContentAsync(
            context.Repository, context.InstallationId, _schemaPath, reference, ct);

        if (string.IsNullOrWhiteSpace(text)) return null;

        try
        {
            return NslParser.Parse(text);
        }
        catch (NslParseException ex)
        {
            // Ayrıştırılamayan bir şema, "değişiklik yok" DEĞİLDİR. Sessizce boş
            // şema saymak, bozuk bir dosyayı "risk yok" diye raporlamak olurdu.
            _logger.LogWarning(ex, "Could not parse {Path} at {Ref}.", _schemaPath, reference);
            throw;
        }
    }

    private readonly record struct BotContext(GithubRepository Repository, long InstallationId);

    private static BotContext? ReadContext(JsonElement root)
    {
        if (!root.TryGetProperty("repository", out var repo)) return null;
        if (!root.TryGetProperty("installation", out var installation)) return null;

        var owner = repo.TryGetProperty("owner", out var o) ? Text(o, "login") : null;
        var name = Text(repo, "name");

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(name)) return null;
        if (!installation.TryGetProperty("id", out var id)) return null;

        return new BotContext(new GithubRepository(owner, name), id.GetInt64());
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
