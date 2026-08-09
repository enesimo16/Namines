using System.Text.RegularExpressions;
using Namines.Core.Enums;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Tests.Fixtures;

namespace Namines.Tests.Ddl;

/// <summary>
/// REGRESYON KORUMASI — G3'te düzeltilen hatanın geri gelmesini engeller.
///
/// Geçmişte altı DDL üreticisinin altısı da her yabancı anahtara koşulsuz
/// <c>ON DELETE CASCADE</c> yazıyordu. İki sonucu vardı:
///
///  1) SQL Server, aynı tabloya birden fazla cascade yolu olan şemayı REDDEDİYORDU:
///     "Msg 1785: Introducing FOREIGN KEY constraint '...' on table '...' may cause
///      cycles or multiple cascade paths."
///     Yani üretilen DDL, sıradan bir e-ticaret modelinde bile çalışmıyordu.
///
///  2) Reddedilmediği motorlarda (PostgreSQL, MySQL) sessiz veri kaybı riski üretiyordu:
///     bir kullanıcı silindiğinde siparişleri de siliniyordu.
///
/// G3'te varsayılan <see cref="Namines.Core.Enums.ReferentialAction.NoAction"/> yapıldı.
/// Bu testler G2'de kırmızı yazıldı (hatanın kanıtı), G3'te yeşile döndü.
/// Artık ana test paketinin parçasıdırlar ve kırmızıya dönerlerse hata geri gelmiş demektir.
///
/// İlgili: <see cref="Namines.Core.Analysis.FkCascadeAnalyzer"/> — kullanıcı açıkça
/// CASCADE seçtiğinde aynı duruma düşülmesini engeller.
/// </summary>
public class CascadePathTests
{
    // ── Test 1 — Çoklu cascade yolu üretilmemeli ─────────────────────────────
    [Theory]
    [InlineData(DatabaseType.MSSQL)]
    public void Mssql_ddl_must_not_contain_multiple_cascade_paths(DatabaseType engine)
    {
        var schema = SchemaFixtures.MultiCascadePath();
        var ddl = new DdlGeneratorFactory().GetGenerator(engine).Generate(schema);

        var edges = ParseCascadeEdges(ddl);
        var violations = FindMultiplePaths(edges);

        Assert.True(
            violations.Count == 0,
            $"{engine}: aynı tabloya birden fazla cascade yolu var — SQL Server bu DDL'i Msg 1785 ile " +
            $"reddeder.{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations.Select(v => $"  {v.From} → {v.To}: {v.PathCount} yol")) +
            $"{Environment.NewLine}Cascade kenarları: {string.Join(", ", edges.Select(e => $"{e.From}→{e.To}"))}");
    }

