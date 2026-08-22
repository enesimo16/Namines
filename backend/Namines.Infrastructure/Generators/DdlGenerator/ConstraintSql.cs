using System;
using System.Collections.Generic;
using System.Linq;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.DdlGenerator;

/// <summary>
/// UNIQUE / CHECK kısıtlarını ve index'leri motora özgü SQL'e çevirir.
///
/// Faz 1'de bu kavramlar modelde HİÇ YOKTU. Index üretemeyen bir veritabanı
/// tasarım aracı, üretimde kullanılamayacak şema üretir — özellikle yabancı
/// anahtar kolonlarında index olmaması en yaygın performans hatasıdır.
///
/// Motor yetenek farkları:
///
///   | Özellik            | MSSQL | PostgreSQL | MySQL/MariaDB | SQLite | Oracle |
///   |--------------------|-------|------------|---------------|--------|--------|
///   | UNIQUE constraint  |  ✔    |     ✔      |      ✔        |   ✔    |   ✔    |
///   | CHECK constraint   |  ✔    |     ✔      |    ✔ (8.0.16+)|   ✔    |   ✔    |
///   | CREATE INDEX       |  ✔    |     ✔      |      ✔        |   ✔    |   ✔    |
///   | Kısmi index (WHERE)|  ✔    |     ✔      |      ✖        |   ✔    |   ✖    |
///   | INCLUDE kolonları  |  ✔    |   ✔ (11+)  |      ✖        |   ✖    |   ✖    |
///   | USING yöntemi      |  ✖    |     ✔      |      ✔        |   ✖    |   ✖    |
///
/// Desteklenmeyen bir özellik SESSİZCE DÜŞÜRÜLMEZ — üretilen SQL'e açıklama
/// satırı yazılır ki kullanıcı ne kaybettiğini görsün.
/// </summary>
internal static class ConstraintSql
{
    private static bool SupportsPartialIndex(DatabaseType e) =>
        e is DatabaseType.MSSQL or DatabaseType.PostgreSQL or DatabaseType.SQLite;

    private static bool SupportsIncludeColumns(DatabaseType e) =>
        e is DatabaseType.MSSQL or DatabaseType.PostgreSQL;

    private static bool SupportsIndexMethod(DatabaseType e) =>
        e is DatabaseType.PostgreSQL or DatabaseType.MySQL or DatabaseType.MariaDB;

    /// <summary>Oracle 12c ve öncesinde tanımlayıcılar 30 karakterle sınırlıdır.</summary>
    private static string Fit(string name, DatabaseType engine) =>
        engine == DatabaseType.Oracle && name.Length > 30 ? name[..30] : name;

    // ── CREATE TABLE içine giren kısıtlar ────────────────────────────────────

    /// <summary>
    /// CREATE TABLE gövdesine eklenecek UNIQUE ve CHECK satırlarını üretir.
    /// PK ve FK burada değildir — onları üreticiler kendi yönetir.
    /// </summary>
    /// <param name="schema">
    /// Enum tanımlarına erişmek için ZORUNLU. Opsiyonel olsaydı bir üretici onu
    /// geçmeyi unutabilir ve o motorda enum kısıtı sessizce kaybolurdu —
    /// kullanıcının koruma sandığı şey, hiç uygulanmayan bir kural olurdu.
    /// </param>
    public static IEnumerable<string> InlineConstraints(
        SchemaTable table,
        DatabaseType engine,
        Func<string, string> quote,
        DatabaseSchema schema)
    {
        var lines = new List<string>();

        // Motorun kendi enum tipi yoksa kısıt CHECK'e çevriliyor; bkz. EnumSql.
        foreach (var column in table.Columns)
        {
            var check = EnumSql.CheckConstraint(table, column, schema, engine);
            if (check is not null) lines.Add("    " + check);
        }

        foreach (var unique in table.Uniques)
        {
            var cols = ResolveColumns(table, unique.ColumnIds);
            if (cols.Count == 0) continue; // geçersiz referans → sessizce atla, bozuk SQL üretme

            var name = Fit(unique.Name ?? $"UQ_{table.Name}_{string.Join("_", cols.Select(c => c.Name))}", engine);
            lines.Add($"    CONSTRAINT {quote(name)} UNIQUE ({string.Join(", ", cols.Select(c => quote(c.Name)))})");
        }

        foreach (var check in table.Checks)
        {
            if (string.IsNullOrWhiteSpace(check.Expression)) continue;

            var name = Fit(check.Name ?? $"CK_{table.Name}_{check.Id}", engine);
            lines.Add($"    CONSTRAINT {quote(name)} CHECK ({check.Expression.Trim()})");
        }

        return lines;
    }

