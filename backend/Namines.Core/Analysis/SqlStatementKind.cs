using System;
using System.Linq;

namespace Namines.Core.Analysis;

/// <summary>Bir SQL ifadesinin ne yaptığı — çalıştırma kararı için.</summary>
public enum SqlKind
{
    /// <summary>Yalnızca okur.</summary>
    Read,

    /// <summary>Veri yazar (INSERT/UPDATE/DELETE/MERGE...).</summary>
    Write,

    /// <summary>Şema değiştirir ya da ne yaptığı ANLAŞILAMADI.</summary>
    Unknown,
}

/// <summary>
/// Bir SQL ifadesinin okuma mı yazma mı olduğunu söyler (08 §2 <c>/query/nl</c>).
///
/// <b>Bu sınıflandırma bir güvenlik kapısı, bir kolaylık değil.</b> Doğal dilden
/// üretilen SQL'i körlemesine çalıştırmak, "geçen ayki siparişleri göster" diye
/// yazan birinin isteğinin <c>DELETE</c>'e dönüşme ihtimalini kabul etmek olur.
///
/// <b>Tanınmayan her şey <see cref="SqlKind.Unknown"/>.</b> Beyaz liste
/// yaklaşımı: yalnızca okuduğundan EMİN olduğumuz fiiller okuma sayılır. Kara
/// liste (yazma fiillerini sayıp gerisini okuma saymak) kullansaydık, listede
/// olmayan tek bir fiil sessizce çalıştırılırdı — ve motorlar arasında o fiil
/// sayısı çok.
/// </summary>
public static class SqlStatementKind
{
    private static readonly string[] ReadVerbs = { "SELECT", "WITH", "SHOW", "EXPLAIN", "DESCRIBE", "DESC" };

    private static readonly string[] WriteVerbs =
    {
        "INSERT", "UPDATE", "DELETE", "MERGE", "UPSERT", "REPLACE", "TRUNCATE",
        "DROP", "CREATE", "ALTER", "GRANT", "REVOKE", "CALL", "EXEC", "EXECUTE",
        "COPY", "VACUUM", "SET", "COMMIT", "ROLLBACK", "BEGIN",
    };

    public static SqlKind Classify(string? sql)
    {
        var first = FirstWord(sql);
        if (first.Length == 0) return SqlKind.Unknown;

        if (WriteVerbs.Contains(first, StringComparer.Ordinal)) return SqlKind.Write;

        // "WITH ... INSERT" gerçek bir kalıp: CTE ile başlayan bir ifade pekâlâ
        // yazabilir. İlk kelimeye bakıp okuma saymak, tam da bu kalıpla
        // atlatılabilir bir kapı bırakırdı.
        if (first == "WITH" && ContainsWriteVerb(sql!)) return SqlKind.Write;

        return ReadVerbs.Contains(first, StringComparer.Ordinal) ? SqlKind.Read : SqlKind.Unknown;
    }

    private static bool ContainsWriteVerb(string sql)
    {
        var words = sql.Split(new[] { ' ', '\t', '\r', '\n', '(', ')', ',', ';' },
            StringSplitOptions.RemoveEmptyEntries);

        return words.Any(w => WriteVerbs.Contains(w.ToUpperInvariant(), StringComparer.Ordinal));
    }

    /// <summary>
    /// İlk anlamlı kelime. Baştaki yorumlar ATLANIYOR: <c>-- rapor</c> ya da
    /// <c>/* x */</c> ile başlayan bir ifadeyi "tanınmadı" saymak, tamamen normal
    /// sorguları reddetmek olurdu — ve daha kötüsü, yorumla başlatarak
    /// sınıflandırmayı şaşırtmak bilinen bir numaradır.
    /// </summary>
    private static string FirstWord(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return string.Empty;

        var i = 0;
        while (i < sql.Length)
        {
            if (char.IsWhiteSpace(sql[i])) { i++; continue; }

            if (sql[i] == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                var newline = sql.IndexOf('\n', i);
                if (newline < 0) return string.Empty;
                i = newline + 1;
                continue;
            }

            if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var close = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (close < 0) return string.Empty;
                i = close + 2;
                continue;
            }

            break;
        }

        var start = i;
        while (i < sql.Length && (char.IsLetter(sql[i]) || sql[i] == '_')) i++;

        return sql[start..i].ToUpperInvariant();
    }
}
