using System.Collections.Generic;
using System.Linq;
using Namines.Core.Models.Auth;

namespace Namines.Core.Analysis;

/// <summary>
/// second-phase/10-COKLU-DB.md — bir tablo/kolon değişikliğinin (özellikle
/// silme) BAŞKA bir veritabanındaki kayıtlı mantıksal ilişkileri kırıp
/// kırmadığını bulur.
///
/// <b>Saf ve deterministik.</b> EF Core'a hiç dokunmuyor — çağıran taraf
/// (controller) ilgili <see cref="CrossDatabaseRelation"/> satırlarını veritabanından
/// çekip buraya veriyor. Bu, <c>SchemaImpactAnalyzer</c>'ın (tek proje içi
/// versiyon karşılaştırması) kapsamına GİRMİYOR bilerek — oradaki diff
/// semantiği (eklendi/silindi/yeniden adlandırıldı) "bu iki veritabanı
/// birbirine referans veriyor" sorusuna karşılık gelmiyor.
/// </summary>
public static class CrossDatabaseImpactAnalyzer
{
    public sealed record Impact(
        string RelationId,
        string OtherProjectId,
        string Direction, // "outgoing" (bu taraf başkasına referans veriyordu) | "incoming" (başkası buna referans veriyordu)
        string? Note);

    /// <summary>
    /// <paramref name="tableId"/> (ve varsa <paramref name="columnId"/>) değişince/
    /// silinince hangi kayıtlı ilişkilerin etkileneceğini döner.
    ///
    /// <paramref name="columnId"/> <c>null</c> ise TÜM tablo etkileniyor demektir
    /// (tablo silme) — o tabloya değen her ilişki eşleşir, kolon fark etmeksizin.
    /// </summary>
    public static IReadOnlyList<Impact> FindAffected(
        IEnumerable<CrossDatabaseRelation> relations,
        string projectId,
        string tableId,
        string? columnId = null)
    {
        var results = new List<Impact>();

        foreach (var r in relations)
        {
            var matchesAsSource = r.SourceProjectId == projectId && r.SourceTableId == tableId &&
                                   (columnId is null || r.SourceColumnId == columnId);
            var matchesAsTarget = r.TargetProjectId == projectId && r.TargetTableId == tableId &&
                                   (columnId is null || r.TargetColumnId == columnId);

            if (matchesAsSource)
                results.Add(new Impact(r.Id, r.TargetProjectId, "outgoing", r.Note));
            else if (matchesAsTarget)
                results.Add(new Impact(r.Id, r.SourceProjectId, "incoming", r.Note));
        }

        return results;
    }

    /// <summary>Bir projeye bağlı (kaynak ya da hedef olarak) diğer projelerin id kümesi.</summary>
    public static IReadOnlyList<string> LinkedProjectIds(IEnumerable<CrossDatabaseRelation> relations, string projectId)
    {
        var ids = new HashSet<string>();
        foreach (var r in relations)
        {
            if (r.SourceProjectId == projectId) ids.Add(r.TargetProjectId);
            else if (r.TargetProjectId == projectId) ids.Add(r.SourceProjectId);
        }
        return ids.ToList();
    }
}
