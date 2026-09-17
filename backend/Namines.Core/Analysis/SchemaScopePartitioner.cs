using System;
using System.Collections.Generic;
using System.Linq;

namespace Namines.Core.Analysis;

/// <param name="Label">Parçanın okunabilir etiketi — alan adlarının birleşimi, bölünmüş alanlarda "(n/m)" ekiyle.</param>
/// <param name="OwnedTables">Bu parçanın ürettiği tablo adları — plandaki isimlerle birebir, başka hiçbir parçada tekrarlanmaz.</param>
public sealed record SchemaChunk(string Label, IReadOnlyList<string> OwnedTables);

/// <summary>
/// Büyük bir <see cref="SchemaScopePlan"/>'ı birkaç paralel üretim çağrısına
/// bölen saf, deterministik fonksiyon.
///
/// <b>Neden deterministik olmak zorunda:</b> aynı plan iki kez bölündüğünde
/// aynı parçalar çıkmalı — sonraki adımlar (Task 5, 7) bu parçaları paralel
/// çağırıp sonuçları birleştiriyor; bölme her seferinde farklı çıksa bir
/// hatayı yeniden üretmek imkansızlaşırdı.
///
/// <b>Neden alan sınırlarını mümkün olduğunca koruyoruz:</b> bir alan
/// (ör. "Identity") tek bir çağrıda üretilirse, o çağrının modeli alan
/// içindeki ilişkileri (FK'ler, ortak sözlük) bütünsel görür. Sadece hedefi
/// aşan büyük alanlar bölünüyor — küçük alanlar önce doldurulan parçaya
/// eklenerek çağrı sayısı gereksiz çoğalmıyor.
/// </summary>
public static class SchemaScopePartitioner
{
    /// <summary>Bir parçanın hedeflediği üst tablo sayısı — ~32k çıktı token tavanına sığacak şekilde seçildi.</summary>
    public const int TargetTablesPerChunk = 9;

    /// <summary>Bu sayının altındaki planlar bölünmeden tek çağrıda üretilebilir; bölme kendi ek maliyetini haklı çıkarmaz.</summary>
    public const int PartitionThreshold = 12;

    /// <summary>
    /// Tabloya isabet eden ortalama çıktı token'ı (spec'in ölçümü: kolon ≈ 50
    /// token, 8 kolonlu + 2 index'li tablo ≈ 550-600 token). Tavandan tablo
    /// sayısına geçmek için bölen olarak kullanılıyor.
    /// </summary>
    private const int EstimatedTokensPerTable = 600;

    /// <summary>
    /// Yukarıdaki iki sabit, Pro/Team tavanlarına (16.000/32.000) göre
    /// seçilmişti (final whole-branch review I2). Free'nin tavanı (6.000 ≈ 10
    /// tablo) bu sabitlerin altında kaldığından, tek-çağrı eşiği olan 12'nin
    /// ALTINDA kalan bir plan bile (ör. 11-12 tablo) tek çağrıya düşüyor ve
    /// kesiliyordu — bölme mekanizması tam da bunu çözerdi ama devreye hiç
    /// girmiyordu.
    ///
    /// <b>Neden tavandan türetiliyor, sabit tutulmuyor:</b> eşiği/hedefi
    /// katman başına ayrı ayrı sabitlemek (Free=8, Pro=12, Team=... gibi) her
    /// yeni tavan/model değişikliğinde bu dosyaya dokunmayı gerektirirdi.
    /// Tavan zaten <see cref="Interfaces.ISchemaDraftSource.EffectiveMaxOutputTokensAsync"/>
    /// üzerinden biliniyor — ondan türetmek tek doğru kaynağı koruyor.
    ///
    /// <b>Üst sınır olarak KORUNUYOR, aşılmıyor:</b> Pro/Team davranışı bugünkü
    /// gibi kalmalı — yalnızca daha düşük tavanlar daha küçük eşik/hedef
    /// alır, hiçbir tavan bugünküden daha büyük bir parça hedeflemez (daha
    /// büyük bir parça, kanıtlanmamış bir sağlayıcı tamamlama sınırına
    /// çarpabilirdi).
    /// </summary>
    private static int ClampedFromCeiling(int maxOutputTokens, int upperBound)
    {
        var derived = maxOutputTokens / EstimatedTokensPerTable;
        return Math.Clamp(derived, 1, upperBound);
    }

