using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.Eject;

/// <summary>
/// Tüm eject hedeflerinin paylaştığı ad dönüşümleri ve şema gezinme yardımcıları.
///
/// Tek kopya olması şart: 15 hedefin her biri kendi PascalCase'ini yazsaydı,
/// aynı şemadan üretilen TypeScript tipiyle C# record'u farklı adlar taşır ve
/// ikisini birlikte kullanan bir projede sessiz uyumsuzluk doğardı.
/// </summary>
internal static class EjectNaming
{
    private static readonly char[] Separators = { '_', '-', ' ', '.' };

    public static string Pascal(string? value)
    {
        var parts = Split(value);
        if (parts.Length == 0) return "Item";

        var sb = new StringBuilder();
        foreach (var part in parts) sb.Append(Capitalize(part));
        return EnsureIdentifier(sb.ToString(), "Item");
    }

    public static string Camel(string? value)
    {
        var pascal = Pascal(value);
        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    public static string Snake(string? value) =>
        string.Join("_", Split(value).Select(p => p.ToLowerInvariant()));

    /// <summary>
    /// Tablo adı zaten çoğulsa olduğu gibi bırakılır.
    ///
    /// Tekilleştirme denenmiyor: "address" → "addres", "status" → "statu" gibi
    /// düzensiz adlarda sessizce yanlış sonuç verir (Prisma üreticisinde alınan
    /// aynı karar). Gerçek tablo adı zaten eşleme ile korunuyor.
    /// </summary>
    public static string PluralCamel(string? value)
    {
        var camel = Camel(value);
        if (camel.EndsWith("s", StringComparison.Ordinal)) return camel;
        return camel + "s";
    }

    public static SchemaColumn? PrimaryKey(SchemaTable table) =>
        table.Columns.FirstOrDefault(c => c.IsPK);

    public static IReadOnlyList<SchemaColumn> PrimaryKeys(SchemaTable table) =>
        table.Columns.Where(c => c.IsPK).ToList();

    /// <summary>
    /// İlişkileri çözer: (kaynak tablo, kaynak kolon, hedef tablo, hedef kolon).
    ///
    /// İlişkiler modelde ID ile tutuluyor; her hedef kendi çözümünü yazsaydı biri
    /// eksik referansı atlamayı unutur ve null referansta patlardı.
    /// </summary>
    public static IEnumerable<(SchemaTable From, SchemaColumn FromColumn, SchemaTable To, SchemaColumn ToColumn, SchemaRelation Relation)>
        Relations(DatabaseSchema schema)
    {
        foreach (var relation in schema.Relations ?? new List<SchemaRelation>())
        {
            var from = schema.Tables.FirstOrDefault(t => t.Id == relation.SourceTableId);
            var to = schema.Tables.FirstOrDefault(t => t.Id == relation.TargetTableId);
            if (from is null || to is null) continue;

            var fromColumn = from.Columns.FirstOrDefault(c => c.Id == relation.SourceColumnId);
            var toColumn = to.Columns.FirstOrDefault(c => c.Id == relation.TargetColumnId);
            if (fromColumn is null || toColumn is null) continue;

            yield return (from, fromColumn, to, toColumn, relation);
        }
    }

    /// <summary>
    /// Hedefin ifade edemediği yapıları toplar — her üretici aynı listeyi bildirsin.
    ///
    /// <paramref name="supportsChecks"/> vb. false ise ilgili yapı uyarıya dönüşür.
    /// Sessizce düşürmek, üretilen dosyanın veritabanından daha gevşek olması demek.
    /// </summary>
    public static void CollectUnsupported(
        DatabaseSchema schema, List<string> warnings,
        bool supportsChecks = false, bool supportsIndexes = true, bool supportsUniques = true)
    {
        foreach (var table in schema.Tables)
        {
            if (!supportsChecks)
                foreach (var check in table.Checks.Where(c => !string.IsNullOrWhiteSpace(c.Expression)))
                    warnings.Add($"{table.Name}: CHECK constraint \"{check.Expression}\" has no equivalent here.");

            if (!supportsIndexes && table.Indexes.Count > 0)
                warnings.Add($"{table.Name}: {table.Indexes.Count} index definition(s) are not represented here.");

            if (!supportsUniques && table.Uniques.Count > 0)
                warnings.Add($"{table.Name}: UNIQUE constraint(s) are not represented here.");
        }
    }

    private static string[] Split(string? value) =>
        (value ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries);

    private static string Capitalize(string word) => word.Length switch
    {
        0 => word,
        1 => word.ToUpperInvariant(),
        // Karışık yazılmış adlar (ör. "userID") bozulmasın diye yalnızca ilk harf.
        _ => char.ToUpperInvariant(word[0]) + word[1..],
    };

    private static string EnsureIdentifier(string candidate, string fallback)
    {
        var cleaned = new string(candidate.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (cleaned.Length == 0) return fallback;
        return char.IsDigit(cleaned[0]) ? fallback + cleaned : cleaned;
    }
}
