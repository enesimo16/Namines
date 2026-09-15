using System;
using System.Collections.Generic;
using System.Linq;
using Namines.Core.Analysis;

namespace Namines.Core.Models;

/// <summary>
/// Bir <see cref="SchemaChunkMerger.Merge"/> çağrısının sonucu: birleşmiş şema
/// artı okunabilir notlar.
///
/// <b>Notlar sözleşmenin bir parçasıdır, hata ayıklama çıktısı değil.</b>
/// Düşürülen bir ilişki ya da tekilleştirilen bir tablo sessizce kaybolursa,
/// bu tasarımın önlemeye çalıştığı tam da o başarısızlık biçimidir — bu yüzden
/// her düşme/tekilleştirme işlemi burada bir not üretir; sonraki bir görev
/// bu notları kullanıcıya gösterecektir.
/// </summary>
public sealed record SchemaMergeResult(DatabaseSchema Schema, IReadOnlyList<string> Notes);

/// <summary>
/// Paralel üretilen şema parçalarını ("chunk") tek bir <see cref="DatabaseSchema"/>'da birleştirir.
///
/// <b>Çözülen temel sorun:</b> chunk B, chunk A'nın ürettiği tablo kimliklerini
/// GÖREMEZ — ikisi de aynı anda, birbirinden habersiz üretiliyor. İkisi de
/// isimden kimlik türetme kuralına (<see cref="SchemaIdConvention"/>) uyması
/// gerekse de, modeller bu kurala her zaman uymaz. Bu yüzden birleştirme,
/// ilişki uçlarını ham kimlik eşleşmiyorsa İSİMLE çözer — ve neyi düşürdüğü
/// konusunda dürüst olmak zorundadır (bkz. <see cref="SchemaMergeResult.Notes"/>).
/// </summary>
public static class SchemaChunkMerger
{
    public static SchemaMergeResult Merge(string schemaName, IReadOnlyList<DatabaseSchema> chunks)
    {
        var notes = new List<string>();
        var schema = new DatabaseSchema
        {
            Name = schemaName,
        };

        // --- 1) Tablolar: normalize edilmiş ada göre tekilleştir, ilk kazanır. ---
        var seenNormalizedNames = new HashSet<string>();
        foreach (var chunk in chunks)
        {
            foreach (var table in chunk.Tables)
            {
                var key = SchemaIdConvention.Normalize(table.Name);
                if (seenNormalizedNames.Add(key))
                {
                    schema.Tables.Add(table);
                }
                else
                {
                    // Not, dropped tablonun kendi adı yerine tekilleştirme
                    // ANAHTARINI (normalize edilmiş ad) taşır — kullanıcı için
                    // hangi tablonun elenip hangi tablonun kazandığı böylece
                    // aynı kanonik isimle eşleşir, "Users" vs "users" gibi
                    // büyük/küçük harf farkı notu tanımsız kılmaz.
                    notes.Add($"[merge] Duplicate table '{key}' from another chunk was dropped.");
                }
            }
        }

        // --- 2) Kimlik çözümleme sözlükleri. ---
        var byId = new Dictionary<string, SchemaTable>();
        foreach (var table in schema.Tables)
        {
            // Gerçek kimlik çakışması olmamalı (tablolar zaten normalize adla
            // tekilleştirildi) ama farklı chunk'lar aynı kimliği farklı tablolara
            // vermiş olabilir — böyle bir durumda ilk kazanır, sessizce.
            byId.TryAdd(table.Id, table);
        }

        // İsimden çözümleme sözlüğü: hem çıplak normalize form ("users") hem de
        // sözleşme formu ("t_users") anahtar olarak eklenir — chunk'lar ilişki
        // uçlarını ikisinden biriyle yazmış olabilir.
        //
        // KASITLI KARAR: normalize formu BOŞ olan adlar (ör. "" ya da "---" →
        // ikisi de "" ve dolayısıyla TableId "t_") bu sözlüğe hiç eklenmez.
        // Aksi halde iki farklı "anlamsız" isimli tablo aynı anahtarda çarpışır
        // ve bir ilişki, hiçbir gerçek ilgisi olmayan bir tabloya rastgele
        // (kayıt sırasına bağlı) yeniden bağlanmış olurdu. Böyle bir kimliğin
        // isimle çözülmesi yerine "bilinmeyen tablo" olarak düşüp not üretmesi
        // daha dürüst bir başarısızlık biçimidir.
        var byName = new Dictionary<string, SchemaTable>();
        foreach (var table in schema.Tables)
        {
            var normalized = SchemaIdConvention.Normalize(table.Name);
            if (string.IsNullOrEmpty(normalized)) continue;

            byName.TryAdd(normalized, table);
            byName.TryAdd(SchemaIdConvention.TableId(table.Name), table);
        }

        // --- 3) İlişkiler: gerçek id önce, sonra isim/sözleşme formu, yoksa düşür. ---
        foreach (var chunk in chunks)
        {
            foreach (var relation in chunk.Relations)
            {
                var resolvedSource = ResolveTableId(relation.SourceTableId, byId, byName);
                var resolvedTarget = ResolveTableId(relation.TargetTableId, byId, byName);

                if (resolvedSource is null)
                {
                    notes.Add($"[merge] Relation to unknown table '{relation.SourceTableId}' was dropped.");
                    continue;
                }

                if (resolvedTarget is null)
                {
                    notes.Add($"[merge] Relation to unknown table '{relation.TargetTableId}' was dropped.");
                    continue;
                }

                relation.SourceTableId = resolvedSource;
                relation.TargetTableId = resolvedTarget;
                schema.Relations.Add(relation);
            }
        }

        // --- 4) Enums/Triggers/StoredProcedures: tüm parçalardan birleştir, id'ye göre tekilleştir. ---
        MergeById(chunks.SelectMany(c => c.Enums), schema.Enums, e => e.Id, notes, "enum");
        MergeById(chunks.SelectMany(c => c.Triggers), schema.Triggers, t => t.Id, notes, "trigger");
        MergeById(chunks.SelectMany(c => c.StoredProcedures), schema.StoredProcedures, p => p.Id, notes, "stored procedure");

        // --- 5) SchemaId: boşsa yeni GUID (yapıcıda zaten üretiliyor, dokunma). ---
        if (string.IsNullOrWhiteSpace(schema.SchemaId))
            schema.SchemaId = Guid.NewGuid().ToString();

        return new SchemaMergeResult(schema, notes);
    }

