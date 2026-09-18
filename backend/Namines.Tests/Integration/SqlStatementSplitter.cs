using System;
using System.Collections.Generic;

namespace Namines.Tests.Integration;

/// <summary>
/// Bir DDL betiğini tek tek çalıştırılabilir ifadelere böler.
///
/// <b>Neden düz <c>Split(';')</c> yetmiyor:</b> PostgreSQL'in trigger
/// fonksiyonları gövdelerini <c>$$ ... $$</c> arasında taşıyor ve o gövde
/// noktalı virgül içeriyor. Düz bölme <c>RETURN NEW;</c> satırını AYRI bir
/// ifade sanıp veritabanına gönderiyordu; Postgres haklı olarak
/// <c>42601 syntax error at or near "RETURN"</c> diyordu ve test bunu
/// "üretilen DDL geçersiz" diye raporluyordu.
///
/// <b>Rapor yanlıştı:</b> üretilen DDL geçerli, bölen bozuktu. Bu ayrım önemli,
/// çünkü iki hata farklı yerde aranır — biri üreticide, diğeri testin
/// kendisinde.
///
/// Aynı sebeple tek tırnaklı metinler de korunuyor: <c>DEFAULT 'a;b'</c> gibi
/// bir varsayılan değer de ifadeyi ortadan kesecekti.
/// </summary>
public static class SqlStatementSplitter
{
    public static IReadOnlyList<string> Split(string ddl)
    {
        var statements = new List<string>();
        var current = new System.Text.StringBuilder();

        var inSingleQuote = false;
        var inLineComment = false;
        string? dollarTag = null;

        for (var i = 0; i < ddl.Length; i++)
        {
            var c = ddl[i];

            if (inLineComment)
            {
                current.Append(c);
                if (c == '\n') inLineComment = false;
                continue;
            }

            if (dollarTag is not null)
            {
                current.Append(c);
                if (c == '$' && Matches(ddl, i, dollarTag))
                {
                    current.Append(dollarTag.AsSpan(1));
                    i += dollarTag.Length - 1;
                    dollarTag = null;
                }
                continue;
            }

            if (inSingleQuote)
            {
                current.Append(c);
                // '' kaçışı: tırnak kapanmaz, tek bir tırnak karakteri olur.
                if (c == '\'')
                {
                    if (i + 1 < ddl.Length && ddl[i + 1] == '\'') { current.Append('\''); i++; }
                    else inSingleQuote = false;
                }
                continue;
            }

            if (c == '-' && i + 1 < ddl.Length && ddl[i + 1] == '-')
            {
                inLineComment = true;
                current.Append(c);
                continue;
            }

            if (c == '\'')
            {
                inSingleQuote = true;
                current.Append(c);
                continue;
            }

            if (c == '$')
            {
                var tag = ReadDollarTag(ddl, i);
                if (tag is not null)
                {
                    dollarTag = tag;
                    current.Append(tag);
                    i += tag.Length - 1;
                    continue;
                }
            }

            if (c == ';')
            {
                Flush(statements, current);
                continue;
            }

            current.Append(c);
        }

        Flush(statements, current);
        return statements;
    }

    /// <summary>
    /// <c>$</c> ile başlayan bir dolar-tırnak etiketi (<c>$$</c> ya da
    /// <c>$body$</c>) okur; değilse <c>null</c>.
    /// </summary>
    private static string? ReadDollarTag(string text, int start)
    {
        var end = start + 1;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;

        return end < text.Length && text[end] == '$'
            ? text[start..(end + 1)]
            : null;
    }

    private static bool Matches(string text, int start, string tag) =>
        start + tag.Length <= text.Length && text.AsSpan(start, tag.Length).SequenceEqual(tag);

    /// <summary>
    /// Biriken metni ifade olarak ekler. Yalnızca yorum ve boşluktan oluşanlar
    /// atlanıyor — veritabanına göndermenin anlamı yok.
    /// </summary>
    private static void Flush(List<string> statements, System.Text.StringBuilder current)
    {
        var text = current.ToString().Trim();
        current.Clear();

        if (text.Length == 0) return;

        var hasCode = false;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith("--", StringComparison.Ordinal)) { hasCode = true; break; }
        }

        if (hasCode) statements.Add(text);
    }
}
