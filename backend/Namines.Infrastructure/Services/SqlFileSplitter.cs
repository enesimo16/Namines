using System.Collections.Generic;
using System.Text;

namespace Namines.Infrastructure.Services;

/// <summary>
/// second-phase/13-DAGITIM-HEDEFLERI.md — paylaşımlı barındırma panellerinin
/// çoğunda içe aktarma dosya boyutu sınırı var. Büyük bir şemayı tek dosyada
/// göndermek yüklemeyi baştan imkânsız kılar.
///
/// <b>Yalnızca ifade sınırında böler</b> (<c>;\n</c>) — bir <c>CREATE TABLE</c>
/// ya da <c>ALTER TABLE</c> ifadesinin ortasından kesmek, ikinci dosyayı tek
/// başına çalıştırılamaz hâle getirirdi.
/// </summary>
public static class SqlFileSplitter
{
    public static List<string> Split(string sql, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(sql) <= maxBytes)
            return new List<string> { sql };

        var statements = sql.Split(";\n");
        var parts = new List<string>();
        var current = new StringBuilder();

        foreach (var raw in statements)
        {
            if (raw.Length == 0) continue;
            var statement = raw + ";\n";
            var statementBytes = Encoding.UTF8.GetByteCount(statement);

            if (current.Length > 0 && Encoding.UTF8.GetByteCount(current.ToString()) + statementBytes > maxBytes)
            {
                parts.Add(current.ToString());
                current.Clear();
            }

            // Tek bir ifade bile sınırdan büyükse (çok geniş bir tablo), yine de
            // kendi dosyasına yazılır — bölünemez bir birim daha küçültülemez,
            // sınırı biraz aşması, ifadeyi ortadan kesmekten daha güvenli.
            current.Append(statement);
        }

        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }
}
