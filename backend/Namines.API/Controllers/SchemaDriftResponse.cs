using System.Linq;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.API.Controllers;

/// <summary>
/// "Kodun şunu diyor, elimizdeki şema şunu diyor" farkının TEK biçimi.
///
/// <b>Neden ortak:</b> aynı hesabı iki uç yapıyor (yüklenen dosyalar ve
/// taranan depo). İkisi kendi kopyasını taşısaydı, aynı depo için iki farklı
/// drift cevabı üretmeleri an meselesiydi — hizalama adımını birinde unutmak
/// yeterdi, ki o adım atlandığında rapor HER tabloyu "silindi + eklendi"
/// gösteriyor (second-phase/11'de yaşanmış ve testle kilitlenmiş bir hata).
/// </summary>
public static class SchemaDriftResponse
{
    /// <param name="fromCode">Koddan/depodan çıkarılan şema.</param>
    /// <param name="current">Karşılaştırılacak şema (canvas ya da veritabanı).</param>
    public static object Build(DatabaseSchema fromCode, DatabaseSchema current, DatabaseType dbType)
    {
        // Kod UUID taşımaz — hizalanmadan karşılaştırmak HER tabloyu
        // "silindi + eklendi" gösterir (bkz. SchemaUuidAligner).
        var aligned = SchemaUuidAligner.AlignTo(fromCode, current);

        // Yön bilinçli: ESKİ = kodun söylediği, YENİ = şu an elimizdeki şema.
        // Böylece "eklendi/silindi" ifadeleri "veritabanında var ama kodda yok"
        // diye okunuyor — kullanıcının sorduğu soru bu.
        var impact = SchemaImpactAnalyzer.Analyze(aligned, current, dbType);

        return new
        {
            hasDrift = impact.AffectedTables.Count > 0 || impact.BreakingChanges.Count > 0,
            overallRisk = impact.OverallRisk.ToString(),
            affectedTables = impact.AffectedTables.Select(t => new
            {
                t.TableName,
                kind = t.Kind.ToString(),
                t.ChangedColumns,
            }),
            breakingChanges = impact.BreakingChanges.Select(b => new
            {
                b.TableName,
                b.ColumnName,
                b.Description,
                kind = b.Kind.ToString(),
            }),
        };
    }
}
