using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Analysis;
using Namines.Core.Github;
using Namines.Core.Interfaces;

namespace Namines.Infrastructure.Services;

/// <inheritdoc cref="IRepositoryScanner"/>
public sealed class RepositoryScanner : IRepositoryScanner
{
    /// <summary>
    /// Kimliksiz bir taramanın çekebileceği en fazla dosya.
    ///
    /// <b>GitHub'ın anonim sınırı saatte 60 istek ve bu sınır IP başına</b> —
    /// yani sunucunun tamamı için. Ayrıştırıcının 200 dosyalık bütçesiyle
    /// tarama yapmak, TEK bir kullanıcının tek bir deposunun bütün kullanıcılara
    /// ait saatlik kotayı bitirmesi demekti; canlı doğrulamada tam olarak bu
    /// yaşandı. 40, ağaç + varsayılan dal çağrılarıyla birlikte kotanın
    /// yarısından azını harcıyor ve sonraki taramalara yer bırakıyor.
    ///
    /// Aşan dosyalar ATILMIYOR, "atlandı" olarak gerekçesiyle bildiriliyor.
    /// </summary>
    public const int AnonymousMaxFiles = 40;

    private readonly IGithubClient _github;

    public RepositoryScanner(IGithubClient github) => _github = github;

    private static int MaxFilesFor(long? installationId) =>
        installationId is null ? AnonymousMaxFiles : CodeSchemaExtractor.MaxFiles;

    public async Task<RepositoryScanResult> ScanAsync(
        GithubRepository repository, string? branch, long? installationId,
        CancellationToken cancellationToken = default)
    {
        var reference = branch;

        // Kullanıcı dal seçmediyse deponun KENDİ varsayılanı sorulur; "main"
        // varsaymak, varsayılanı "master" ya da "develop" olan depolarda
        // sessizce boş sonuç üretirdi.
        if (string.IsNullOrWhiteSpace(reference))
            reference = await _github.GetDefaultBranchAsync(repository, installationId, cancellationToken) ?? "main";

        var tree = await _github.GetRepositoryTreeAsync(repository, reference, installationId, cancellationToken);

        // Bütçe AĞAÇTA uygulanıyor, indirdikten sonra değil: sınırın amacı
        // ayrıştırma maliyetinden önce ağ maliyetini sınırlamak. Ayrıştırıcının
        // kendi sınırı ikinci bir savunma olarak yerinde kalıyor.
        var selection = RepositoryCandidateSelector.Select(tree.Paths, MaxFilesFor(installationId));

        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var skipped = new List<SkippedItem>(selection.Skipped);

        foreach (var path in selection.Paths)
        {
            var content = await _github.GetFileContentAsync(
                repository, installationId, path, reference, cancellationToken);

            // null: dosya ağaç listelendikten sonra kaybolmuş (dal ilerlemiş)
            // ya da okunamamış. Sessizce atlamak, eksik şemayı tam gibi
            // göstermek olurdu.
            if (content is null) skipped.Add(new SkippedItem(path, "Could not be read at this ref."));
            else files[path] = content;
        }

        // Hiçbir dosya okunamadığında da ayrıştırıcıya gidiliyor: "tanıyamadım"
        // mesajını üreten ve tek elden yöneten yer orası. Burada ikinci bir
        // metin yazmak, aynı durumun iki farklı cevabı olurdu.
        var extraction = CodeSchemaExtractor.Extract(files);

        return new RepositoryScanResult(
            extraction.Schema,
            extraction.Format,
            reference,
            files.Keys.ToList(),
            skipped.Concat(extraction.Skipped).ToList(),
            tree.Truncated);
    }
}