    // ── Test 2 — Cascade döngüsü üretilmemeli ────────────────────────────────
    // Kendine referans veren tablo (parent_id) + CASCADE = döngü.
    // SQL Server bunu da reddeder.
    [Theory]
    [InlineData(DatabaseType.MSSQL)]
    public void Self_referencing_fk_must_not_cascade(DatabaseType engine)
    {
        var schema = SchemaFixtures.SelfReferencing();
        var ddl = new DdlGeneratorFactory().GetGenerator(engine).Generate(schema);

        var selfCascades = ParseCascadeEdges(ddl)
            .Where(e => string.Equals(e.From, e.To, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(
            selfCascades.Count == 0,
            $"{engine}: '{string.Join(", ", selfCascades.Select(e => e.From))}' tablosu kendine CASCADE ile " +
            "bağlanıyor — bu bir cascade döngüsüdür ve SQL Server tarafından reddedilir.");
    }

    // ── Test 3 — Varsayılan davranış CASCADE olmamalı ────────────────────────
    // Kullanıcı açıkça istemediyse silme davranışı NO ACTION / RESTRICT olmalıdır.
    // Sessiz veri kaybı, veritabanı tasarım aracının üretebileceği en kötü çıktıdır.
    [Theory]
    [InlineData(DatabaseType.MSSQL)]
    [InlineData(DatabaseType.PostgreSQL)]
    [InlineData(DatabaseType.MySQL)]
    [InlineData(DatabaseType.MariaDB)]
    [InlineData(DatabaseType.SQLite)]
    [InlineData(DatabaseType.Oracle)]
    public void Cascade_must_not_be_the_unconditional_default(DatabaseType engine)
    {
        var schema = SchemaFixtures.ECommerce();
        var ddl = new DdlGeneratorFactory().GetGenerator(engine).Generate(schema);

        var cascadeCount = Regex.Matches(ddl, @"ON\s+DELETE\s+CASCADE", RegexOptions.IgnoreCase).Count;

        Assert.True(
            cascadeCount == 0,
            $"{engine}: kullanıcı istemediği halde {cascadeCount} adet ON DELETE CASCADE üretildi. " +
            "Varsayılan NO ACTION olmalı; CASCADE yalnızca açıkça seçilirse yazılmalıdır.");
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────

    private readonly record struct CascadeEdge(string From, string To);
    private readonly record struct PathViolation(string From, string To, int PathCount);

    /// <summary>
    /// Üretilen DDL'den "ON DELETE CASCADE" ile biten FK ilişkilerini çıkarır.
    /// Hem ALTER TABLE (MSSQL/PG/MySQL/Oracle) hem inline FOREIGN KEY (SQLite) biçimini destekler.
    /// </summary>
    private static List<CascadeEdge> ParseCascadeEdges(string ddl)
    {
        var edges = new List<CascadeEdge>();

        // ALTER TABLE [A] ... FOREIGN KEY(...) REFERENCES [B] (...) ON DELETE CASCADE
        var alterPattern = new Regex(
            @"ALTER\s+TABLE\s+[\[""`]?(?<from>\w+)[\]""`]?.*?REFERENCES\s+[\[""`]?(?<to>\w+)[\]""`]?\s*\([^)]*\)\s*ON\s+DELETE\s+CASCADE",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match m in alterPattern.Matches(ddl))
            edges.Add(new CascadeEdge(m.Groups["from"].Value, m.Groups["to"].Value));

        // SQLite: CREATE TABLE "A" ( ... FOREIGN KEY ("x") REFERENCES "B" ("y") ON DELETE CASCADE ... )
        var createPattern = new Regex(
            @"CREATE\s+TABLE\s+[\[""`]?(?<from>\w+)[\]""`]?\s*\((?<body>.*?)\n\s*\);",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match table in createPattern.Matches(ddl))
        {
            var from = table.Groups["from"].Value;
            var inlineFk = new Regex(
                @"FOREIGN\s+KEY\s*\([^)]*\)\s*REFERENCES\s+[\[""`]?(?<to>\w+)[\]""`]?\s*\([^)]*\)\s*ON\s+DELETE\s+CASCADE",
                RegexOptions.IgnoreCase);

            foreach (Match fk in inlineFk.Matches(table.Groups["body"].Value))
                edges.Add(new CascadeEdge(from, fk.Groups["to"].Value));
        }

        return edges;
    }

    /// <summary>
    /// Cascade grafiğinde bir tablodan diğerine birden fazla ayrı yol olup olmadığını bulur.
    /// SQL Server'ın reddettiği durum tam olarak budur.
    /// </summary>
    private static List<PathViolation> FindMultiplePaths(List<CascadeEdge> edges)
    {
        var nodes = edges.SelectMany(e => new[] { e.From, e.To })
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .ToList();

        var violations = new List<PathViolation>();

        foreach (var start in nodes)
        {
            var pathCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            CountPaths(start, edges, new HashSet<string>(StringComparer.OrdinalIgnoreCase), pathCounts);

            foreach (var (target, count) in pathCounts.Where(p => p.Value > 1))
                violations.Add(new PathViolation(start, target, count));
        }

        return violations;
    }

    private static void CountPaths(
        string current,
        List<CascadeEdge> edges,
        HashSet<string> visiting,
        Dictionary<string, int> pathCounts)
    {
        if (!visiting.Add(current)) return; // döngü koruması

        foreach (var edge in edges.Where(e => string.Equals(e.From, current, StringComparison.OrdinalIgnoreCase)))
        {
            pathCounts[edge.To] = pathCounts.GetValueOrDefault(edge.To) + 1;
            CountPaths(edge.To, edges, visiting, pathCounts);
        }

        visiting.Remove(current);
    }
}
