using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Interfaces;

/// <summary>
/// Ajan hattının <b>AI tarafı</b> — taslak üretmek ve verilen bulgulara göre
/// düzeltmek.
///
/// <b>Arayüz olmasının sebebi test edilebilirlik değil, sınırı görünür kılmak:</b>
/// bu arayüzün ardındaki her şey tahmin üretir; önündeki her şey (denetim, karar,
/// tur sayısı) deterministiktir. İkisini aynı sınıfa koymak, "AI ne zaman
/// durur" sorusunu yine AI'ya sormak olurdu.
/// </summary>
public interface ISchemaDraftSource
{
    /// <summary>
    /// Üretimden önce kısa bir plan çıkarır.
    ///
    /// <c>null</c> dönebilir: plan turu de bütçe harcar ve hat onu atlayabilir —
    /// o durumda taslak plansız üretilir.
    /// </summary>
    Task<string?> PlanAsync(string prompt, DatabaseType engine, CancellationToken cancellationToken = default);

    /// <summary>Kullanıcının cümlesinden ilk taslağı üretir.</summary>
    /// <param name="plan">Plan turunun çıktısı; tur atlandıysa <c>null</c>.</param>
    Task<DatabaseSchema> DraftAsync(
        string prompt, DatabaseType engine, string? plan, CancellationToken cancellationToken = default);

    /// <summary>
    /// Büyük bir şemanın TEK BİR parçasını ("chunk") üretir.
    ///
    /// <b>Neden <see cref="DraftAsync"/>'ten ayrı bir metot:</b> bir parça çağrısı
    /// yalnızca <paramref name="chunk"/>'ın sahip olduğu tabloları TANIMLAR;
    /// şemanın geri kalanını yalnızca İSİMLE görür (<paramref name="allTableNames"/>)
    /// — ilişki kurabilsin diye, ama yeniden üretmesin. <see cref="DraftAsync"/>
    /// tüm şemayı tek çağrıda ister; bu ikisini aynı metotta birleştirmek, parça
    /// çağrılarının hangi tabloları "sahiplendiğini" çağıranın (deterministik
    /// birleştirici) değil, modelin kararına bırakırdı — ve iki parça aynı tabloyu
    /// tekrar üretirse birleştirme çakışır.
    /// </summary>
    /// <param name="chunk">Bu çağrının sahiplendiği tablolar ve etiketi.</param>
    /// <param name="allTableNames">
    /// Şemadaki TÜM tabloların adları (bu parçanınkiler dahil) — model, kendi
    /// sahiplenmediği bir tabloya ilişki kurarken doğru ada referans versin diye.
    /// </param>
    Task<DatabaseSchema> DraftChunkAsync(
        string prompt,
        DatabaseType engine,
        SchemaChunk chunk,
        IReadOnlyList<string> allTableNames,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verilen bulguları düzeltir.
    ///
    /// <paramref name="findings"/> <b>deterministik motorlardan</b> geliyor —
    /// linter ve gerçek DDL üreticileri. Modele "sen bir daha bak" demek değil,
    /// "şu somut şeyler yanlış, düzelt" demek.
    /// </summary>
    Task<DatabaseSchema> RepairAsync(
        DatabaseSchema schema,
        IReadOnlyList<string> findings,
        DatabaseType engine,
        CancellationToken cancellationToken = default);
}
