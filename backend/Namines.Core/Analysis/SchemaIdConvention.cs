using System.Text;

namespace Namines.Core.Analysis;

/// <summary>
/// Tablo/kolon adlarından kimlik türeten TEK yer.
///
/// <b>Neden kimlikler isimden türetiliyor, rastgele üretilmiyor:</b> büyük bir
/// şema birkaç paralel AI çağrısına ("chunk") bölündüğünde, her chunk
/// diğerlerinin ürettiği tabloları GÖRMÜYOR — aynı anda çalışıyorlar. Chunk B,
/// chunk A'nın "users" tablosuna "t_a3f9" gibi rastgele bir kimlik verdiğini
/// bilemez. Ama ikisi de aynı deterministik kurala göre "users" → "t_users"
/// üretirse, kimlikler çarpışmadan hizalanır — birleştirme adımı isimle
/// eşleştirip sapmaları düzeltir, ama bu kural ilk savunma hattı.
/// </summary>
public static class SchemaIdConvention
{
    /// <summary>
    /// Bir adı kimlik gövdesine indirger: küçük harf, kelime sınırları `_`,
    /// ardışık `_` tekilleştirilmiş, baş/son `_` kırpılmış.
    ///
    /// PascalCase/camelCase sınırlarına da `_` eklenir ("OrderItems" →
    /// "order_items") — AI çıktısı bazen tabloyu PascalCase, bazen snake_case
    /// döndürüyor; ikisi de aynı kimliğe düşmezse aynı tablo iki farklı
    /// kimlikle görünür ve birleştirme kırılır.
    /// </summary>
    public static string Normalize(string name)
    {
        var input = name?.Trim() ?? string.Empty;
        var sb = new StringBuilder(input.Length + 8);

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];

            if (char.IsLetterOrDigit(c))
            {
                // camelCase/PascalCase sınırı: küçük/rakamdan büyüğe geçiş ("orderItems" → "order_Items").
                if (char.IsUpper(c) && i > 0 && char.IsLetterOrDigit(input[i - 1]) && !char.IsUpper(input[i - 1]))
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                // Boşluk, tire, nokta, alt çizgi vb. hepsi tek bir ayraca indirgenir.
                sb.Append('_');
            }
        }

        // Ardışık ayraçları tekille, baştaki/sondaki ayracı kırp.
        var collapsed = new StringBuilder(sb.Length);
        var lastWasUnderscore = false;
        foreach (var c in sb.ToString())
        {
            if (c == '_')
            {
                if (lastWasUnderscore) continue;
                lastWasUnderscore = true;
            }
            else
            {
                lastWasUnderscore = false;
            }

            collapsed.Append(c);
        }

        return collapsed.ToString().Trim('_');
    }

    /// <summary>Bir tablonun deterministik kimliği.</summary>
    public static string TableId(string tableName) => "t_" + Normalize(tableName);

    /// <summary>
    /// Bir kolonun deterministik kimliği — tablo adını da taşır ki iki farklı
    /// tablodaki aynı kolon adı ("id" gibi) çakışmasın.
    /// </summary>
    public static string ColumnId(string tableName, string columnName) =>
        "c_" + Normalize(tableName) + "_" + Normalize(columnName);
}
