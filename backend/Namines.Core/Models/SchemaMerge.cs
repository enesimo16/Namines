using System.Collections.Generic;
using System.Linq;

namespace Namines.Core.Models;

/// <summary>
/// Kısmi bir revizyon sonucunu tam şemayla birleştirir.
///
/// <b>Neden gerekli:</b> revizyon promptu modelden yalnızca <c>tables</c> +
/// <c>relations</c> istiyor. Dönen JSON'dan kurulan <see cref="DatabaseSchema"/>'da
/// <see cref="DatabaseSchema.Triggers"/>, <see cref="DatabaseSchema.StoredProcedures"/>
/// ve <see cref="DatabaseSchema.Enums"/> BOŞ olur. Bu nesneyi doğrudan kullanmak,
/// her düzeltme turunda kullanıcının trigger'larını ve enum'larını sessizce
/// silmek demekti — üstelik bu "düzeltme" adı altında oluyordu.
///
/// <b>Kural:</b> kısmi sonuç bir listeyi DÖNDÜRDÜYSE o kazanır (model o alanı
/// bilerek değiştirmiştir); döndürmediyse orijinal korunur.
/// </summary>
public static class SchemaMerge
{
    public static DatabaseSchema PreserveUnrevised(DatabaseSchema original, DatabaseSchema partial)
    {
        if (partial.Triggers.Count == 0 && original.Triggers.Count > 0)
            partial.Triggers = original.Triggers.ToList();

        if (partial.StoredProcedures.Count == 0 && original.StoredProcedures.Count > 0)
            partial.StoredProcedures = original.StoredProcedures.ToList();

        if (partial.Enums.Count == 0 && original.Enums.Count > 0)
            partial.Enums = original.Enums.ToList();

        if (string.IsNullOrWhiteSpace(partial.SchemaId)) partial.SchemaId = original.SchemaId;
        if (string.IsNullOrWhiteSpace(partial.Name)) partial.Name = original.Name;

        return partial;
    }

    /// <summary>
    /// Kapsamlı (scoped) bir onarım turunun sonucunu tam şemayla birleştirir.
    ///
    /// <b>Neden <see cref="PreserveUnrevised"/> yetmiyor:</b> o metot
    /// <c>partial.Tables</c>'ı olduğu gibi ŞEMANIN TAMAMI sayıyordu — bu,
    /// modele HER ZAMAN tüm tabloların gönderildiği eski davranışta doğruydu.
    /// Artık onarım turu yalnızca bulguda adı geçen tabloları model'e
    /// GÖNDERİYOR (bkz. <c>GroqSchemaDraftSource.RepairAsync</c>), yani
    /// <paramref name="repairedSubset"/>.Tables gerçekten bir ALT KÜME —
    /// onu tam liste sayıp kullanmak, kapsam dışındaki her tabloyu sessizce
    /// silmek olurdu.
    ///
    /// <b>Kural (isme göre):</b>
    /// <list type="bullet">
    /// <item>Tam şemada olan ve alt kümede adı GEÇEN tablo → alt kümedeki
    /// hâliyle DEĞİŞTİRİLİR (model onu düzeltti).</item>
    /// <item>Tam şemada olan ve alt kümede adı GEÇMEYEN tablo → AYNEN kalır
    /// (model hiç görmedi, dokunmadı).</item>
    /// <item>Alt kümede olan ama tam şemada adı hiç geçmeyen tablo → modelin
    /// bilerek eklediği YENİ bir tablo sayılır ve sonuca EKLENİR. Bunu
    /// sessizce atmak, modelin bulguyu çözmek için gerçekten ihtiyaç duyduğu
    /// bir tabloyu (ör. bir NSL024 bulgusunu kapatmak için eklediği bir
    /// köprü/audit tablosu) kaybetmek olurdu — ki bu da "onarım" adı
    /// altında veri kaybı demek.</item>
    /// </list>
    ///
    /// Trigger/saklı yordam/enum ve şema kimliği için <see cref="PreserveUnrevised"/>'ın
    /// koruma mantığı aynen uygulanır — bu metot onu SARMALAR, yeniden yazmaz.
    /// İlişkiler (Relations) bugünkü davranışla aynı kalır: onarım turu
    /// kapsamlı modda bile TÜM ilişkileri modele gönderiyor (yalnızca
    /// <c>SelectedTables</c> daraltılıyor), yani bu alanda ekstra bir
    /// birleştirmeye gerek yok.
    /// </summary>
    public static DatabaseSchema SpliceTables(DatabaseSchema full, DatabaseSchema repairedSubset)
    {
        var merged = PreserveUnrevised(full, repairedSubset);

        var spliced = new List<SchemaTable>(full.Tables.Count);
        var namesInFull = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var original in full.Tables)
        {
            namesInFull.Add(original.Name);

            var replacement = repairedSubset.Tables.FirstOrDefault(
                t => string.Equals(t.Name, original.Name, System.StringComparison.OrdinalIgnoreCase));

            spliced.Add(replacement ?? original);
        }

        // Alt kümede olup tam şemada hiç adı geçmeyen tablolar — model
        // bilerek yeni bir tablo eklemiş demektir.
        foreach (var candidate in repairedSubset.Tables)
        {
            if (!string.IsNullOrWhiteSpace(candidate.Name) && namesInFull.Add(candidate.Name))
                spliced.Add(candidate);
        }

        merged.Tables = spliced;
        return merged;
    }
}
