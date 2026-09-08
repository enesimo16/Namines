using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Analysis;

public enum CascadeIssueKind
{
    /// <summary>Aynı tabloya birden fazla cascade yolu. SQL Server Msg 1785 ile reddeder.</summary>
    MultipleCascadePaths,

    /// <summary>Cascade döngüsü (A→B→A veya A→A). Tüm motorlarda sorunlu.</summary>
    CascadeCycle,

    /// <summary>SET NULL hedefi NOT NULL kolon — çalışma zamanında ihlal üretir.</summary>
    SetNullOnNotNullColumn,

    /// <summary>SET DEFAULT hedefinin DEFAULT değeri yok.</summary>
    SetDefaultWithoutDefaultValue
}

public sealed record CascadeIssue(
    CascadeIssueKind Kind,
    string Message,
    string? RelationId = null,
    string? FromTable = null,
    string? ToTable = null);

/// <summary>
/// Yabancı anahtar davranışlarını DDL üretilmeden ÖNCE denetler.
///
/// Amaç: çalıştırılamayan veya veri kaybettiren DDL'in kullanıcıya ulaşmasını engellemek.
///
/// En kritik kontrol <see cref="CascadeIssueKind.MultipleCascadePaths"/>. SQL Server,
/// bir tabloya birden fazla cascade yolu olan şemayı reddeder:
///
///   Msg 1785: Introducing FOREIGN KEY constraint '...' on table '...' may cause
///             cycles or multiple cascade paths.
///
/// Bu, e-ticarette son derece yaygın bir modelde ortaya çıkar:
///   Orders → Users            (doğrudan)
///   Orders → Addresses → Users (dolaylı)
/// </summary>
public static class FkCascadeAnalyzer
{
    /// <summary>Cascade davranışını "yayılan" sayan fiiller — grafikte kenar oluştururlar.</summary>
    private static bool Propagates(ReferentialAction action) =>
        action is ReferentialAction.Cascade or ReferentialAction.SetNull or ReferentialAction.SetDefault;

    public static IReadOnlyList<CascadeIssue> Analyze(DatabaseSchema schema, DatabaseType engine)
    {
        var issues = new List<CascadeIssue>();
        if (schema.Relations is null || schema.Relations.Count == 0) return issues;

        var tablesById = schema.Tables.ToDictionary(t => t.Id, t => t);

        // ── 1. Kolon seviyesi tutarlılık ──────────────────────────────────────
        foreach (var rel in schema.Relations)
        {
            if (!tablesById.TryGetValue(rel.SourceTableId, out var sourceTable)) continue;
            var sourceCol = sourceTable.Columns.FirstOrDefault(c => c.Id == rel.SourceColumnId);
            if (sourceCol is null) continue;

            if (rel.OnDelete == ReferentialAction.SetNull && !sourceCol.IsNullable)
            {
                issues.Add(new CascadeIssue(
                    CascadeIssueKind.SetNullOnNotNullColumn,
                    $"'{sourceTable.Name}.{sourceCol.Name}' uses ON DELETE SET NULL, but the column is NOT NULL. " +
                    "Make the column nullable, or pick a different delete action.",
                    rel.Id, sourceTable.Name));
            }

            if (rel.OnDelete == ReferentialAction.SetDefault && string.IsNullOrWhiteSpace(sourceCol.DefaultValue))
            {
                issues.Add(new CascadeIssue(
                    CascadeIssueKind.SetDefaultWithoutDefaultValue,
                    $"'{sourceTable.Name}.{sourceCol.Name}' uses ON DELETE SET DEFAULT, but the column has no " +
                    "DEFAULT value to fall back to.",
                    rel.Id, sourceTable.Name));
            }
        }

        // ── 2. Cascade grafiği ────────────────────────────────────────────────
        // Kenar yönü: çocuk tablo → ebeveyn tablo (silme bu yönde tetiklenir).
        var edges = schema.Relations
            .Where(r => Propagates(r.OnDelete))
            .Select(r => (
                From: tablesById.TryGetValue(r.SourceTableId, out var s) ? s.Name : null,
                To: tablesById.TryGetValue(r.TargetTableId, out var t) ? t.Name : null))
            .Where(e => e.From is not null && e.To is not null)
            .Select(e => (From: e.From!, To: e.To!))
            .ToList();

        if (edges.Count == 0) return issues;

        // ── 3. Döngü tespiti (self-loop dahil) ────────────────────────────────
        foreach (var node in edges.SelectMany(e => new[] { e.From, e.To }).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (HasPathBackToSelf(node, edges))
            {
                issues.Add(new CascadeIssue(
                    CascadeIssueKind.CascadeCycle,
                    $"Table '{node}' sits in a cascade cycle. SQL Server rejects this outright; " +
                    "other engines accept it and delete further than you expect.",
                    FromTable: node));
            }
        }

        // ── 4. Çoklu cascade yolu (SQL Server'ın reddettiği durum) ────────────
        foreach (var start in edges.Select(e => e.From).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            CountPaths(start, edges, new HashSet<string>(StringComparer.OrdinalIgnoreCase), counts);

            foreach (var (target, count) in counts.Where(c => c.Value > 1))
            {
                issues.Add(new CascadeIssue(
                    CascadeIssueKind.MultipleCascadePaths,
                    $"There are {count} separate cascade paths from '{start}' to '{target}'. " +
                    (engine == DatabaseType.MSSQL
                        ? "SQL Server rejects this DDL (Msg 1785). Set one of the relations to NO ACTION."
                        : "This schema cannot move to SQL Server, and the cascade order here is unpredictable."),
                    FromTable: start, ToTable: target));
            }
        }

        return issues;
    }

    /// <summary>Bu şema hedef motorda çalıştırılabilir mi? (yalnızca engelleyici sorunlar)</summary>
    public static bool HasBlockingIssues(DatabaseSchema schema, DatabaseType engine) =>
        Analyze(schema, engine).Any(i =>
            i.Kind is CascadeIssueKind.MultipleCascadePaths or CascadeIssueKind.CascadeCycle);

    private static bool HasPathBackToSelf(string start, List<(string From, string To)> edges)
    {
        var stack = new Stack<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in edges.Where(e => string.Equals(e.From, start, StringComparison.OrdinalIgnoreCase)))
            stack.Push(e.To);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (string.Equals(node, start, StringComparison.OrdinalIgnoreCase)) return true;
            if (!seen.Add(node)) continue;

            foreach (var e in edges.Where(e => string.Equals(e.From, node, StringComparison.OrdinalIgnoreCase)))
                stack.Push(e.To);
        }

        return false;
    }

    private static void CountPaths(
        string current,
        List<(string From, string To)> edges,
        HashSet<string> visiting,
        Dictionary<string, int> counts)
    {
        if (!visiting.Add(current)) return; // döngüde sonsuza gitme

        foreach (var edge in edges.Where(e => string.Equals(e.From, current, StringComparison.OrdinalIgnoreCase)))
        {
            counts[edge.To] = counts.GetValueOrDefault(edge.To) + 1;
            CountPaths(edge.To, edges, visiting, counts);
        }

        visiting.Remove(current);
    }
}