    // ── CREATE TABLE sonrasına giren index'ler ───────────────────────────────

    /// <summary>
    /// Tablonun index'leri için CREATE INDEX ifadelerini üretir.
    /// Hiç index yoksa boş string döner (çıktıya gereksiz boşluk eklenmez).
    /// </summary>
    public static string CreateIndexes(
        SchemaTable table,
        DatabaseType engine,
        Func<string, string> quote)
    {
        if (table.Indexes.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();

        foreach (var index in table.Indexes)
        {
            var cols = ResolveIndexColumns(table, index.Columns);
            if (cols.Count == 0) continue;

            var prefix = index.IsUnique ? "UX" : "IX";
            var name = Fit(
                index.Name ?? $"{prefix}_{table.Name}_{string.Join("_", cols.Select(c => c.Column.Name))}",
                engine);

            var colList = string.Join(", ", cols.Select(c =>
                c.Descending ? $"{quote(c.Column.Name)} DESC" : quote(c.Column.Name)));

            var unique = index.IsUnique ? "UNIQUE " : string.Empty;

            // USING yöntemi — kolon listesinden ÖNCE (PG) veya SONRA (MySQL) gelir.
            var usingBefore = string.Empty;
            var usingAfter = string.Empty;
            if (!string.IsNullOrWhiteSpace(index.Method) && SupportsIndexMethod(engine))
            {
                if (engine == DatabaseType.PostgreSQL)
                    usingBefore = $" USING {index.Method.ToLowerInvariant()}";
                else
                    usingAfter = $" USING {index.Method.ToUpperInvariant()}";
            }
            else if (!string.IsNullOrWhiteSpace(index.Method))
            {
                sb.AppendLine($"-- NOT: '{index.Method}' index yöntemi {engine} tarafından desteklenmiyor, varsayılan kullanıldı.");
            }

            sb.Append($"CREATE {unique}INDEX {quote(name)} ON {quote(table.Name)}{usingBefore} ({colList}){usingAfter}");

            // INCLUDE — kapsayan index
            if (index.IncludeColumnIds.Count > 0)
            {
                var includeCols = ResolveColumns(table, index.IncludeColumnIds);
                if (includeCols.Count > 0)
                {
                    if (SupportsIncludeColumns(engine))
                        sb.Append($" INCLUDE ({string.Join(", ", includeCols.Select(c => quote(c.Name)))})");
                    else
                        sb.Append($" /* INCLUDE ({string.Join(", ", includeCols.Select(c => c.Name))}) — {engine} desteklemiyor */");
                }
            }

            // Kısmi (filtreli) index
            if (!string.IsNullOrWhiteSpace(index.Where))
            {
                if (SupportsPartialIndex(engine))
                {
                    sb.Append($" WHERE {index.Where.Trim()}");
                }
                else
                {
                    // Koşulu düşürmek index'i SESSİZCE farklı bir şeye çevirirdi.
                    // Kullanıcının bunu görmesi gerekir.
                    sb.Append($" /* WHERE {index.Where.Trim()} — {engine} kısmi index desteklemiyor */");
                }
            }

            sb.AppendLine(";");
        }

        return sb.ToString();
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────

    private static List<SchemaColumn> ResolveColumns(SchemaTable table, IEnumerable<string> columnIds) =>
        columnIds
            .Select(id => table.Columns.FirstOrDefault(c => c.Id == id))
            .Where(c => c is not null)
            .Select(c => c!)
            .ToList();

    private static List<(SchemaColumn Column, bool Descending)> ResolveIndexColumns(
        SchemaTable table,
        IEnumerable<SchemaIndexColumn> indexColumns) =>
        indexColumns
            .Select(ic => (Column: table.Columns.FirstOrDefault(c => c.Id == ic.ColumnId), ic.Descending))
            .Where(x => x.Column is not null)
            .Select(x => (x.Column!, x.Descending))
            .ToList();
}
