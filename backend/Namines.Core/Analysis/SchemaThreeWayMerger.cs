using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Namines.Core.Models;

namespace Namines.Core.Analysis;

/// <summary>
/// Çakışmanın türü. <b>Sınırdan STRING olarak geçer</b> — istemcide bire bir
/// kopyalanmış bir birlik tipiyle eşlemek bu projede bir kez kırıldı.
/// </summary>
public enum MergeConflictKind
{
    TableAdded,
    TableDeleted,
    TableRenamed,
    ColumnAdded,
    ColumnDeleted,
    ColumnModified,

    /// <summary>İki taraf da aynı ADI kullanan AYRI birer nesne eklemiş.</summary>
    NameCollision,

    /// <summary>Bir taraf sildi, diğeri değiştirdi. Otomatik seçim veri kaybıdır.</summary>
    DeleteVsModify,
}

/// <param name="Id">Arayüzün seçim takibi için kararlı anahtar.</param>
/// <param name="OursValue">Üzerine birleşilen tarafın (hedef) değeri.</param>
/// <param name="TheirsValue">Birleşen tarafın (kaynak) değeri.</param>
/// <param name="Blocking">
/// Otomatik ya da elle seçimle çözülemez; merge durdurulmalı. Yalnızca
/// <see cref="MergeConflictKind.DeleteVsModify"/> için true.
/// </param>
public sealed record MergeConflict(
    string Id,
    MergeConflictKind Kind,
    string TableName,
    string? ColumnName,
    object? OursValue,
    object? TheirsValue,
    bool Blocking,
    string Explanation);

/// <param name="Merged">Otomatik kararlar uygulanmış şema.</param>
/// <param name="Conflicts">İnsana sorulacak ya da merge'i bloke eden farklar.</param>
/// <param name="AutoMerged">Sorulmadan uygulanan her değişikliğin kısa açıklaması.</param>
public sealed record ThreeWayMergeResult(
    DatabaseSchema Merged,
    IReadOnlyList<MergeConflict> Conflicts,
    IReadOnlyList<string> AutoMerged);

