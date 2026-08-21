using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Models;

namespace Namines.Core.Interfaces;

/// <summary>G14 + Faz B/08 — bkz. GatewayModels.cs sınıf yorumu.</summary>
public interface IGatewayService
{
    /// <param name="orderByColumn">Sayfalar arası tutarlılık için sıralama kolonu (genelde PK).
    /// Null ise motor satır sırasını garanti etmez — çağıran bunu kullanıcıya bildirmelidir.</param>
    /// <param name="includeTotalCount">false ise pahalı COUNT(*) atlanır ve
    /// <see cref="GatewayListResult.TotalCount"/> -1 döner ("değişmedi").</param>
    /// <param name="filters">
    /// Uygulanacak filtreler. COUNT da AYNI filtrelerle çalışır — aksi hâlde sayfalama
    /// çubuğu filtrelenmiş listeyle çelişen bir toplam gösterirdi.
    /// </param>
    Task<GatewayListResult> ListAsync(
        string connectionString, string dbType, string tableName,
        int page, int pageSize, string? orderByColumn = null,
        bool includeTotalCount = true,
        GatewaySortDirection sortDirection = GatewaySortDirection.Asc,
        IReadOnlyList<GatewayFilter>? filters = null,
        CancellationToken cancellationToken = default);

    Task<GatewayRow?> DetailAsync(
        string connectionString, string dbType, string tableName,
        string pkColumn, string pkValue, CancellationToken cancellationToken = default);

    // ── Yazma yolu (Faz B/08) ────────────────────────────────────────────────
    //
    // GÜVENLİK SÖZLEŞMESİ, üç madde — hepsi GatewayService'te uygulanıyor:
    //
    //  1. UPDATE/DELETE bir birincil anahtar koşulu OLMADAN ASLA üretilmez. Filtresiz
    //     bir UPDATE/DELETE tek bir hatayla tüm tabloyu siler; bu yüzden imza bunu
    //     "unutulabilir bir seçenek" olarak sunmuyor, ZORUNLU parametre yapıyor.
    //  2. Her yazma bir işlem (transaction) içinde çalışır ve etkilenen satır sayısı
    //     doğrulanır. Tekil anahtarla eşleşen satır 1'den fazlaysa geri alınır:
    //     verilen kolon gerçekte benzersiz değildir ve niyet edilenden fazla satır
    //     değişiyordur.
    //  3. Kolon adları katı bir tanımlayıcı doğrulamasından geçer, değerler
    //     parametrelidir. Tanımlayıcılar asla kullanıcı metninden SQL'e yazılmaz.

    Task<GatewayWriteResult> CreateAsync(
        string connectionString, string dbType, string tableName,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken cancellationToken = default);

    /// <param name="values">Yalnızca verilen kolonlar güncellenir (kısmi güncelleme).</param>
    Task<GatewayWriteResult> UpdateAsync(
        string connectionString, string dbType, string tableName,
        string pkColumn, string pkValue,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken cancellationToken = default);

    Task<GatewayWriteResult> DeleteAsync(
        string connectionString, string dbType, string tableName,
        string pkColumn, string pkValue,
        CancellationToken cancellationToken = default);
}
