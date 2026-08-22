using System;
using System.Collections.Generic;
using System.Linq;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Nsl;

/// <param name="Code">NSL001 gibi sabit kod — mesaj metni değişse bile kural
/// kimliği sabit kalır, böylece kullanıcı belirli bir kuralı susturabilir.</param>
/// <param name="Severity">"error" | "warning" | "info".</param>
/// <param name="AutoFixable">Doküman §6'daki "Auto-fix" sütunu.</param>
public sealed record NslFinding(
    string Code, string Severity, string Message,
    string? Table = null, string? Column = null, bool AutoFixable = false);

/// <summary>
/// Şema doğrulama kuralları (new-phase/04-NSL-SCHEMA-IR.md §6).
///
/// <b>Kapsam, açıkça:</b> dokümandaki 25 kuralın modelde karşılığı OLANLAR
/// uygulandı. View (NSL022), RLS (NSL021), enum (NSL017) ve PII etiketi (NSL020)
/// <see cref="DatabaseSchema"/>'da temsil edilmiyor; onlar için kural yazmak, hiç
/// tetiklenmeyecek bir kontrolü "var" göstermek olurdu. 3NF şüphesi (NSL019) ve
/// AI ile açıklama doldurma (NSL025) da bilinçli olarak dışarıda — ilki sezgisel
/// ve yanlış pozitifi yüksek, ikincisi doğrulama değil üretim.
///
/// <b>Cascade kuralları (NSL006/007) yeniden yazılmadı:</b> <see cref="FkCascadeAnalyzer"/>
/// zaten gerçek motorlara karşı doğrulanmış hâlde bu işi yapıyor. İkinci bir
/// uygulama, biri güncellenip diğeri unutulduğunda çelişkili sonuç verirdi.
/// </summary>
public static class NslValidator
{
    /// <summary>Motor başına tanımlayıcı uzunluk sınırı (NSL009).</summary>
    private static int NameLimit(DatabaseType engine) => engine switch
    {
        DatabaseType.PostgreSQL => 63,
        DatabaseType.Oracle => 128,
        DatabaseType.MSSQL => 128,
        DatabaseType.MySQL or DatabaseType.MariaDB => 64,
        _ => 64,
    };

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "select", "from", "where", "table", "column", "order", "group", "user", "index",
        "key", "primary", "foreign", "check", "default", "null", "not", "and", "or",
        "insert", "update", "delete", "create", "drop", "alter", "join", "union",
        "case", "when", "then", "else", "end", "as", "on", "in", "is", "like", "values",
    };

    public static IReadOnlyList<NslFinding> Validate(
        DatabaseSchema schema, DatabaseType engine = DatabaseType.PostgreSQL)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var findings = new List<NslFinding>();
        var limit = NameLimit(engine);

        // NSL001 — tablo adı benzersiz
        foreach (var group in schema.Tables
                     .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
            findings.Add(new("NSL001", "error",
                $"Table name '{group.Key}' is declared {group.Count()} times.", group.Key, AutoFixable: true));

        foreach (var table in schema.Tables)
        {
            // NSL002 — kolon adı benzersiz
            foreach (var group in table.Columns
                         .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                         .Where(g => g.Count() > 1))
                findings.Add(new("NSL002", "error",
                    $"Column '{group.Key}' is declared {group.Count()} times.", table.Name, group.Key, true));

            var primaryKeys = table.Columns.Where(c => c.IsPK).ToList();

            // NSL003 — her tabloda PK olmalı. Uyarı, hata değil: birincil anahtarsız
            // tablolar (log, olay akışı) meşru olabilir — ama düzenlenemezler.
            if (primaryKeys.Count == 0)
                findings.Add(new("NSL003", "warning",
                    "Table has no primary key, so individual rows cannot be updated or deleted safely.",
                    table.Name, AutoFixable: true));

            // NSL016 — nullable PK
            foreach (var column in primaryKeys.Where(c => c.IsNullable))
                findings.Add(new("NSL016", "error",
                    "A primary key column cannot be nullable.", table.Name, column.Name, true));

            foreach (var column in table.Columns)
            {
                // NSL008 — rezerve kelime
                if (Reserved.Contains(column.Name))
                    findings.Add(new("NSL008", "warning",
                        $"'{column.Name}' is a reserved word in SQL and must be quoted in every query.",
                        table.Name, column.Name, true));

                // NSL009 — ad uzunluğu
                if (column.Name.Length > limit)
                    findings.Add(new("NSL009", "error",
                        $"Column name is {column.Name.Length} characters; {engine} allows {limit}.",
                        table.Name, column.Name, true));

                var kind = column.Type?.Trim().ToUpperInvariant() ?? string.Empty;

                // NSL013 — uzunluksuz varchar
                if (kind is "VARCHAR" or "NVARCHAR" or "CHAR" && column.Length is null or <= 0)
                    findings.Add(new("NSL013", "warning",
                        $"{kind} has no length; engines pick different defaults and MySQL may truncate.",
                        table.Name, column.Name, true));

                // NSL014 — para için float. Kayan nokta parayı yuvarlar ve toplamlar
                // kuruş kaçırır; bu, tespiti en zor hata sınıflarından biri.
                if (kind is "FLOAT" or "REAL" or "DOUBLE" && LooksMonetary(column.Name))
                    findings.Add(new("NSL014", "warning",
                        "Monetary values in a floating-point column lose precision; use decimal(19,4).",
                        table.Name, column.Name, true));

                // NSL015 — saat dilimsiz zaman damgası
                if (kind is "DATETIME" or "TIMESTAMP")
                    findings.Add(new("NSL015", "info",
                        "Timestamp without a time zone is ambiguous across regions; consider timestamptz.",
                        table.Name, column.Name, true));
            }

            if (table.Name.Length > limit)
                findings.Add(new("NSL009", "error",
                    $"Table name is {table.Name.Length} characters; {engine} allows {limit}.",
                    table.Name, AutoFixable: true));

            if (Reserved.Contains(table.Name))
                findings.Add(new("NSL008", "warning",
                    $"'{table.Name}' is a reserved word in SQL and must be quoted in every query.",
                    table.Name, AutoFixable: true));

            // NSL011 — yinelenen index
            var signatures = new HashSet<string>(StringComparer.Ordinal);
            foreach (var index in table.Indexes)
            {
                var signature = string.Join(",", index.Columns.Select(c => c.ColumnId));
                if (signature.Length > 0 && !signatures.Add(signature))
                    findings.Add(new("NSL011", "warning",
                        "Two indexes cover the same columns; the duplicate costs writes and storage for nothing.",
                        table.Name, AutoFixable: true));

                // NSL012 — aşırı geniş index
                if (index.Columns.Count > 5)
                    findings.Add(new("NSL012", "info",
                        $"Index covers {index.Columns.Count} columns; wide indexes are rarely used past the first few.",
                        table.Name));
            }
        }

        ValidateRelations(schema, findings);

        // NSL006/007 — cascade yolları. Yeniden yazılmadı, mevcut ve gerçek
        // motorlara karşı doğrulanmış analizör kullanılıyor.
        foreach (var issue in FkCascadeAnalyzer.Analyze(schema, engine))
        {
            var code = issue.Kind == CascadeIssueKind.CascadeCycle ? "NSL007" : "NSL006";
            var severity = issue.Kind is CascadeIssueKind.MultipleCascadePaths or CascadeIssueKind.CascadeCycle
                ? "error" : "warning";
            findings.Add(new(code, severity, issue.Message, issue.FromTable,
                AutoFixable: issue.Kind == CascadeIssueKind.MultipleCascadePaths));
        }

        return findings;
    }

    private static void ValidateRelations(DatabaseSchema schema, List<NslFinding> findings)
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var relation in schema.Relations)
        {
            var from = schema.Tables.FirstOrDefault(t => t.Id == relation.SourceTableId);
            var to = schema.Tables.FirstOrDefault(t => t.Id == relation.TargetTableId);
            if (from is null || to is null) continue;

            referenced.Add(from.Id);
            referenced.Add(to.Id);

            var fromColumn = from.Columns.FirstOrDefault(c => c.Id == relation.SourceColumnId);
            var toColumn = to.Columns.FirstOrDefault(c => c.Id == relation.TargetColumnId);
            if (fromColumn is null || toColumn is null) continue;

            // NSL004 — tip uyumu. Farklı tipler bazı motorlarda sessizce dönüştürülür
            // ve index kullanılamaz hâle gelir; bazılarında FK hiç kurulamaz.
            if (!TypesCompatible(fromColumn.Type, toColumn.Type))
                findings.Add(new("NSL004", "error",
                    $"Foreign key type '{fromColumn.Type}' does not match '{to.Name}.{toColumn.Name}' " +
                    $"('{toColumn.Type}').", from.Name, fromColumn.Name, true));

            // NSL005 — hedef PK veya UNIQUE olmalı
            var targetIsUnique = toColumn.IsPK ||
                to.Uniques.Any(u => u.ColumnIds.Count == 1 && u.ColumnIds[0] == toColumn.Id) ||
                to.Indexes.Any(i => i.IsUnique && i.Columns.Count == 1 && i.Columns[0].ColumnId == toColumn.Id);

            if (!targetIsUnique)
                findings.Add(new("NSL005", "error",
                    $"Foreign key target '{to.Name}.{toColumn.Name}' is neither a primary key nor unique, " +
                    "so a row could reference many rows.", from.Name, fromColumn.Name));

            // NSL010 — index'siz FK kolonu. Index olmadan hedef satır silinirken
            // kaynak tablo tam taranır; büyük tabloda silme dakikalar sürebilir.
            var indexed = fromColumn.IsPK ||
                from.Indexes.Any(i => i.Columns.Count > 0 && i.Columns[0].ColumnId == fromColumn.Id) ||
                from.Uniques.Any(u => u.ColumnIds.Count > 0 && u.ColumnIds[0] == fromColumn.Id);

            if (!indexed)
                findings.Add(new("NSL010", "warning",
                    "Foreign key column has no index; deletes on the target table will scan this one.",
                    from.Name, fromColumn.Name, true));
        }

        // NSL018 — yetim tablo. Bilgi seviyesi: tek başına duran tablolar
        // (ayarlar, sözlük) tamamen meşrudur.
        if (schema.Tables.Count > 1)
            foreach (var table in schema.Tables.Where(t => !referenced.Contains(t.Id)))
                findings.Add(new("NSL018", "info",
                    "Table has no relationships to any other table.", table.Name));
    }

    /// <summary>
    /// İki tip yabancı anahtar için uyumlu mu?
    ///
    /// Karşılaştırma KANONİK SINIF üzerinden: <c>INT</c> ile <c>INTEGER</c> aynı
    /// şeydir, metin eşitliği bunları farklı sayıp yanlış alarm üretirdi.
    /// </summary>
    private static bool TypesCompatible(string? left, string? right)
    {
        static string Normalize(string? type) => (type ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "INT" or "INTEGER" or "SERIAL" or "SMALLINT" or "TINYINT" => "int",
            "BIGINT" or "BIGSERIAL" => "bigint",
            "UUID" or "UNIQUEIDENTIFIER" => "uuid",
            "VARCHAR" or "NVARCHAR" or "CHAR" or "TEXT" or "NTEXT" => "text",
            "DECIMAL" or "NUMERIC" or "MONEY" => "decimal",
            var other => other.ToLowerInvariant(),
        };

        var a = Normalize(left);
        var b = Normalize(right);

        // int ↔ bigint bilinçli olarak uyumlu sayılıyor: yaygın ve çalışan bir
        // desen (int FK → bigserial PK). Hata vermek gürültü olurdu.
        if (a is "int" or "bigint" && b is "int" or "bigint") return true;

        return a == b;
    }

    private static bool LooksMonetary(string name)
    {
        string[] hints = { "price", "amount", "total", "cost", "fee", "salary", "balance", "tutar", "fiyat" };
        return hints.Any(h => name.Contains(h, StringComparison.OrdinalIgnoreCase));
    }
}