/// <summary>
/// İki branch şemasını ORTAK ATALARINA bakarak birleştirir
/// (github/03-COKLU-GELISTIRICI-MERGE.md).
///
/// <b>Ortak atanın olması her şeyi değiştiriyor:</b> iki yollu bir diff
/// "yalnızca onlar değiştirdi" ile "ikimiz de değiştirdik"i ayırt edemez ve
/// bu yüzden HER farkı kullanıcıya sormak zorundadır. Base varken farkların
/// çoğu tek taraflıdır ve sorulmadan birleşir; geriye yalnızca gerçek
/// çakışmalar kalır.
///
/// <b>Saf:</b> veritabanı, HTTP ve dil modeli yok. Birleştirme kararı
/// deterministik bir kural motoru — PR'a bakıp merge kararı veren insan için
/// bu fark belirleyici.
/// </summary>
public static class SchemaThreeWayMerger
{
    public static ThreeWayMergeResult Merge(
        DatabaseSchema baseSchema, DatabaseSchema ours, DatabaseSchema theirs)
    {
        var conflicts = new List<MergeConflict>();
        var autoMerged = new List<string>();

        // Tablo dışındaki her şey (ilişkiler, enum'lar, motor bilgisi) hedef
        // taraftan geliyor — birleştirme onun üzerine yapılıyor. Alan alan
        // kopyalamak yerine KLONLAYIP tabloları değiştirmek, şemaya yarın yeni
        // bir alan eklendiğinde onun sessizce düşmesini engelliyor.
        var merged = Clone(ours);
        merged.Tables = new List<SchemaTable>();

        var baseTables = ById(baseSchema.Tables);
        var ourTables = ById(ours.Tables);
        var theirTables = ById(theirs.Tables);

        foreach (var uuid in AllKeys(baseTables, ourTables, theirTables))
        {
            baseTables.TryGetValue(uuid, out var b);
            ourTables.TryGetValue(uuid, out var o);
            theirTables.TryGetValue(uuid, out var t);

            // Tablo seviyesinde YALNIZCA varlık kararı veriliyor. Tabloların
            // tamamını (kolonlarıyla) karşılaştırmak, farklı kolonlara dokunan
            // iki branch'i "tablo çakışması" sayardı — oysa asıl iş kolon
            // kolon çözülmek zorunda.
            if (o is null && t is null)
                continue;   // iki tarafta da silinmiş ya da hiç olmamış

            if (b is not null && (o is null || t is null))
            {
                // Bir taraf tabloyu silmiş. Diğeri ona dokunmadıysa silme
                // otomatik uygulanır; dokunduysa otomatik seçim veri kaybı olur.
                var survivor = o ?? t;
                if (Same(b, survivor))
                {
                    autoMerged.Add($"{b.Name} removed");
                    continue;
                }

                merged.Tables.Add(Clone(survivor!));
                conflicts.Add(new MergeConflict(
                    uuid, MergeConflictKind.DeleteVsModify, survivor!.Name, null, o, t, true,
                    $"One branch deleted '{survivor.Name}' while the other changed it — merging either way loses work."));
                continue;
            }

            merged.Tables.Add(MergeTable(b, o, t, conflicts, autoMerged));

            if (b is null) autoMerged.Add($"{(o ?? t)!.Name} added");
        }

        // Yeni eklenen tablolar iki tarafta da AYRI uuid'lerle gelmiş olabilir;
        // uuid eşleşmesi onları ayrı tablo sayar ve aynı isimde iki tablo
        // bırakır. Ad çakışması bu yüzden birleştirmeden SONRA ayrıca aranıyor.
        ResolveNameCollisions(baseSchema, merged, conflicts);

        return new ThreeWayMergeResult(merged, conflicts, autoMerged);
    }

    // ── Tablo içi birleştirme ────────────────────────────────────────────────

    private static SchemaTable MergeTable(
        SchemaTable? b, SchemaTable? o, SchemaTable? t,
        List<MergeConflict> conflicts, List<string> autoMerged)
    {
        var winner = Clone((o ?? t)!);

        // Tablo adı kendi başına bir karar: bir taraf yeniden adlandırmış,
        // diğeri kolon eklemiş olabilir — satır bazlı bir merge'in çözemediği,
        // StableUuid sayesinde çözülen durum.
        if (b is not null && o is not null && t is not null)
        {
            var nameDecision = Decide(b.Name, o.Name, t.Name);
            if (nameDecision.Outcome == Outcome.Conflict)
            {
                conflicts.Add(new MergeConflict(
                    $"{winner.StableUuid}-name", MergeConflictKind.TableRenamed,
                    o.Name, null, o.Name, t.Name, false,
                    $"Both branches renamed this table: '{o.Name}' and '{t.Name}'."));
                winner.Name = o.Name;
            }
            else
            {
                winner.Name = (string)nameDecision.Value!;
                if (nameDecision.Note is not null && winner.Name != b.Name)
                    autoMerged.Add($"{b.Name} renamed to {winner.Name}");
            }
        }

        winner.Columns = new List<SchemaColumn>();

        var baseColumns = ById(b?.Columns);
        var ourColumns = ById(o?.Columns);
        var theirColumns = ById(t?.Columns);

        foreach (var uuid in AllKeys(baseColumns, ourColumns, theirColumns))
        {
            baseColumns.TryGetValue(uuid, out var bc);
            ourColumns.TryGetValue(uuid, out var oc);
            theirColumns.TryGetValue(uuid, out var tc);

            var decision = Decide(bc, oc, tc);

            switch (decision.Outcome)
            {
                case Outcome.Take:
                    winner.Columns.Add(Clone((SchemaColumn)decision.Value!));
                    if (decision.Note is { } note) autoMerged.Add($"{winner.Name}.{((SchemaColumn)decision.Value!).Name} {note}");
                    break;

                case Outcome.Drop:
                    if (decision.Note is not null && bc is not null)
                        autoMerged.Add($"{winner.Name}.{bc.Name} removed");
                    break;

                case Outcome.Conflict:
                    var survivingColumn = oc ?? tc;
                    if (survivingColumn is not null) winner.Columns.Add(Clone(survivingColumn));

                    conflicts.Add(new MergeConflict(
                        $"{winner.StableUuid}-{uuid}",
                        decision.Kind,
                        winner.Name,
                        (oc ?? tc ?? bc)!.Name,
                        oc, tc,
                        decision.Kind == MergeConflictKind.DeleteVsModify,
                        decision.Explanation!));
                    break;
            }
        }

        return winner;
    }

