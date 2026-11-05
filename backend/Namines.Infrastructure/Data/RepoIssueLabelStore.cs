using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Hub;

namespace Namines.Infrastructure.Data;

/// <summary>Issue etiketlerinin tek yazma yolu: katalog satırını bulur ya da açar, issue'ya bağlar.</summary>
public static class RepoIssueLabelStore
{
    /// <summary>
    /// Metni ayrıştırıp bağları issue'ya ekler; kaydetmez, çağıranın <c>SaveChanges</c>'iyle tek
    /// işlemde yazılır. İki istek aynı yeni etiketi aynı anda açarsa UNIQUE ikincisini reddeder;
    /// çağıran eklenenleri sökmeli (<see cref="DetachPending"/>) ve yeniden denemeli.
    /// </summary>
    public static async Task AttachAsync(
        this AuthDbContext context, RepoIssue issue, string? labelsCsv, CancellationToken ct = default)
    {
        var parsed = IssueLabelRules.Parse(labelsCsv);
        if (parsed.Count == 0) return;

        var keys = parsed.Select(p => p.Key).ToList();
        var existing = await context.Set<IssueLabel>()
            .Where(l => l.ProjectId == issue.ProjectId && keys.Contains(l.NormalizedName))
            .ToDictionaryAsync(l => l.NormalizedName, StringComparer.Ordinal, ct);

        foreach (var label in parsed)
        {
            if (!existing.TryGetValue(label.Key, out var row))
            {
                row = new IssueLabel { ProjectId = issue.ProjectId, Name = label.Display, NormalizedName = label.Key };
                context.Set<IssueLabel>().Add(row);
            }

            issue.LabelLinks.Add(new RepoIssueLabel
            {
                RepoIssueId = issue.Id,
                Label = row,
                DisplayName = label.Display,
                Position = label.Position,
            });
        }
    }

    /// <summary>Başarısız kayıttan sonra: yeni deneme eski (Added) satırları tekrar yazmasın.</summary>
    public static void DetachPending(this AuthDbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries()
                     .Where(e => e.State == EntityState.Added && e.Entity is RepoIssue or IssueLabel or RepoIssueLabel)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Etiket filtresi: tam eşleşme, büyük/küçük harf duyarsız. Proje koşulu bilerek yineleniyor:
    /// UNIQUE (ProjectId, NormalizedName) ancak onunla kullanılabilir, yoksa katalog taranır.
    /// </summary>
    public static IQueryable<RepoIssue> WithLabel(this IQueryable<RepoIssue> query, string projectId, string label)
    {
        var key = Namines.Core.Models.Auth.ProjectTagRules.Key(label);
        return query.Where(i => i.LabelLinks.Any(l => l.Label.ProjectId == projectId && l.Label.NormalizedName == key));
    }
}
