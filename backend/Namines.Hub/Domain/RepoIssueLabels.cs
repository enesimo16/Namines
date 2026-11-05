using System;
using System.Collections.Generic;
using System.Linq;

namespace Namines.Hub.Domain
{
    /// <summary>
    /// <c>hub.issue_labels</c>: projenin etiket kataloğu. Spesifikasyondaki global tablo yerine PROJE
    /// kapsamlı: global satırda bir kiracının yazımı ve etiket metni diğerine karışır, hesap
    /// silindiğinde metin geride kalırdı. Satır ilk kullanımda doğar, projeyle (FK) ve son issue
    /// bağı silinince (DB tetikleyicisi) ölür.
    /// </summary>
    public sealed class IssueLabel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ProjectId { get; set; } = string.Empty;

        /// <summary>İlk görülen yazım.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary><see cref="ProjectTagRules.Key"/>: kırpılmış, kültürden bağımsız küçük harf.</summary>
        public string NormalizedName { get; set; } = string.Empty;
    }

    /// <summary>
    /// <c>hub.repo_issue_labels</c>. <see cref="DisplayName"/> ve <see cref="Position"/> API'nin
    /// tarihsel metnini kayıpsız üretmek için: "Bug" yazan issue ile "bug" yazan issue aynı etikete
    /// bağlanır ama yazımını korur.
    /// </summary>
    public sealed class RepoIssueLabel
    {
        public string RepoIssueId { get; set; } = string.Empty;
        public string IssueLabelId { get; set; } = string.Empty;

        /// <summary>Bileşik FK'lerin ortak kolonu: issue ve etiket aynı projeden olmak zorunda (BL-C1).</summary>
        public string ProjectId { get; set; } = string.Empty;
        public IssueLabel Label { get; set; } = null!;
        public string DisplayName { get; set; } = string.Empty;
        public int Position { get; set; }
    }

    public sealed record ParsedIssueLabel(string Key, string Display, int Position);

    /// <summary>Etiket kuralları: kırp, boşları at, büyük/küçük harf duyarsız yinelemede ilk yazımı tut.</summary>
    public static class IssueLabelRules
    {
        public const int MaxNameLength = 256;

        public static IReadOnlyList<ParsedIssueLabel> Parse(string? csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<ParsedIssueLabel>();

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<ParsedIssueLabel>();
            foreach (var part in csv.Split(','))
            {
                var display = part.Trim();
                if (display.Length == 0) continue;
                var key = ProjectTagRules.Key(display);
                if (!seen.Add(key)) continue;
                result.Add(new ParsedIssueLabel(key, display, result.Count));
            }
            return result;
        }

        /// <summary>API'nin tarihsel biçimi; etiket yoksa null.</summary>
        public static string? ToCsv(IEnumerable<string> orderedDisplayNames)
        {
            var list = orderedDisplayNames.ToList();
            return list.Count == 0 ? null : string.Join(",", list);
        }
    }
}
