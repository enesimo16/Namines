using System;
using System.Collections.Generic;
using System.Linq;
using Namines.Core.Analysis;

namespace Namines.Core.Github;

/// <param name="Paths">İçeriği çekilecek dosyalar, önem sırasıyla.</param>
/// <param name="Skipped">Aday olabilecekken alınmayan her şey ve NEDENİ.</param>
public sealed record CandidateSelection(
    IReadOnlyList<string> Paths,
    IReadOnlyList<SkippedItem> Skipped);

/// <summary>
/// github/01-DEPO-TARAMA.md — depo ağacından hangi dosyaların okunacağına
/// karar verir.
///
/// <b>Ağdan bağımsız ve saf.</b> Taramanın asıl kararları (öncelik, gürültü
/// filtresi, bütçe) burada; HTTP olmadan test edilebilmeleri, bir depoyu yanlış
/// okuduğumuzda sebebin ağda mı kararda mı olduğunu ayırt edebilmemizi sağlıyor.
/// </summary>
public static class RepositoryCandidateSelector
{
    /// <summary>
    /// Aday bile sayılmayan yollar. <b>Bütçeden ÖNCE uygulanır:</b> sonra
    /// uygulansaydı 200 dosyalık bütçe bir Next.js deposunda ilk 200
    /// <c>node_modules</c> dosyasıyla dolar ve gerçek şema dosyasına hiç sıra
    /// gelmezdi.
    /// </summary>
    private static readonly string[] IgnoredSegments =
    {
        "/node_modules/", "/.next/", "/bin/", "/obj/", "/vendor/", "/dist/", "/build/", "/.git/",
    };

    /// <summary>
    /// Öncelik sırası; küçük sayı önce alınır. Bütçe dolduğunda kaybedilenin
    /// rastgele bir <c>.sql</c> olması, <c>schema.prisma</c> olmasından iyidir.
    /// </summary>
    private static int? Priority(string path)
    {
        var lower = path.ToLowerInvariant();

        // Namines'in kendi IR'ı: varsa çıkarım yapmaya bile gerek yok.
        if (lower.EndsWith("/schema.nsl", StringComparison.Ordinal) || lower == "schema.nsl" ||
            lower.EndsWith("/ir.json", StringComparison.Ordinal) || lower == "ir.json") return 0;

        if (lower.EndsWith(".prisma", StringComparison.Ordinal)) return 1;
        if (lower.Contains("migrations/", StringComparison.Ordinal) &&
            lower.EndsWith(".sql", StringComparison.Ordinal)) return 2;
        if (lower.EndsWith("dbcontext.cs", StringComparison.Ordinal)) return 3;
        if (lower.EndsWith(".cs", StringComparison.Ordinal) &&
            (lower.Contains("/entities/", StringComparison.Ordinal) ||
             lower.Contains("/models/", StringComparison.Ordinal) ||
             lower.Contains("/domain/", StringComparison.Ordinal))) return 4;
        if (lower.EndsWith(".sql", StringComparison.Ordinal)) return 5;

        return null;
    }

    public static CandidateSelection Select(IReadOnlyList<string> paths, int maxFiles)
    {
        var skipped = new List<SkippedItem>();
        var ranked = new List<(string Path, int Priority)>();

        foreach (var path in paths)
        {
            // Baştaki '/' kök seviyesindeki klasörlerin de desene uymasını
            // sağlıyor: "node_modules/x" yolunda "/node_modules/" aranınca
            // aksi hâlde eşleşmezdi.
            var probe = "/" + path.Replace('\\', '/');

            if (IgnoredSegments.Any(s => probe.Contains(s, StringComparison.OrdinalIgnoreCase)))
            {
                skipped.Add(new SkippedItem(path, "Build output or dependency directory."));
                continue;
            }

            var priority = Priority(path);

            // Sessizce atlanır: bir README ya da .ts dosyası "atlanan şema"
            // değil. Her depoda binlerce tane var; hepsini gerekçeyle
            // listelemek, gerçekten atlanan şema dosyalarını görünmez yapardı.
            if (priority is null) continue;

            ranked.Add((path, priority.Value));
        }

        var ordered = ranked
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Path, StringComparer.Ordinal)
            .ToList();

        var kept = ordered.Take(maxFiles).Select(r => r.Path).ToList();

        foreach (var (path, _) in ordered.Skip(maxFiles))
            skipped.Add(new SkippedItem(path, $"Exceeded the {maxFiles} file budget for one scan."));

        return new CandidateSelection(kept, skipped);
    }
}
