using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Namines.Core.Analysis;

/// <summary>
/// RFC 4180 uyumlu CSV üretimi (new-phase/08-GATEWAY-API.md §2 <c>/export</c>).
///
/// Kendi yazmamızın sebebi kaçış kuralları: virgül, tırnak veya satır sonu taşıyan
/// bir değer kaçırılmazsa CSV'nin SÜTUN SAYISI değişir ve dosya sessizce bozulur —
/// açan kişi bunu ancak veriler kaymış hâlde fark eder. Bir alan bu üç karakterden
/// birini içeriyorsa tırnaklanır, içindeki tırnaklar ikilenir.
/// </summary>
public static class CsvWriter
{
    public static string Write(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0) return string.Empty;

        // Başlık İLK satırdan alınır ve tüm satırlar aynı sıraya zorlanır. Satır
        // başına anahtar sırasına güvenmek, bir satırda eksik kolon olduğunda
        // değerleri yanlış sütuna yazardı.
        var headers = rows[0].Keys.ToList();

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(Escape)));

        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",", headers.Select(h =>
                Escape(Format(row.TryGetValue(h, out var value) ? value : null)))));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Değeri metne çevirir.
    ///
    /// Kültürden BAĞIMSIZ: Türkçe kültürde ondalık ayırıcı virgüldür ve
    /// <c>12,5</c> yazmak CSV'de yeni bir sütun açar. Tarihler ISO-8601 — yerel
    /// biçim, dosyayı başka bir makinede açan için belirsizdir.
    /// </summary>
    internal static string Format(object? value) => value switch
    {
        null or DBNull => string.Empty,
        bool b => b ? "true" : "false",
        DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToBase64String(bytes),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    internal static string Escape(string field)
    {
        if (field.Length == 0) return field;

        var needsQuotes =
            field.Contains(',') || field.Contains('"') ||
            field.Contains('\n') || field.Contains('\r') ||
            // Baştaki/sondaki boşluk bazı ayrıştırıcılarda kırpılır; tırnaklamak korur.
            field[0] == ' ' || field[^1] == ' ';

        if (!needsQuotes) return field;

        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }
}