    /// <summary>
    /// Bir ilişki ucundaki ham kimliği gerçek tabloya çözer.
    ///
    /// <b>Sıra kasıtlı:</b> önce gerçek <c>byId</c> eşleşmesi denenir — bir
    /// chunk kurala uyup doğru kimliği üretmişse buna güvenilir. Ancak
    /// eşleşmezse isim/sözleşme formuna düşülür. Sıra TERS çevrilirse, kurala
    /// uyan bir chunk'ın kimliği, aynı normalize isme sahip BAŞKA bir tabloyla
    /// çakışıp yanlış tabloya yeniden bağlanabilirdi.
    /// </summary>
    private static string? ResolveTableId(
        string rawId,
        IReadOnlyDictionary<string, SchemaTable> byId,
        IReadOnlyDictionary<string, SchemaTable> byName)
    {
        if (byId.TryGetValue(rawId, out var byIdMatch)) return byIdMatch.Id;
        if (byName.TryGetValue(rawId, out var byNameMatch)) return byNameMatch.Id;
        return null;
    }

    /// <summary>
    /// <see cref="SchemaEnum"/>/<see cref="SchemaTrigger"/>/<see cref="SchemaStoredProcedure"/>
    /// gibi kimlikle tekilleştirilen koleksiyonları birleştirir.
    ///
    /// <b>Not üretmek zorunlu:</b> iki chunk bağımsız olarak aynı türetilmiş
    /// kimlikle (ör. aynı "status" enum'u) bir öğe üretebilir. Bu durumda
    /// ikincisi sessizce atılırsa, tablo/ilişki tarafında özenle kaçınılan
    /// "sessiz kayıp" burada da tekrarlanmış olur — dosyanın geri kalanıyla
    /// tutarlı olmak için her atılan yinelenen kendi notunu üretir.
    /// </summary>
    private static void MergeById<T>(
        IEnumerable<T> items,
        List<T> target,
        Func<T, string> idSelector,
        List<string> notes,
        string kind)
    {
        var seen = new HashSet<string>();
        foreach (var item in items)
        {
            var id = idSelector(item);
            if (seen.Add(id))
            {
                target.Add(item);
            }
            else
            {
                notes.Add($"[merge] Duplicate {kind} '{id}' from another chunk was dropped.");
            }
        }
    }
}
