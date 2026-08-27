using System.Collections.Generic;
using System.Text;

namespace Namines.Infrastructure.Services;

/// <summary>
/// second-phase/13-DAGITIM-HEDEFLERI.md — paylaşımlı barındırma panellerinin
/// çoğunda içe aktarma dosya boyutu sınırı var. Büyük bir şemayı tek dosyada
/// göndermek yüklemeyi baştan imkânsız kılar.
///
/// <b>Yalnızca ifade sınırında böler</b> — bir <c>CREATE TABLE</c> ya da
/// <c>ALTER TABLE</c> ifadesinin ortasından kesmek, ikinci dosyayı tek başına
/// çalıştırılamaz hâle getirirdi.
///
/// <b>Satır sonu biçiminden BAĞIMSIZ.</b> İlk hâli <c>";\n"</c> ile bölüyordu;
/// oysa DDL üreticileri <c>StringBuilder.AppendLine</c> kullanıyor ve Windows'ta
/// <c>";\r\n"</c> üretiyor — yani gerçek çıktıda hiçbir sınır bulunamıyor ve
/// bölme sessizce hiç çalışmıyordu. Birim test bunu kaçırmıştı çünkü fixture'ı
/// elle <c>"\n"</c> ile kuruyordu, üreticinin gerçek çıktısıyla değil.
/// </summary>
public static class SqlFileSplitter
{
    public static List<string> Split(string sql, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(sql) <= maxBytes)
            return new List<string> { sql };

        var parts = new List<string>();
        var current = new StringBuilder();
        var currentBytes = 0;

        foreach (var statement in SplitIntoStatements(sql))
        {
            var statementBytes = Encoding.UTF8.GetByteCount(statement);

            if (current.Length > 0 && currentBytes + statementBytes > maxBytes)
            {
                parts.Add(current.ToString());
                current.Clear();
                currentBytes = 0;
            }

            // Tek bir ifade bile sınırdan büyükse (çok geniş bir tablo), yine de
            // kendi dosyasına yazılır — bölünemez bir birim daha küçültülemez,
            // sınırı biraz aşması, ifadeyi ortadan kesmekten daha güvenli.
            current.Append(statement);
            currentBytes += statementBytes;
        }

        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }

    /// <summary>
    /// Metni ifadelere böler; her parça kendi noktalı virgülünü ve satır sonunu
    /// KORUR (birleştirildiğinde orijinal metin aynen geri gelir).
    ///
    /// Sınır = noktalı virgülden hemen sonra bir satır sonu (<c>\n</c> ya da
    /// <c>\r\n</c>) gelmesi. Satır sonu gelmiyorsa noktalı virgül bir sınır
    /// sayılmaz — böylece <c>DEFAULT 'a;b'</c> gibi bir dize sabitinin
    /// ortasından kesilmiyor.
    /// </summary>
    private static IEnumerable<string> SplitIntoStatements(string sql)
    {
        var start = 0;
        var i = 0;

        while (i < sql.Length)
        {
            if (sql[i] != ';') { i++; continue; }

            var end = i + 1;
            if (end < sql.Length && sql[end] == '\r') end++;

            if (end < sql.Length && sql[end] == '\n')
            {
                end++;
            }
            else if (end < sql.Length)
            {
                // ";" var ama ardından satır sonu yok — sınır değil.
                i++;
                continue;
            }

            yield return sql[start..end];
            start = end;
            i = end;
        }

        if (start < sql.Length) yield return sql[start..];
    }
}
