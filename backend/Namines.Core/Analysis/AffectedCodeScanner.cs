using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Namines.Core.Models;

namespace Namines.Core.Analysis;

/// <summary>
/// G13 — "Etkilenen API/UI statik tahmini" (new-phase/28-IMPACT-ANALYSIS-ENGINE.md §5).
/// SAF fonksiyon — HTTP/DB'den ayrı, <see cref="ChangeRequestApprovalPolicy"/> ve
/// <see cref="SchemaImpactAnalyzer"/> ile aynı desen (iş kuralı ucuz birim testlerle
/// kanıtlanabilsin diye). Gateway/proje-içi export geçmişi yok (Faz 0'da yok) — bu yüzden
/// tarama HEDEFİ kullanıcının bu istekte yapıştırdığı/yüklediği dosyalardır, otomatik
/// keşfedilmiş bir kod tabanı değil.
///
/// Basit kelime-sınırı metin taraması — AST DEĞİL (doc'un izin verdiği iki seçenekten
/// ucuz olanı). Bir eşleşme YALNIZCA "bu isim bu dosyada geçiyor" demektir; yorum, string
/// literal veya aynı isimde alakasız bir şey olabilir. Kesinlik iddia edilmez.
/// </summary>
public static class AffectedCodeScanner
{
    /// <summary>ImpactReport'tan taranacak aday kimlik adlarını çıkarır — en yüksek sinyalli
    /// kaynak BreakingChanges'tir (doc'un kendi örneği: "değişen kolon adı, dosyalarda
    /// referans veriliyor mu?"), AffectedTables/ChangedColumns daha geniş bir ağ sağlar.</summary>
    public static IReadOnlyList<string> ExtractCandidateIdentifiers(ImpactReport impact)
    {
        var identifiers = new List<string>();

        foreach (var b in impact.BreakingChanges)
        {
            if (!string.IsNullOrWhiteSpace(b.ColumnName)) identifiers.Add(b.ColumnName!);
            if (!string.IsNullOrWhiteSpace(b.TableName)) identifiers.Add(b.TableName!);
        }

        foreach (var t in impact.AffectedTables)
        {
            if (t.Kind is ChangeKind.Removed or ChangeKind.RenamedFrom or ChangeKind.Modified)
                identifiers.Add(t.TableName);
            if (!string.IsNullOrWhiteSpace(t.PreviousName)) identifiers.Add(t.PreviousName!);
            identifiers.AddRange(t.ChangedColumns);
        }

        return identifiers
            .Where(id => id.Length >= 2) // tek harfli adlar gürültüden başka bir şey üretmez
            .Distinct()
            .ToList();
    }

    /// <summary><paramref name="files"/>: dosya adı → içerik. Her satırda kelime-sınırlı
    /// eşleşme aranır (büyük/küçük harf duyarsız).</summary>
    public static IReadOnlyList<AffectedCodeMatch> Scan(IReadOnlyList<string> identifiers, IReadOnlyDictionary<string, string> files)
    {
        if (identifiers.Count == 0) return new List<AffectedCodeMatch>();

        var pattern = string.Join("|", identifiers.Select(Regex.Escape));
        var regex = new Regex($@"\b(?:{pattern})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        var matches = new List<AffectedCodeMatch>();
        foreach (var (fileName, content) in files)
        {
            var lines = content.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var m = regex.Match(lines[i]);
                if (!m.Success) continue;

                matches.Add(new AffectedCodeMatch(fileName, i + 1, m.Value, lines[i].Trim()));
            }
        }

        return matches;
    }
}
