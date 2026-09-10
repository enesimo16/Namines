using System.Collections.Generic;

namespace Namines.Core.Models;

/// <summary>
/// G14 — Minimal Gateway (new-phase/27-LIFECYCLE-PIVOT.md §4 Adım 8,
/// new-phase/28-IMPACT-ANALYSIS-ENGINE.md §5 — "Gateway tam olarak var olmadan bile...
/// minimal salt-okunur REST"). Şemadan otomatik üretilen liste/detay sorguları —
/// yazma yolu YOK, sadece SELECT. DbIntrospectController ile aynı güvenlik modeli:
/// connection string hiçbir yerde saklanmaz, her istekte bir kez kullanılır.
/// </summary>
public sealed record GatewayRow(IReadOnlyDictionary<string, object?> Values);

/// <param name="StablePagination">
/// Sayfalamanin KARARLI olup olmadigi.
///
/// <b>Neden sozlesmede:</b> `ORDER BY` olmadan `LIMIT/OFFSET` sayfalamasi
/// hicbir motorda kararli sayilmaz -- Postgres bir UPDATE/VACUUM sonrasi ya da
/// paralel taramada satir sirasini degistirebilir. Sonuc: "sonraki sayfa" ayni
/// satiri tekrar gosterip baska birini HIC gostermez.
///
/// Sunucu bunu zaten biliyor ve `BuildListSql` yorumunda yaziyor, ama uyariyi
/// "cagiran (UI) kullaniciyi uyarir" diye BIRAKIYORDU: dogruluk garantisi
/// arayuzun hatirlamasina baglanmis oluyordu. Bir alan olarak dondugunde
/// istemci onu gormezden gelmeyi SECEBILIR ama artik bilmediğini soyleyemez.
///
/// <c>false</c> ise: siralama kolonu verilmemis, sayfalar arasinda satir
/// tekrari/atlamasi mumkun.
/// </param>
public sealed record GatewayListResult(
    IReadOnlyList<GatewayRow> Rows,
    int Page,
    int PageSize,
    long TotalCount,
    bool StablePagination = true);
