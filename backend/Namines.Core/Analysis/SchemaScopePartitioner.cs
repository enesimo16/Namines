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

    /// <summary>Plan bölünmeyi hak ediyor mu — tek çağrıya güvenle sığmayacak kadar büyük mü.</summary>
    public static bool ShouldPartition(SchemaScopePlan plan) => plan.TableCount > PartitionThreshold;

    /// <summary>
    /// Planı, sırayı koruyan deterministik bir açgözlü (greedy) algoritmayla parçalara böler.
    /// Hiçbir tablo kaybolmaz veya tekrarlanmaz — çağıran bunu güvenle varsayabilir.
    /// </summary>
    public static IReadOnlyList<SchemaChunk> Partition(SchemaScopePlan plan)
    {
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
            if (domain.Tables.Count > TargetTablesPerChunk)
            {
                // Büyük alan: önce açık parçayı kapat (bu alanı önceki
                // parçalarla karıştırmamak için), sonra alanı kendi
                // dilimlerine böl — her dilim kendi başına bir parça.
                CloseOpenChunk();

                var sliceCount = (domain.Tables.Count + TargetTablesPerChunk - 1) / TargetTablesPerChunk;
                for (var slice = 0; slice < sliceCount; slice++)
                {
                    var sliceTables = domain.Tables
                        .Skip(slice * TargetTablesPerChunk)
                        .Take(TargetTablesPerChunk)
                        .ToList();

                    chunks.Add(new SchemaChunk($"{domain.Name} ({slice + 1}/{sliceCount})", sliceTables));
                }

                continue;
            }

            if (openTables.Count + domain.Tables.Count > TargetTablesPerChunk)
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
