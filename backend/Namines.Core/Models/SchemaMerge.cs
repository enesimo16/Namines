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
}
