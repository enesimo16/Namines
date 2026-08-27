using System;
using System.Collections.Generic;
using System.Linq;
using Namines.Core.Models;

namespace Namines.Core.Analysis;

/// <summary>
/// second-phase/11-KODDAN-SEMA.md — koddan çıkarılan bir şemayı, canlı şemayla
/// karşılaştırılabilir hâle getirir.
///
/// <b>Neden gerekli:</b> <see cref="SchemaImpactAnalyzer"/> tabloları/kolonları
/// <c>StableUuid</c> ile eşleştiriyor ve bu, kendi amacı için DOĞRU — aynı
/// projenin iki sürümü karşılaştırılırken UUID sabit kalır, bu sayede "yeniden
/// adlandırıldı" ile "silindi + eklendi" ayırt edilebiliyor.
///
/// Ama koddan çıkarılan şemada UUID <b>yok</b> — bir <c>schema.prisma</c>
/// dosyası UUID taşımaz, her ayrıştırmada yenisi üretilir. Hizalama yapılmazsa
/// analizör HER tabloyu "silinmiş + eklenmiş" görür ve rapor tamamen anlamsız
/// çıkar (bu gerçek bir hatadır, canlı denemede yakalandı — birim testler
/// ayrıştırıcıyı ve analizörü ayrı ayrı doğruladığı için ikisinin birleşimini
/// kaçırmıştı).
///
/// <b>Analizörü değiştirmek yerine burada hizalanıyor</b>, çünkü UUID
/// eşleştirmesi branch diff / change review akışlarında kasıtlı ve doğru;
/// oraya ad-tabanlı bir yedek eklemek, gerçek bir yeniden adlandırmayı
/// "silme" gibi göstermeye başlardı.
///
/// <b>Ad eşleştirmesinin sınırı dürüstçe kabul ediliyor:</b> kodun taşıdığı
/// tek kimlik addır. Bir tablo hem yeniden adlandırılıp hem değiştirilmişse
/// bu, "silindi + eklendi" olarak görünür — çünkü kodda o iki durumu ayırt
/// edecek bir bilgi gerçekten yoktur.
/// </summary>
public static class SchemaUuidAligner
{
    /// <summary>
    /// <paramref name="extracted"/> içindeki tablo/kolonların StableUuid'lerini,
    /// <paramref name="reference"/> içinde AYNI ADA sahip olanlarınkiyle
    /// değiştirir. Eşleşmeyenler kendi UUID'lerini korur — böylece analizör
    /// onları gerçekten "yeni" ya da "kaldırılmış" sayar.
    /// </summary>
    public static DatabaseSchema AlignTo(DatabaseSchema extracted, DatabaseSchema reference)
    {
        var refTables = reference.Tables
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var table in extracted.Tables)
        {
            if (!refTables.TryGetValue(table.Name, out var refTable)) continue;

            table.StableUuid = refTable.StableUuid;

            var refCols = refTable.Columns
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var column in table.Columns)
            {
                if (refCols.TryGetValue(column.Name, out var refCol))
                    column.StableUuid = refCol.StableUuid;
            }
        }

        return extracted;
    }
}
