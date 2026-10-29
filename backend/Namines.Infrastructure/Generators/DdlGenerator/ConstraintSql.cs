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
        DatabaseSchema schema,
        ObjectNameRegistry names)
    {
        var lines = new List<string>();

        // Motorun kendi enum tipi yoksa kısıt CHECK'e çevriliyor; bkz. EnumSql.
        foreach (var column in table.Columns)
        {
            var check = EnumSql.CheckConstraint(table, column, schema, engine, names);
            if (check is not null) lines.Add("    " + check);
        }

        foreach (var unique in table.Uniques)
        {
            var cols = ResolveColumns(table, unique.ColumnIds);
            if (cols.Count == 0) continue; // geçersiz referans → sessizce atla, bozuk SQL üretme

            var rawUniqueName = unique.Name;
            var isDuplicateAcrossTables = !string.IsNullOrWhiteSpace(rawUniqueName) &&
                schema.Tables.Count(t => t.Uniques.Any(u => string.Equals(u.Name, rawUniqueName, StringComparison.OrdinalIgnoreCase))) > 1;
            var isDuplicateInSameTable = !string.IsNullOrWhiteSpace(rawUniqueName) &&
                table.Uniques.Count(u => string.Equals(u.Name, rawUniqueName, StringComparison.OrdinalIgnoreCase)) > 1;

            if (isDuplicateAcrossTables || isDuplicateInSameTable)
            {
                rawUniqueName = $"UQ_{table.Name}_{rawUniqueName}";
            }

            var baseName = rawUniqueName ?? $"UQ_{table.Name}_{string.Join("_", cols.Select(c => c.Name))}";
            // UNIQUE kısıtı aynı adlı bir index de yaratır; iki ad uzayına birden yazılır.
            var name = names.ClaimConstraint(table.Name, baseName, createsIndex: true);
            lines.Add($"    CONSTRAINT {quote(name)} UNIQUE ({string.Join(", ", cols.Select(c => quote(c.Name)))})");
        }

        foreach (var check in table.Checks)
        {
            if (string.IsNullOrWhiteSpace(check.Expression)) continue;

            // Güvenlik doğrulaması: ortak ham-SQL doğrulayıcısı (bkz. SqlFragmentGuard) —
            // noktalı virgül, satır sonu, yorum ve ters eğik çizgi reddedilir.
            var trimmedExpr = SqlFragmentGuard.EnsureSafeExpression(
                check.Expression, $"CHECK constraint {check.Name ?? check.Id}", engine);

            var rawCheckName = check.Name;
            var isDuplicateAcrossTables = !string.IsNullOrWhiteSpace(rawCheckName) &&
                schema.Tables.Count(t => t.Checks.Any(c => string.Equals(c.Name, rawCheckName, StringComparison.OrdinalIgnoreCase))) > 1;
            var isDuplicateInSameTable = !string.IsNullOrWhiteSpace(rawCheckName) &&
                table.Checks.Count(c => string.Equals(c.Name, rawCheckName, StringComparison.OrdinalIgnoreCase)) > 1;

            if (isDuplicateAcrossTables || isDuplicateInSameTable)
            {
                rawCheckName = $"CK_{table.Name}_{rawCheckName}";
            }

            var baseName = rawCheckName ?? $"CK_{table.Name}_{check.Id}";
            var name = names.ClaimConstraint(table.Name, baseName);
            lines.Add($"    CONSTRAINT {quote(name)} CHECK ({trimmedExpr})");
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
        Func<string, string> quote,
        ObjectNameRegistry names)
    {
        if (table.Indexes.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();

        foreach (var index in table.Indexes)
        {
            var cols = ResolveIndexColumns(table, index.Columns);
            if (cols.Count == 0) continue;

            var prefix = index.IsUnique ? "UX" : "IX";
            var name = names.ClaimIndex(
                table.Name,
                index.Name ?? $"{prefix}_{table.Name}_{string.Join("_", cols.Select(c => c.Column.Name))}");

            var colList = string.Join(", ", cols.Select(c =>
                c.Descending ? $"{quote(c.Column.Name)} DESC" : quote(c.Column.Name)));

            var unique = index.IsUnique ? "UNIQUE " : string.Empty;

            // USING yöntemi — kolon listesinden ÖNCE (PG) veya SONRA (MySQL) gelir.
            var usingBefore = string.Empty;
            var usingAfter = string.Empty;
            if (!string.IsNullOrWhiteSpace(index.Method) && SupportsIndexMethod(engine))
            {
                if (engine == DatabaseType.PostgreSQL)
                    usingBefore = $" USING {SqlFragmentGuard.EnsureIndexMethod(index.Method)}";
                else
                    usingAfter = $" USING {SqlFragmentGuard.EnsureIndexMethod(index.Method).ToUpperInvariant()}";
            }
            else if (!string.IsNullOrWhiteSpace(index.Method))
            {
                sb.AppendLine($"-- NOTE: index method '{SqlFragmentGuard.CommentText(index.Method)}' is not supported by {engine}; the default was used.");
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
                        sb.Append($" /* INCLUDE ({SqlFragmentGuard.CommentText(string.Join(", ", includeCols.Select(c => c.Name)))}) — not supported by {engine} */");
                }
            }

            // Kısmi (filtreli) index
            if (!string.IsNullOrWhiteSpace(index.Where))
            {
                if (SupportsPartialIndex(engine))
                {
                    sb.Append($" WHERE {SqlFragmentGuard.EnsureSafeExpression(index.Where, "index WHERE", engine)}");
                }
                else
                {
                    // Koşulu düşürmek index'i SESSİZCE farklı bir şeye çevirirdi.
                    // Kullanıcının bunu görmesi gerekir.
                    sb.Append($" /* WHERE {SqlFragmentGuard.EnsureSafeExpression(index.Where, "index WHERE", engine)} — partial indexes are not supported by {engine} */");
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
