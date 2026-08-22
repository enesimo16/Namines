using System.Collections.Generic;

namespace Namines.Core.Models;

/// <summary>
/// Toplu içe aktarım sonucu (08 §2 <c>/import</c>).
/// </summary>
/// <param name="InsertedRows">Yazılan satır sayısı.</param>
public sealed record GatewayImportResult(int InsertedRows);

/// <summary>
/// Ham sorgu ya da fonksiyon çağrısının sonucu.
/// </summary>
/// <param name="Rows">Sorgu satır döndürdüyse satırlar; döndürmediyse boş.</param>
/// <param name="AffectedRows">
/// Satır döndürmeyen bir ifadede etkilenen satır sayısı; satır döndüren bir
/// sorguda -1. İkisini tek bir alanda birleştirmek, "0 satır döndü" ile
/// "0 satır etkilendi" arasındaki farkı yok ederdi.
/// </param>
/// <param name="Truncated">
/// Sonuç tavana takıldıysa true. Sessizce kesilen bir sonuç, çağıranın eksik
/// veriyi tam sanması demektir.
/// </param>
public sealed record GatewayQueryResult(
    IReadOnlyList<GatewayRow> Rows,
    int AffectedRows,
    bool Truncated);
