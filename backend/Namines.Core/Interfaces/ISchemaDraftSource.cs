using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
    /// <summary>Kullanıcının cümlesinden ilk taslağı üretir.</summary>
    Task<DatabaseSchema> DraftAsync(string prompt, DatabaseType engine, CancellationToken cancellationToken = default);

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
