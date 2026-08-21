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

public sealed record GatewayListResult(
    IReadOnlyList<GatewayRow> Rows,
    int Page,
    int PageSize,
    long TotalCount);
