using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Namines.Core.Analysis;

/// <summary>
/// second-phase/11-KODDAN-SEMA.md — verilen dosyalarda hangi ORM olduğunu
/// tanır ve doğru ayrıştırıcıya yönlendirir.
///
/// <b>Sınırlar burada zorlanıyor</b> (doc: "büyük depolarda tarama
/// sınırlandırılmalı"). Sınır aşılırsa istek REDDEDİLMİYOR — dosyalar
/// sıralanıp ilk N tanesi alınıyor ve atlananlar açıkça bildiriliyor;
/// sessizce yarım bir sonuç üretip "işte şeman" demek, bu doc'un
/// baştan yasakladığı şey.
/// </summary>
public static class CodeSchemaExtractor
{
    public const int MaxFiles = 200;
    public const int MaxTotalBytes = 2_000_000;

    /// <summary>Ayrıştırılabilecek bir şey bulunamadığında fırlatılır — çağıran bunu 400'e çevirir.</summary>
    public sealed class UnknownFormatException : Exception
    {
        public UnknownFormatException(string message) : base(message) { }
    }

    public static CodeExtractionResult Extract(IReadOnlyDictionary<string, string> files)
    {
        if (files.Count == 0)
            throw new UnknownFormatException("No files were provided.");

        var (limited, limitSkips) = ApplyLimits(files);

        // Prisma önce: `.prisma` uzantısı kesin bir sinyal, C# taraması ise
        // buluşsal. Belirsizlik olduğunda kesin olanı seçmek doğru.
        var prismaFiles = limited
            .Where(f => f.Key.EndsWith(".prisma", StringComparison.OrdinalIgnoreCase) ||
                        f.Value.Contains("generator client", StringComparison.Ordinal))
            .ToList();

        if (prismaFiles.Count > 0)
        {
            var combined = string.Join("\n", prismaFiles.Select(f => f.Value));
            var result = PrismaSchemaParser.Parse(combined);
            return result with { Skipped = result.Skipped.Concat(limitSkips).ToList() };
        }

        var csharpFiles = limited
            .Where(f => f.Key.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);

        if (csharpFiles.Count > 0)
        {
            var result = EfCoreEntityParser.Parse(csharpFiles);
            return result with { Skipped = result.Skipped.Concat(limitSkips).ToList() };
        }

        // Ham SQL — Supabase migration klasörünün biçimi
        // (second-phase/12-ENTEGRASYONLAR.md adım 2). Dosya adına göre SIRALI
        // birleştiriliyor: migration'lar zaman sırasıyla adlandırılır ve sonraki
        // bir dosya öncekinin tablosunu değiştirebilir.
        var sqlFiles = limited
            .Where(f => f.Key.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .ToList();

        if (sqlFiles.Count > 0)
        {
            var combined = string.Join("\n", sqlFiles.Select(f => f.Value));
            var result = SqlDdlSchemaParser.Parse(combined);
            return result with { Skipped = result.Skipped.Concat(limitSkips).ToList() };
        }

        // Doc'un kuralı: tanınmayan formatta AI'ya düşmek "son çare" olarak
        // düşünülmüştü ama bu kademede YAPILMIYOR — iki formatı gerçekten iyi
        // yapmak, sekizini yarım yapmaktan değerli. Uydurmak yerine dürüstçe
        // "tanıyamadım" deniyor.
        throw new UnknownFormatException(
            "Could not recognise the format. Supported today: Prisma (.prisma), EF Core entity classes (.cs), and raw SQL migrations (.sql).");
    }

    private static (IReadOnlyDictionary<string, string> Files, List<SkippedItem> Skips) ApplyLimits(
        IReadOnlyDictionary<string, string> files)
    {
        var skips = new List<SkippedItem>();
        var kept = new Dictionary<string, string>(StringComparer.Ordinal);
        var totalBytes = 0;

        // İlgili olma ihtimali yüksek dosyalar önce: sınıra takılırsa
        // kesilenler en az bilgi taşıyanlar olsun.
        var ordered = files.OrderByDescending(Relevance).ThenBy(f => f.Key, StringComparer.Ordinal);

        foreach (var file in ordered)
        {
            if (kept.Count >= MaxFiles)
            {
                skips.Add(new SkippedItem(file.Key, $"file limit reached ({MaxFiles})"));
                continue;
            }

            var size = Encoding.UTF8.GetByteCount(file.Value);
            if (totalBytes + size > MaxTotalBytes)
            {
                skips.Add(new SkippedItem(file.Key, $"total size limit reached ({MaxTotalBytes / 1000} KB)"));
                continue;
            }

            totalBytes += size;
            kept[file.Key] = file.Value;
        }

        return (kept, skips);
    }

    private static int Relevance(KeyValuePair<string, string> file)
    {
        if (file.Key.EndsWith(".prisma", StringComparison.OrdinalIgnoreCase)) return 4;
        if (file.Value.Contains("DbSet<", StringComparison.Ordinal)) return 3;
        if (file.Key.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return 2;
        if (file.Key.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)) return 1;
        return 0;
    }
}