    // ── Karar motoru ─────────────────────────────────────────────────────────

    private enum Outcome { Take, Drop, Conflict }

    private sealed record Decision(
        Outcome Outcome, object? Value, string? Note, MergeConflictKind Kind = default, string? Explanation = null);

    /// <summary>
    /// Üçlü karar. <b>Belirsizlikte en kısıtlayıcı davranışa düşülür</b>
    /// (<c>ReferentialActionSql</c>'in kuralı): otomatik seçim yapılmaz,
    /// insana sorulur ya da bloke edilir. Şemada "son yazan kazanır" veri
    /// kaybıdır.
    /// </summary>
    private static Decision Decide<T>(T? b, T? o, T? t) where T : class
    {
        var ourChanged = !Same(b, o);
        var theirChanged = !Same(b, t);

        // Hiçbiri dokunmamış.
        if (!ourChanged && !theirChanged)
            return o is null ? new Decision(Outcome.Drop, null, null) : new Decision(Outcome.Take, o, null);

        // Aynı sonuca varmışlar — çakışma değil.
        if (Same(o, t))
            return o is null
                ? new Decision(Outcome.Drop, null, "removed on both sides")
                : new Decision(Outcome.Take, o, "changed identically on both sides");

        // Tek taraf dokunmuş: o taraf kazanır, sorulmaz. Ortak atanın bütün
        // değeri bu satırda.
        if (ourChanged && !theirChanged)
            return o is null ? new Decision(Outcome.Drop, null, "removed") : new Decision(Outcome.Take, o, "changed");

        if (!ourChanged && theirChanged)
            return t is null ? new Decision(Outcome.Drop, null, "removed") : new Decision(Outcome.Take, t, "changed");

        // İkisi de dokunmuş ve farklı sonuçlar.
        if (o is null || t is null)
            return new Decision(Outcome.Conflict, null, null, MergeConflictKind.DeleteVsModify,
                "One branch deleted this while the other changed it — merging either way loses work.");

        var kind = typeof(T) == typeof(SchemaColumn)
            ? MergeConflictKind.ColumnModified
            : typeof(T) == typeof(SchemaTable) ? MergeConflictKind.TableRenamed : MergeConflictKind.ColumnModified;

        return new Decision(Outcome.Conflict, null, null, kind,
            "Both branches changed this in different ways.");
    }

    // ── Ad çakışmaları ───────────────────────────────────────────────────────