    /// <summary>
    /// Plan bölünmeyi hak ediyor mu — tek çağrıya güvenle sığmayacak kadar büyük mü.
    ///
    /// <paramref name="maxOutputTokens"/> verilmezse (veya &lt;= 0) bugünkü
    /// sabit eşik (<see cref="PartitionThreshold"/>) kullanılır — çağıranın
    /// tavanı bilmediği yollar (ör. eski testler) davranış değiştirmez.
    /// </summary>
    public static bool ShouldPartition(SchemaScopePlan plan, int maxOutputTokens = 0)
    {
        var threshold = maxOutputTokens > 0
            ? ClampedFromCeiling(maxOutputTokens, PartitionThreshold)
            : PartitionThreshold;

        return plan.TableCount > threshold;
    }

    /// <summary>
    /// Planı, sırayı koruyan deterministik bir açgözlü (greedy) algoritmayla parçalara böler.
    /// Hiçbir tablo kaybolmaz veya tekrarlanmaz — çağıran bunu güvenle varsayabilir.
    ///
    /// <paramref name="maxOutputTokens"/> verilmezse (veya &lt;= 0) bugünkü
    /// sabit hedef (<see cref="TargetTablesPerChunk"/>) kullanılır.
    /// </summary>
    public static IReadOnlyList<SchemaChunk> Partition(SchemaScopePlan plan, int maxOutputTokens = 0)
    {
        var targetTablesPerChunk = maxOutputTokens > 0
            ? ClampedFromCeiling(maxOutputTokens, TargetTablesPerChunk)
            : TargetTablesPerChunk;

        var chunks = new List<SchemaChunk>();

        // Açık (henüz kapatılmamış) parçanın tablolarını ve kapsadığı alan
        // adlarını ayrı tutuyoruz ki etiketi en son, gerçek içerikten üretelim.
        var openTables = new List<string>();
        var openDomainNames = new List<string>();

        void CloseOpenChunk()
        {
            if (openTables.Count == 0) return;

            chunks.Add(new SchemaChunk(string.Join(" + ", openDomainNames), openTables));
            openTables = new List<string>();
            openDomainNames = new List<string>();
        }

        foreach (var domain in plan.Domains)
        {
            if (domain.Tables.Count > targetTablesPerChunk)
            {
                // Büyük alan: önce açık parçayı kapat (bu alanı önceki
                // parçalarla karıştırmamak için), sonra alanı kendi
                // dilimlerine böl — her dilim kendi başına bir parça.
                CloseOpenChunk();

                var sliceCount = (domain.Tables.Count + targetTablesPerChunk - 1) / targetTablesPerChunk;
                for (var slice = 0; slice < sliceCount; slice++)
                {
                    var sliceTables = domain.Tables
                        .Skip(slice * targetTablesPerChunk)
                        .Take(targetTablesPerChunk)
                        .ToList();

                    chunks.Add(new SchemaChunk($"{domain.Name} ({slice + 1}/{sliceCount})", sliceTables));
                }

                continue;
            }

            if (openTables.Count + domain.Tables.Count > targetTablesPerChunk)
            {
                // Alan açık parçaya eklenince hedefi aşıyor: mevcut parçayı
                // kapatıp bu alanla yeni bir parça açıyoruz.
                CloseOpenChunk();
            }

            openTables.AddRange(domain.Tables);
            openDomainNames.Add(domain.Name);
        }

        CloseOpenChunk();

        return chunks;
    }
}