    /// <summary>
    /// Aynı adı taşıyan iki AYRI nesne kaldıysa bunu çakışma olarak bildirir ve
    /// birleşen şemadan fazlasını çıkarır.
    ///
    /// <b>Neden gerekli:</b> iki geliştirici aynı tabloya <c>status</c> kolonu
    /// eklerse iki farklı <c>StableUuid</c> üretilir; uuid eşleşmesine göre
    /// ikisi de "yeni kolon"dur ve şema iki <c>status</c> ile ÇALIŞMAZ hâle
    /// gelir. Kimlik eşleşmesi bunu göremez, ad taraması görür.
    ///
    /// Base'de zaten aynı adla var olan bir şey çakışma sayılmaz — o durum
    /// kimlik eşleşmesiyle zaten çözülmüştür.
    /// </summary>
    private static void ResolveNameCollisions(
        DatabaseSchema baseSchema, DatabaseSchema merged, List<MergeConflict> conflicts)
    {
        var baseTableNames = baseSchema.Tables.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var group in merged.Tables
                     .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1 && !baseTableNames.Contains(g.Key))
                     .ToList())
        {
            var kept = group.First();
            foreach (var extra in group.Skip(1)) merged.Tables.Remove(extra);

            conflicts.Add(new MergeConflict(
                $"collision-table-{group.Key}", MergeConflictKind.NameCollision,
                group.Key, null, kept, group.Skip(1).First(), false,
                $"Both branches added a different table called '{group.Key}'."));
        }

        foreach (var table in merged.Tables)
        {
            var baseColumnNames = baseSchema.Tables
                .FirstOrDefault(x => x.StableUuid == table.StableUuid)?.Columns
                .Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in table.Columns
                         .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                         .Where(g => g.Count() > 1 && !baseColumnNames.Contains(g.Key))
                         .ToList())
            {
                var kept = group.First();
                foreach (var extra in group.Skip(1)) table.Columns.Remove(extra);

                conflicts.Add(new MergeConflict(
                    $"collision-{table.StableUuid}-{group.Key}", MergeConflictKind.NameCollision,
                    table.Name, group.Key, kept, group.Skip(1).First(), false,
                    $"Both branches added a different column called '{group.Key}' to {table.Name}."));
            }
        }
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────

    private static Dictionary<string, T> ById<T>(IEnumerable<T>? items) where T : class
    {
        var map = new Dictionary<string, T>(StringComparer.Ordinal);
        if (items is null) return map;

        foreach (var item in items)
        {
            var key = KeyOf(item);
            // Aynı uuid iki kez geçerse ilki tutulur: ikincisi zaten bozuk bir
            // girdidir ve onu sessizce üzerine yazmak, hangisinin kaybolduğunu
            // belirsizleştirirdi.
            map.TryAdd(key, item);
        }

        return map;
    }

    private static string KeyOf<T>(T item) => item switch
    {
        SchemaTable table => Fallback(table.StableUuid, table.Id, table.Name),
        SchemaColumn column => Fallback(column.StableUuid, column.Id, column.Name),
        _ => item!.ToString()!,
    };

    /// <summary>
    /// Kimliği olmayan şemalar için ada düşülür.
    ///
    /// <b>Gerekli:</b> koddan çıkarılan ve elle kurulan şemalarda
    /// <c>StableUuid</c> boş olabiliyor; boş anahtarla eşleştirmek HER nesneyi
    /// "silindi + eklendi" gösterirdi — bu hata bu projede bir kez yaşandı.
    /// </summary>
    private static string Fallback(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? string.Empty;

    private static IEnumerable<string> AllKeys<T>(
        Dictionary<string, T> a, Dictionary<string, T> b, Dictionary<string, T> c) =>
        a.Keys.Concat(b.Keys).Concat(c.Keys).Distinct(StringComparer.Ordinal);

    /// <summary>
    /// İki değerin aynı olup olmadığı. Serileştirilmiş karşılaştırma, çünkü
    /// şema nesneleri alan alan karşılaştırılırsa yeni bir alan eklendiğinde
    /// (ör. <c>Collation</c>) karşılaştırma onu SESSİZCE yok sayar ve gerçek
    /// bir değişiklik "değişmedi" sayılır.
    /// </summary>
    private static bool Same<T>(T? x, T? y) where T : class
    {
        if (x is null && y is null) return true;
        if (x is null || y is null) return false;
        if (x is string sx && y is string sy) return string.Equals(sx, sy, StringComparison.Ordinal);

        return JsonSerializer.Serialize(x) == JsonSerializer.Serialize(y);
    }

    private static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
